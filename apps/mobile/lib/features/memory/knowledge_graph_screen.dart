import 'dart:async';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'graph_layout.dart';
import 'graph_model.dart';

export 'graph_model.dart' show humanPredicate;

part 'knowledge_graph_map.dart';
part 'graph_entity_screen.dart';
part 'knowledge_graph_viewport.dart';
part 'knowledge_graph_sheet.dart';

const _sheetPeek = 0.34;
const _minScale = 0.12;
const _maxScale = 3.2;

/// Highlights only help once a graph is too big to take in at a glance.
const _highlightsMinimum = 6;

/// Categorical colours for entity types. The app's semantic colours are deliberately close to the indigo accent,
/// which makes a map of eight types unreadable, so the graph gets its own palette tuned per brightness.
Color graphTypeColor(String? type, [BuildContext? context]) {
  final dark = context != null && JarvisColors.of(context).isDark;
  return switch (type) {
    'person' => dark ? const Color(0xff9a91ff) : const Color(0xff5b4ef0),
    'place' => dark ? const Color(0xff34d399) : const Color(0xff0f9d76),
    'organization' => dark ? const Color(0xff60a5fa) : const Color(0xff2563eb),
    'project' => dark ? const Color(0xfffbbf24) : const Color(0xffd97706),
    'event' => dark ? const Color(0xfff472b6) : const Color(0xffdb2777),
    'pet' => dark ? const Color(0xfffb8c6a) : const Color(0xffe0573a),
    'topic' => dark ? const Color(0xff22d3ee) : const Color(0xff0891b2),
    _ => dark ? const Color(0xff9ca3af) : const Color(0xff6b7280),
  };
}

IconData graphTypeIcon(String? type) => switch (type) {
  'person' => PhosphorIconsRegular.user,
  'place' => PhosphorIconsRegular.mapPin,
  'organization' => PhosphorIconsRegular.users,
  'project' => PhosphorIconsRegular.rocketLaunch,
  'event' => PhosphorIconsRegular.calendarBlank,
  'pet' => PhosphorIconsRegular.heart,
  'topic' => PhosphorIconsRegular.lightbulb,
  _ => PhosphorIconsRegular.puzzlePiece,
};

/// Temporal knowledge graph as a pan-and-zoom map of people, places, and facts.
class KnowledgeGraphScreen extends StatefulWidget {
  const KnowledgeGraphScreen({required this.http, super.key});

  final Dio http;

  @override
  State<KnowledgeGraphScreen> createState() => _KnowledgeGraphScreenState();
}

/// Holds graph fields so map and sheet mixins can share state.
abstract class _KnowledgeGraphController extends State<KnowledgeGraphScreen>
    with TickerProviderStateMixin {
  /// Everything loaded; [_snapshot] is what the map shows at [_asOf].
  GraphSnapshot _all = GraphSnapshot.empty;
  GraphSnapshot _snapshot = GraphSnapshot.empty;
  Map<String, Offset> _layout = const {};
  List<GraphCluster> _clusters = const [];
  Map<String, int> _degrees = const {};
  Map<String, DateTime> _firstSeen = const {};
  int _maxDegree = 1;
  ({DateTime first, DateTime last})? _range;
  DateTime? _asOf;
  bool _timeTravel = false;
  late final AnimationController _pulse;
  late final AnimationController _replay;
  Map<String, dynamic>? _status;
  final _query = TextEditingController();
  final _transform = TransformationController();
  late final AnimationController _motion;
  VoidCallback? _motionTick;
  bool _loading = true;
  String? _error;
  String? _selectedId;
  Set<String>? _types;
  bool _fitted = false;
  bool _fitQueued = false;
  Size _viewport = Size.zero;
  double _covered = 0;
  double _world = 1200;
  int _reveal = 0;
  int _revealed = 0;
  int _requestRevision = 0;

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final responses = await Future.wait([
        widget.http.get<dynamic>(
          '/api/v1/graph/overview',
          queryParameters: const {'limit': 120},
        ),
        widget.http.get<dynamic>('/api/v1/memory/index-status'),
      ]);
      final snapshot = parseGraphOverview(
        jsonObject(responses[0].data) ?? const {},
      );
      final degrees = graphDegrees(snapshot);
      final yous = snapshot.nodes.where((node) => node.isYou).toList()
        ..sort((a, b) => degrees[b.id]!.compareTo(degrees[a.id]!));
      final ids = [for (final node in snapshot.nodes) node.id];
      final pairs = [
        for (final link in snapshot.links) (link.fromId, link.toId),
      ];
      final communities = graphCommunities(
        ids,
        pairs,
        hubs: {for (final node in yous) node.id},
      );
      if (!mounted || revision != _requestRevision) return;
      _replay.stop();
      setState(() {
        _all = snapshot;
        _clusters = graphClusters(communities);
        _firstSeen = nodeFirstSeen(snapshot);
        _range = graphTimeRange(snapshot);
        if (_range == null) _timeTravel = false;
        _layout = layoutGraph(
          ids,
          pairs,
          pinned: yous.firstOrNull?.id,
          groups: communities,
        );
        _applyTime(_timeTravel ? _asOf : null);
        _status = jsonObject(responses[1].data);
        _world = _worldFor(snapshot.nodes.length);
        _loading = false;
        _error = null;
        _fitted = false;
        if (_snapshot.node(_selectedId) == null) _selectedId = null;
        final selected = _snapshot.node(_selectedId);
        if (selected != null && !_passesType(selected)) _selectedId = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load the knowledge graph.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load the knowledge graph.';
      });
    }
  }

  /// Shows the graph as known at [time]; null shows everything.
  void _applyTime(DateTime? time) {
    _asOf = time;
    _snapshot = time == null ? _all : snapshotAsOf(_all, time);
    _degrees = graphDegrees(_snapshot);
    _maxDegree = math.max(
      1,
      _degrees.entries
          .where((entry) => !(_snapshot.node(entry.key)?.isYou ?? false))
          .fold<int>(1, (top, entry) => math.max(top, entry.value)),
    );
    if (_snapshot.node(_selectedId) == null) _selectedId = null;
  }

  void _setTimeTravel(bool on) {
    _replay.stop();
    setState(() {
      _timeTravel = on && _range != null;
      _applyTime(_timeTravel ? _range!.last : null);
    });
  }

  void _scrubTo(double fraction) {
    final range = _range;
    if (range == null) return;
    final span = range.last.difference(range.first).inMilliseconds;
    setState(
      () => _applyTime(
        range.first.add(Duration(milliseconds: (span * fraction).round())),
      ),
    );
  }

  double get _timeFraction {
    final range = _range;
    final asOf = _asOf;
    if (range == null || asOf == null) return 1;
    final span = range.last.difference(range.first).inMilliseconds;
    if (span <= 0) return 1;
    return (asOf.difference(range.first).inMilliseconds / span).clamp(0.0, 1.0);
  }

  /// Plays the graph growing from its first fact to today.
  void _toggleReplay() {
    if (_replay.isAnimating) {
      _replay.stop();
      setState(() {});
      return;
    }
    if (!_timeTravel) _setTimeTravel(true);
    final from = _timeFraction >= .999 ? 0.0 : _timeFraction;
    unawaited(
      _replay.forward(from: from).whenComplete(() {
        if (mounted) setState(() {});
      }),
    );
  }

  /// Node diameter in screen pixels: hubs read bigger, the user's own node biggest.
  /// Grows a little as you zoom in so the overview stays uncluttered and close-ups get room for icons.
  double _nodeDiameter(GraphNode node) {
    final zoom = math
        .sqrt(_transform.value.getMaxScaleOnAxis() / .6)
        .clamp(.72, 1.25);
    if (node.isYou) return 30 * zoom;
    final degree = _degrees[node.id] ?? 0;
    final weight = math.sqrt(degree / _maxDegree).clamp(0.0, 1.0);
    return (11 + weight * 15) * zoom;
  }

  bool _isRecent(GraphNode node) {
    final seen = _firstSeen[node.id];
    final reference = _asOf ?? DateTime.now();
    if (seen == null || seen.isAfter(reference)) return false;
    return reference.difference(seen) <= const Duration(days: 7);
  }

  double _worldFor(int count) {
    final n = math.max(count, 1);
    return (860 + math.sqrt(n) * 120).clamp(980, 1760).toDouble();
  }

  bool _passesType(GraphNode node) =>
      _types == null || _types!.contains(node.type);

  List<GraphNode> get _typedNodes => sortedGraphNodes(
    _snapshot.nodes.where(_passesType).toList(growable: false),
  );

  List<GraphNode> get _listedNodes => _typedNodes
      .where((node) => nodeMatchesQuery(node, _snapshot, _query.text))
      .toList(growable: false);

  Offset _worldPoint(String id) {
    final point = _layout[id] ?? const Offset(.5, .5);
    const pad = 60.0;
    final span = math.max(_world - pad * 2, 1.0);
    return Offset(pad + point.dx * span, pad + point.dy * span);
  }

  Matrix4 _placed(double scale, double dx, double dy) {
    final matrix = Matrix4.identity();
    matrix.storage[0] = scale;
    matrix.storage[5] = scale;
    matrix.storage[12] = dx;
    matrix.storage[13] = dy;
    return matrix;
  }

  Matrix4 _fitMatrix(Size viewport, double covered) {
    if (viewport.isEmpty || _world <= 0) return Matrix4.identity();
    final visibleHeight = math.max(140.0, viewport.height - covered - 8);
    final scale =
        math.min(viewport.width / _world, visibleHeight / _world) * .96;
    final clamped = scale.clamp(_minScale, _maxScale);
    final dx = (viewport.width - _world * clamped) / 2;
    final dy = math.max(8.0, (visibleHeight - _world * clamped) / 2);
    return _placed(clamped, dx, dy);
  }

  void _scheduleFit(Size viewport, double covered) {
    _viewport = viewport;
    _covered = covered;
    if (_fitted || _fitQueued || viewport.isEmpty || _all.nodes.isEmpty) {
      return;
    }
    _fitQueued = true;
    WidgetsBinding.instance.addPostFrameCallback((_) {
      _fitQueued = false;
      if (!mounted || _fitted || _viewport.isEmpty) return;
      _fitted = true;
      _transform.value = _fitMatrix(_viewport, _covered);
      setState(() {});
    });
  }

  void _animateTo(Matrix4 target) {
    final tween = Matrix4Tween(begin: _transform.value.clone(), end: target);
    void tick() => _transform.value = tween.transform(_motion.value);
    if (_motionTick != null) _motion.removeListener(_motionTick!);
    _motionTick = tick;
    _motion.addListener(tick);
    _motion.forward(from: 0);
  }

  void _zoomBy(double factor) {
    if (_viewport.isEmpty) return;
    final current = _transform.value.getMaxScaleOnAxis();
    final next = (current * factor).clamp(_minScale, _maxScale);
    if ((next - current).abs() < 0.01) return;
    final focal = Offset(_viewport.width / 2, _viewport.height * .32);
    final scene = MatrixUtils.transformPoint(
      Matrix4.inverted(_transform.value),
      focal,
    );
    _animateTo(
      _placed(next, focal.dx - scene.dx * next, focal.dy - scene.dy * next),
    );
  }

  void _resetView() {
    if (_viewport.isEmpty) return;
    _animateTo(_fitMatrix(_viewport, _covered));
  }

  void _focusOn(String id) {
    if (_viewport.isEmpty) return;
    final scene = _worldPoint(id);
    final current = _transform.value.getMaxScaleOnAxis();
    final next = math.max(current, math.min(1.15, _maxScale));
    final focal = Offset(_viewport.width / 2, _viewport.height * .3);
    _animateTo(
      _placed(next, focal.dx - scene.dx * next, focal.dy - scene.dy * next),
    );
  }

  void _select(String id, {bool toggle = true}) {
    if (_snapshot.node(id) == null) return;
    final next = toggle && _selectedId == id ? null : id;
    setState(() {
      _selectedId = next;
      if (next != null) _reveal++;
    });
    if (next != null) {
      _focusOn(next);
      if (!MediaQuery.disableAnimationsOf(context)) {
        unawaited(_pulse.forward(from: 0));
      }
    }
  }

  void _clearSelection() {
    if (_selectedId == null) return;
    setState(() => _selectedId = null);
  }

  void _toggleType(String type) {
    setState(() {
      if (_types == null) {
        _types = {type};
      } else if (_types!.contains(type)) {
        final next = {..._types!}..remove(type);
        _types = next.isEmpty ? null : next;
      } else {
        _types = {..._types!, type};
      }
      final selected = _snapshot.node(_selectedId);
      if (selected != null && !_passesType(selected)) _selectedId = null;
    });
  }

  void _submitSearch(String value) {
    final matches = _listedNodes;
    if (matches.isEmpty) return;
    _select(matches.first.id, toggle: false);
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => GraphEntityScreen(http: widget.http, entityId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  /// Hop distance from the selection, cached until the selection or visible graph changes.
  Map<String, int> get _hops {
    final selected = _selectedId;
    if (selected == null) return const {};
    if (_hopsFor != selected || !identical(_hopsSnapshot, _snapshot)) {
      _hopsFor = selected;
      _hopsSnapshot = _snapshot;
      _hopsCache = graphNeighborhood(
        selected,
        _snapshot.links,
        barriers: {
          for (final node in _snapshot.nodes)
            if (node.isYou) node.id,
        },
      );
    }
    return _hopsCache;
  }

  String? _hopsFor;
  GraphSnapshot? _hopsSnapshot;
  Map<String, int> _hopsCache = const {};
}

class _KnowledgeGraphScreenState extends _KnowledgeGraphController
    with _KnowledgeGraphViewport, _KnowledgeGraphSheet {
  @override
  void initState() {
    super.initState();
    _motion = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 280),
    );
    _pulse = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 2600),
      value: 1,
    );
    _replay =
        AnimationController(vsync: this, duration: const Duration(seconds: 7))
          ..addListener(() {
            if (_replay.isAnimating) _scrubTo(_replay.value);
          });
    unawaited(_load());
  }

  @override
  void dispose() {
    if (_motionTick != null) _motion.removeListener(_motionTick!);
    _motion.dispose();
    _pulse.dispose();
    _replay.dispose();
    _transform.dispose();
    _query.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Knowledge graph'),
      actions: [
        IconButton(
          tooltip: 'Refresh',
          onPressed: () => unawaited(_load()),
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _all.nodes.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: const EmptyState(
        icon: PhosphorIconsRegular.graph,
        title: 'No connections yet',
        message:
            'As you tell Jarvis about people, places, and projects, it links them here and keeps track of how facts change over time.',
      ),
      child: _body(),
    ),
  );

  Widget _body() => LayoutBuilder(
    builder: (context, constraints) {
      final covered = constraints.maxHeight * _sheetPeek;
      return Stack(
        clipBehavior: Clip.none,
        children: [
          Column(
            children: [
              _header(),
              Expanded(child: _map(covered)),
            ],
          ),
          _sheet(),
        ],
      );
    },
  );

  Widget _header() {
    final types = graphTypesPresent(_snapshot.nodes);
    final stats = graphStatsLabel(
      entities: _snapshot.nodes.length,
      links: _snapshot.links.length,
      details: _snapshot.literals.length,
    );
    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 6),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          _statusRow(),
          const SizedBox(height: 10),
          TextField(
            key: const Key('graph-search'),
            controller: _query,
            textInputAction: TextInputAction.search,
            onChanged: (_) => setState(() {}),
            onSubmitted: _submitSearch,
            decoration: InputDecoration(
              hintText: 'Search people, places, facts',
              isDense: true,
              prefixIcon: const Icon(PhosphorIconsRegular.magnifyingGlass),
              suffixIcon: _query.text.isEmpty
                  ? null
                  : IconButton(
                      tooltip: 'Clear search',
                      onPressed: () {
                        _query.clear();
                        setState(() {});
                      },
                      icon: const Icon(PhosphorIconsRegular.x, size: 18),
                    ),
            ),
          ),
          const SizedBox(height: 8),
          SizedBox(
            height: 36,
            child: ListView(
              scrollDirection: Axis.horizontal,
              children: [
                _typeChip(null, 'All', _snapshot.nodes.length, _types == null),
                for (final type in types)
                  _typeChip(
                    type,
                    graphTypeLabel(type),
                    _snapshot.nodes.where((node) => node.type == type).length,
                    _types?.contains(type) ?? false,
                  ),
              ],
            ),
          ),
          const SizedBox(height: 4),
          Row(
            children: [
              Expanded(
                child: Text(
                  _range != null
                      ? stats
                      : '$stats · drag to pan, pinch to zoom',
                  key: const Key('graph-stats'),
                  style: TextStyle(
                    fontSize: 12,
                    color: JarvisColors.of(context).muted,
                  ),
                ),
              ),
              if (_range != null)
                TextButton.icon(
                  key: const Key('graph-time-travel'),
                  onPressed: () => _setTimeTravel(!_timeTravel),
                  style: TextButton.styleFrom(
                    visualDensity: VisualDensity.compact,
                    padding: const EdgeInsets.symmetric(horizontal: 8),
                    foregroundColor: _timeTravel
                        ? JarvisColors.of(context).accent
                        : JarvisColors.of(context).inkSoft,
                  ),
                  icon: const Icon(
                    PhosphorIconsRegular.clockCounterClockwise,
                    size: 16,
                  ),
                  label: Text(
                    _timeTravel ? 'Now' : 'Time travel',
                    style: const TextStyle(fontSize: 12.5),
                  ),
                ),
            ],
          ),
          if (_timeTravel && _range != null) _timeline(_range!),
        ],
      ),
    );
  }

  Widget _timeline(({DateTime first, DateTime last}) range) {
    final colors = JarvisColors.of(context);
    final asOf = (_asOf ?? range.last).toLocal();
    final playing = _replay.isAnimating;
    return Row(
      key: const Key('graph-timeline'),
      children: [
        IconButton(
          key: const Key('graph-replay'),
          tooltip: playing ? 'Pause' : 'Replay how the graph grew',
          visualDensity: VisualDensity.compact,
          onPressed: _toggleReplay,
          icon: Icon(
            playing
                ? PhosphorIconsRegular.pauseCircle
                : PhosphorIconsRegular.play,
            size: 20,
            color: colors.accent,
          ),
        ),
        Expanded(
          child: SliderTheme(
            data: SliderTheme.of(context).copyWith(
              trackHeight: 3,
              overlayShape: SliderComponentShape.noOverlay,
              thumbShape: const RoundSliderThumbShape(enabledThumbRadius: 7),
            ),
            child: Slider(
              key: const Key('graph-time-slider'),
              value: _timeFraction,
              activeColor: colors.accent,
              inactiveColor: colors.outline,
              onChangeStart: (_) => _replay.stop(),
              onChanged: _scrubTo,
            ),
          ),
        ),
        const SizedBox(width: 10),
        SizedBox(
          width: 84,
          child: Text(
            formatGraphDay(asOf),
            textAlign: TextAlign.right,
            style: TextStyle(
              fontSize: 12,
              fontWeight: FontWeight.w600,
              color: colors.ink,
              fontFeatures: const [FontFeature.tabularFigures()],
            ),
          ),
        ),
      ],
    );
  }

  Widget _typeChip(String? type, String label, int count, bool selected) {
    final color = type == null
        ? JarvisColors.of(context).ink
        : graphTypeColor(type, context);
    return Padding(
      padding: const EdgeInsets.only(right: 8),
      child: Material(
        color: selected
            ? color.withValues(alpha: .12)
            : JarvisColors.of(context).surface,
        borderRadius: BorderRadius.circular(999),
        child: InkWell(
          key: Key('type-filter-${type ?? 'all'}'),
          borderRadius: BorderRadius.circular(999),
          onTap: () {
            if (type == null) {
              setState(() => _types = null);
            } else {
              _toggleType(type);
            }
          },
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(999),
              border: Border.all(
                color: selected ? color : JarvisColors.of(context).outline,
              ),
            ),
            child: Row(
              children: [
                Container(
                  width: 7,
                  height: 7,
                  decoration: BoxDecoration(
                    color: color,
                    shape: BoxShape.circle,
                  ),
                ),
                const SizedBox(width: 6),
                Text(
                  '$label $count',
                  style: const TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w600,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _statusRow() {
    final status = _status ?? const {};
    final active = asJsonInt(status['active']);
    final embedded = asJsonInt(status['embedded']);
    final model = asJsonString(status['embeddingModel']);
    final semantic = model != null && embedded > 0;
    return Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        StatusPill(
          label: semantic
              ? 'Semantic search on · $embedded/$active memories'
              : 'Keyword search only — add an embedding model in Models',
          color: semantic
              ? JarvisColors.of(context).success
              : JarvisColors.of(context).warning,
        ),
        StatusPill(
          label: '${asJsonInt(status['graphIndexed'])}/$active memories linked',
          color: JarvisColors.of(context).accent,
        ),
      ],
    );
  }
}
