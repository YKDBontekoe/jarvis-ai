import 'dart:async';
import 'dart:io';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../reminders/reminders_screen.dart';
import 'capture_models.dart';
import 'capture_store.dart';

/// What was done with a capture, with a way to open the result.
class _Handled {
  const _Handled(this.label, this.open);

  final String label;
  final VoidCallback? open;
}

/// Things shared into Jarvis from other apps, waiting for the owner. Nothing
/// here has been sent to the assistant: each one is previewed and edited first,
/// then saved to the Library, asked about, turned into a reminder, or discarded.
class CapturesScreen extends StatefulWidget {
  const CapturesScreen({
    required this.http,
    this.store,
    this.onOpenConversation,
    this.onOpenDestination,
    this.onAskInChat,
    super.key,
  });

  final Dio http;
  final CaptureStore? store;
  final Future<void> Function(String conversationId)? onOpenConversation;

  /// Opens a page such as `entity:library:<id>` or `library`.
  final ValueChanged<String>? onOpenDestination;

  /// Starts a chat with this text; only offered once the owner chose to ask.
  final ValueChanged<String>? onAskInChat;

  @override
  State<CapturesScreen> createState() => _CapturesScreenState();
}

class _CapturesScreenState extends State<CapturesScreen> {
  final Map<String, TextEditingController> _edits = {};
  final Set<String> _busy = {};
  final List<_Handled> _handled = [];
  String? _error;

  CaptureStore get _store => widget.store ?? CaptureStore.instance;

  @override
  void initState() {
    super.initState();
    unawaited(_store.refresh());
    unawaited(_store.markPresented());
  }

  @override
  void dispose() {
    for (final controller in _edits.values) {
      controller.dispose();
    }
    super.dispose();
  }

  TextEditingController _editFor(CaptureItem item) =>
      _edits.putIfAbsent(item.id, () {
        final text = switch (item.kind) {
          CaptureKind.link => item.text ?? '',
          CaptureKind.text => item.text ?? '',
          CaptureKind.file => '',
        };
        return TextEditingController(text: text);
      });

  /// Runs one action on one capture; others stay usable meanwhile.
  Future<void> _run(
    CaptureItem item,
    Future<_Handled?> Function() action,
  ) async {
    if (!_busy.add(item.id)) return;
    setState(() => _error = null);
    try {
      final handled = await action();
      if (!mounted) return;
      if (handled != null) {
        _handled.insert(0, handled);
        await _store.remove(item.id);
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              describeApiError(error),
        );
      }
    } catch (_) {
      if (mounted)
        setState(() => _error = 'Jarvis could not do that. Try again.');
    } finally {
      if (mounted) setState(() => _busy.remove(item.id));
    }
  }

  String _noteTitle(CaptureItem item, String text) {
    final first = text
        .split('\n')
        .map((l) => l.trim())
        .firstWhere((l) => l.isNotEmpty, orElse: () => '');
    if (first.isEmpty) return item.title;
    return first.length > 80 ? '${first.substring(0, 80)}…' : first;
  }

  Future<String> _upload(CaptureItem item) async {
    final path = item.filePath!;
    final bytes = await File(path).readAsBytes();
    final response = await widget.http.post<dynamic>(
      '/api/v1/files',
      data: FormData.fromMap({
        'file': MultipartFile.fromBytes(
          bytes,
          filename: item.fileName ?? 'capture',
          contentType: DioMediaType.parse(
            item.mimeType ?? 'application/octet-stream',
          ),
        ),
      }),
      options: Options(
        sendTimeout: const Duration(minutes: 2),
        receiveTimeout: const Duration(minutes: 2),
      ),
    );
    final id = asJsonString(jsonObject(response.data)?['id']);
    if (id == null) throw const FormatException('Missing file id.');
    return id;
  }

  Future<void> _saveToLibrary(CaptureItem item) => _run(item, () async {
    final text = _editFor(item).text.trim();
    switch (item.kind) {
      case CaptureKind.file:
        await _upload(item);
        return _Handled(
          'Saved “${item.title}” to Files',
          () => widget.onOpenDestination?.call('files'),
        );
      case CaptureKind.link:
        final response = await widget.http.post<dynamic>(
          '/api/v1/library/clip',
          data: {'url': item.url, if (text.isNotEmpty) 'note': text},
        );
        final id = asJsonString(jsonObject(response.data)?['id']);
        return _Handled(
          'Saved the link to your Library',
          () => widget.onOpenDestination?.call(
            id == null ? 'library' : 'entity:library:$id',
          ),
        );
      case CaptureKind.text:
        if (text.isEmpty) throw const FormatException('Nothing to save.');
        final response = await widget.http.post<dynamic>(
          '/api/v1/library/notes',
          data: {'title': _noteTitle(item, text), 'content': text},
        );
        final id = asJsonString(jsonObject(response.data)?['id']);
        return _Handled(
          'Saved to your Library',
          () => widget.onOpenDestination?.call(
            id == null ? 'library' : 'entity:library:$id',
          ),
        );
    }
  });

  Future<void> _ask(CaptureItem item) => _run(item, () async {
    if (item.kind == CaptureKind.file) {
      final fileId = await _upload(item);
      final response = await widget.http.post<dynamic>(
        '/api/v1/files/$fileId/ask',
      );
      final chat = asJsonString(jsonObject(response.data)?['conversationId']);
      if (chat == null) throw const FormatException('No chat.');
      await widget.onOpenConversation?.call(chat);
      return _Handled(
        'Started a chat about “${item.title}”',
        () => unawaited(widget.onOpenConversation?.call(chat)),
      );
    }
    final text = _editFor(item).text.trim();
    final prompt = item.kind == CaptureKind.link
        ? [if (text.isNotEmpty) text, item.url].join('\n')
        : text;
    if (prompt.trim().isEmpty) throw const FormatException('Nothing to ask.');
    widget.onAskInChat?.call(prompt);
    if (mounted) unawaited(Navigator.of(context).maybePop());
    return const _Handled('Sent to Jarvis in a chat', null);
  });

  Future<void> _remind(CaptureItem item) async {
    final text = _editFor(item).text.trim();
    final title = item.kind == CaptureKind.file
        ? item.title
        : _noteTitle(item, text.isEmpty ? (item.url ?? '') : text);
    final saved = await Navigator.of(context).push<bool>(
      MaterialPageRoute<bool>(
        builder: (context) => RemindersScreen(
          http: widget.http,
          startCreating: true,
          initialTitle: title,
          onCreateDone: (message) => Navigator.of(context).pop(message != null),
        ),
      ),
    );
    if (saved == true && mounted) {
      setState(
        () => _handled.insert(
          0,
          _Handled(
            'Reminder set for “${item.title}”',
            () => widget.onOpenDestination?.call('reminders'),
          ),
        ),
      );
      await _store.remove(item.id);
    }
  }

  Future<void> _discard(CaptureItem item) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Discard this capture?',
      message: 'It will be removed from this phone. Nothing is sent anywhere.',
      cancelLabel: 'Keep',
      confirmLabel: 'Discard',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (confirmed && mounted) await _store.remove(item.id);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('Captures')),
    body: ListenableBuilder(
      listenable: _store,
      builder: (context, _) {
        final items = _store.items;
        if (items.isEmpty && _handled.isEmpty) {
          return const EmptyState(
            icon: PhosphorIconsRegular.tray,
            title: 'Nothing shared yet',
            message: 'Use Share in another app and choose Jarvis. What you share waits here until you decide what to do with it.',
          );
        }
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  if (_error != null)
                    InlineNotice(
                      key: const Key('captures-error'),
                      message: _error!,
                      tone: NoticeTone.danger,
                      margin: const EdgeInsets.only(bottom: 12),
                    ),
                  for (final handled in _handled)
                    InlineNotice(
                      key: const Key('capture-handled'),
                      tone: NoticeTone.success,
                      message: handled.label,
                      margin: const EdgeInsets.only(bottom: 10),
                      actions: [
                        if (handled.open != null)
                          TextButton(
                            key: const Key('capture-open-result'),
                            onPressed: handled.open,
                            child: const Text('Open'),
                          ),
                      ],
                    ),
                  if (items.isNotEmpty)
                    GroupLabel('${items.length} waiting for you'),
                  for (final item in items) _card(context, item),
                ],
              ),
            ),
          ],
        );
      },
    ),
  );

  Widget _card(BuildContext context, CaptureItem item) {
    final colors = JarvisColors.of(context);
    final busy = _busy.contains(item.id);
    final problem = item.problem;
    final icon = switch (item.kind) {
      CaptureKind.file when item.isImage => PhosphorIconsRegular.fileImage,
      CaptureKind.file => PhosphorIconsRegular.fileText,
      CaptureKind.link => PhosphorIconsRegular.link,
      CaptureKind.text => PhosphorIconsRegular.note,
    };
    return Padding(
      key: Key('capture-${item.id}'),
      padding: const EdgeInsets.only(bottom: 12),
      child: SurfaceCard(
        padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                IconBadge(icon: icon, size: 40),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        item.title,
                        maxLines: 2,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                      Text(
                        MaterialLocalizations.of(context)
                            .formatMediumDate(item.createdAt),
                        style: TextStyle(color: colors.muted, fontSize: 12.5),
                      ),
                    ],
                  ),
                ),
                if (busy)
                  const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
              ],
            ),
            if (problem != null)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: InlineNotice(
                  key: Key('capture-problem-${item.id}'),
                  message: problem,
                  tone: NoticeTone.warning,
                ),
              )
            else if (item.kind != CaptureKind.file)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: TextField(
                  key: Key('capture-edit-${item.id}'),
                  controller: _editFor(item),
                  minLines: 2,
                  maxLines: 8,
                  enabled: !busy,
                  decoration: InputDecoration(
                    labelText: item.kind == CaptureKind.link
                        ? 'Add a note about this link (optional)'
                        : 'Edit before you decide',
                    alignLabelWithHint: true,
                  ),
                ),
              )
            else if (item.isImage && item.filePath != null)
              Padding(
                padding: const EdgeInsets.only(top: 10),
                child: ClipRRect(
                  borderRadius: BorderRadius.circular(12),
                  child: Image.file(
                    File(item.filePath!),
                    height: 160,
                    fit: BoxFit.cover,
                    errorBuilder: (_, _, _) => const SizedBox.shrink(),
                  ),
                ),
              ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                if (problem == null) ...[
                  PillAction(
                    key: Key('capture-save-${item.id}'),
                    compact: true,
                    primary: true,
                    icon: PhosphorIconsRegular.bookmarkSimple,
                    label: item.kind == CaptureKind.file
                        ? 'Save to Files'
                        : 'Save to Library',
                    onPressed: busy
                        ? null
                        : () => unawaited(_saveToLibrary(item)),
                  ),
                  PillAction(
                    key: Key('capture-ask-${item.id}'),
                    compact: true,
                    icon: PhosphorIconsRegular.chatCircle,
                    label: 'Ask Jarvis',
                    onPressed: busy ? null : () => unawaited(_ask(item)),
                  ),
                  PillAction(
                    key: Key('capture-remind-${item.id}'),
                    compact: true,
                    icon: PhosphorIconsRegular.alarm,
                    label: 'Create reminder',
                    onPressed: busy ? null : () => unawaited(_remind(item)),
                  ),
                ],
                PillAction(
                  key: Key('capture-discard-${item.id}'),
                  compact: true,
                  label: 'Discard',
                  onPressed: busy ? null : () => unawaited(_discard(item)),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
