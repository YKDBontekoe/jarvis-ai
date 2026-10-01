import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

/// A message typed while Jarvis was unreachable, waiting to be sent.
class OutboxMessage {
  const OutboxMessage({
    required this.id,
    required this.conversationId,
    required this.content,
    required this.queuedAt,
    this.photos = const [],
  });

  final String id;
  final String conversationId;
  final String content;
  final DateTime queuedAt;

  /// Already uploaded photos as `(fileId, fileName)`.
  final List<({String fileId, String fileName})> photos;

  Map<String, Object?> toJson() => {
    'id': id,
    'conversationId': conversationId,
    'content': content,
    'queuedAt': queuedAt.toUtc().toIso8601String(),
    'photos': [
      for (final photo in photos)
        {'fileId': photo.fileId, 'fileName': photo.fileName},
    ],
  };

  static OutboxMessage? fromJson(Object? value) {
    if (value is! Map) return null;
    final id = value['id'];
    final conversationId = value['conversationId'];
    final content = value['content'];
    final queuedAt = DateTime.tryParse('${value['queuedAt']}');
    if (id is! String ||
        conversationId is! String ||
        content is! String ||
        queuedAt == null) {
      return null;
    }
    final photos = value['photos'];
    return OutboxMessage(
      id: id,
      conversationId: conversationId,
      content: content,
      queuedAt: queuedAt,
      photos: [
        if (photos is List)
          for (final photo in photos)
            if (photo is Map && photo['fileId'] is String)
              (
                fileId: photo['fileId'] as String,
                fileName: photo['fileName'] is String
                    ? photo['fileName'] as String
                    : 'Photo',
              ),
      ],
    );
  }
}

/// Messages waiting for a connection, kept on this device in send order.
/// Cleared on sign-out.
class OutboxStore {
  OutboxStore._(this._prefs, this._items);

  OutboxStore.memory() : this._(null, []);

  static const storageKey = 'jarvis.outbox.v1';

  /// Older messages are dropped rather than sent long after the fact.
  static const maxAge = Duration(days: 2);
  static const maxItems = 50;

  final SharedPreferences? _prefs;
  final List<OutboxMessage> _items;

  static Future<OutboxStore> open({DateTime? now}) async {
    try {
      final prefs = await SharedPreferences.getInstance();
      final store = OutboxStore._(prefs, _decode(prefs.getString(storageKey)));
      store._dropExpired(now ?? DateTime.now());
      return store;
    } catch (_) {
      return OutboxStore.memory();
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

  static List<OutboxMessage> _decode(String? raw) {
    if (raw == null || raw.isEmpty) return [];
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! List) return [];
      return decoded
          .map(OutboxMessage.fromJson)
          .whereType<OutboxMessage>()
          .toList();
    } catch (_) {
      return [];
    }
  }

  bool get isEmpty => _items.isEmpty;

  List<OutboxMessage> forConversation(String conversationId) => [
    for (final item in _items)
      if (item.conversationId == conversationId) item,
  ];

  Future<void> add(OutboxMessage message) async {
    _items.add(message);
    while (_items.length > maxItems) {
      _items.removeAt(0);
    }
    await _save();
  }

  Future<void> remove(String id) async {
    final before = _items.length;
    _items.removeWhere((item) => item.id == id);
    if (_items.length != before) await _save();
  }

  Future<void> clear() async {
    _items.clear();
    await _prefs?.remove(storageKey);
  }

  void _dropExpired(DateTime now) {
    final before = _items.length;
    _items.removeWhere((item) => now.difference(item.queuedAt) > maxAge);
    if (_items.length != before) _save();
  }

  Future<void> _save() async {
    final prefs = _prefs;
    if (prefs == null) return;
    try {
      if (_items.isEmpty) {
        await prefs.remove(storageKey);
      } else {
        await prefs.setString(
          storageKey,
          jsonEncode([for (final item in _items) item.toJson()]),
        );
      }
    } catch (_) {
      // The queue still lives in memory for this session.
    }
  }
}
