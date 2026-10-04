import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import 'tile_layout.dart';
import 'tile_models.dart';
import 'tile_registry.dart';

/// Owns which tiles are pinned to Home and at what size, and saves every change
/// on this device.
class TileLayoutController extends ChangeNotifier {
  TileLayoutController({TileLayoutStore? store})
    : _store = store ?? TileLayoutStore.memory(),
      _layout = List.of(defaultTileLayout);

  TileLayoutStore _store;
  List<TilePlacement> _layout;

  List<TilePlacement> get layout => List.unmodifiable(_layout);

  /// Opens device storage and shows the saved layout.
  Future<void> open() async {
    _store = await TileLayoutStore.open();
    _layout = List.of(_store.read());
    notifyListeners();
  }

  bool contains(String id) => _layout.any((item) => item.id == id);

  TileSize? sizeOf(String id) {
    for (final item in _layout) {
      if (item.id == id) return item.size;
    }
    return null;
  }

  void pin(String id, TileSize size) {
    final spec = tileSpecFor(id);
    if (spec == null) return;
    final next = TilePlacement(id, spec.nearest(size));
    final index = _layout.indexWhere((item) => item.id == id);
    if (index >= 0) {
      _layout[index] = next;
    } else {
      _layout.add(next);
    }
    _changed();
  }

  void unpin(String id) {
    final before = _layout.length;
    _layout.removeWhere((item) => item.id == id);
    if (_layout.length != before) _changed();
  }

  /// Moves to the next size the tile supports, wrapping around.
  void cycleSize(String id) {
    final index = _layout.indexWhere((item) => item.id == id);
    final spec = tileSpecFor(id);
    if (index < 0 || spec == null || spec.sizes.length < 2) return;
    final at = spec.sizes.indexOf(_layout[index].size);
    final next = spec.sizes[(at + 1) % spec.sizes.length];
    _layout[index] = _layout[index].withSize(next);
    _changed();
  }

  void move(int from, int to) {
    if (from == to ||
        from < 0 ||
        to < 0 ||
        from >= _layout.length ||
        to >= _layout.length) {
      return;
    }
    _layout.insert(to, _layout.removeAt(from));
    _changed();
  }

  void reset() {
    _layout = List.of(defaultTileLayout);
    _changed();
  }

  void _changed() {
    unawaited(_store.write(_layout));
    notifyListeners();
  }
}

/// Loads what tiles show. Tiles that read the home briefing share one request
/// until [invalidate] is called.
class TileDataSource {
  TileDataSource(this.http, {this.clock});

  final Dio http;

  /// Replaces the wall clock in tests.
  final DateTime Function()? clock;
  Future<Map<String, dynamic>?>? _briefing;

  /// The last data each tile loaded, so Home shows something at once when it
  /// is opened again.
  final Map<String, TileData?> cache = {};

  /// The last briefing that loaded.
  Map<String, dynamic>? lastBriefing;

  Future<Map<String, dynamic>?> briefing() => _briefing ??= _fetchBriefing();

  Future<Map<String, dynamic>?> _fetchBriefing() async {
    try {
      final response = await http.get<dynamic>('/api/v1/home');
      final data = response.data;
      return lastBriefing = data is Map
          ? Map<String, dynamic>.from(data)
          : null;
    } on DioException {
      return null;
    } catch (_) {
      return null;
    }
  }

  /// The next [briefing] call fetches again.
  void invalidate() => _briefing = null;

  /// Forgets everything, for a different account.
  void reset() {
    _briefing = null;
    lastBriefing = null;
    cache.clear();
  }

  TileEnv env() => TileEnv(http: http, briefing: briefing, now: clock?.call());

  /// What [spec] shows now, or null when it has no data or the call failed.
  Future<TileData?> load(TileSpec spec) async {
    final loader = spec.load;
    if (loader == null) return null;
    try {
      final data = await loader(env());
      cache[spec.id] = data;
      return data;
    } catch (_) {
      return null;
    }
  }
}
