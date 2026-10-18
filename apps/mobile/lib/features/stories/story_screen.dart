import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

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
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
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
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Could not export it.',
        );
      }
    }
  }

  static const _kindIcons = {
    'decision': PhosphorIconsRegular.gavel,
    'task': PhosphorIconsRegular.checkCircle,
    'journal': PhosphorIconsRegular.notebook,
    'expense': PhosphorIconsRegular.wallet,
    'contact': PhosphorIconsRegular.users,
    'conversation': PhosphorIconsRegular.chatCircle,
  };

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final story = _story ?? const <String, dynamic>{};
    final items = jsonMaps(story['items']);
    final patterns = jsonMaps(story['patterns']);
    final counts = jsonObject(story['counts']) ?? const {};
    final note = asJsonString(story['patternsNote']);
    final reflection = asJsonString(story['reflection']);
    final saved =
        asJsonInt(story['savedVersion']) == asJsonInt(story['version']);
    final kept = items.where((i) => i['removed'] != true).length;
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
                      FadeSlideIn(
                        child: HeroCard(
                          label:
                              '${asJsonString(story['from']) ?? ''} – ${asJsonString(story['to']) ?? ''}',
                          title: items.isEmpty
                              ? 'Nothing recorded'
                              : '$kept moment${kept == 1 ? '' : 's'}',
                          subtitle: counts.isEmpty
                              ? null
                              : counts.entries
                                    .map((e) => '${e.value} ${e.key}')
                                    .join(' · '),
                          footer: HeroNote(
                            'Version ${asJsonInt(story['version'])} · ${saved ? 'saved privately to your Library' : 'not saved yet'}',
                            key: const Key('story-version'),
                          ),
                        ),
                      ),
                      if (counts.isNotEmpty)
                        const SizedBox.shrink(key: Key('story-counts')),
                      const GroupLabel('Moments'),
                      if (items.isEmpty)
                        const EmptyState(
                          key: Key('story-empty'),
                          icon: PhosphorIconsRegular.notebook,
                          title: 'Nothing was recorded in this period',
                          message: 'Try a longer period.',
                        ),
                      for (var i = 0; i < items.length; i++) _item(items[i], i),
                      if (patterns.isNotEmpty || note != null)
                        const GroupLabel('Patterns'),
                      for (var i = 0; i < patterns.length; i++)
                        FeatureRow(
                          key: Key('story-pattern-$i'),
                          index: i,
                          icon: PhosphorIconsRegular.chartLine,
                          title: asJsonString(patterns[i]['headline']) ?? '',
                          subtitle:
                              '${asJsonString(patterns[i]['detail']) ?? ''}\n${asJsonString(patterns[i]['limits']) ?? ''}',
                        ),
                      if (note != null)
                        FeatureRow(
                          key: const Key('story-patterns-note'),
                          icon: PhosphorIconsRegular.info,
                          title: 'No patterns yet',
                          subtitle: note,
                          quiet: true,
                        ),
                      if (reflection != null) ...[
                        const GroupLabel('One thing to think about'),
                        FeatureRow(
                          key: const Key('story-reflection'),
                          icon: PhosphorIconsRegular.lightbulb,
                          title: reflection,
                        ),
                      ],
                      const GroupLabel('Share a copy'),
                      Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: Text(
                          'Leave out:',
                          style: TextStyle(
                            color: colors.inkSoft,
                            fontSize: 13.5,
                          ),
                        ),
                      ),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
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
                      const SizedBox(height: 16),
                      Wrap(
                        spacing: 10,
                        runSpacing: 10,
                        children: [
                          PillAction(
                            key: const Key('story-save'),
                            primary: true,
                            icon: PhosphorIconsRegular.bookmarkSimple,
                            label: 'Save to Library',
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
                          ),
                          PillAction(
                            key: const Key('story-export'),
                            icon: PhosphorIconsRegular.copy,
                            label: 'Copy as Markdown',
                            onPressed: _busy
                                ? null
                                : () => unawaited(_copyExport()),
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

  Widget _item(Map<String, dynamic> item, int index) {
    final id = asJsonString(item['id']) ?? '';
    final removed = item['removed'] == true;
    final kind = asJsonString(item['kind']) ?? '';
    final edited =
        asJsonString(item['text']) != asJsonString(item['originalText']);
    return FeatureRow(
      key: Key('story-item-$id'),
      index: index,
      icon: _kindIcons[kind] ?? PhosphorIconsRegular.circle,
      caption:
          '${asJsonString(item['date']) ?? ''} · $kind${edited ? ' · edited' : ''}',
      title: asJsonString(item['text']) ?? '',
      quiet: removed,
      strike: removed,
      trailing: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          CircleIconButton(
            key: Key('story-up-$id'),
            tooltip: 'Move earlier in the story',
            icon: Icons.arrow_upward,
            size: 36,
            onPressed: _busy ? null : () => unawaited(_move(id, 'up')),
          ),
          CircleIconButton(
            key: Key('story-down-$id'),
            tooltip: 'Move later in the story',
            icon: Icons.arrow_downward,
            size: 36,
            onPressed: _busy ? null : () => unawaited(_move(id, 'down')),
          ),
        ],
      ),
      actions: [
        PillAction(
          compact: true,
          key: Key('story-edit-$id'),
          icon: PhosphorIconsRegular.pencilSimple,
          label: 'Edit',
          onPressed: _busy ? null : () => unawaited(_edit(item)),
        ),
        PillAction(
          compact: true,
          key: Key('story-remove-$id'),
          label: removed ? 'Put back' : 'Leave out',
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
        ),
      ],
    );
  }

  Future<void> _move(String id, String direction) => _send(
    () => widget.http.post<dynamic>(
      '$_path/items/$id/move',
      data: {'direction': direction},
    ),
  );
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
