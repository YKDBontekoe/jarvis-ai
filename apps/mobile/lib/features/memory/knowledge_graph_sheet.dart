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
