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
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/graph/entities/${widget.entityId}',
      );
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _details = jsonObject(response.data);
        _error = _details == null ? 'Could not load this entity.' : null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this entity.',
      );
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() => _error = 'Could not load this entity.');
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Forget this entity?',
      message: 'Jarvis removes it and every fact that links to it from the graph. Your memories stay.',
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

  Future<void> _editEntity() async {
    final entity = jsonObject(_details?['entity']) ?? const {};
    final saved = await showDialog<_EntityEditValues>(
      context: context,
      builder: (_) => _EntityEditDialog(
        name: asJsonString(entity['name']) ?? '',
        type: asJsonString(entity['type']) ?? 'thing',
        summary: asJsonString(entity['summary']) ?? '',
      ),
    );
    if (saved == null || !mounted) return;
    try {
      await widget.http.put<void>(
        '/api/v1/graph/entities/${widget.entityId}',
        data: {
          'name': saved.name,
          'type': saved.type,
          'summary': saved.summary.isEmpty ? null : saved.summary,
        },
      );
      if (mounted) await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not update this entity.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not update this entity.');
    }
  }

  Future<void> _addFact() async {
    final entity = jsonObject(_details?['entity']) ?? const {};
    final subject = asJsonString(entity['name']) ?? '';
    final saved = await showDialog<_FactValues>(
      context: context,
      builder: (_) => const _AddFactDialog(),
    );
    if (saved == null || !mounted) return;
    if (saved.predicate.isEmpty || saved.object.isEmpty) {
      setState(() => _error = 'A fact needs a predicate and an object.');
      return;
    }
    try {
      await widget.http.post<void>(
        '/api/v1/graph/facts',
        data: {
          'subject': subject,
          'subjectType': asJsonString(entity['type']),
          'predicate': saved.predicate,
          'object': saved.object,
          'objectIsEntity': saved.objectIsEntity,
          'exclusive': true,
        },
      );
      if (mounted) await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not save that fact.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not save that fact.');
    }
  }

  Future<void> _closeFact(Map<String, dynamic> fact) async {
    final id = jsonId(fact);
    if (id == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Close this fact?',
      message: 'Jarvis marks it as no longer current. History stays.',
      confirmLabel: 'Close fact',
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<void>('/api/v1/graph/relations/$id');
      if (mounted) await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not close that fact.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not close that fact.');
    }
  }

  Future<void> _mergeEntity() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/graph/entities');
      if (!mounted) return;
      final others = jsonMaps(response.data)
          .where((item) => asJsonString(item['id']) != widget.entityId)
          .toList();
      if (others.isEmpty) {
        setState(
          () => _error = 'There is no other entity to merge into this one.',
        );
        return;
      }
      String? absorbId = asJsonString(others.first['id']);
      final saved = await showDialog<bool>(
        context: context,
        builder: (dialogContext) => StatefulBuilder(
          builder: (context, setDialog) => AlertDialog(
            title: const Text('Merge another entity into this one'),
            content: DropdownButtonFormField<String>(
              initialValue: absorbId,
              items: [
                for (final item in others)
                  DropdownMenuItem(
                    value: asJsonString(item['id']),
                    child: Text(asJsonString(item['name']) ?? 'Entity'),
                  ),
              ],
              onChanged: (value) => setDialog(() => absorbId = value),
            ),
            actions: [
              TextButton(
                onPressed: () => Navigator.pop(dialogContext, false),
                child: const Text('Cancel'),
              ),
              FilledButton(
                onPressed: () => Navigator.pop(dialogContext, true),
                child: const Text('Merge'),
              ),
            ],
          ),
        ),
      );
      if (saved != true || absorbId == null || !mounted) return;
      await widget.http.post<void>(
        '/api/v1/graph/entities/merge',
        data: {'keepId': widget.entityId, 'absorbId': absorbId},
      );
      if (mounted) await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not merge those entities.',
      );
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not merge those entities.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final details = _details;
    final entity = jsonObject(details?['entity']) ?? const {};
    final current = jsonMaps(details?['current']);
    final history = jsonMaps(details?['history']);
    final summary = asJsonString(entity['summary']);
    final updated = jsonDate(entity['updatedAt'], local: true);
    return Scaffold(
      appBar: AppBar(
        title: Text(asJsonString(entity['name']) ?? 'Entity'),
        actions: [
          if (details != null)
            PopupMenuButton<String>(
              onSelected: (value) {
                switch (value) {
                  case 'edit':
                    unawaited(_editEntity());
                  case 'fact':
                    unawaited(_addFact());
                  case 'merge':
                    unawaited(_mergeEntity());
                  case 'forget':
                    unawaited(_delete());
                }
              },
              itemBuilder: (_) => const [
                PopupMenuItem(value: 'edit', child: Text('Edit')),
                PopupMenuItem(value: 'fact', child: Text('Add fact')),
                PopupMenuItem(value: 'merge', child: Text('Merge into this')),
                PopupMenuItem(value: 'forget', child: Text('Forget')),
              ],
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
                            color: graphTypeColor(
                              asJsonString(entity['type']),
                              context,
                            ),
                          ),
                          for (final alias in jsonStrings(entity['aliases']))
                            StatusPill(
                              label: 'aka $alias',
                              color: JarvisColors.of(context).muted,
                            ),
                          if (updated != null)
                            StatusPill(
                              label: 'Updated ${formatGraphDay(updated)}',
                              color: JarvisColors.of(context).inkSoft,
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
      return SurfaceCard(
        child: Text(
          'Nothing recorded yet.',
          style: TextStyle(color: JarvisColors.of(context).inkSoft),
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
              color: currentFacts
                  ? JarvisColors.of(context).success
                  : JarvisColors.of(context).muted,
              size: 32,
            ),
            title: Text(
              '${asJsonString(fact['subjectName'])} ${humanPredicate(asJsonString(fact['predicate']) ?? '')} '
              '${asJsonString(fact['objectName']) ?? asJsonString(fact['objectValue']) ?? ''}',
              style: currentFacts
                  ? null
                  : TextStyle(
                      color: JarvisColors.of(context).inkSoft,
                      decoration: TextDecoration.lineThrough,
                    ),
            ),
            subtitle: Text(_factMeta(fact)),
            trailing: currentFacts
                ? IconButton(
                    tooltip: 'Close fact',
                    onPressed: () => unawaited(_closeFact(fact)),
                    icon: const Icon(
                      PhosphorIconsRegular.minusCircle,
                      size: 20,
                    ),
                  )
                : null,
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

class _EntityEditValues {
  const _EntityEditValues({
    required this.name,
    required this.type,
    required this.summary,
  });

  final String name;
  final String type;
  final String summary;
}

class _EntityEditDialog extends StatefulWidget {
  const _EntityEditDialog({
    required this.name,
    required this.type,
    required this.summary,
  });

  final String name;
  final String type;
  final String summary;

  @override
  State<_EntityEditDialog> createState() => _EntityEditDialogState();
}

class _EntityEditDialogState extends State<_EntityEditDialog> {
  late final _name = TextEditingController(text: widget.name);
  late final _summary = TextEditingController(text: widget.summary);
  late String _type = graphTypeOrder.contains(widget.type)
      ? widget.type
      : 'thing';

  @override
  void dispose() {
    _name.dispose();
    _summary.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Edit entity'),
    content: SizedBox(
      width: 420,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            controller: _name,
            decoration: const InputDecoration(labelText: 'Name'),
          ),
          const SizedBox(height: 8),
          DropdownButtonFormField<String>(
            initialValue: _type,
            decoration: const InputDecoration(labelText: 'Type'),
            items: [
              for (final item in graphTypeOrder)
                DropdownMenuItem(value: item, child: Text(item)),
            ],
            onChanged: (value) => setState(() => _type = value ?? _type),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _summary,
            maxLines: 3,
            decoration: const InputDecoration(labelText: 'Summary'),
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(
          context,
          _EntityEditValues(
            name: _name.text.trim(),
            type: _type,
            summary: _summary.text.trim(),
          ),
        ),
        child: const Text('Save'),
      ),
    ],
  );
}

class _FactValues {
  const _FactValues({
    required this.predicate,
    required this.object,
    required this.objectIsEntity,
  });

  final String predicate;
  final String object;
  final bool objectIsEntity;
}

class _AddFactDialog extends StatefulWidget {
  const _AddFactDialog();

  @override
  State<_AddFactDialog> createState() => _AddFactDialogState();
}

class _AddFactDialogState extends State<_AddFactDialog> {
  final _predicate = TextEditingController();
  final _object = TextEditingController();
  var _objectIsEntity = false;

  @override
  void dispose() {
    _predicate.dispose();
    _object.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Add a fact'),
    content: SizedBox(
      width: 420,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            controller: _predicate,
            decoration: const InputDecoration(
              labelText: 'Predicate',
              hintText: 'lives_in',
            ),
          ),
          const SizedBox(height: 8),
          TextField(
            controller: _object,
            decoration: const InputDecoration(
              labelText: 'Object or value',
              hintText: 'Amsterdam',
            ),
          ),
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            title: const Text('Object is another entity'),
            value: _objectIsEntity,
            onChanged: (value) => setState(() => _objectIsEntity = value),
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(
          context,
          _FactValues(
            predicate: _predicate.text.trim(),
            object: _object.text.trim(),
            objectIsEntity: _objectIsEntity,
          ),
        ),
        child: const Text('Save'),
      ),
    ],
  );
}
