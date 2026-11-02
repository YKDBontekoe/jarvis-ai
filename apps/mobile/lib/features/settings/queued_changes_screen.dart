import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/mutation_queue.dart';
import '../../api/waiting_changes.dart';
import '../chat/outbox_store.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Where a queued change belongs: the group it is listed under and the page it
/// opens. Unknown kinds are listed under "Other" with no link.
({String group, String? destination}) mutationDestination(QueuedMutation m) =>
    switch (m.kind) {
      MutationKinds.reminderComplete => (
        group: 'Reminders',
        destination: 'reminders',
      ),
      MutationKinds.habitCheckIn => (group: 'Habits', destination: 'habits'),
      _ => (group: 'Other', destination: null),
    };

/// Everything waiting to be sent, grouped by where it belongs: changes made
/// while Jarvis could not be reached, and chat messages not sent yet. Nothing
/// here counts as done until the server confirms it; each one can be opened,
/// retried or discarded.
class QueuedChangesScreen extends StatefulWidget {
  const QueuedChangesScreen({
    required this.http,
    this.queue,
    this.waiting,
    this.clock,
    super.key,
  });

  final Dio http;
  final MutationQueue? queue;
  final WaitingChanges? waiting;
  final DateTime Function()? clock;

  @override
  State<QueuedChangesScreen> createState() => _QueuedChangesScreenState();
}

class _QueuedChangesScreenState extends State<QueuedChangesScreen> {
  bool _syncing = false;
  String? _note;

  MutationQueue get _queue => widget.queue ?? MutationQueue.instance;
  WaitingChanges get _waiting => widget.waiting ?? WaitingChanges.instance;
  DateTime get _now => (widget.clock ?? DateTime.now)();

  Future<void> _syncNow() async {
    setState(() {
      _syncing = true;
      _note = null;
    });
    final result = await _queue.drain(widget.http);
    if (!mounted) return;
    setState(() {
      _syncing = false;
      _note = switch (result.stopped) {
        'offline' => 'Jarvis still cannot be reached. Your changes are safe on this phone.',
        'signed_out' => 'You need to sign in again before these can be sent.',
        _ =>
          result.done == 0
              ? 'Nothing was waiting to send.'
              : '${result.done} sent and confirmed.',
      };
    });
  }

  String _age(DateTime at) {
    final age = _now.difference(at);
    if (age.inMinutes < 1) return 'just now';
    if (age.inHours < 1) return '${age.inMinutes} min ago';
    if (age.inDays < 1) return '${age.inHours} h ago';
    return '${age.inDays} d ago';
  }

  Widget _mutationRow(
    BuildContext context,
    QueuedMutation item,
    int index,
    String? destination,
  ) {
    final colors = JarvisColors.of(context);
    final open = destination == null
        ? null
        : () => _waiting.openDestination?.call(destination);
    return FeatureRow(
      key: Key('queue-${item.id}'),
      index: index,
      icon: switch (item.status) {
        MutationStatus.queued => PhosphorIconsRegular.clock,
        MutationStatus.conflict => PhosphorIconsRegular.warningCircle,
        _ => PhosphorIconsRegular.xCircle,
      },
      badgeColor: item.status == MutationStatus.queued
          ? colors.accent
          : colors.warning,
      caption: switch (item.status) {
        MutationStatus.queued => 'Waiting · ${_age(item.createdAt)}',
        MutationStatus.failed => 'Needs you',
        MutationStatus.conflict => 'Could not apply',
        MutationStatus.expired => 'Too old',
      },
      title: item.label,
      subtitle: switch ((item.status, item.message)) {
        (MutationStatus.conflict, final message) =>
          message == null || message.isEmpty
              ? 'What this was about has changed, so this was not applied.'
              : message,
        (_, final message) =>
          message == null || message.isEmpty ? null : message,
      },
      actions: [
        if (item.status != MutationStatus.queued &&
            item.status != MutationStatus.conflict)
          PillAction(
            key: Key('queue-retry-${item.id}'),
            compact: true,
            primary: true,
            label: 'Try again',
            onPressed: () => unawaited(_queue.retry(item.id)),
          ),
        if (open != null && _waiting.openDestination != null)
          PillAction(
            key: Key('queue-open-${item.id}'),
            compact: true,
            label: item.status == MutationStatus.conflict
                ? 'Open current item'
                : 'Open',
            onPressed: open,
          ),
        PillAction(
          key: Key('queue-discard-${item.id}'),
          compact: true,
          label: 'Discard',
          onPressed: () => unawaited(_queue.discard(item.id)),
        ),
      ],
    );
  }

  Widget _messageRow(BuildContext context, OutboxMessage message, int index) {
    final colors = JarvisColors.of(context);
    final expired = OutboxStore.isExpired(message, _now);
    return FeatureRow(
      key: Key('queue-${message.id}'),
      index: index,
      icon: expired
          ? PhosphorIconsRegular.warningCircle
          : PhosphorIconsRegular.chatCircle,
      badgeColor: expired ? colors.warning : colors.accent,
      caption: expired
          ? 'Too old to send by itself · ${_age(message.queuedAt)}'
          : 'Waiting · ${_age(message.queuedAt)}',
      title: message.content,
      subtitle: message.photos.isEmpty
          ? null
          : message.photos.length == 1
          ? 'With 1 photo'
          : 'With ${message.photos.length} photos',
      actions: [
        if (_waiting.openConversation != null)
          PillAction(
            key: Key('queue-open-${message.id}'),
            compact: true,
            primary: true,
            label: 'Open chat',
            onPressed: () =>
                _waiting.openConversation?.call(message.conversationId),
          ),
        PillAction(
          key: Key('queue-discard-${message.id}'),
          compact: true,
          label: 'Discard',
          onPressed: () => unawaited(_waiting.discardMessage?.call(message.id)),
        ),
      ],
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Waiting to sync')),
      body: ListenableBuilder(
        listenable: Listenable.merge([_queue.items, _waiting.messages]),
        builder: (context, _) {
          final items = _queue.items.value;
          final messages = _waiting.messages.value;
          final waiting =
              items.where((m) => m.status == MutationStatus.queued).length +
              messages.where((m) => !OutboxStore.isExpired(m, _now)).length;
          final stuck =
              items.length -
              items.where((m) => m.status == MutationStatus.queued).length +
              messages.where((m) => OutboxStore.isExpired(m, _now)).length;
          final total = items.length + messages.length;
          // Rows grouped by where they belong, largest groups first by name.
          final groups = <String, List<Widget>>{};
          for (final (i, item) in items.indexed) {
            final target = mutationDestination(item);
            groups
                .putIfAbsent(target.group, () => [])
                .add(_mutationRow(context, item, i, target.destination));
          }
          for (final (i, message) in messages.indexed) {
            groups
                .putIfAbsent('Chats', () => [])
                .add(_messageRow(context, message, i));
          }
          final names = groups.keys.toList()..sort();
          return ListView(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            children: [
              ContentWidth(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    FadeSlideIn(
                      child: KeyedSubtree(
                        key: const Key('queue-summary'),
                        child: HeroCard(
                          label: 'On this phone',
                          title: total == 0
                              ? 'Nothing is waiting'
                              : '$waiting waiting${stuck > 0 ? ' · $stuck need you' : ''}',
                          subtitle: total == 0
                              ? 'Changes you make without a connection show up here until Jarvis confirms them.'
                              : 'They are not done until Jarvis confirms them. Approvals, sends and deletes always need a connection.',
                          footer: total == 0
                              ? null
                              : PillAction(
                                  key: const Key('queue-sync'),
                                  primary: true,
                                  onDark: true,
                                  icon: PhosphorIconsRegular.arrowsClockwise,
                                  label: _syncing ? 'Sending…' : 'Send now',
                                  onPressed: _syncing
                                      ? null
                                      : () => unawaited(_syncNow()),
                                ),
                        ),
                      ),
                    ),
                    if (_note != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 12),
                        child: Text(
                          _note!,
                          key: const Key('queue-note'),
                          style: TextStyle(color: colors.inkSoft),
                        ),
                      ),
                    for (final name in names) ...[
                      GroupLabel('$name · ${groups[name]!.length}'),
                      ...groups[name]!,
                    ],
                  ],
                ),
              ),
            ],
          );
        },
      ),
    );
  }
}
