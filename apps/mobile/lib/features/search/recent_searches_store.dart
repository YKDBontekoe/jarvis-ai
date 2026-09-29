import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

const _storageKey = 'jarvis.recent_searches.v1';
const _maxRecent = 12;

class RecentSearchesStore {
  RecentSearchesStore(this._prefs);

  final SharedPreferences _prefs;

  static Future<RecentSearchesStore> open() async =>
      RecentSearchesStore(await SharedPreferences.getInstance());

  static Future<void> clearAll() async {
    final prefs = await SharedPreferences.getInstance();
    await prefs.remove(_storageKey);
  }

  List<String> read() {
    final raw = _prefs.getString(_storageKey);
    if (raw == null || raw.isEmpty) return [];
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! List) return [];
      return decoded
          .map((item) => item is String ? item.trim() : '')
          .where((item) => item.isNotEmpty)
          .take(_maxRecent)
          .toList();
    } catch (_) {
      return [];
    }
  }

  Future<void> remember(String query) async {
    final trimmed = query.trim();
    if (trimmed.isEmpty) return;
    final next = [trimmed, ...read().where((item) => item != trimmed)].take(_maxRecent).toList();
    await _prefs.setString(_storageKey, jsonEncode(next));
  }

  Future<void> remove(String query) async {
    final next = read().where((item) => item != query).toList();
    await _prefs.setString(_storageKey, jsonEncode(next));
  }
}
