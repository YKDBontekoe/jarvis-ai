import 'dart:async';
import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../ui/jarvis_ui.dart';

/// Unfinished form fields kept on this device, keyed per form. Drafts belong to
/// the server they were typed against (part of the key) and are removed on
/// sign-out, so another account never sees them.
class DraftStore {
  DraftStore._(this._prefs, this._drafts);

  DraftStore.memory() : this._(null, {});

  static const storageKey = 'jarvis.form_drafts.v1';
  static const maxDrafts = 40;
  static const expiry = Duration(days: 30);

  final SharedPreferences? _prefs;
  final Map<String, ({DateTime savedAt, Map<String, String> fields})> _drafts;

  /// The key for [form] (for example `journal:2026-11-02`) on [serverUrl].
  static String keyFor(String serverUrl, String form) => '$serverUrl|$form';

  static Future<DraftStore> open({DateTime? now}) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      return DraftStore._(
        prefs,
        _decode(prefs.getString(storageKey), now ?? DateTime.now()),
      );
    } catch (_) {
      return DraftStore.memory();
    }
  }

  static Future<void> clearAll() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      await prefs.remove(storageKey);
    } catch (_) {
      // Nothing was stored when device storage is unavailable.
    }
  }

  static Map<String, ({DateTime savedAt, Map<String, String> fields})> _decode(
    String? raw,
    DateTime now,
  ) {
    if (raw == null || raw.isEmpty) return {};
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return {};
      final result =
          <String, ({DateTime savedAt, Map<String, String> fields})>{};
      for (final entry in decoded.entries) {
        final value = entry.value;
        if (entry.key is! String || value is! Map) continue;
        final savedAt = DateTime.tryParse('${value['savedAt']}');
        final fields = value['fields'];
        if (savedAt == null || fields is! Map) continue;
        if (now.difference(savedAt) > expiry) continue;
        result[entry.key as String] = (
          savedAt: savedAt,
          fields: {
            for (final field in fields.entries)
              if (field.value is String) '${field.key}': field.value as String,
          },
        );
      }
      return result;
    } catch (_) {
      return {};
    }
  }

  Map<String, String>? read(String key) => _drafts[key]?.fields;

  void write(String key, Map<String, String> fields, {DateTime? now}) {
    _drafts.remove(key);
    _drafts[key] = (savedAt: now ?? DateTime.now(), fields: fields);
    while (_drafts.length > maxDrafts) {
      _drafts.remove(_drafts.keys.first);
    }
  }

  void remove(String key) => _drafts.remove(key);

  Future<void> flush() async {
    final prefs = _prefs;
    if (prefs == null) return;
    try {
      if (_drafts.isEmpty) {
        await prefs.remove(storageKey);
      } else {
        await prefs.setString(
          storageKey,
          jsonEncode({
            for (final entry in _drafts.entries)
              entry.key: {
                'savedAt': entry.value.savedAt.toUtc().toIso8601String(),
                'fields': entry.value.fields,
              },
          }),
        );
      }
    } catch (_) {
      // A draft that cannot be saved still lives in memory for this session.
    }
  }
}

/// Saves one form's fields shortly after each change and when the app goes to
/// the background, and offers them back when the form opens again.
class FormDraft with WidgetsBindingObserver {
  FormDraft({
    required this.key,
    required this.read,
    required this.apply,
    required this.onChanged,
    this.saveDelay = const Duration(milliseconds: 600),
    DraftStore? store,
    // The public name is `store`; the field is private and set after open().
    // ignore: prefer_initializing_formals
  }) : _store = store;

  final String key;

  /// The current field values; an unchanged form saves no draft.
  final Map<String, String> Function() read;

  /// Puts saved values back into the form's fields.
  final void Function(Map<String, String> fields) apply;

  /// Rebuilds the form when [pending] changes.
  final VoidCallback onChanged;
  final Duration saveDelay;

  DraftStore? _store;
  Map<String, String>? _initial;
  Timer? _timer;
  bool _disposed = false;
  bool _finished = false;

  /// A saved draft waiting for the owner to restore or discard it.
  Map<String, String>? pending;

  Future<void> attach() async {
    _initial = read();
    WidgetsBinding.instance.addObserver(this);
    _store ??= await DraftStore.open();
    if (_disposed) return;
    final saved = _store!.read(key);
    if (saved != null && !_same(saved, _initial!)) {
      pending = saved;
      onChanged();
    } else if (saved != null) {
      _store!.remove(key);
    }
  }

  bool _same(Map<String, String> a, Map<String, String> b) =>
      a.length == b.length && a.entries.every((e) => b[e.key] == e.value);

  /// Call after any edit.
  void changed() {
    if (_store == null || pending != null || _finished) return;
    _timer?.cancel();
    _timer = Timer(saveDelay, () => unawaited(flush()));
  }

  Future<void> flush() async {
    _timer?.cancel();
    final store = _store;
    final initial = _initial;
    if (store == null || initial == null || pending != null || _finished) {
      return;
    }
    final now = read();
    if (_same(now, initial)) {
      store.remove(key);
    } else {
      store.write(key, now);
    }
    await store.flush();
  }

  void restore() {
    final fields = pending;
    if (fields == null) return;
    pending = null;
    apply(fields);
    onChanged();
  }

  /// Drops the offered draft and starts from what the form already holds.
  void discardPending() {
    pending = null;
    _store?.remove(key);
    unawaited(_store?.flush());
    onChanged();
  }

  /// Removes the draft after a successful save or an intentional discard.
  Future<void> clear() async {
    _finished = true;
    _timer?.cancel();
    pending = null;
    _store?.remove(key);
    await _store?.flush();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.paused ||
        state == AppLifecycleState.inactive ||
        state == AppLifecycleState.hidden) {
      unawaited(flush());
    }
  }

  void dispose() {
    _disposed = true;
    _timer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    // Persist what was typed so a closed or killed app still offers it back.
    unawaited(flush());
  }
}

/// The Restore / Discard offer shown above a form that has a saved draft.
class DraftBanner extends StatelessWidget {
  const DraftBanner({
    required this.draft,
    this.margin = const EdgeInsets.only(bottom: 12),
    super.key,
  });

  final FormDraft draft;
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) {
    if (draft.pending == null) return const SizedBox.shrink();
    return InlineNotice(
      key: const Key('draft-banner'),
      tone: NoticeTone.info,
      message: 'You have an unfinished draft from earlier.',
      margin: margin,
      actions: [
        TextButton(
          key: const Key('draft-discard'),
          onPressed: draft.discardPending,
          child: const Text('Discard'),
        ),
        TextButton(
          key: const Key('draft-restore'),
          onPressed: draft.restore,
          child: const Text('Restore'),
        ),
      ],
    );
  }
}
