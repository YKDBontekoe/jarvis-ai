import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'story_screen.dart';

/// Recaps and retrospectives built from your own timeline. Drafting one
/// changes no records and shares nothing.
class StoriesScreen extends StatefulWidget {
  const StoriesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<StoriesScreen> createState() => _StoriesScreenState();
}

class _StoriesScreenState extends State<StoriesScreen> {
  List<Map<String, dynamic>> _stories = const [];
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
      final response = await widget.http.get<dynamic>('/api/v1/stories');
      if (!mounted) return;
      setState(() {
        _stories = [
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
            'Could not load your stories.';
      });
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => StoryScreen(http: widget.http, storyId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create() async {
    final request = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _NewStorySheet(),
    );
    if (request == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/stories',
        data: request,
      );
      final id = jsonString(jsonObject(response.data) ?? const {}, 'id');
      if (id != null && mounted) await _open(id);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Could not draft the story.',
            ),
          ),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Stories'),
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
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: _stories.isEmpty
                      ? Padding(
                          padding: const EdgeInsets.only(top: 24),
                          child: EmptyState(
                            icon: PhosphorIconsRegular.bookOpen,
                            title: 'Look back on a stretch of your life',
                            message:
                                'Pick a period and get a recap built from your '
                                'decisions, tasks and journal. You edit it; '
                                'nothing is shared.',
                            action: FilledButton(
                              key: const Key('story-start'),
                              onPressed: _create,
                              child: const Text('Draft a recap'),
                            ),
                          ),
                        )
                      : Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            for (final story in _stories)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 10),
                                child: SurfaceCard(
                                  key: Key('story-${story['id']}'),
                                  onTap: () =>
                                      unawaited(_open(story['id'] as String)),
                                  child: Column(
                                    crossAxisAlignment:
                                        CrossAxisAlignment.start,
                                    children: [
                                      Text(
                                        asJsonString(story['title']) ?? 'Story',
                                        style: const TextStyle(
                                          fontWeight: FontWeight.w600,
                                        ),
                                      ),
                                      Text(
                                        '${asJsonString(story['from']) ?? ''} – ${asJsonString(story['to']) ?? ''}'
                                        '${asJsonInt(story['savedVersion']) == asJsonInt(story['version']) ? ' · saved' : ''}',
                                        style: TextStyle(
                                          color: colors.inkSoft,
                                          fontSize: 13,
                                        ),
                                      ),
                                    ],
                                  ),
                                ),
                              ),
                          ],
                        ),
                ),
              ],
            ),
    );
  }
}

class _NewStorySheet extends StatefulWidget {
  const _NewStorySheet();

  @override
  State<_NewStorySheet> createState() => _NewStorySheetState();
}

class _NewStorySheetState extends State<_NewStorySheet> {
  int _days = 30;
  String _style = 'recap';
  final Set<String> _extra = {};

  static const _styles = {
    'recap': 'Recap',
    'retrospective': 'Retrospective',
    'what_changed': 'What changed',
  };
  static const _optIn = {
    'expense': 'Money',
    'contact': 'People',
    'conversation': 'Chats',
  };

  String _date(DateTime value) =>
      '${value.year.toString().padLeft(4, '0')}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')}';

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('New story', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            children: [
              for (final days in const [7, 30, 90])
                ChoiceChip(
                  key: Key('story-days-$days'),
                  label: Text('Last $days days'),
                  selected: _days == days,
                  onSelected: (_) => setState(() => _days = days),
                ),
            ],
          ),
          Wrap(
            spacing: 8,
            children: [
              for (final entry in _styles.entries)
                ChoiceChip(
                  key: Key('story-style-${entry.key}'),
                  label: Text(entry.value),
                  selected: _style == entry.key,
                  onSelected: (_) => setState(() => _style = entry.key),
                ),
            ],
          ),
          const SizedBox(height: 8),
          const Text(
            'Decisions, tasks and journal are included. Add more only if you want:',
          ),
          Wrap(
            spacing: 8,
            children: [
              for (final entry in _optIn.entries)
                FilterChip(
                  key: Key('story-include-${entry.key}'),
                  label: Text(entry.value),
                  selected: _extra.contains(entry.key),
                  onSelected: (on) => setState(
                    () => on ? _extra.add(entry.key) : _extra.remove(entry.key),
                  ),
                ),
            ],
          ),
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('story-create'),
            onPressed: () {
              final now = DateTime.now();
              Navigator.pop<Map<String, Object?>>(context, {
                'from': _date(now.subtract(Duration(days: _days - 1))),
                'to': _date(now),
                'style': _style,
                if (_extra.isNotEmpty)
                  'kinds': ['decision', 'task', 'journal', ..._extra],
              });
            },
            child: const Text('Draft it'),
          ),
        ],
      ),
    ),
  );
}
