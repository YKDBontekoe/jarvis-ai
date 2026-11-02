import 'dart:async';
import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// A photo already uploaded for an unsent message.
typedef DraftPhoto = ({String fileId, String fileName});

class _Draft {
  const _Draft(this.text, this.photos);

  final String text;
  final List<DraftPhoto> photos;

  bool get isEmpty => text.trim().isEmpty && photos.isEmpty;
}

/// Unsent composer text and uploaded photos per conversation, kept on this
/// device so a draft survives switching chats and restarting the app. Cleared
/// on sign-out.
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
  final Map<String, _Draft> _drafts;
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

  static Map<String, _Draft> _decode(String? raw) {
    if (raw == null || raw.isEmpty) return {};
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return {};
      final result = <String, _Draft>{};
      for (final entry in decoded.entries) {
        final key = entry.key;
        final value = entry.value;
        if (key is! String) continue;
        _Draft? draft;
        if (value is String) {
          draft = _Draft(value, const []);
        } else if (value is Map) {
          final photos = value['p'];
          draft = _Draft(value['t'] is String ? value['t'] as String : '', [
            if (photos is List)
              for (final photo in photos)
                if (photo is Map && photo['id'] is String)
                  (
                    fileId: photo['id'] as String,
                    fileName: photo['name'] is String
                        ? photo['name'] as String
                        : 'Photo',
                  ),
          ]);
        }
        if (draft != null && !draft.isEmpty) result[key] = draft;
      }
      return result;
    } catch (_) {
      return {};
    }
  }

  /// What each conversation's unsent draft looks like in the Chats list: its
  /// text, or "Photo" / "2 photos" when only photos are waiting.
  Map<String, String> previews() => {
    for (final entry in _drafts.entries)
      if (entry.value.text.trim().isNotEmpty)
        entry.key: entry.value.text.trim()
      else if (entry.value.photos.isNotEmpty)
        entry.key: entry.value.photos.length == 1
            ? 'Photo'
            : '${entry.value.photos.length} photos',
  };

  String read(String conversationId) => _drafts[conversationId]?.text ?? '';

  List<DraftPhoto> readPhotos(String conversationId) =>
      _drafts[conversationId]?.photos ?? const [];

  /// Remembers [text] for [conversationId], keeping its photos; blank text and
  /// no photos forgets the draft.
  void write(String conversationId, String text) =>
      _put(conversationId, text, readPhotos(conversationId));

  /// Remembers the uploaded [photos] of [conversationId], keeping its text.
  void writePhotos(String conversationId, List<DraftPhoto> photos) =>
      _put(conversationId, read(conversationId), photos);

  void _put(String conversationId, String text, List<DraftPhoto> photos) {
    final previous = _drafts.remove(conversationId);
    final draft = _Draft(
      text.length > maxLength ? text.substring(0, maxLength) : text,
      photos,
    );
    if (draft.isEmpty) {
      if (previous != null) _scheduleSave();
      return;
    }
    _drafts[conversationId] = draft;
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
        await prefs.setString(
          storageKey,
          jsonEncode({
            for (final entry in _drafts.entries)
              // Text-only drafts keep the original plain-string shape.
              entry.key: entry.value.photos.isEmpty
                  ? entry.value.text
                  : {
                      't': entry.value.text,
                      'p': [
                        for (final photo in entry.value.photos)
                          {'id': photo.fileId, 'name': photo.fileName},
                      ],
                    },
          }),
        );
      }
    } catch (_) {
      // A draft that cannot be saved still lives in memory for this session.
    }
  }

  void dispose() => _saveTimer?.cancel();
}
