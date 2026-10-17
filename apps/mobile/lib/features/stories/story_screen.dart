import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';

/// One story: the moments in your words (edit, remove or reorder them without
/// touching the records behind them), honest patterns with their limits, a
/// private save and a copy that leaves out what you choose.
class StoryScreen extends StatefulWidget {
  const StoryScreen({required this.http, required this.storyId, super.key});

  final Dio http;
  final String storyId;

  @override
  State<StoryScreen> createState() => _StoryScreenState();
}

class _StoryScreenState extends State<StoryScreen> {
  Map<String, dynamic>? _story;
  bool _loading = true;
  bool _busy = false;
  String? _error;
  final Set<String> _leaveOut = {};

  String get _path => '/api/v1/stories/${widget.storyId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      setState(() {
        _story = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this story.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _send(
    Future<Response<dynamic>> Function() call, {
    String? done,
  }) async {
    setState(() => _busy = true);
    try {
      final response = await call();
      if (!mounted) return;
      setState(() => _story = jsonObject(response.data) ?? _story);
      if (done != null) _toast(done);
    } on DioException catch (error) {
      if (mounted)
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _edit(Map<String, dynamic> item) async {
    final id = asJsonString(item['id']) ?? '';
    final text = await showJarvisDialog<String>(
      context: context,
      builder: (_) => _EditDialog(initial: asJsonString(item['text']) ?? ''),
    );
    if (text == null) return;
    await _send(
      () => widget.http.put<dynamic>('$_path/items/$id', data: {'text': text}),
    );
  }

  Future<void> _copyExport() async {
    try {
      final response = await widget.http.get<dynamic>(
        '$_path/export',
        queryParameters: {
          if (_leaveOut.isNotEmpty) 'redact': _leaveOut.join(','),
        },
        options: Options(responseType: ResponseType.plain),
      );
      await Clipboard.setData(ClipboardData(text: '${response.data}'));
      if (mounted) _toast('Copied as Markdown. Nothing was sent.');
    } on DioException catch (error) {
      if (mounted)
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Could not export it.',
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final story = _story ?? const <String, dynamic>{};
    final items = jsonMaps(story['items']);
    final patterns = jsonMaps(story['patterns']);
    final counts = jsonObject(story['counts']) ?? const {};
    final note = asJsonString(story['patternsNote']);
    final reflection = asJsonString(story['reflection']);
    return Scaffold(
      appBar: AppBar(title: PageTitle(asJsonString(story['title']) ?? 'Story')),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Text(
                        'Version ${asJsonInt(story['version'])}'
                        '${asJsonInt(story['savedVersion']) == asJsonInt(story['version']) ? ' · saved privately to your Library' : ' · not saved yet'}',
                        key: const Key('story-version'),
                        style: TextStyle(color: colors.muted),
                      ),
                      if (counts.isNotEmpty)
                        Text(
                          counts.entries
                              .map((e) => '${e.value} ${e.key}')
                              .join(' · '),
                          key: const Key('story-counts'),
                          style: TextStyle(color: colors.inkSoft, fontSize: 13),
                        ),
                      const SizedBox(height: 8),
                      if (items.isEmpty)
                        const Text(
                          'Nothing was recorded in this period.',
                          key: Key('story-empty'),
                        ),
                      for (final item in items) _item(item, colors),
                      if (patterns.isNotEmpty || note != null)
                        const SectionHeader('Patterns'),
                      for (final pattern in patterns)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 8),
                          child: SurfaceCard(
                            key: const Key('story-pattern'),
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  asJsonString(pattern['headline']) ?? '',
                                  style: const TextStyle(
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                                Text(asJsonString(pattern['detail']) ?? ''),
                                Text(
                                  asJsonString(pattern['limits']) ?? '',
                                  style: TextStyle(
                                    color: colors.muted,
                                    fontSize: 12,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      if (note != null)
                        Text(
                          note,
                          key: const Key('story-patterns-note'),
                          style: TextStyle(color: colors.muted),
                        ),
                      if (reflection != null) ...[
                        const SectionHeader('One thing to think about'),
                        Text(reflection, key: const Key('story-reflection')),
                      ],
                      const SectionHeader('Share a copy'),
                      const Text('Leave out:'),
                      Wrap(
                        spacing: 8,
                        children: [
                          for (final entry in const {
                            'journal': 'Journal',
                            'people': 'People',
                            'expense': 'Money',
                          }.entries)
                            FilterChip(
                              key: Key('story-leave-${entry.key}'),
                              label: Text(entry.value),
                              selected: _leaveOut.contains(entry.key),
                              onSelected: (on) => setState(
                                () => on
                                    ? _leaveOut.add(entry.key)
                                    : _leaveOut.remove(entry.key),
                              ),
                            ),
                        ],
                      ),
                      const SizedBox(height: 12),
                      Wrap(
                        spacing: 8,
                        children: [
                          FilledButton(
                            key: const Key('story-save'),
                            onPressed: _busy
                                ? null
                                : () => unawaited(
                                    _send(
                                      () => widget.http.post<dynamic>(
                                        '$_path/save',
                                      ),
                                      done: 'Saved privately to your Library.',
                                    ),
                                  ),
                            child: const Text('Save to Library'),
                          ),
                          OutlinedButton(
                            key: const Key('story-export'),
                            onPressed: _busy
                                ? null
                                : () => unawaited(_copyExport()),
                            child: const Text('Copy as Markdown'),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _item(Map<String, dynamic> item, JarvisColors colors) {
    final id = asJsonString(item['id']) ?? '';
    final removed = item['removed'] == true;
    final edited =
        asJsonString(item['text']) != asJsonString(item['originalText']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: SurfaceCard(
        key: Key('story-item-$id'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${asJsonString(item['date']) ?? ''} · ${asJsonString(item['kind']) ?? ''}${edited ? ' · edited' : ''}',
              style: TextStyle(color: colors.muted, fontSize: 12),
            ),
            Text(
              asJsonString(item['text']) ?? '',
              style: TextStyle(
                decoration: removed ? TextDecoration.lineThrough : null,
              ),
            ),
            Wrap(
              spacing: 4,
              children: [
                TextButton(
                  key: Key('story-edit-$id'),
                  onPressed: _busy ? null : () => unawaited(_edit(item)),
                  child: const Text('Edit'),
                ),
                TextButton(
                  key: Key('story-remove-$id'),
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _send(
                            () => widget.http.put<dynamic>(
                              '$_path/items/$id/removed',
                              data: {'removed': !removed},
                            ),
                          ),
                        ),
                  child: Text(removed ? 'Put back' : 'Leave out'),
                ),
                IconButton(
                  key: Key('story-up-$id'),
                  tooltip: 'Move earlier in the story',
                  icon: const Icon(Icons.arrow_upward, size: 18),
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _send(
                            () => widget.http.post<dynamic>(
                              '$_path/items/$id/move',
                              data: {'direction': 'up'},
                            ),
                          ),
                        ),
                ),
                IconButton(
                  key: Key('story-down-$id'),
                  tooltip: 'Move later in the story',
                  icon: const Icon(Icons.arrow_downward, size: 18),
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _send(
                            () => widget.http.post<dynamic>(
                              '$_path/items/$id/move',
                              data: {'direction': 'down'},
                            ),
                          ),
                        ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _EditDialog extends StatefulWidget {
  const _EditDialog({required this.initial});

  final String initial;

  @override
  State<_EditDialog> createState() => _EditDialogState();
}

class _EditDialogState extends State<_EditDialog> {
  late final _controller = TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Your wording'),
    content: TextField(
      key: const Key('story-edit-field'),
      controller: _controller,
      minLines: 2,
      maxLines: 6,
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('story-edit-save'),
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: const Text('Save'),
      ),
    ],
  );
}
