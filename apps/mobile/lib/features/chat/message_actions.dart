import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../json_maps.dart' show firstProblemMessage;
import '../../ui/phosphor_icons.dart';
import '../../ui/submit_state.dart';
import '../../ui/unsaved_changes_guard.dart';

/// A short title for a message saved on its own: its first line without
/// Markdown marks, cut at a word near 80 characters.
String suggestedNoteTitle(String content, {int max = 80}) {
  for (final raw in content.split('\n')) {
    final line = raw
        .replaceAll(RegExp(r'^[\s>#*\-•\d.)]+'), '')
        .replaceAll(RegExp(r'[*_`]+'), '')
        .trim();
    if (line.isEmpty) continue;
    if (line.length <= max) return line;
    final cut = line.substring(0, max);
    final space = cut.lastIndexOf(' ');
    return '${(space > max ~/ 2 ? cut.substring(0, space) : cut).trimRight()}…';
  }
  return '';
}

/// Saves a chat message to the Library after the owner has checked its title
/// and text, and links the note back to the chat it came from. Returns the new
/// note's id, or null when cancelled.
Future<String?> showSaveToLibrarySheet(
  BuildContext context, {
  required Dio http,
  required String content,
  String? conversationId,
}) => showModalBottomSheet<String>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _SaveToLibrarySheet(
    http: http,
    content: content,
    conversationId: conversationId,
  ),
);

class _SaveToLibrarySheet extends StatefulWidget {
  const _SaveToLibrarySheet({
    required this.http,
    required this.content,
    required this.conversationId,
  });

  final Dio http;
  final String content;
  final String? conversationId;

  @override
  State<_SaveToLibrarySheet> createState() => _SaveToLibrarySheetState();
}

class _SaveToLibrarySheetState extends State<_SaveToLibrarySheet>
    with SubmitState {
  late final _title = TextEditingController(
    text: suggestedNoteTitle(widget.content),
  );
  late final _body = TextEditingController(text: widget.content.trim());

  @override
  void dispose() {
    _title.dispose();
    _body.dispose();
    super.dispose();
  }

  Future<String?> _save() async {
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/library/notes',
        data: {'title': _title.text.trim(), 'content': _body.text.trim()},
      );
      final data = response.data;
      final id = data is Map ? data['id']?.toString() : null;
      final chat = widget.conversationId;
      if (id != null && chat != null) {
        // The note keeps a way back to the chat it came from. A failure here
        // never undoes the save.
        try {
          await widget.http.post<dynamic>(
            '/api/v1/entities/links',
            data: {
              'from': 'conversation:$chat',
              'to': 'library:$id',
              'relation': 'source',
            },
          );
        } on DioException {
          // The note is saved either way.
        }
      }
      _savedId = id ?? '';
      return null;
    } on DioException catch (error) {
      return firstProblemMessage(error.response?.data) ??
          describeApiError(error);
    }
  }

  String? _savedId;

  Future<void> _submit() async {
    if (submitting || _body.text.trim().isEmpty) return;
    if (await submit(_save) && mounted) {
      Navigator.of(context).pop(_savedId);
    }
  }

  @override
  Widget build(BuildContext context) => UnsavedChangesGuard(
    enabled: !submitting,
    changes: Listenable.merge([_title, _body]),
    snapshot: () => '${_title.text}\u0001${_body.text}',
    child: Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              'Save to Library',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 14),
            TextField(
              key: const Key('save-note-title'),
              controller: _title,
              maxLength: 200,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Title',
                counterText: '',
              ),
            ),
            const SizedBox(height: 12),
            TextField(
              textCapitalization: TextCapitalization.sentences,
              key: const Key('save-note-content'),
              controller: _body,
              minLines: 4,
              maxLines: 10,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(
                labelText: 'What to keep',
                alignLabelWithHint: true,
              ),
            ),
            if (widget.conversationId != null)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Row(
                  children: [
                    const Icon(PhosphorIconsRegular.link, size: 14),
                    const SizedBox(width: 6),
                    Text(
                      'Linked to this chat',
                      key: const Key('save-note-source'),
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
            submitErrorNotice(),
            const SizedBox(height: 14),
            FilledButton(
              key: const Key('save-note-submit'),
              onPressed: _body.text.trim().isEmpty || submitting
                  ? null
                  : () => unawaited(_submit()),
              child: submitting
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text('Save'),
            ),
          ],
        ),
      ),
    ),
  );
}
