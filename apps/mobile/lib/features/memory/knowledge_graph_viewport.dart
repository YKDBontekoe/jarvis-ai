part of 'knowledge_graph_screen.dart';

const _maxSatellites = 7;

mixin _KnowledgeGraphViewport on _KnowledgeGraphController {
  Widget _map(double coveredBySheet) => LayoutBuilder(
    builder: (context, constraints) {
      final viewport = Size(constraints.maxWidth, constraints.maxHeight);
      final overlap = math.min(viewport.height, coveredBySheet);
      _scheduleFit(viewport, overlap);
      final colors = JarvisColors.of(context);
      return Semantics(
        container: true,
        explicitChildNodes: true,
        label: 'Knowledge graph map',
        child: Stack(
          clipBehavior: Clip.hardEdge,
          children: [
            ColoredBox(
              color: colors.canvas,
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
                        edges: _mapEdges(),
                        halos: _halos(),
                        colors: colors,
                        flow: _pulse,
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
    final hops = _hops;
    final ranked = [..._typedNodes]
      ..sort((a, b) {
        final rankA = _labelRank(a, matched, query, hops);
        final rankB = _labelRank(b, matched, query, hops);
        if (rankA != rankB) return rankA.compareTo(rankB);
        return (_degrees[b.id] ?? 0).compareTo(_degrees[a.id] ?? 0);
      });
    final positions = <String, Offset>{};
    final obstacles = <Rect>[];
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
      if (_emphasis(node, matched, query, hops) >= .5) {
        // Only the core of each dot blocks a label, so dense areas still get some names.
        obstacles.add(
          Rect.fromCircle(center: screen, radius: _nodeDiameter(node) * .3),
        );
      }
    }

    final selected = _snapshot.node(_selectedId);
    final selectedAt = selected == null ? null : positions[selected.id];
    final satellites = selectedAt == null
        ? const <_Satellite>[]
        : _satellites(selected!, selectedAt);
    obstacles.addAll(satellites.map((satellite) => satellite.rect));

    // Fewer names when zoomed out so the map reads as shapes first, then detail as you zoom in.
    final scale = _transform.value.getMaxScaleOnAxis();
    final budget = scale < .45 ? 10 : (scale < .8 ? 22 : 1 << 20);
    final anchors = <({String id, Offset anchor, bool force})>[];
    var unforced = 0;
    for (final node in ranked) {
      final screen = positions[node.id];
      if (screen == null) continue;
      if (_emphasis(node, matched, query, hops) < .5) continue;
      final force = node.id == _selectedId || node.isYou;
      if (!force && unforced >= budget) continue;
      if (!force) unforced++;
      anchors.add((
        id: node.id,
        anchor: screen + Offset(0, _hitExtent(node) / 2 - 13),
        force: force,
      ));
    }
    final labels = chooseLabelIds(
      anchors,
      obstacles: obstacles,
      widths: {
        for (final anchor in anchors)
          anchor.id: math.min(
            108.0,
            16 + (_snapshot.node(anchor.id)?.name.length ?? 12) * 6.2,
          ),
      },
    );
    final colors = JarvisColors.of(context);
    return Stack(
      children: [
        if (selectedAt != null && satellites.isNotEmpty)
          Positioned.fill(
            child: IgnorePointer(
              child: AnimatedBuilder(
                animation: _pulse,
                builder: (context, _) => CustomPaint(
                  painter: _SpokePainter(
                    center: selectedAt,
                    satellites: satellites,
                    color: graphTypeColor(selected!.type, context),
                    progress: Curves.easeOutCubic.transform(
                      (_pulse.value * 4).clamp(0.0, 1.0),
                    ),
                  ),
                ),
              ),
            ),
          ),
        // Least important first so labels and the selection are painted on top.
        for (final node in ranked.reversed)
          if (positions[node.id] case final screen?)
            Positioned(
              left: screen.dx - 56,
              top: screen.dy - _hitExtent(node) / 2,
              width: 112,
              child: _MapNode(
                node: node,
                diameter: _nodeDiameter(node),
                selected: node.id == _selectedId,
                emphasis: _emphasis(node, matched, query, hops),
                showLabel: labels.contains(node.id),
                recent: _isRecent(node),
                pulse: node.id == _selectedId ? _pulse : null,
                onTap: () => _select(node.id),
              ),
            ),
        for (final satellite in satellites)
          Positioned.fromRect(
            rect: satellite.rect,
            child: AnimatedBuilder(
              animation: _pulse,
              builder: (context, child) {
                final t = Curves.easeOutBack.transform(
                  ((_pulse.value * 4) - satellite.delay).clamp(0.0, 1.0),
                );
                return Opacity(
                  opacity: t.clamp(0.0, 1.0),
                  child: Transform.scale(scale: .6 + t * .4, child: child),
                );
              },
              child: _SatelliteChip(
                satellite: satellite,
                colors: colors,
                onTap: satellite.more > 0
                    ? () => unawaited(_open(selected!.id))
                    : null,
              ),
            ),
          ),
      ],
    );
  }

  /// Details (literal facts) of the selected node, arranged in a ring around it.
  List<_Satellite> _satellites(GraphNode node, Offset center) {
    final literals = [
      for (final fact in factsFor(node.id, _snapshot))
        if (fact.literal) fact,
    ];
    if (literals.isEmpty) return const [];
    final shown = literals.take(_maxSatellites).toList();
    final more = literals.length - shown.length;
    final count = shown.length + (more > 0 ? 1 : 0);
    final radius = _nodeDiameter(node) / 2 + 58 + count * 3;
    final satellites = <_Satellite>[];
    for (var index = 0; index < count; index++) {
      final angle = -math.pi / 2 + index * math.pi * 2 / count;
      // Wider ellipse so horizontal chips don't collide at the sides.
      final at =
          center +
          Offset(math.cos(angle) * radius * 1.35, math.sin(angle) * radius);
      final isMore = index == shown.length;
      final label = isMore ? '+$more more' : shown[index].compact;
      const width = 118.0;
      satellites.add(
        _Satellite(
          label: label,
          tooltip: isMore ? 'Open timeline' : shown[index].sentence,
          rect: Rect.fromCenter(center: at, width: width, height: 24),
          delay: index * .08,
          more: isMore ? more : 0,
        ),
      );
    }
    return satellites;
  }

  int _labelRank(
    GraphNode node,
    Set<String> matched,
    String query,
    Map<String, int> hops,
  ) {
    if (node.id == _selectedId) return 0;
    if (node.isYou) return 1;
    if (hops[node.id] == 1) return 2;
    if (query.trim().isNotEmpty && matched.contains(node.id)) return 3;
    return 4;
  }

  /// 1 for the focus and its direct links, fading with distance from the selection or with a search miss.
  double _emphasis(
    GraphNode node,
    Set<String> matched,
    String query,
    Map<String, int> hops,
  ) {
    if (node.id == _selectedId) return 1;
    if (_selectedId != null) {
      return switch (hops[node.id]) {
        1 => 1,
        2 => .5,
        _ => .16,
      };
    }
    return query.trim().isNotEmpty && !matched.contains(node.id) ? .26 : 1;
  }

  double _hitExtent(GraphNode node) {
    final selected = node.id == _selectedId;
    return _nodeDiameter(node) + (selected ? 14 : 8);
  }

  /// Soft clouds behind each circle of related entities, tinted by its most common type.
  List<_Halo> _halos() {
    if (_clusters.isEmpty) return const [];
    final shown = _typedNodes.map((node) => node.id).toSet();
    final halos = <_Halo>[];
    for (final cluster in _clusters.take(8)) {
      final members = [
        for (final id in cluster.memberIds)
          if (shown.contains(id)) id,
      ];
      if (members.length < 3) continue;
      final counts = <String, int>{};
      for (final id in members) {
        final type = _snapshot.node(id)?.type ?? 'thing';
        counts[type] = (counts[type] ?? 0) + 1;
      }
      final dominant = counts.entries
          .reduce((a, b) => b.value > a.value ? b : a)
          .key;
      final dimmed = _selectedId != null && !members.contains(_selectedId);
      halos.add(
        _Halo(
          points: [for (final id in members) _worldPoint(id)],
          color: graphTypeColor(dominant, context),
          strength: dimmed ? .35 : 1,
        ),
      );
    }
    return halos;
  }

  List<_MapEdge> _mapEdges() {
    final shown = _typedNodes.map((node) => node.id).toSet();
    final query = _query.text.trim();
    final matched = {
      for (final node in _typedNodes)
        if (nodeMatchesQuery(node, _snapshot, query)) node.id,
    };
    final selected = _selectedId;
    final hops = _hops;
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
    final colors = JarvisColors.of(context);
    final edges = <_MapEdge>[];
    for (var index = 0; index < _snapshot.links.length; index++) {
      final link = _snapshot.links[index];
      if (!shown.contains(link.fromId) || !shown.contains(link.toId)) continue;
      final isProminent =
          selected != null &&
          (link.fromId == selected || link.toId == selected);
      final from = _snapshot.node(link.fromId);
      final to = _snapshot.node(link.toId);
      final confidence = (link.confidence ?? .8).clamp(0.0, 1.0);
      double alpha;
      if (isProminent) {
        alpha = 1;
      } else if (selected != null) {
        // Links among the selection's neighbours stay faintly visible: they show how its world connects.
        alpha = hops[link.fromId] == 1 && hops[link.toId] == 1 ? .4 : .07;
      } else if (query.isNotEmpty &&
          (!matched.contains(link.fromId) || !matched.contains(link.toId))) {
        alpha = .1;
      } else {
        alpha = .28 + confidence * .32;
      }
      // Edges into "You" take the colour of the other end so the hub isn't a single-colour starburst.
      final fromColor = graphTypeColor(
        from?.isYou ?? false ? to?.type : from?.type,
        context,
      );
      final toColor = graphTypeColor(
        to?.isYou ?? false ? from?.type : to?.type,
        context,
      );
      edges.add(
        _MapEdge(
          from: _worldPoint(link.fromId),
          to: _worldPoint(link.toId),
          fromColor: isProminent || selected == null
              ? fromColor
              : colors.outlineStrong,
          toColor: isProminent || selected == null
              ? toColor
              : colors.outlineStrong,
          alpha: alpha,
          width: isProminent ? 1.6 + confidence * 1.6 : .8 + confidence * 1.1,
          prominent: isProminent,
          dashed: confidence < .6,
          lane: lanes[index].lane,
          lanes: lanes[index].lanes,
          bow: (link.fromId.hashCode ^ link.toId.hashCode).isEven ? 1 : -1,
          flowsOut: link.fromId == selected,
          label: labeled.contains(index)
              ? humanPredicate(link.predicate)
              : null,
        ),
      );
    }
    // Draw the highlighted edges last so they sit on top.
    edges.sort((a, b) => (a.prominent ? 1 : 0).compareTo(b.prominent ? 1 : 0));
    return edges;
  }
}
