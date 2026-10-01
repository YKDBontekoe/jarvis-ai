import 'dart:async';
import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// Unsent composer text per conversation, kept on this device so a draft
/// survives switching chats and restarting the app. Cleared on sign-out.
class ComposerDrafts {
  ComposerDrafts._(this._prefs, this._drafts);

  /// A store that only lives in memory, for when device storage is missing.
  ComposerDrafts.memory() : this._(null, {});

  static const storageKey = 'jarvis.composer_drafts.v1';
  static const maxDrafts = 30;
  static const maxLength = 32000;
  static const _saveDelay = Duration(milliseconds: 400);

  final SharedPreferences? _prefs;

  /// Oldest first, so trimming drops the drafts left alone the longest.
  final Map<String, String> _drafts;
  Timer? _saveTimer;

  static Future<ComposerDrafts> open() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      return ComposerDrafts._(prefs, _decode(prefs.getString(storageKey)));
    } catch (_) {
      return ComposerDrafts.memory();
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

  static Map<String, String> _decode(String? raw) {
    if (raw == null || raw.isEmpty) return {};
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return {};
      return {
        for (final entry in decoded.entries)
          if (entry.key is String &&
              entry.value is String &&
              (entry.value as String).trim().isNotEmpty)
            entry.key as String: entry.value as String,
      };
    } catch (_) {
      return {};
    }
  }

  String read(String conversationId) => _drafts[conversationId] ?? '';

  /// Remembers [text] for [conversationId]; blank text forgets the draft.
  void write(String conversationId, String text) {
    final previous = _drafts.remove(conversationId);
    if (text.trim().isEmpty) {
      if (previous != null) _scheduleSave();
      return;
    }
    _drafts[conversationId] = text.length > maxLength
        ? text.substring(0, maxLength)
        : text;
    while (_drafts.length > maxDrafts) {
      _drafts.remove(_drafts.keys.first);
    }
    _scheduleSave();
  }

  void remove(String conversationId) {
    if (_drafts.remove(conversationId) != null) _scheduleSave();
  }

  /// Forgets every draft in memory and on the device.
  Future<void> clear() async {
    _saveTimer?.cancel();
    _drafts.clear();
    await _prefs?.remove(storageKey);
  }

  void _scheduleSave() {
    if (_prefs == null) return;
    _saveTimer?.cancel();
    _saveTimer = Timer(_saveDelay, () => unawaited(flush()));
  }

  /// Writes pending changes now, for example when the app goes to background.
  Future<void> flush() async {
    _saveTimer?.cancel();
    _saveTimer = null;
    final prefs = _prefs;
    if (prefs == null) return;
    try {
      if (_drafts.isEmpty) {
        await prefs.remove(storageKey);
      } else {
        await prefs.setString(storageKey, jsonEncode(_drafts));
      }
    } catch (_) {
      // A draft that cannot be saved still lives in memory for this session.
    }
  }

  void dispose() => _saveTimer?.cancel();
}
