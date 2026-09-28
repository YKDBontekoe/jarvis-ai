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

part 'knowledge_graph_map.dart';
part 'graph_entity_screen.dart';
part 'knowledge_graph_viewport.dart';
part 'knowledge_graph_sheet.dart';

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

/// Holds graph fields so map and sheet mixins can share state.
abstract class _KnowledgeGraphController extends State<KnowledgeGraphScreen>
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
      final you = snapshot.nodes
          .where((node) => node.isYou)
          .map((node) => node.id)
          .firstOrNull;
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _snapshot = snapshot;
        _layout = layoutGraph(
          [for (final node in snapshot.nodes) node.id],
          [for (final link in snapshot.links) (link.fromId, link.toId)],
          pinned: you,
        );
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
}
