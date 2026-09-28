part of 'knowledge_graph_screen.dart';

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
      final response = await widget.http.get<dynamic>(
        '/api/v1/graph/entities/${widget.entityId}',
      );
      if (mounted) {
        setState(() {
          _details = jsonObject(response.data);
          _error = _details == null ? 'Could not load this entity.' : null;
        });
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not load this entity.',
        );
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not load this entity.');
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
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<void>(
        '/api/v1/graph/entities/${widget.entityId}',
      );
      if (mounted) Navigator.pop(context);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not forget this entity.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not forget this entity.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final details = _details;
    final entity = jsonObject(details?['entity']) ?? const {};
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
