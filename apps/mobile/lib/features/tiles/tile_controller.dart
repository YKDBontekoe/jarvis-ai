import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart' show deviceTimeZoneLookup;
import 'tile_actions.dart';
import 'tile_layout.dart';
import 'tile_models.dart';
import 'tile_registry.dart';

/// Owns which tiles are pinned to Home and at what size, and saves every change
/// on this device.
class TileLayoutController extends ChangeNotifier {
  TileLayoutController({TileLayoutStore? store})
    : _store = store ?? TileLayoutStore.memory(),
      _layout = List.of(defaultTileLayout),
      _ready = true;

  /// A controller that reads this device's saved layout; [ready] turns true
  /// once [open] has finished, so Home does not draw the default layout first.
  TileLayoutController.device()
    : _store = TileLayoutStore.memory(),
      _layout = List.of(defaultTileLayout),
      _ready = false;

  TileLayoutStore _store;
  List<TilePlacement> _layout;
  bool _ready;

  /// The saved layout is loaded, so the grid can be drawn.
  bool get ready => _ready;

  List<TilePlacement> get layout => List.unmodifiable(_layout);

  /// Opens device storage and shows the saved layout.
  Future<void> open() async {
    _store = await TileLayoutStore.open();
    _layout = List.of(_store.read());
    _ready = true;
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
  TileDataSource(this.http, {this.clock, this.ensureMorningBriefing = false});

  final Dio http;

  /// Whether the first successful load also asks the server to turn the
  /// morning briefing on for an owner who never configured it. Off by default
  /// so tile tests do not touch the platform time zone.
  final bool ensureMorningBriefing;

  /// Replaces the wall clock in tests.
  final DateTime Function()? clock;
  Future<Map<String, dynamic>?>? _briefing;
  Future<String?>? _preferredName;
  int _accountRevision = 0;

  /// Owner's Persona name, retained when returning to Home or while offline.
  String? lastPreferredName;

  Future<String?> preferredName() => _preferredName ??= _fetchPreferredName();

  Future<String?> _fetchPreferredName() async {
    final revision = _accountRevision;
    try {
      final response = await http.get<dynamic>('/api/v1/persona');
      if (revision != _accountRevision) return null;
      final name = asJsonString(jsonObject(response.data)?['preferredName']);
      return lastPreferredName = name?.trim();
    } catch (_) {
      return revision == _accountRevision ? lastPreferredName : null;
    }
  }

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
      if (ensureMorningBriefing) unawaited(_ensureMorningBriefing());
      return lastBriefing = data is Map
          ? Map<String, dynamic>.from(data)
          : null;
    } on DioException {
      return null;
    } catch (_) {
      return null;
    }
  }

  int _briefingEnsuredFor = -1;

  /// Once per account, tells the server this device's time zone so a first-time
  /// owner gets the morning briefing at 08:00 without visiting Settings. The
  /// server leaves any saved briefing settings, including "off", untouched.
  Future<void> _ensureMorningBriefing() async {
    if (_briefingEnsuredFor == _accountRevision) return;
    _briefingEnsuredFor = _accountRevision;
    try {
      final zone = await deviceTimeZoneLookup();
      if (zone == null) return;
      await http.post<dynamic>(
        '/api/v1/briefings/daily/default',
        data: {'timeZoneId': zone},
      );
    } catch (_) {
      // The briefing stays as it was; the next account session tries again.
    }
  }

  /// The next [briefing] call fetches again.
  void invalidate() {
    _briefing = null;
    _preferredName = null;
  }

  /// Forgets everything, for a different account.
  void reset() {
    _accountRevision++;
    _briefing = null;
    lastBriefing = null;
    _preferredName = null;
    lastPreferredName = null;
    cache.clear();
  }

  TileEnv env() => TileEnv(http: http, briefing: briefing, now: clock?.call());

  /// Runs a quick action from a tile; see [runTileAction].
  Future<TileActionOutcome> act(
    String tileId,
    String actionId,
    String? itemId,
  ) => runTileAction(http, tileId, actionId, itemId);

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
