part of 'knowledge_graph_screen.dart';

mixin _KnowledgeGraphSheet on _KnowledgeGraphController {
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
          color: JarvisColors.of(context).surface,
          elevation: 18,
          shadowColor: const Color(0x30111113),
          borderRadius: const BorderRadius.vertical(top: Radius.circular(22)),
          clipBehavior: Clip.antiAlias,
          child: OrbRefresh(
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
                Center(
                  child: DecoratedBox(
                    decoration: BoxDecoration(
                      color: JarvisColors.of(context).outlineStrong,
                      borderRadius: BorderRadius.all(Radius.circular(99)),
                    ),
                    child: SizedBox(width: 36, height: 4),
                  ),
                ),
                const SizedBox(height: 12),
                if (selected != null) ...[
                  _inspector(selected),
                  const SizedBox(height: 18),
                ] else if (_query.text.trim().isEmpty &&
                    _types == null &&
                    _snapshot.nodes.length >= _highlightsMinimum) ...[
                  _highlights(),
                  const SizedBox(height: 18),
                ],
                SectionHeader(title),
                if (listed.isEmpty)
                  Padding(
                    padding: EdgeInsets.fromLTRB(4, 4, 4, 12),
                    child: Text(
                      'Nothing matches this filter.',
                      style: TextStyle(color: JarvisColors.of(context).inkSoft),
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

  /// Hubs, circles, and the newest facts: a way into a big graph without hunting through dots.
  Widget _highlights() {
    final colors = JarvisColors.of(context);
    final hubs = sortedGraphNodes(
      _snapshot.nodes.where((node) => !node.isYou).toList(),
    )..sort((a, b) => (_degrees[b.id] ?? 0).compareTo(_degrees[a.id] ?? 0));
    final circles = [
      for (final cluster in _clusters)
        if (cluster.memberIds
                .where((id) => _snapshot.node(id) != null)
                .length >=
            3)
          cluster,
    ].take(6).toList();
    final recent = recentGraphFacts(_snapshot);
    TextStyle caption() => TextStyle(
      fontSize: 12,
      fontWeight: FontWeight.w600,
      color: colors.muted,
      letterSpacing: .2,
    );
    return Column(
      key: const Key('graph-highlights'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader('Highlights'),
        if (hubs.isNotEmpty) ...[
          Text('Most connected', style: caption()),
          const SizedBox(height: 8),
          SizedBox(
            height: 34,
            child: ListView(
              scrollDirection: Axis.horizontal,
              children: [
                for (final node in hubs.take(6))
                  _highlightChip(
                    key: Key('hub-${node.name}'),
                    color: graphTypeColor(node.type, context),
                    icon: graphTypeIcon(node.type),
                    label: '${node.name} · ${_degrees[node.id] ?? 0}',
                    onTap: () => _select(node.id, toggle: false),
                  ),
              ],
            ),
          ),
          const SizedBox(height: 14),
        ],
        if (circles.isNotEmpty) ...[
          Text('Circles', style: caption()),
          const SizedBox(height: 8),
          SizedBox(
            height: 34,
            child: ListView(
              scrollDirection: Axis.horizontal,
              children: [for (final cluster in circles) _circleChip(cluster)],
            ),
          ),
          const SizedBox(height: 14),
        ],
        if (recent.isNotEmpty) ...[
          Text('Recently learned', style: caption()),
          const SizedBox(height: 2),
          for (final item in recent)
            InkWell(
              onTap: () => _select(item.entityId, toggle: false),
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 6),
                child: Row(
                  children: [
                    Icon(
                      item.fact.literal
                          ? PhosphorIconsRegular.notePencil
                          : PhosphorIconsRegular.linkSimple,
                      size: 15,
                      color: graphTypeColor(
                        _snapshot.node(item.entityId)?.type,
                        context,
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: Text(
                        item.fact.sentence,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                    const SizedBox(width: 8),
                    Text(
                      formatGraphDay(item.at.toLocal()),
                      style: TextStyle(fontSize: 12, color: colors.muted),
                    ),
                  ],
                ),
              ),
            ),
        ],
      ],
    );
  }

  Widget _circleChip(GraphCluster cluster) {
    final members = [for (final id in cluster.memberIds) ?_snapshot.node(id)]
      ..sort((a, b) => (_degrees[b.id] ?? 0).compareTo(_degrees[a.id] ?? 0));
    final lead = members.first;
    final counts = <String, int>{};
    for (final member in members) {
      counts[member.type] = (counts[member.type] ?? 0) + 1;
    }
    final dominant = counts.entries
        .reduce((a, b) => b.value > a.value ? b : a)
        .key;
    return _highlightChip(
      key: Key('circle-${lead.name}'),
      color: graphTypeColor(dominant, context),
      icon: PhosphorIconsRegular.intersectThree,
      label: '${lead.name} +${members.length - 1}',
      onTap: () => _select(lead.id, toggle: false),
    );
  }

  Widget _highlightChip({
    required Key key,
    required Color color,
    required IconData icon,
    required String label,
    required VoidCallback onTap,
  }) => Padding(
    padding: const EdgeInsets.only(right: 8),
    child: Material(
      color: color.withValues(alpha: .1),
      borderRadius: BorderRadius.circular(999),
      child: InkWell(
        key: key,
        borderRadius: BorderRadius.circular(999),
        onTap: onTap,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(icon, size: 14, color: color),
              const SizedBox(width: 6),
              Text(
                label,
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

  /// Neighbour types as a stacked bar, e.g. mostly people with a few places.
  Widget _reach(GraphNode node) {
    final colors = JarvisColors.of(context);
    final mix = neighborTypeMix(node.id, _snapshot);
    if (mix.isEmpty) return const SizedBox.shrink();
    final direct = mix.fold<int>(0, (sum, item) => sum + item.count);
    final twoSteps = _hops.values.where((hop) => hop == 2).length;
    String plural(String type, int count) {
      final label = graphTypeLabel(type).toLowerCase();
      if (count == 1) return '1 $label';
      return switch (type) {
        'person' => '$count people',
        'pet' => '$count pets',
        _ => '$count ${label}s',
      };
    }

    return Padding(
      key: const Key('graph-reach'),
      padding: const EdgeInsets.only(top: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          ClipRRect(
            borderRadius: BorderRadius.circular(99),
            child: SizedBox(
              height: 6,
              child: Row(
                children: [
                  for (final item in mix)
                    Expanded(
                      flex: item.count,
                      child: ColoredBox(
                        color: graphTypeColor(item.type, context),
                      ),
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 6),
          Text(
            [
              mix
                  .take(4)
                  .map((item) => plural(item.type, item.count))
                  .join(' · '),
              if (twoSteps > 0)
                '$twoSteps more within two steps'
              else if (direct > 0)
                'no wider circle yet',
            ].join(' — '),
            style: TextStyle(fontSize: 12, color: colors.inkSoft),
          ),
        ],
      ),
    );
  }

  Widget _inspector(GraphNode node) {
    final facts = factsFor(node.id, _snapshot);
    final extra = node.relationCount - facts.length;
    final updated = jsonDate(node.updatedAt, local: true);
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
                    style: TextStyle(
                      color: JarvisColors.of(context).inkSoft,
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
            style: TextStyle(color: JarvisColors.of(context).inkSoft),
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
            style: TextStyle(
              fontSize: 12,
              color: JarvisColors.of(context).muted,
            ),
          ),
        ],
        _reach(node),
        const SizedBox(height: 10),
        if (facts.isEmpty)
          Text(
            'No current facts in this view.',
            style: TextStyle(color: JarvisColors.of(context).inkSoft),
          )
        else
          for (final fact in facts) _factRow(fact),
        if (extra > 0)
          Padding(
            padding: const EdgeInsets.only(top: 4),
            child: Text(
              '$extra more in the timeline',
              style: TextStyle(
                fontSize: 12.5,
                color: JarvisColors.of(context).muted,
              ),
            ),
          ),
      ],
    );
  }

  Widget _factRow(GraphFactView fact) {
    final other = _snapshot.node(fact.otherId);
    final color = fact.literal
        ? JarvisColors.of(context).inkSoft
        : graphTypeColor(other?.type, context);
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
                      style: TextStyle(
                        fontSize: 12,
                        color: JarvisColors.of(context).muted,
                      ),
                    ),
                ],
              ),
            ),
            if (fact.otherId != null)
              Icon(
                PhosphorIconsRegular.caretRight,
                size: 14,
                color: JarvisColors.of(context).muted,
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
      selectedTileColor: graphTypeColor(
        node.type,
        context,
      ).withValues(alpha: .08),
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
        icon: Icon(
          PhosphorIconsRegular.caretRight,
          size: 16,
          color: JarvisColors.of(context).muted,
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
    final color = graphTypeColor(node.type, context);
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
