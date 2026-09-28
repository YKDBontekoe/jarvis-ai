import 'dart:async';
import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'graph_layout.dart';
import 'graph_model.dart';

export 'graph_model.dart' show humanPredicate;

const _typeColors = {
  'person': JarvisColors.accent,
  'place': JarvisColors.success,
  'organization': JarvisColors.info,
  'project': JarvisColors.warning,
  'event': JarvisColors.rose,
  'pet': JarvisColors.violet,
  'topic': JarvisColors.sky,
  'thing': JarvisColors.inkSoft,
};

const _sheetPeek = 0.34;
const _minScale = 0.12;
const _maxScale = 3.2;

Color graphTypeColor(String? type) => _typeColors[type] ?? JarvisColors.inkSoft;

/// Temporal knowledge graph as a pan-and-zoom map of people, places, and facts.
class KnowledgeGraphScreen extends StatefulWidget {
  const KnowledgeGraphScreen({required this.http, super.key});

  final Dio http;

  @override
  State<KnowledgeGraphScreen> createState() => _KnowledgeGraphScreenState();
}

class _KnowledgeGraphScreenState extends State<KnowledgeGraphScreen>
    with TickerProviderStateMixin {
  GraphSnapshot _snapshot = GraphSnapshot.empty;
  Map<String, Offset> _layout = const {};
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

  @override
  void initState() {
    super.initState();
    _motion = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 280),
    );
    unawaited(_load());
  }

  @override
  void dispose() {
    if (_motionTick != null) _motion.removeListener(_motionTick!);
    _motion.dispose();
    _transform.dispose();
    _query.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final responses = await Future.wait([
        widget.http.get<Map<String, dynamic>>(
          '/api/v1/graph/overview',
          queryParameters: const {'limit': 120},
        ),
        widget.http.get<Map<String, dynamic>>('/api/v1/memory/index-status'),
      ]);
      final snapshot = parseGraphOverview(responses[0].data ?? const {});
      final you = snapshot.nodes
          .where((node) => node.isYou)
          .map((node) => node.id)
          .firstOrNull;
      if (!mounted) return;
      setState(() {
        _snapshot = snapshot;
        _layout = layoutGraph(
          [for (final node in snapshot.nodes) node.id],
          [for (final link in snapshot.links) (link.fromId, link.toId)],
          pinned: you,
        );
        _status = responses[1].data;
        _world = _worldFor(snapshot.nodes.length);
        _loading = false;
        _error = null;
        _fitted = false;
        if (_snapshot.node(_selectedId) == null) _selectedId = null;
        final selected = _snapshot.node(_selectedId);
        if (selected != null && !_passesType(selected)) _selectedId = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load the knowledge graph.';
      });
    }
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
    const pad = 80.0;
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
        math.min(viewport.width / _world, visibleHeight / _world) * .92;
    final clamped = scale.clamp(_minScale, _maxScale);
    final dx = (viewport.width - _world * clamped) / 2;
    final dy = math.max(8.0, (visibleHeight - _world * clamped) / 2);
    return _placed(clamped, dx, dy);
  }

  void _scheduleFit(Size viewport, double covered) {
    _viewport = viewport;
    _covered = covered;
    if (_fitted || _fitQueued || viewport.isEmpty || _snapshot.nodes.isEmpty) {
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
    if (next != null) _focusOn(next);
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

  bool _linkedToSelected(String id) {
    final selected = _selectedId;
    if (selected == null || selected == id) return true;
    for (final link in _snapshot.links) {
      final touches =
          (link.fromId == selected && link.toId == id) ||
          (link.toId == selected && link.fromId == id);
      if (touches) return true;
    }
    return false;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Knowledge graph'),
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
      isEmpty: _snapshot.nodes.isEmpty,
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
          const SizedBox(height: 6),
          Text(
            '$stats · drag to pan, pinch to zoom',
            style: const TextStyle(fontSize: 12, color: JarvisColors.muted),
          ),
        ],
      ),
    );
  }

  Widget _typeChip(String? type, String label, int count, bool selected) {
    final color = type == null ? JarvisColors.ink : graphTypeColor(type);
    return Padding(
      padding: const EdgeInsets.only(right: 8),
      child: Material(
        color: selected ? color.withValues(alpha: .12) : JarvisColors.surface,
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
                color: selected ? color : JarvisColors.outline,
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
          color: semantic ? JarvisColors.success : JarvisColors.warning,
        ),
        StatusPill(
          label: '${asJsonInt(status['graphIndexed'])}/$active memories linked',
          color: JarvisColors.accent,
        ),
      ],
    );
  }

  Widget _map(double coveredBySheet) => LayoutBuilder(
    builder: (context, constraints) {
      final viewport = Size(constraints.maxWidth, constraints.maxHeight);
      final overlap = math.min(viewport.height, coveredBySheet);
      _scheduleFit(viewport, overlap);
      return Semantics(
        container: true,
        explicitChildNodes: true,
        label: 'Knowledge graph map',
        child: Stack(
          clipBehavior: Clip.hardEdge,
          children: [
            ColoredBox(
              color: JarvisColors.canvas,
              child: InteractiveViewer(
                transformationController: _transform,
                constrained: false,
                minScale: _minScale,
                maxScale: _maxScale,
                boundaryMargin: const EdgeInsets.all(280),
                clipBehavior: Clip.hardEdge,
                child: SizedBox(
                  width: _world,
                  height: _world,
                  child: GestureDetector(
                    behavior: HitTestBehavior.opaque,
                    onTap: _clearSelection,
                    child: CustomPaint(
                      painter: _GraphPainter(_mapEdges()),
                      size: Size(_world, _world),
                    ),
                  ),
                ),
              ),
            ),
            if (_fitted)
              AnimatedBuilder(
                animation: _transform,
                builder: (context, _) => _nodeLayer(viewport),
              ),
            Positioned(
              top: 12,
              right: 12,
              child: Column(
                children: [
                  CircleIconButton(
                    icon: PhosphorIconsRegular.plus,
                    tooltip: 'Zoom in',
                    onPressed: () => _zoomBy(1.25),
                  ),
                  const SizedBox(height: 8),
                  CircleIconButton(
                    icon: PhosphorIconsRegular.minusCircle,
                    tooltip: 'Zoom out',
                    onPressed: () => _zoomBy(.8),
                  ),
                  const SizedBox(height: 8),
                  CircleIconButton(
                    icon: PhosphorIconsRegular.arrowsClockwise,
                    tooltip: 'Reset view',
                    onPressed: _resetView,
                  ),
                ],
              ),
            ),
          ],
        ),
      );
    },
  );

  Widget _nodeLayer(Size viewport) {
    final query = _query.text;
    final matched = {
      for (final node in _typedNodes)
        if (nodeMatchesQuery(node, _snapshot, query)) node.id,
    };
    final ranked = [..._typedNodes]
      ..sort((a, b) {
        final rankA = _labelRank(a, matched, query);
        final rankB = _labelRank(b, matched, query);
        if (rankA != rankB) return rankA.compareTo(rankB);
        return b.relationCount.compareTo(a.relationCount);
      });
    final anchors = <({String id, Offset anchor, bool force})>[];
    final positions = <String, Offset>{};
    for (final node in ranked) {
      final screen = MatrixUtils.transformPoint(
        _transform.value,
        _worldPoint(node.id),
      );
      if (screen.dx < -140 ||
          screen.dy < -140 ||
          screen.dx > viewport.width + 140 ||
          screen.dy > viewport.height + 140) {
        continue;
      }
      positions[node.id] = screen;
      final dimmed = _dimmed(node, matched, query);
      if (dimmed && node.id != _selectedId) continue;
      anchors.add((
        id: node.id,
        anchor: screen,
        force: node.id == _selectedId || node.isYou,
      ));
    }
    final labels = chooseLabelIds(anchors);
    return Stack(
      children: [
        for (final node in _typedNodes)
          if (positions[node.id] case final screen?)
            Positioned(
              left: screen.dx - 56,
              top: screen.dy - _hitExtent(node) / 2,
              width: 112,
              child: _MapNode(
                node: node,
                selected: node.id == _selectedId,
                dimmed: _dimmed(node, matched, query),
                showLabel: labels.contains(node.id),
                onTap: () => _select(node.id),
              ),
            ),
      ],
    );
  }

  int _labelRank(GraphNode node, Set<String> matched, String query) {
    if (node.id == _selectedId) return 0;
    if (node.isYou) return 1;
    if (query.trim().isNotEmpty && matched.contains(node.id)) return 2;
    return 3;
  }

  bool _dimmed(GraphNode node, Set<String> matched, String query) {
    if (node.id == _selectedId) return false;
    if (_selectedId != null && !_linkedToSelected(node.id)) return true;
    return query.trim().isNotEmpty && !matched.contains(node.id);
  }

  double _hitExtent(GraphNode node) {
    final selected = node.id == _selectedId;
    final diameter = selected ? 26.0 : (node.isYou ? 20.0 : 16.0);
    return diameter + (selected ? 10 : 6);
  }

  List<_MapEdge> _mapEdges() {
    final shown = _typedNodes.map((node) => node.id).toSet();
    final query = _query.text.trim();
    final matched = {
      for (final node in _typedNodes)
        if (nodeMatchesQuery(node, _snapshot, query)) node.id,
    };
    final selected = _selectedId;
    final prominent = <int>[];
    for (var index = 0; index < _snapshot.links.length; index++) {
      final link = _snapshot.links[index];
      if (!shown.contains(link.fromId) || !shown.contains(link.toId)) continue;
      if (selected != null &&
          (link.fromId == selected || link.toId == selected)) {
        prominent.add(index);
      }
    }
    prominent.sort(
      (a, b) => (_snapshot.links[b].confidence ?? 0).compareTo(
        _snapshot.links[a].confidence ?? 0,
      ),
    );
    final labeled = prominent.take(6).toSet();
    final lanes = edgeLanes(_snapshot.links);
    final edges = <_MapEdge>[];
    for (var index = 0; index < _snapshot.links.length; index++) {
      final link = _snapshot.links[index];
      if (!shown.contains(link.fromId) || !shown.contains(link.toId)) continue;
      final isProminent =
          selected != null &&
          (link.fromId == selected || link.toId == selected);
      final faded =
          !isProminent &&
          (selected != null ||
              (query.isNotEmpty &&
                  (!matched.contains(link.fromId) ||
                      !matched.contains(link.toId))));
      final color = isProminent
          ? graphTypeColor(_snapshot.node(link.fromId)?.type)
          : JarvisColors.outlineStrong.withValues(alpha: faded ? .28 : .9);
      edges.add(
        _MapEdge(
          from: _worldPoint(link.fromId),
          to: _worldPoint(link.toId),
          color: color,
          prominent: isProminent,
          lane: lanes[index].lane,
          lanes: lanes[index].lanes,
          label: labeled.contains(index)
              ? humanPredicate(link.predicate)
              : null,
        ),
      );
    }
    return edges;
  }

  Widget _sheet() {
    final listed = _listedNodes;
    final selected = _snapshot.node(_selectedId);
    final total = _snapshot.nodes.length;
    final title = listed.length == total
        ? 'Entities'
        : 'Entities · ${listed.length} of $total';
    return DraggableScrollableSheet(
      initialChildSize: _sheetPeek,
      minChildSize: .16,
      maxChildSize: .88,
      snap: true,
      snapSizes: const [_sheetPeek],
      builder: (context, controller) {
        if (_reveal != _revealed) {
          final token = _reveal;
          WidgetsBinding.instance.addPostFrameCallback((_) {
            if (!mounted || token != _reveal) return;
            _revealed = token;
            if (controller.hasClients) controller.jumpTo(0);
          });
        }
        return Material(
          color: JarvisColors.surface,
          elevation: 18,
          shadowColor: const Color(0x30111113),
          borderRadius: const BorderRadius.vertical(top: Radius.circular(22)),
          clipBehavior: Clip.antiAlias,
          child: RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              controller: controller,
              physics: const AlwaysScrollableScrollPhysics(),
              padding: EdgeInsets.fromLTRB(
                16,
                0,
                16,
                28 + MediaQuery.paddingOf(context).bottom,
              ),
              children: [
                const SizedBox(height: 8),
                const Center(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      color: JarvisColors.outlineStrong,
                      borderRadius: BorderRadius.all(Radius.circular(99)),
                    ),
                    child: SizedBox(width: 36, height: 4),
                  ),
                ),
                const SizedBox(height: 12),
                if (selected != null) ...[
                  _inspector(selected),
                  const SizedBox(height: 18),
                ],
                SectionHeader(title),
                if (listed.isEmpty)
                  const Padding(
                    padding: EdgeInsets.fromLTRB(4, 4, 4, 12),
                    child: Text(
                      'Nothing matches this filter.',
                      style: TextStyle(color: JarvisColors.inkSoft),
                    ),
                  )
                else
                  GroupedSection(
                    dividerIndent: 60,
                    children: [for (final node in listed) _entityTile(node)],
                  ),
              ],
            ),
          ),
        );
      },
    );
  }

  Widget _inspector(GraphNode node) {
    final facts = factsFor(node.id, _snapshot);
    final extra = node.relationCount - facts.length;
    final updated = DateTime.tryParse(node.updatedAt ?? '')?.toLocal();
    return Column(
      key: const Key('graph-inspector'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            _avatar(node, radius: 18),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    node.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                  Text(
                    '${graphTypeLabel(node.type)} · ${node.relationCount} current ${node.relationCount == 1 ? 'fact' : 'facts'}',
                    style: const TextStyle(
                      color: JarvisColors.inkSoft,
                      fontSize: 12.5,
                    ),
                  ),
                ],
              ),
            ),
            IconButton(
              key: const Key('open-timeline'),
              tooltip: 'Open timeline',
              visualDensity: VisualDensity.compact,
              onPressed: () => unawaited(_open(node.id)),
              icon: const Icon(PhosphorIconsRegular.arrowSquareOut, size: 20),
            ),
            IconButton(
              tooltip: 'Close',
              visualDensity: VisualDensity.compact,
              onPressed: _clearSelection,
              icon: const Icon(PhosphorIconsRegular.x, size: 18),
            ),
          ],
        ),
        if (node.aliases.isNotEmpty) ...[
          const SizedBox(height: 8),
          Text(
            'Also known as ${node.aliases.join(', ')}',
            style: const TextStyle(color: JarvisColors.inkSoft),
          ),
        ],
        if (node.summary != null) ...[
          const SizedBox(height: 6),
          Text(node.summary!),
        ],
        if (updated != null) ...[
          const SizedBox(height: 4),
          Text(
            'Updated ${formatGraphDay(updated)}',
            style: const TextStyle(fontSize: 12, color: JarvisColors.muted),
          ),
        ],
        const SizedBox(height: 10),
        if (facts.isEmpty)
          const Text(
            'No current facts in this view.',
            style: TextStyle(color: JarvisColors.inkSoft),
          )
        else
          for (final fact in facts) _factRow(fact),
        if (extra > 0)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(
              '$extra more in the timeline',
              style: const TextStyle(fontSize: 12.5, color: JarvisColors.muted),
            ),
          ),
      ],
    );
  }

  Widget _factRow(GraphFactView fact) {
    final other = _snapshot.node(fact.otherId);
    final color = fact.literal
        ? JarvisColors.inkSoft
        : graphTypeColor(other?.type);
    final meta = [
      if (fact.validFrom != null) formatFactRange(fact.validFrom, null),
      ?confidenceLabel(fact.confidence),
    ].join(' · ');
    return InkWell(
      onTap: fact.otherId == null
          ? null
          : () => _select(fact.otherId!, toggle: false),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 7),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              margin: const EdgeInsets.only(top: 5),
              width: 8,
              height: 8,
              decoration: BoxDecoration(color: color, shape: BoxShape.circle),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(fact.sentence),
                  if (meta.isNotEmpty)
                    Text(
                      meta,
                      style: const TextStyle(
                        fontSize: 12,
                        color: JarvisColors.muted,
                      ),
                    ),
                ],
              ),
            ),
            if (fact.otherId != null)
              const Icon(
                PhosphorIconsRegular.caretRight,
                size: 14,
                color: JarvisColors.muted,
              ),
          ],
        ),
      ),
    );
  }

  Widget _entityTile(GraphNode node) {
    final selected = node.id == _selectedId;
    return ListTile(
      key: Key('entity-${node.name}'),
      selected: selected,
      selectedTileColor: graphTypeColor(node.type).withValues(alpha: .08),
      leading: _avatar(node, radius: 16),
      title: Text(node.name),
      subtitle: Text(
        _entitySubtitle(node),
        maxLines: 3,
        overflow: TextOverflow.ellipsis,
      ),
      trailing: IconButton(
        key: Key('timeline-${node.name}'),
        tooltip: 'Open timeline',
        visualDensity: VisualDensity.compact,
        onPressed: () => unawaited(_open(node.id)),
        icon: const Icon(
          PhosphorIconsRegular.caretRight,
          size: 16,
          color: JarvisColors.muted,
        ),
      ),
      onTap: () => _select(node.id),
    );
  }

  String _entitySubtitle(GraphNode node) {
    final count = node.relationCount;
    final lines = <String>[
      '${graphTypeLabel(node.type)} · $count current ${count == 1 ? 'fact' : 'facts'}',
    ];
    if (node.aliases.isNotEmpty) {
      lines.add('aka ${node.aliases.take(3).join(', ')}');
    }
    if (node.summary != null) lines.add(node.summary!);
    final preview = factPreview(factsFor(node.id, _snapshot));
    if (preview.isNotEmpty) lines.add(preview);
    return lines.join('\n');
  }

  Widget _avatar(GraphNode node, {required double radius}) {
    final color = graphTypeColor(node.type);
    final letter = node.name.trim().isEmpty
        ? '?'
        : node.name.trim().characters.first.toUpperCase();
    return CircleAvatar(
      radius: radius,
      backgroundColor: color.withValues(alpha: .15),
      child: Text(
        letter,
        style: TextStyle(color: color, fontWeight: FontWeight.w700),
      ),
    );
  }
}

class _MapNode extends StatelessWidget {
  const _MapNode({
    required this.node,
    required this.selected,
    required this.dimmed,
    required this.showLabel,
    required this.onTap,
  });

  final GraphNode node;
  final bool selected;
  final bool dimmed;
  final bool showLabel;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final color = graphTypeColor(node.type);
    final diameter = selected ? 26.0 : (node.isYou ? 20.0 : 16.0);
    return Opacity(
      opacity: dimmed ? .32 : 1,
      child: GestureDetector(
        key: Key('node-${node.name}'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Semantics(
              button: true,
              label: '${node.name}, ${graphTypeLabel(node.type)}',
              child: Container(
                width: diameter + (selected ? 10 : 6),
                height: diameter + (selected ? 10 : 6),
                alignment: Alignment.center,
                decoration: selected
                    ? BoxDecoration(
                        shape: BoxShape.circle,
                        color: color.withValues(alpha: .18),
                      )
                    : null,
                child: Container(
                  width: diameter,
                  height: diameter,
                  decoration: BoxDecoration(
                    color: color,
                    shape: BoxShape.circle,
                    border: Border.all(
                      color: JarvisColors.surface,
                      width: selected ? 3 : 2,
                    ),
                    boxShadow: JarvisShadows.soft,
                  ),
                ),
              ),
            ),
            if (showLabel)
              Container(
                margin: const EdgeInsets.only(top: 2),
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                constraints: const BoxConstraints(maxWidth: 108),
                decoration: BoxDecoration(
                  color: JarvisColors.surface.withValues(alpha: .94),
                  borderRadius: BorderRadius.circular(6),
                  border: Border.all(
                    color: selected ? color : JarvisColors.outline,
                  ),
                ),
                child: Text(
                  node.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: node.isYou || selected ? 12 : 11,
                    fontWeight: FontWeight.w600,
                    color: JarvisColors.ink,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _MapEdge {
  const _MapEdge({
    required this.from,
    required this.to,
    required this.color,
    required this.prominent,
    required this.lane,
    required this.lanes,
    this.label,
  });

  final Offset from;
  final Offset to;
  final Color color;
  final bool prominent;
  final int lane;
  final int lanes;
  final String? label;
}

class _GraphPainter extends CustomPainter {
  const _GraphPainter(this.edges);

  final List<_MapEdge> edges;

  @override
  void paint(Canvas canvas, Size size) {
    final dot = Paint()..color = const Color(0xffe3dfd6);
    for (var x = 22.0; x < size.width; x += 28) {
      for (var y = 22.0; y < size.height; y += 28) {
        canvas.drawCircle(Offset(x, y), 1.15, dot);
      }
    }
    for (final edge in edges) {
      final control = _control(edge);
      final path = Path()
        ..moveTo(edge.from.dx, edge.from.dy)
        ..quadraticBezierTo(control.dx, control.dy, edge.to.dx, edge.to.dy);
      canvas.drawPath(
        path,
        Paint()
          ..color = edge.color
          ..style = PaintingStyle.stroke
          ..strokeWidth = edge.prominent ? 2.2 : 1.25
          ..strokeCap = StrokeCap.round,
      );
      if (!edge.prominent) continue;
      _arrow(canvas, control, edge.to, edge.color);
      final label = edge.label;
      if (label == null || label.isEmpty) continue;
      final painter = TextPainter(
        text: TextSpan(
          text: label,
          style: const TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: JarvisColors.inkSoft,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: 120);
      final origin = control - Offset(painter.width / 2, painter.height / 2);
      final rect = Rect.fromLTWH(
        origin.dx - 5,
        origin.dy - 2,
        painter.width + 10,
        painter.height + 4,
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()..color = JarvisColors.surface.withValues(alpha: .94),
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()
          ..color = JarvisColors.outline
          ..style = PaintingStyle.stroke,
      );
      painter.paint(canvas, origin);
    }
  }

  Offset _control(_MapEdge edge) {
    final mid = Offset.lerp(edge.from, edge.to, .5)!;
    if (edge.lanes <= 1) return mid;
    final delta = edge.to - edge.from;
    final length = math.max(delta.distance, 1).toDouble();
    final normal = Offset(-delta.dy / length, delta.dx / length);
    final shift = (edge.lane - (edge.lanes - 1) / 2) * 42;
    return mid + normal * shift;
  }

  void _arrow(Canvas canvas, Offset control, Offset to, Color color) {
    final direction = to - control;
    final length = math.max(direction.distance, 1).toDouble();
    final unit = direction / length;
    final tip = to - unit * 16;
    final normal = Offset(-unit.dy, unit.dx);
    final base = tip - unit * 9;
    final path = Path()
      ..moveTo(tip.dx, tip.dy)
      ..lineTo(base.dx + normal.dx * 4.5, base.dy + normal.dy * 4.5)
      ..lineTo(base.dx - normal.dx * 4.5, base.dy - normal.dy * 4.5)
      ..close();
    canvas.drawPath(path, Paint()..color = color);
  }

  @override
  bool shouldRepaint(_GraphPainter oldDelegate) => oldDelegate.edges != edges;
}

/// One entity's current facts and how they changed over time.
class GraphEntityScreen extends StatefulWidget {
  const GraphEntityScreen({
    required this.http,
    required this.entityId,
    super.key,
  });

  final Dio http;
  final String entityId;

  @override
  State<GraphEntityScreen> createState() => _GraphEntityScreenState();
}

class _GraphEntityScreenState extends State<GraphEntityScreen> {
  Map<String, dynamic>? _details;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/graph/entities/${widget.entityId}',
      );
      if (mounted) setState(() => _details = response.data);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not load this entity.',
        );
      }
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Forget this entity?',
      message:
          'Jarvis removes it and every fact that links to it from the graph. Your memories stay.',
      confirmLabel: 'Forget',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed) return;
    await widget.http.delete<void>('/api/v1/graph/entities/${widget.entityId}');
    if (mounted) Navigator.pop(context);
  }

  @override
  Widget build(BuildContext context) {
    final details = _details;
    final entity = Map<String, dynamic>.from(details?['entity'] as Map? ?? {});
    final current = jsonMaps(details?['current']);
    final history = jsonMaps(details?['history']);
    final summary = asJsonString(entity['summary']);
    final updated = DateTime.tryParse(
      asJsonString(entity['updatedAt']) ?? '',
    )?.toLocal();
    return Scaffold(
      appBar: AppBar(
        title: Text(asJsonString(entity['name']) ?? 'Entity'),
        actions: [
          if (details != null)
            IconButton(
              tooltip: 'Forget',
              onPressed: () => unawaited(_delete()),
              icon: const Icon(PhosphorIconsRegular.trash),
            ),
        ],
      ),
      body: details == null
          ? (_error == null
                ? const LoadingState()
                : ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  ))
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          StatusPill(
                            label: graphTypeLabel(asJsonString(entity['type'])),
                            color: graphTypeColor(asJsonString(entity['type'])),
                          ),
                          for (final alias in jsonStrings(entity['aliases']))
                            StatusPill(
                              label: 'aka $alias',
                              color: JarvisColors.muted,
                            ),
                          if (updated != null)
                            StatusPill(
                              label: 'Updated ${formatGraphDay(updated)}',
                              color: JarvisColors.inkSoft,
                            ),
                        ],
                      ),
                      if (summary != null && summary.isNotEmpty) ...[
                        const SizedBox(height: 12),
                        Text(summary),
                      ],
                      const SizedBox(height: 16),
                      const SectionHeader('Now'),
                      _facts(current, currentFacts: true),
                      if (history.isNotEmpty) ...[
                        const SizedBox(height: 20),
                        const SectionHeader('Timeline'),
                        _facts(history, currentFacts: false),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _facts(
    List<Map<String, dynamic>> facts, {
    required bool currentFacts,
  }) {
    if (facts.isEmpty) {
      return const SurfaceCard(
        child: Text(
          'Nothing recorded yet.',
          style: TextStyle(color: JarvisColors.inkSoft),
        ),
      );
    }
    return GroupedSection(
      dividerIndent: 56,
      children: [
        for (final fact in facts)
          ListTile(
            leading: IconBadge(
              icon: currentFacts
                  ? PhosphorIconsRegular.checkCircle
                  : PhosphorIconsRegular.clockCounterClockwise,
              color: currentFacts ? JarvisColors.success : JarvisColors.muted,
              size: 32,
            ),
            title: Text(
              '${asJsonString(fact['subjectName'])} ${humanPredicate(asJsonString(fact['predicate']) ?? '')} '
              '${asJsonString(fact['objectName']) ?? asJsonString(fact['objectValue']) ?? ''}',
              style: currentFacts
                  ? null
                  : const TextStyle(
                      color: JarvisColors.inkSoft,
                      decoration: TextDecoration.lineThrough,
                    ),
            ),
            subtitle: Text(_factMeta(fact)),
          ),
      ],
    );
  }

  String _factMeta(Map<String, dynamic> fact) {
    final range = formatFactRange(
      asJsonString(fact['validFrom']),
      asJsonString(fact['validTo']),
    );
    final confidence = confidenceLabel(graphJsonDouble(fact['confidence']));
    return confidence == null ? range : '$range · $confidence';
  }
}
