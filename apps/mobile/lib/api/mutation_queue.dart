import 'dart:async';
import 'dart:convert';
import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'offline_cache.dart';

/// What a queued change will do. Only changes that are safe to repeat and easy to
/// undo are queued; approvals, sends and deletes always need a connection.
class MutationKinds {
  static const reminderComplete = 'reminder.complete';
  static const habitCheckIn = 'habit.check_in';
}

enum MutationStatus {
  /// Waiting for a connection.
  queued,

  /// The server answered that it cannot do it as asked; the owner decides.
  failed,

  /// The thing it was about no longer exists.
  conflict,

  /// Waited too long to be applied unasked.
  expired,
}

/// One change made while Jarvis could not be reached. It stays here, visible,
/// until the server confirms it or the owner discards it. An unknown outcome is
/// never reported as done.
@immutable
class QueuedMutation {
  const QueuedMutation({
    required this.id,
    required this.server,
    required this.kind,
    required this.targetId,
    required this.label,
    required this.payload,
    required this.group,
    required this.createdAt,
    this.status = MutationStatus.queued,
    this.attempts = 0,
    this.nextAttemptAt,
    this.message,
  });

  /// A stable id for the command itself.
  final String id;

  /// The server this change belongs to; it is never sent anywhere else.
  final String server;
  final String kind;
  final String targetId;

  /// What the owner sees, such as "Mark “Call the plumber” as done".
  final String label;
  final Map<String, Object?> payload;

  /// Changes in one group are applied in order, and a newer one replaces an
  /// older queued one for the same thing.
  final String group;
  final DateTime createdAt;
  final MutationStatus status;
  final int attempts;
  final DateTime? nextAttemptAt;
  final String? message;

  QueuedMutation copyWith({
    MutationStatus? status,
    int? attempts,
    DateTime? nextAttemptAt,
    String? message,
    Map<String, Object?>? payload,
    String? label,
    bool clearNext = false,
  }) => QueuedMutation(
    id: id,
    server: server,
    kind: kind,
    targetId: targetId,
    label: label ?? this.label,
    payload: payload ?? this.payload,
    group: group,
    createdAt: createdAt,
    status: status ?? this.status,
    attempts: attempts ?? this.attempts,
    nextAttemptAt: clearNext ? null : nextAttemptAt ?? this.nextAttemptAt,
    message: message ?? this.message,
  );

  Map<String, Object?> toJson() => {
    'id': id,
    'server': server,
    'kind': kind,
    'targetId': targetId,
    'label': label,
    'payload': payload,
    'group': group,
    'createdAt': createdAt.toUtc().toIso8601String(),
    'status': status.name,
    'attempts': attempts,
    'nextAttemptAt': nextAttemptAt?.toUtc().toIso8601String(),
    'message': message,
  };

  static QueuedMutation? fromJson(Object? value) {
    if (value is! Map) return null;
    final createdAt = DateTime.tryParse('${value['createdAt']}');
    final id = value['id'];
    final server = value['server'];
    final kind = value['kind'];
    final targetId = value['targetId'];
    final label = value['label'];
    final group = value['group'];
    if (createdAt == null ||
        id is! String ||
        server is! String ||
        kind is! String ||
        targetId is! String ||
        label is! String ||
        group is! String) {
      return null;
    }
    final payload = value['payload'];
    return QueuedMutation(
      id: id,
      server: server,
      kind: kind,
      targetId: targetId,
      label: label,
      payload: payload is Map ? Map<String, Object?>.from(payload) : const {},
      group: group,
      createdAt: createdAt.toLocal(),
      status: MutationStatus.values.firstWhere(
        (s) => s.name == value['status'],
        orElse: () => MutationStatus.queued,
      ),
      attempts: value['attempts'] is int ? value['attempts'] as int : 0,
      nextAttemptAt: DateTime.tryParse('${value['nextAttemptAt']}')?.toLocal(),
      message: value['message'] is String ? value['message'] as String : null,
    );
  }
}

/// Where the queue is kept. Tests use [MemoryMutationStore].
abstract class MutationStore {
  Future<String?> read();
  Future<void> write(String value);
  Future<void> clear();
}

class MemoryMutationStore implements MutationStore {
  String? _value;

  @override
  Future<String?> read() async => _value;

  @override
  Future<void> write(String value) async => _value = value;

  @override
  Future<void> clear() async => _value = null;
}

class PreferencesMutationStore implements MutationStore {
  static const _key = 'jarvis.mutationQueue.v1';

  @override
  Future<String?> read() async =>
      (await SharedPreferences.getInstance()).getString(_key);

  @override
  Future<void> write(String value) async =>
      (await SharedPreferences.getInstance()).setString(_key, value);

  @override
  Future<void> clear() async =>
      (await SharedPreferences.getInstance()).remove(_key);
}

/// What happened when the queue was worked through.
@immutable
class DrainResult {
  const DrainResult({this.done = 0, this.stopped});

  final int done;

  /// Why it stopped early: `offline`, `signed_out` or null.
  final String? stopped;
}

/// A small, bounded queue of changes made offline: a reminder marked done, a
/// habit checked in. Each has a stable id, is applied in order, and ends in one
/// durable outcome: confirmed by the server, or shown here for the owner to retry,
/// edit or discard.
class MutationQueue {
  MutationQueue({MutationStore? store, DateTime Function()? clock})
    : _store = store ?? PreferencesMutationStore(),
      _clock = clock ?? DateTime.now;

  static final instance = MutationQueue();

  /// More than this and new changes are refused with an explanation.
  static const maxItems = 50;

  /// An untouched change this old is not applied unasked.
  static const maxAge = Duration(days: 7);

  /// Attempts before an item waits for the owner instead of retrying.
  static const maxAttempts = 6;

  final MutationStore _store;
  final DateTime Function() _clock;
  final items = ValueNotifier<List<QueuedMutation>>(const []);
  bool _loaded = false;
  bool _draining = false;
  String? _server;

  int get waiting =>
      items.value.where((m) => m.status == MutationStatus.queued).length;

  /// Loads what was saved, keeping only this server's changes. Anything saved for
  /// another server is removed, never shown and never sent.
  Future<void> bind(String server) async {
    _server = server;
    final raw = await _store.read();
    var loaded = <QueuedMutation>[];
    if (raw != null) {
      try {
        final decoded = jsonDecode(raw);
        if (decoded is List) {
          loaded = [
            for (final entry in decoded) ?QueuedMutation.fromJson(entry),
          ];
        }
      } catch (_) {
        loaded = [];
      }
    }
    final mine = [
      for (final item in loaded)
        if (item.server == server) item,
    ];
    items.value = _expire(mine);
    _loaded = true;
    if (mine.length != loaded.length) await _save();
  }

  /// Removes everything, on sign-out or when the account changes.
  Future<void> clear() async {
    items.value = const [];
    _loaded = true;
    try {
      await _store.clear();
    } catch (_) {
      // Storage that cannot be cleared also cannot be read back.
    }
  }

  /// Adds a change, or returns null with a reason when it cannot be queued.
  Future<({QueuedMutation? mutation, String? problem})> enqueue({
    required String kind,
    required String targetId,
    required String label,
    required String group,
    Map<String, Object?> payload = const {},
  }) async {
    final server = _server;
    if (server == null) {
      return (
        mutation: null,
        problem: 'Jarvis is not ready to keep changes yet.',
      );
    }
    if (!_loaded) await bind(server);
    final current = [...items.value];
    // The newest change for the same thing replaces the older queued one; the same
    // reminder completed twice is one change.
    final sameGroup = current.indexWhere(
      (m) => m.group == group && m.status != MutationStatus.expired,
    );
    if (sameGroup >= 0) {
      final existing = current[sameGroup];
      final next = existing.copyWith(
        payload: payload,
        label: label,
        status: MutationStatus.queued,
        attempts: 0,
        clearNext: true,
      );
      current[sameGroup] = next;
      items.value = current;
      await _save();
      return (mutation: next, problem: null);
    }
    if (current.length >= maxItems) {
      return (
        mutation: null,
        problem:
            'Too many changes are waiting ($maxItems). Reconnect, or discard some first.',
      );
    }
    final mutation = QueuedMutation(
      id: '${_clock().microsecondsSinceEpoch.toRadixString(36)}-${current.length}',
      server: server,
      kind: kind,
      targetId: targetId,
      label: label,
      payload: payload,
      group: group,
      createdAt: _clock(),
    );
    items.value = [...current, mutation];
    await _save();
    return (mutation: mutation, problem: null);
  }

  Future<void> discard(String id) async {
    items.value = [
      for (final item in items.value)
        if (item.id != id) item,
    ];
    await _save();
  }

  /// Puts a failed or expired change back in the line for another try.
  Future<void> retry(String id) async {
    items.value = [
      for (final item in items.value)
        if (item.id == id)
          item.copyWith(
            status: MutationStatus.queued,
            attempts: 0,
            clearNext: true,
            message: '',
          )
        else
          item,
    ];
    await _save();
  }

  /// Works through the queue in order. Stops at the first connection failure
  /// or expired sign-in instead of cycling every item again.
  Future<DrainResult> drain(Dio http) async {
    if (_draining || !_loaded) return const DrainResult();
    final server = _server;
    if (server == null || http.options.baseUrl != server) {
      return const DrainResult();
    }
    _draining = true;
    var done = 0;
    try {
      items.value = _expire(items.value);
      for (final item in [...items.value]) {
        if (item.status != MutationStatus.queued) continue;
        final due = item.nextAttemptAt;
        if (due != null && due.isAfter(_clock())) continue;
        final outcome = await _apply(http, item);
        switch (outcome) {
          case _Outcome.done:
            items.value = [
              for (final m in items.value)
                if (m.id != item.id) m,
            ];
            done++;
          case _Outcome.offline:
            await _replace(_backoff(item));
            return DrainResult(done: done, stopped: 'offline');
          case _Outcome.signedOut:
            return DrainResult(done: done, stopped: 'signed_out');
          case _Outcome.conflict:
            await _replace(
              item.copyWith(
                status: MutationStatus.conflict,
                message: 'It no longer exists, so this change was not applied.',
              ),
            );
          case _Outcome.rejected:
            await _replace(
              item.copyWith(
                status: MutationStatus.failed,
                message:
                    'Jarvis could not apply this. Retry it, or discard it.',
              ),
            );
        }
        await _save();
      }
      return DrainResult(done: done);
    } finally {
      _draining = false;
    }
  }

  Future<_Outcome> _apply(Dio http, QueuedMutation item) async {
    try {
      switch (item.kind) {
        case MutationKinds.reminderComplete:
          await http.post<dynamic>(
            '/api/v1/reminders/${item.targetId}/complete',
            options: Options(extra: {OfflineCache.skipExtra: true}),
          );
        case MutationKinds.habitCheckIn:
          await http.post<dynamic>(
            '/api/v1/habits/${item.targetId}/check-ins',
            data: item.payload,
            options: Options(extra: {OfflineCache.skipExtra: true}),
          );
        default:
          return _Outcome.rejected;
      }
      return _Outcome.done;
    } on DioException catch (error) {
      final status = error.response?.statusCode;
      if (status == null) {
        return OfflineCache.isConnectionFailure(error)
            ? _Outcome.offline
            : _Outcome.rejected;
      }
      if (status == 401) return _Outcome.signedOut;
      if (status == 404) return _Outcome.conflict;
      if (status >= 500 || status == 429) return _Outcome.offline;
      return _Outcome.rejected;
    }
  }

  QueuedMutation _backoff(QueuedMutation item) {
    final attempts = item.attempts + 1;
    if (attempts >= maxAttempts) {
      return item.copyWith(
        status: MutationStatus.failed,
        attempts: attempts,
        message: 'Still could not reach Jarvis after several tries. Retry it when you are back online.',
      );
    }
    final seconds = math.min(15 * 60, 5 * math.pow(2, attempts).toInt());
    return item.copyWith(
      attempts: attempts,
      nextAttemptAt: _clock().add(Duration(seconds: seconds)),
    );
  }

  List<QueuedMutation> _expire(List<QueuedMutation> list) => [
    for (final item in list)
      if (item.status == MutationStatus.queued &&
          _clock().difference(item.createdAt) > maxAge)
        item.copyWith(
          status: MutationStatus.expired,
          message: 'It waited more than 7 days, so it will not be applied on its own.',
        )
      else
        item,
  ];

  Future<void> _replace(QueuedMutation next) async {
    items.value = [for (final m in items.value) m.id == next.id ? next : m];
    await _save();
  }

  Future<void> _save() async {
    try {
      await _store.write(
        jsonEncode([for (final item in items.value) item.toJson()]),
      );
    } catch (_) {
      // A full disk keeps the queue in memory for this session.
    }
  }
}

enum _Outcome { done, offline, signedOut, conflict, rejected }
