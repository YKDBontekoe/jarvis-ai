import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'graph_layout.dart';

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

Color graphTypeColor(String? type) => _typeColors[type] ?? JarvisColors.inkSoft;

String humanPredicate(String predicate) => predicate.replaceAll('_', ' ');

/// Temporal knowledge graph: who and what Jarvis knows about, and how facts changed over time.
class KnowledgeGraphScreen extends StatefulWidget {
  const KnowledgeGraphScreen({required this.http, super.key});

  final Dio http;

  @override
  State<KnowledgeGraphScreen> createState() => _KnowledgeGraphScreenState();
}

class _KnowledgeGraphScreenState extends State<KnowledgeGraphScreen> {
  List<Map<String, dynamic>> _entities = const [];
  List<Map<String, dynamic>> _edges = const [];
  Map<String, dynamic>? _status;
  Map<String, Offset> _layout = const {};
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final responses = await Future.wait([
        widget.http.get<Map<String, dynamic>>('/api/v1/graph/overview'),
        widget.http.get<Map<String, dynamic>>('/api/v1/memory/index-status'),
      ]);
      final overview = responses[0].data ?? const {};
      final entities = jsonMaps(overview['entities']);
      final edges = jsonMaps(overview['edges']);
      final you = entities
          .where((entity) => entity['name'] == 'You')
          .map((entity) => entity['id'] as String?)
          .firstOrNull;
      if (!mounted) return;
      setState(() {
        _entities = entities;
        _edges = edges;
        _status = responses[1].data;
        _layout = layoutGraph(
          [for (final entity in entities) asJsonString(entity['id']) ?? ''],
          [
            for (final edge in edges)
              (
                asJsonString(edge['from']) ?? '',
                asJsonString(edge['to']) ?? '',
              ),
          ],
          pinned: you,
        );
        _loading = false;
        _error = null;
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

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => GraphEntityScreen(http: widget.http, entityId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Knowledge graph')),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _entities.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: const EmptyState(
        icon: PhosphorIconsRegular.graph,
        title: 'No connections yet',
        message:
            'As you tell Jarvis about people, places, and projects, it links them here and keeps track of how facts change over time.',
      ),
      child: RefreshIndicator(
        onRefresh: _load,
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            ContentWidth(
              maxWidth: 900,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _statusRow(),
                  const SizedBox(height: 12),
                  SurfaceCard(
                    padding: EdgeInsets.zero,
                    child: AspectRatio(
                      aspectRatio: 1.25,
                      child: LayoutBuilder(
                        builder: (context, constraints) => _canvas(
                          Size(constraints.maxWidth, constraints.maxHeight),
                        ),
                      ),
                    ),
                  ),
                  const SizedBox(height: 20),
                  const SectionHeader('Entities'),
                  GroupedSection(
                    dividerIndent: 60,
                    children: [
                      for (final entity in _entities)
                        ListTile(
                          key: Key('entity-${entity['name']}'),
                          leading: CircleAvatar(
                            radius: 16,
                            backgroundColor: graphTypeColor(
                              asJsonString(entity['type']),
                            ).withValues(alpha: .15),
                            child: Text(
                              (asJsonString(entity['name']) ?? '?')
                                  .characters
                                  .first,
                              style: TextStyle(
                                color: graphTypeColor(
                                  asJsonString(entity['type']),
                                ),
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                          title: Text(asJsonString(entity['name']) ?? ''),
                          subtitle: Text(
                            '${asJsonString(entity['type']) ?? 'thing'} · ${asJsonInt(entity['relationCount'])} current facts',
                          ),
                          trailing: const Icon(
                            PhosphorIconsRegular.caretRight,
                            size: 16,
                            color: JarvisColors.muted,
                          ),
                          onTap: () => unawaited(
                            _open(asJsonString(entity['id']) ?? ''),
                          ),
                        ),
                    ],
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );

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

  Widget _canvas(Size size) {
    final byId = {
      for (final entity in _entities) asJsonString(entity['id']) ?? '': entity,
    };
    Offset at(String id) {
      final point = _layout[id] ?? const Offset(.5, .5);
      return Offset(point.dx * size.width, point.dy * size.height);
    }

    return Stack(
      children: [
        Positioned.fill(
          child: CustomPaint(
            painter: _EdgePainter([
              for (final edge in _edges)
                (
                  at(asJsonString(edge['from']) ?? ''),
                  at(asJsonString(edge['to']) ?? ''),
                  humanPredicate(asJsonString(edge['predicate']) ?? ''),
                ),
            ]),
          ),
        ),
        for (final MapEntry(key: id, value: entity) in byId.entries)
          Positioned(
            left: at(id).dx - 48,
            top: at(id).dy - 18,
            width: 96,
            child: _node(id, entity),
          ),
      ],
    );
  }

  Widget _node(String id, Map<String, dynamic> entity) {
    final name = asJsonString(entity['name']) ?? '';
    final color = graphTypeColor(asJsonString(entity['type']));
    final you = name == 'You';
    return GestureDetector(
      onTap: () => unawaited(_open(id)),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: you ? 22 : 16,
            height: you ? 22 : 16,
            decoration: BoxDecoration(
              color: color,
              shape: BoxShape.circle,
              border: Border.all(color: JarvisColors.surface, width: 3),
              boxShadow: JarvisShadows.soft,
            ),
          ),
          const SizedBox(height: 3),
          Text(
            name,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            textAlign: TextAlign.center,
            style: TextStyle(
              fontSize: you ? 13 : 11.5,
              fontWeight: you ? FontWeight.w700 : FontWeight.w600,
              color: JarvisColors.ink,
            ),
          ),
        ],
      ),
    );
  }
}

class _EdgePainter extends CustomPainter {
  _EdgePainter(this.edges);

  final List<(Offset, Offset, String)> edges;

  @override
  void paint(Canvas canvas, Size size) {
    final line = Paint()
      ..color = JarvisColors.outlineStrong
      ..strokeWidth = 1.4;
    for (final (from, to, label) in edges) {
      canvas.drawLine(from, to, line);
      final painter = TextPainter(
        text: TextSpan(
          text: label,
          style: const TextStyle(fontSize: 9.5, color: JarvisColors.muted),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: 110);
      final middle = Offset.lerp(from, to, .5)!;
      painter.paint(
        canvas,
        middle - Offset(painter.width / 2, painter.height / 2),
      );
    }
  }

  @override
  bool shouldRepaint(_EdgePainter oldDelegate) => oldDelegate.edges != edges;
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
                        children: [
                          StatusPill(
                            label: asJsonString(entity['type']) ?? 'thing',
                            color: graphTypeColor(asJsonString(entity['type'])),
                          ),
                          for (final alias in jsonStrings(entity['aliases']))
                            StatusPill(
                              label: 'aka $alias',
                              color: JarvisColors.muted,
                            ),
                        ],
                      ),
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
            subtitle: Text(_range(fact)),
          ),
      ],
    );
  }

  String _range(Map<String, dynamic> fact) {
    String day(String? iso) {
      final time = DateTime.tryParse(iso ?? '')?.toLocal();
      if (time == null) return 'now';
      const months = [
        'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', //
        'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
      ];
      return '${time.day} ${months[time.month - 1]} ${time.year}';
    }

    final from = day(asJsonString(fact['validFrom']));
    final to = asJsonString(fact['validTo']);
    return to == null ? 'Since $from' : '$from → ${day(to)}';
  }
}
