import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart' show deviceTimeZoneLookup;
import '../home/home_context.dart';
import 'home_layout_sync.dart';
import 'tile_actions.dart';
import 'tile_layout.dart';
import 'tile_models.dart';
import 'tile_registry.dart';

/// Owns which tiles are pinned to Home, at what size, and which Home cards are
/// put away. Every change is kept on this device and sent to the account, where
/// Jarvis can read and rearrange it; [sync] brings in changes made elsewhere.
class TileLayoutController extends ChangeNotifier {
  TileLayoutController({TileLayoutStore? store, this.http})
    : _store = store ?? TileLayoutStore.memory(),
      _layout = List.of(defaultTileLayout),
      _ready = true;

  /// A controller that reads this device's saved layout; [ready] turns true
  /// once [open] has finished, so Home does not draw the default layout first.
  /// With [http] it keeps the layout in step with the account.
  TileLayoutController.device({this.http})
    : _store = TileLayoutStore.memory(),
      _layout = List.of(defaultTileLayout),
      _ready = false;

  TileLayoutStore _store;

  /// Reaches the account's copy of Home; null keeps the layout on this device.
  final Dio? http;
  List<TilePlacement> _layout;
  Set<String> _hidden = {};
  List<String> _sections = List.of(HomeSectionIds.defaults);
  bool _ready;

  /// The server revision this device last had, 0 before the first sync.
  int _revision = 0;

  /// A change made here has not reached the server yet.
  bool _dirty = false;

  /// Counts local changes, so a sync that started before one does not undo it.
  int _localChanges = 0;

  /// Bumped on sign-out, so answers for the previous account are dropped.
  int _account = 0;
  bool _syncing = false;
  JarvisHomeChange? _jarvisChange;

  /// The saved layout is loaded, so the grid can be drawn.
  bool get ready => _ready;

  List<TilePlacement> get layout => List.unmodifiable(_layout);

  /// Home cards the owner (or Jarvis, when asked) put away; see [HomeCardIds].
  Set<String> get hiddenCards => Set.unmodifiable(_hidden);

  bool showsCard(String card) => !_hidden.contains(card);

  /// Home's sections top to bottom ([HomeSectionIds]), the tiles among them.
  List<String> get sections => List.unmodifiable(_sections);

  /// A change Jarvis made to Home since this device last looked, so Home can
  /// say so and offer Undo. Null once seen.
  JarvisHomeChange? get jarvisChange => _jarvisChange;

  /// The layout Home is showing, so pages such as an app's detail screen can
  /// offer "Pin to Home". Null until Home opened its layout.
  static TileLayoutController? active;

  /// Opens device storage and shows the saved layout.
  Future<void> open() async {
    active = this;
    _store = await TileLayoutStore.open();
    _layout = List.of(_store.read());
    final meta = _store.readMeta();
    _hidden = {...meta.hiddenCards};
    _sections = HomeSectionIds.normalize(meta.sections);
    _revision = meta.revision;
    _dirty = meta.dirty;
    _ready = true;
    notifyListeners();
  }

  // --- Editing session -------------------------------------------------
  //
  // While Home is being customised every change is kept in memory with an undo
  // history; the layout is saved only when the owner taps Done, and Cancel puts
  // back what Home looked like before.

  ({List<TilePlacement> tiles, Set<String> hidden, List<String> sections})?
  _sessionStart;
  final List<
    ({List<TilePlacement> tiles, Set<String> hidden, List<String> sections})
  >
  _undo = [];

  /// Most steps Undo can go back.
  static const maxUndo = 50;

  bool get editing => _sessionStart != null;
  bool get canUndo => _undo.isNotEmpty;

  ({List<TilePlacement> tiles, Set<String> hidden, List<String> sections})
  get _snapshot => (
    tiles: List.of(_layout),
    hidden: {..._hidden},
    sections: List.of(_sections),
  );

  /// Starts an editing session. Changes are not saved until [commitEdit].
  void beginEdit() {
    if (editing) return;
    _sessionStart = _snapshot;
    _undo.clear();
    notifyListeners();
  }

  /// Saves the edited layout and ends the session.
  void commitEdit() {
    final start = _sessionStart;
    if (start == null) return;
    final changed =
        !_same(start.tiles, _layout) ||
        !setEquals(start.hidden, _hidden) ||
        !listEquals(start.sections, _sections);
    _sessionStart = null;
    _undo.clear();
    if (changed) _save();
    notifyListeners();
  }

  /// Puts back the layout from before the session and ends it.
  void cancelEdit() {
    final start = _sessionStart;
    if (start == null) return;
    _layout = List.of(start.tiles);
    _hidden = {...start.hidden};
    _sections = List.of(start.sections);
    _sessionStart = null;
    _undo.clear();
    notifyListeners();
  }

  /// Takes back the latest move, resize, removal or card switch.
  void undo() {
    if (_undo.isEmpty) return;
    final last = _undo.removeLast();
    _layout = last.tiles;
    _hidden = last.hidden;
    _sections = last.sections;
    notifyListeners();
  }

  static bool _same(List<TilePlacement> a, List<TilePlacement> b) =>
      a.length == b.length &&
      [
        for (var i = 0; i < a.length; i++)
          a[i].id == b[i].id && a[i].size == b[i].size,
      ].every((same) => same);

  /// Records the layout before a change so [undo] can return to it.
  void _remember() {
    if (!editing) return;
    _undo.add(_snapshot);
    if (_undo.length > maxUndo) _undo.removeAt(0);
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
    _remember();
    if (index >= 0) {
      _layout[index] = next;
    } else {
      _layout.add(next);
    }
    _changed();
  }

  void unpin(String id) {
    final before = _layout.length;
    _remember();
    _layout.removeWhere((item) => item.id == id);
    if (_layout.length != before) {
      _changed();
    } else if (editing && _undo.isNotEmpty) {
      _undo.removeLast();
    }
  }

  /// Moves to the next size the tile supports, wrapping around.
  void cycleSize(String id) {
    final index = _layout.indexWhere((item) => item.id == id);
    final spec = tileSpecFor(id);
    if (index < 0 || spec == null || spec.sizes.length < 2) return;
    final at = spec.sizes.indexOf(_layout[index].size);
    final next = spec.sizes[(at + 1) % spec.sizes.length];
    _remember();
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
    _remember();
    _layout.insert(to, _layout.removeAt(from));
    _changed();
  }

  /// Shows or puts away one of the cards above the tiles ([HomeCardIds]).
  void setCardVisible(String card, bool visible) {
    if (!HomeCardIds.all.containsKey(card) || showsCard(card) == visible) {
      return;
    }
    _remember();
    visible ? _hidden.remove(card) : _hidden.add(card);
    _changed();
  }

  /// Moves a section of Home from [from] to [to] in [sections].
  void moveSection(int from, int to) {
    if (from == to ||
        from < 0 ||
        to < 0 ||
        from >= _sections.length ||
        to >= _sections.length) {
      return;
    }
    _remember();
    _sections.insert(to, _sections.removeAt(from));
    _changed();
  }

  void reset() {
    _remember();
    _layout = List.of(defaultTileLayout);
    _hidden = {};
    _sections = List.of(HomeSectionIds.defaults);
    _changed();
  }

  void _changed() {
    // During an editing session nothing is saved until Done.
    if (!editing) _save();
    notifyListeners();
  }

  /// Keeps the layout on this device and sends it to the account.
  void _save() {
    _localChanges++;
    _dirty = http != null;
    unawaited(_store.write(_layout));
    unawaited(_writeMeta());
    if (http != null) unawaited(_push());
  }

  Future<void> _writeMeta() => _store.writeMeta(
    TileLayoutMeta(
      hiddenCards: _hidden,
      revision: _revision,
      dirty: _dirty,
      sections: _sections,
    ),
  );

  // --- Account sync ----------------------------------------------------

  /// Sends this device's layout to the account. Left marked dirty when it
  /// fails, so the next [sync] tries again.
  Future<void> _push() async {
    final http = this.http;
    if (http == null) return;
    final account = _account;
    final changes = _localChanges;
    try {
      final response = await http.put<dynamic>(
        '/api/v1/home/layout',
        data: {
          'tiles': [for (final item in _layout) item.toJson()],
          'hiddenCards': _hidden.toList()..sort(),
          'sections': _sections,
        },
      );
      if (account != _account) return;
      final remote = RemoteHomeLayout.fromJson(response.data);
      if (remote == null) return;
      _revision = remote.revision;
      // A newer change here is still on its way; it clears the flag itself.
      if (changes == _localChanges) _dirty = false;
      await _writeMeta();
    } catch (_) {
      // Offline or an older server: Home keeps working from this device.
    }
  }

  /// Brings in a layout changed on another device or by Jarvis, or sends this
  /// device's own when the account has none yet or a change here never
  /// arrived. Does nothing while Home is being edited.
  Future<void> sync() async {
    final http = this.http;
    if (http == null || !_ready || editing || _syncing) return;
    _syncing = true;
    final account = _account;
    final changes = _localChanges;
    try {
      if (_dirty) {
        await _push();
        return;
      }
      final response = await http.get<dynamic>('/api/v1/home/layout');
      if (account != _account || editing || changes != _localChanges) return;
      final remote = RemoteHomeLayout.fromJson(response.data);
      if (remote == null) return;
      if (!remote.stored) {
        _localChanges++;
        await _push();
        return;
      }
      if (remote.revision == _revision) return;
      final firstSync = _revision == 0;
      _apply(remote);
      // A new device simply takes the account's Home; one that was already in
      // step says what Jarvis changed.
      _jarvisChange = !firstSync && remote.changedBy == homeLayoutByJarvis
          ? JarvisHomeChange(
              revision: remote.revision,
              note: remote.note,
              canUndo: remote.canUndo,
            )
          : null;
      notifyListeners();
    } catch (_) {
      // Offline or an older server: keep what this device has.
    } finally {
      _syncing = false;
    }
  }

  void _apply(RemoteHomeLayout remote) {
    _layout = List.of(remote.tiles.isEmpty ? defaultTileLayout : remote.tiles);
    _hidden = {...remote.hiddenCards};
    _sections = List.of(remote.sections);
    _revision = remote.revision;
    _dirty = false;
    unawaited(_store.write(_layout));
    unawaited(_writeMeta());
  }

  /// The owner saw what Jarvis changed and keeps it.
  void keepJarvisChange() {
    if (_jarvisChange == null) return;
    _jarvisChange = null;
    notifyListeners();
  }

  /// Puts back Home from before Jarvis's change. False when that failed.
  Future<bool> undoJarvisChange() async {
    final http = this.http;
    if (http == null || _jarvisChange == null) return false;
    final account = _account;
    try {
      final response = await http.post<dynamic>('/api/v1/home/layout/undo');
      if (account != _account) return false;
      final remote = RemoteHomeLayout.fromJson(response.data);
      if (remote == null) return false;
      _apply(remote);
      _jarvisChange = null;
      notifyListeners();
      return true;
    } catch (_) {
      return false;
    }
  }

  /// Forgets the layout on sign-out without touching the account's copy.
  void forgetAccount() {
    _account++;
    _sessionStart = null;
    _undo.clear();
    _layout = List.of(defaultTileLayout);
    _hidden = {};
    _sections = List.of(HomeSectionIds.defaults);
    _revision = 0;
    _dirty = false;
    _jarvisChange = null;
    unawaited(_store.clear());
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

  Future<HomeContext?>? _context;

  /// The last Home context that loaded.
  HomeContext? lastContext;

  /// `/home/context`, shared until [invalidate]. Null on an older server.
  Future<HomeContext?> context() => _context ??= _fetchContext();

  Future<HomeContext?> _fetchContext() async {
    final revision = _accountRevision;
    try {
      final response = await http.get<dynamic>('/api/v1/home/context');
      if (revision != _accountRevision) return null;
      return lastContext = HomeContext.fromJson(response.data);
    } catch (_) {
      return revision == _accountRevision ? lastContext : null;
    }
  }

  /// Puts a Home card away on the server; [mute] hides its whole kind.
  Future<void> dismiss(String id, String kind, {bool mute = false}) async {
    try {
      await http.post<dynamic>(
        '/api/v1/home/context/dismiss',
        data: {'id': id, 'kind': kind, 'mute': mute},
      );
    } catch (_) {
      // Hidden here for now; the next load brings it back if the server missed it.
    }
  }

  /// The next [briefing] call fetches again.
  void invalidate() {
    _briefing = null;
    _preferredName = null;
    _context = null;
  }

  /// Forgets everything, for a different account.
  void reset() {
    _accountRevision++;
    _briefing = null;
    lastBriefing = null;
    _context = null;
    lastContext = null;
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
