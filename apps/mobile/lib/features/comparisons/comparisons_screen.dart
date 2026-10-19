import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'comparison_screen.dart';

/// Comparisons: weigh a few options against what matters, see what is still unknown, and decide.
class ComparisonsScreen extends StatefulWidget {
  const ComparisonsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ComparisonsScreen> createState() => _ComparisonsScreenState();
}

class _ComparisonsScreenState extends State<ComparisonsScreen> {
  List<Map<String, dynamic>> _items = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/comparisons');
      if (!mounted) return;
      setState(() {
        _items = [
          for (final item in jsonMaps(response.data))
            if (jsonString(item, 'id') != null) item,
        ];
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your comparisons.';
      });
    }
  }

  Future<void> _create() async {
    final draft = await showModalBottomSheet<_NewComparison>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _NewComparisonSheet(),
    );
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/comparisons',
        data: {'definition': draft.toJson()},
      );
      final id = jsonString(jsonObject(response.data) ?? const {}, 'id');
      if (!mounted) return;
      if (id != null) await _open(id);
      if (mounted) unawaited(_load());
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Could not create the comparison.',
            ),
          ),
        );
    }
  }

  Future<void> _open(String id) => Navigator.of(context).push<void>(
    MaterialPageRoute(
      builder: (_) => ComparisonScreen(http: widget.http, comparisonId: id),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Comparisons'),
        actions: [
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: _create,
          ),
        ],
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : _items.isEmpty
          ? EmptyState(
              icon: PhosphorIconsRegular.sliders,
              title: 'Weigh your options',
              message:
                  'List what you are choosing between and what matters. '
                  'Jarvis ranks them, shows what is still unknown, and '
                  'helps you decide.',
              action: FilledButton(
                key: const Key('comparison-start'),
                onPressed: _create,
                child: const Text('Start a comparison'),
              ),
            )
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
                children: [
                  for (final item in _items)
                    ContentWidth(
                      child: Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: SurfaceCard(
                          key: Key('comparison-${item['id']}'),
                          onTap: () => unawaited(_open(item['id'] as String)),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                asJsonString(item['title']) ?? 'Comparison',
                                style: TextStyle(
                                  fontSize: 16,
                                  fontWeight: FontWeight.w600,
                                  color: colors.ink,
                                ),
                              ),
                              const SizedBox(height: 4),
                              Text(
                                [
                                  '${asJsonInt(item['options'])} options',
                                  '${asJsonInt(item['criteria'])} criteria',
                                  if (asJsonString(item['leader'])
                                      case final leader?)
                                    'Leading: $leader',
                                  if (asJsonBool(item['decided'])) 'Decided',
                                ].join(' · '),
                                style: TextStyle(
                                  fontSize: 13,
                                  color: colors.inkSoft,
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                ],
              ),
            ),
    );
  }
}

class _NewComparison {
  const _NewComparison(this.title, this.question, this.options, this.criteria);

  final String title;
  final String? question;
  final List<String> options;
  final List<String> criteria;

  static String _slug(String name, Set<String> used) {
    var slug = name
        .toLowerCase()
        .replaceAll(RegExp(r'[^a-z0-9]+'), '-')
        .replaceAll(RegExp(r'^-+|-+$'), '');
    if (slug.isEmpty) slug = 'item';
    if (slug.length > 24) slug = slug.substring(0, 24);
    var candidate = slug;
    for (var index = 2; used.contains(candidate); index++) {
      candidate = '$slug-$index';
    }
    used.add(candidate);
    return candidate;
  }

  Map<String, Object?> toJson() {
    final usedCriteria = <String>{};
    final usedOptions = <String>{};
    return {
      'title': title,
      'question': question,
      'criteria': [
        for (final name in criteria)
          {'id': _slug(name, usedCriteria), 'name': name, 'weight': 5},
      ],
      'options': [
        for (final name in options)
          {
            'id': _slug(name, usedOptions),
            'name': name,
            'summary': null,
            'cells': <String, Object?>{},
          },
      ],
    };
  }
}

class _NewComparisonSheet extends StatefulWidget {
  const _NewComparisonSheet();

  @override
  State<_NewComparisonSheet> createState() => _NewComparisonSheetState();
}

class _NewComparisonSheetState extends State<_NewComparisonSheet> {
  final _title = TextEditingController();
  final _options = TextEditingController();
  final _criteria = TextEditingController();

  @override
  void dispose() {
    _title.dispose();
    _options.dispose();
    _criteria.dispose();
    super.dispose();
  }

  static List<String> _lines(String text) => [
    for (final line in text.split('\n'))
      if (line.trim().isNotEmpty) line.trim(),
  ];

  bool get _valid =>
      _title.text.trim().isNotEmpty &&
      _lines(_options.text).length >= 2 &&
      _lines(_criteria.text).isNotEmpty;

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'New comparison',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('comparison-title-field'),
            controller: _title,
            maxLength: 120,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'What are you deciding?',
              hintText: 'Which boiler?',
            ),
          ),
          TextField(
            key: const Key('comparison-options-field'),
            controller: _options,
            minLines: 2,
            maxLines: 5,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'Options, one per line',
              hintText: 'Bosch\nVaillant',
            ),
          ),
          const SizedBox(height: 8),
          TextField(
            key: const Key('comparison-criteria-field'),
            controller: _criteria,
            minLines: 2,
            maxLines: 5,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'What matters, one per line',
              hintText: 'Price\nNoise\nWarranty',
            ),
          ),
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('comparison-create'),
            onPressed: _valid
                ? () => Navigator.pop(
                    context,
                    _NewComparison(
                      _title.text.trim(),
                      null,
                      _lines(_options.text),
                      _lines(_criteria.text),
                    ),
                  )
                : null,
            child: const Text('Create'),
          ),
        ],
      ),
    ),
  );
}
