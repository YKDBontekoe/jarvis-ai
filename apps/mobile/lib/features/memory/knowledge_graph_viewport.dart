part of 'knowledge_graph_screen.dart';

mixin _KnowledgeGraphViewport on _KnowledgeGraphController {
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
              color: JarvisColors.of(context).canvas,
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
                      painter: _GraphPainter(
                        _mapEdges(),
                        JarvisColors.of(context),
                      ),
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
          ? graphTypeColor(_snapshot.node(link.fromId)?.type, context)
          : JarvisColors.of(context).outlineStrong
                .withValues(alpha: faded ? .28 : .9);
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
}
