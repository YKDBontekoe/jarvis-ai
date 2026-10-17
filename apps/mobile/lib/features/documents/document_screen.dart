import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../search/context_commands.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';

/// One draft: sections you edit in your own words, shorten or rebuild from
/// the sources, with every change reversible, a review checklist, a cited save
/// to the Library and a Markdown export. Nothing here sends or shares.
class DocumentScreen extends StatefulWidget {
  const DocumentScreen({required this.http, required this.documentId, super.key});

  final Dio http;
  final String documentId;

  @override
  State<DocumentScreen> createState() => _DocumentScreenState();
}

class _DocumentScreenState extends State<DocumentScreen> with PaletteContextMixin<DocumentScreen> {
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
        _error = firstProblemMessage(error.response?.data) ?? 'Could not load this draft.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _send(Future<Response<dynamic>> Function() call, {String? done}) async {
    setState(() => _busy = true);
    try {
      final response = await call();
      if (!mounted) return;
      setState(() => _view = jsonObject(response.data) ?? _view);
      if (done != null) _toast(done);
    } on DioException catch (error) {
      if (mounted) {
        _toast(firstProblemMessage(error.response?.data) ?? 'That did not work.');
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
    await _send(() => widget.http.put<dynamic>('$_path/sections/$id', data: {'body': text}));
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
        _toast(firstProblemMessage(error.response?.data) ?? 'Could not export it.');
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final sections = jsonMaps(_draft['sections']);
    final checklist = jsonMaps(_view?['checklist']);
    return Scaffold(
      appBar: AppBar(title: PageTitle(asJsonString(_draft['title']) ?? 'Draft')),
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
                        'Version ${asJsonInt(_draft['version'])}'
                        '${asJsonInt(_draft['savedVersion']) == asJsonInt(_draft['version']) ? ' · saved to your Library' : ' · not saved yet'}',
                        key: const Key('document-version'),
                        style: TextStyle(color: colors.muted),
                      ),
                      const SizedBox(height: 8),
                      for (final section in sections) _section(section, colors),
                      const SectionHeader('Before you share it'),
                      SurfaceCard(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            for (final item in checklist)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 6),
                                child: Text(
                                  '${item['ok'] == true ? '✓' : '•'} ${asJsonString(item['text']) ?? ''}',
                                  key: const Key('document-check'),
                                ),
                              ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 12),
                      Wrap(
                        spacing: 8,
                        children: [
                          FilledButton(
                            key: const Key('document-save'),
                            onPressed: _busy
                                ? null
                                : () => unawaited(
                                    _send(
                                      () => widget.http.post<dynamic>('$_path/save'),
                                      done: 'Saved to your Library with its sources.',
                                    ),
                                  ),
                            child: const Text('Save to Library'),
                          ),
                          OutlinedButton(
                            key: const Key('document-export'),
                            onPressed: _busy ? null : () => unawaited(_copyExport()),
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

  Widget _section(Map<String, dynamic> section, JarvisColors colors) {
    final id = asJsonString(section['id']) ?? '';
    final refs = jsonStrings(section['refs']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: SurfaceCard(
        key: Key('document-section-$id'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${asJsonString(section['heading']) ?? ''}${section['edited'] == true ? ' · edited' : ''}',
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 4),
            Text(asJsonString(section['body']) ?? ''),
            if (refs.isNotEmpty)
              Text(
                'Sources: ${refs.join(', ')}',
                style: TextStyle(color: colors.muted, fontSize: 12),
              ),
            Wrap(
              spacing: 4,
              children: [
                TextButton(
                  key: Key('document-edit-$id'),
                  onPressed: _busy ? null : () => unawaited(_edit(section)),
                  child: const Text('Edit'),
                ),
                TextButton(
                  key: Key('document-shorten-$id'),
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _send(() => widget.http.post<dynamic>(
                            '$_path/sections/$id/revise',
                            data: {'action': 'shorten'},
                          )),
                        ),
                  child: const Text('Shorten'),
                ),
                TextButton(
                  key: Key('document-rebuild-$id'),
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _send(() => widget.http.post<dynamic>(
                            '$_path/sections/$id/revise',
                            data: {'action': 'rebuild'},
                          )),
                        ),
                  child: const Text('Rebuild from sources'),
                ),
                if (section['previous'] != null)
                  TextButton(
                    key: Key('document-undo-$id'),
                    onPressed: _busy
                        ? null
                        : () => unawaited(
                            _send(() => widget.http.post<dynamic>('$_path/sections/$id/undo')),
                          ),
                    child: const Text('Undo'),
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
      TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
      FilledButton(
        key: const Key('document-edit-save'),
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: const Text('Save'),
      ),
    ],
  );
}
