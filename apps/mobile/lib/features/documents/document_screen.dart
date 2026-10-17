import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../search/context_commands.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// One draft: sections you edit in your own words, shorten or rebuild from
/// the sources, with every change reversible, a review checklist, a cited save
/// to the Library and a Markdown export. Nothing here sends or shares.
class DocumentScreen extends StatefulWidget {
  const DocumentScreen({
    required this.http,
    required this.documentId,
    super.key,
  });

  final Dio http;
  final String documentId;

  @override
  State<DocumentScreen> createState() => _DocumentScreenState();
}

class _DocumentScreenState extends State<DocumentScreen>
    with PaletteContextMixin<DocumentScreen> {
  @override
  String get paletteContextValue => 'document:${widget.documentId}';

  Map<String, dynamic>? _view;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  String get _path => '/api/v1/documents/${widget.documentId}';
  Map<String, dynamic> get _draft => jsonObject(_view?['draft']) ?? const {};

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
        _view = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this draft.';
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
      setState(() => _view = jsonObject(response.data) ?? _view);
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

  Future<void> _edit(Map<String, dynamic> section) async {
    final id = asJsonString(section['id']) ?? '';
    final text = await showJarvisDialog<String>(
      context: context,
      builder: (_) => _EditDialog(
        heading: asJsonString(section['heading']) ?? '',
        initial: asJsonString(section['body']) ?? '',
      ),
    );
    if (text == null) return;
    await _send(
      () =>
          widget.http.put<dynamic>('$_path/sections/$id', data: {'body': text}),
    );
  }

  Future<void> _copyExport() async {
    try {
      final response = await widget.http.get<dynamic>(
        '$_path/export',
        queryParameters: {'format': 'md'},
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

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final sections = jsonMaps(_draft['sections']);
    final checklist = jsonMaps(_view?['checklist']);
    final saved =
        asJsonInt(_draft['savedVersion']) == asJsonInt(_draft['version']);
    final open = checklist.where((c) => c['ok'] != true).length;
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(asJsonString(_draft['title']) ?? 'Draft'),
      ),
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
                          label: 'Version ${asJsonInt(_draft['version'])}',
                          title: open == 0
                              ? 'Ready to share'
                              : '$open thing${open == 1 ? '' : 's'} to check',
                          subtitle: 'Editing, saving and copying send nothing. Sharing is a step you take yourself.',
                          footer: Text(
                            saved ? 'Saved to your Library' : 'Not saved yet',
                            key: const Key('document-version'),
                            style: TextStyle(
                              fontFamily: 'Geist',
                              fontSize: 13,
                              color: colors.onInk.withValues(alpha: .6),
                            ),
                          ),
                        ),
                      ),
                      const GroupLabel('Sections'),
                      for (var i = 0; i < sections.length; i++)
                        _section(sections[i], i),
                      GroupLabel(
                        'Before you share it',
                        count: open == 0 ? null : open,
                      ),
                      for (var i = 0; i < checklist.length; i++)
                        FeatureRow(
                          key: Key('document-check-$i'),
                          index: i,
                          icon: checklist[i]['ok'] == true
                              ? PhosphorIconsRegular.checkCircle
                              : PhosphorIconsRegular.warningCircle,
                          badgeColor: checklist[i]['ok'] == true
                              ? colors.accent
                              : colors.warning,
                          title: asJsonString(checklist[i]['text']) ?? '',
                        ),
                      const SizedBox(height: 8),
                      Wrap(
                        spacing: 10,
                        runSpacing: 10,
                        children: [
                          PillAction(
                            key: const Key('document-save'),
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
                                      done: 'Saved to your Library with its sources.',
                                    ),
                                  ),
                          ),
                          PillAction(
                            key: const Key('document-export'),
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

  Widget _section(Map<String, dynamic> section, int index) {
    final id = asJsonString(section['id']) ?? '';
    final refs = jsonStrings(section['refs']);
    Future<void> revise(String action) => _send(
      () => widget.http.post<dynamic>(
        '$_path/sections/$id/revise',
        data: {'action': action},
      ),
    );
    return FeatureRow(
      key: Key('document-section-$id'),
      index: index,
      icon: PhosphorIconsRegular.fileText,
      caption: section['edited'] == true ? 'Edited' : null,
      title: asJsonString(section['heading']) ?? '',
      subtitle: [
        asJsonString(section['body']) ?? '',
        if (refs.isNotEmpty) 'Sources: ${refs.join(', ')}',
      ].join('\n'),
      actions: [
        PillAction(
          compact: true,
          key: Key('document-edit-$id'),
          icon: PhosphorIconsRegular.pencilSimple,
          label: 'Edit',
          onPressed: _busy ? null : () => unawaited(_edit(section)),
        ),
        PillAction(
          compact: true,
          key: Key('document-shorten-$id'),
          label: 'Shorten',
          onPressed: _busy ? null : () => unawaited(revise('shorten')),
        ),
        PillAction(
          compact: true,
          key: Key('document-rebuild-$id'),
          icon: PhosphorIconsRegular.arrowsClockwise,
          label: 'Rebuild',
          onPressed: _busy ? null : () => unawaited(revise('rebuild')),
        ),
        if (section['previous'] != null)
          PillAction(
            compact: true,
            key: Key('document-undo-$id'),
            icon: PhosphorIconsRegular.arrowBendUpLeft,
            label: 'Undo',
            onPressed: _busy
                ? null
                : () => unawaited(
                    _send(
                      () =>
                          widget.http.post<dynamic>('$_path/sections/$id/undo'),
                    ),
                  ),
          ),
      ],
    );
  }
}

class _EditDialog extends StatefulWidget {
  const _EditDialog({required this.heading, required this.initial});

  final String heading;
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
    title: Text(widget.heading),
    content: TextField(
      key: const Key('document-edit-field'),
      controller: _controller,
      minLines: 3,
      maxLines: 8,
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('document-edit-save'),
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: const Text('Save'),
      ),
    ],
  );
}
