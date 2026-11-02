import 'package:flutter/foundation.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../tiles/tile_models.dart';
import '../tiles/tile_registry.dart';

/// The last few features opened, newest first, kept on this device only. Opening
/// the same feature again moves it to the front instead of adding a copy.
class RecentFeatures extends ChangeNotifier {
  RecentFeatures({SharedPreferences? preferences}) : _prefs = preferences;

  static final instance = RecentFeatures();
  static const storageKey = 'jarvis.recent_features.v1';
  static const maxItems = 6;

  SharedPreferences? _prefs;
  List<String> _ids = const [];
  bool _loaded = false;

  /// Feature ids, newest first.
  List<String> get ids => _ids;

  /// The recent features that still exist.
  List<TileSpec> get specs => [for (final id in _ids) ?tileSpecFor(id)];

  Future<SharedPreferences?> _store() async {
    try {
      return _prefs ??= await SharedPreferences.getInstance();
    } catch (_) {
      return null;
    }
  }

  Future<void> load() async {
    if (_loaded) return;
    _loaded = true;
    final stored = (await _store())?.getStringList(storageKey);
    if (stored != null) {
      // Anything opened before the saved list finished loading stays in front.
      _ids = _merge(_ids, stored);
      notifyListeners();
    }
  }

  List<String> _merge(List<String> front, List<String> rest) {
    final seen = <String>{};
    return [
      for (final id in [...front, ...rest])
        if (seen.add(id)) id,
    ].take(maxItems).toList();
  }

  /// Notes that the feature behind [destination] was opened. Destinations that
  /// are not a feature (a single record, a settings page) are ignored.
  Future<void> recordDestination(String destination) async {
    TileSpec? spec;
    for (final candidate in tileSpecs) {
      if (candidate.destination == destination) {
        spec = candidate;
        break;
      }
    }
    if (spec == null) return;
    _ids = _merge([spec.id], _ids);
    notifyListeners();
    await (await _store())?.setStringList(storageKey, _ids);
  }

  Future<void> clear() async {
    _ids = const [];
    notifyListeners();
    await (await _store())?.remove(storageKey);
  }

  /// Forgets the list on this device (sign-out) without needing an instance.
  static Future<void> clearAll() async {
    instance._ids = const [];
    instance.notifyListeners();
    try {
      await (await SharedPreferences.getInstance()).remove(storageKey);
    } catch (_) {
      // Nothing was stored when device storage is unavailable.
    }
  }
}
