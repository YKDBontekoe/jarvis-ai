import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../ui/phosphor_icons.dart';

import 'conversation_export.dart';
import 'conversation_groups.dart';
import '../projects/move_to_project.dart';
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';

typedef ConversationPickerResult = ({
  String? conversationId,
  bool deletedCurrent,
});

class ConversationsScreen extends StatefulWidget {
  const ConversationsScreen({
    required this.http,
    required this.selectedConversationId,
    super.key,
  });

  final Dio http;
  final String? selectedConversationId;

  @override
  State<ConversationsScreen> createState() => _ConversationsScreenState();
}

class _ConversationsScreenState extends State<ConversationsScreen> {
  List<Map<String, dynamic>> _conversations = [];
  bool _loading = true;
  bool _creating = false;
  String? _error;
  int _requestRevision = 0;
  final _search = TextEditingController();
  String _query = '';
  final Set<String> _updating = {};

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  List<Map<String, dynamic>> get _visible {
    final query = _query.trim().toLowerCase();
    if (query.isEmpty) return _conversations;
    return _conversations
        .where(
          (item) => (asJsonString(item['title']) ?? 'New conversation')
              .toLowerCase()
              .contains(query),
        )
        .toList();
  }

  /// Sends a rename or pin change and swaps in the server's copy of the row.
  /// The row changes right away and goes back if Jarvis refuses.
  Future<void> _update(
    Map<String, dynamic> conversation, {
    String? title,
    bool? pinned,
    required String failure,
  }) async {
    final id = jsonString(conversation, 'id');
    if (id == null || _updating.contains(id)) return;
    final previous = Map<String, dynamic>.of(conversation);
    void replace(Map<String, dynamic> next) {
      final index = _conversations.indexWhere((item) => item['id'] == id);
      if (index >= 0) _conversations[index] = next;
    }

    setState(() {
      _updating.add(id);
      _error = null;
      replace({...conversation, 'title': ?title, 'pinned': ?pinned});
      _sort();
    });
    try {
      final response = await widget.http.patch<dynamic>(
        '/api/v1/conversations/$id',
        data: {'title': ?title, 'pinned': ?pinned},
      );
      final updated = jsonObject(response.data);
      if (!mounted) return;
      setState(() {
        if (updated != null && jsonString(updated, 'id') == id) {
          replace({...previous, ...updated});
        }
        _sort();
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        replace(previous);
        _sort();
        _error = failure;
      });
    } finally {
      if (mounted) setState(() => _updating.remove(id));
    }
  }

  /// Pinned first (keeping server order inside each part), like the API.
  void _sort() {
    final pinned = _conversations.where((item) => item['pinned'] == true);
    final rest = _conversations.where((item) => item['pinned'] != true);
    _conversations = [...pinned, ...rest];
  }

  Future<void> _rename(Map<String, dynamic> conversation) async {
    final current = asJsonString(conversation['title']) ?? 'New conversation';
    final title = await showJarvisDialog<String>(
      context: context,
      builder: (_) => _RenameDialog(initial: current),
    );
    if (title == null || title == current || !mounted) return;
    await _update(
      conversation,
      title: title,
      failure: 'Jarvis could not rename this conversation.',
    );
  }

  /// Copies the whole conversation as Markdown, fetched fresh so messages
  /// that are not loaded in the chat are included too.
  Future<void> _copyAsMarkdown(Map<String, dynamic> conversation) async {
    final id = jsonString(conversation, 'id');
    if (id == null || _updating.contains(id)) return;
    setState(() {
      _updating.add(id);
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/conversations/$id',
      );
      final body = jsonObject(response.data);
      final markdown = conversationMarkdown(
        title:
            asJsonString(body?['title']) ??
            asJsonString(conversation['title']) ??
            'Conversation',
        messages: jsonMaps(body?['messages']),
      );
      await Clipboard.setData(ClipboardData(text: markdown));
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          const SnackBar(content: Text('Conversation copied as Markdown')),
        );
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Jarvis could not copy this conversation.');
      }
    } finally {
      if (mounted) setState(() => _updating.remove(id));
    }
  }

  Future<void> _moveToProject(Map<String, dynamic> conversation) async {
    final id = jsonString(conversation, 'id');
    if (id == null || _updating.contains(id)) return;
    final moved = await showMoveToProjectSheet(
      context,
      http: widget.http,
      kind: 'conversations',
      id: id,
      currentProjectId: asJsonString(conversation['projectId']),
    );
    if (moved && mounted) await _load();
  }

  Future<void> _togglePin(Map<String, dynamic> conversation) => _update(
    conversation,
    pinned: conversation['pinned'] != true,
    failure: conversation['pinned'] == true
        ? 'Jarvis could not unpin this conversation.'
        : 'Jarvis could not pin this conversation.',
  );

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/conversations');
      if (mounted && revision == _requestRevision) {
        setState(() => _conversations = jsonMaps(response.data));
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load conversations.');
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load conversations.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _createConversation() async {
    setState(() => _creating = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/conversations',
        data: const {'title': 'New conversation'},
      );
      final id = asJsonString(jsonObject(response.data)?['id']);
      if (id == null || id.isEmpty) {
        throw const FormatException('Missing conversation ID.');
      }
      if (mounted) {
        Navigator.of(context).pop((conversationId: id, deletedCurrent: false));
      }
    } on DioException {
      if (mounted) {
        setState(() => _error = 'Jarvis could not create a conversation.');
      }
    } on FormatException {
      if (mounted) {
        setState(() => _error = 'Jarvis returned an invalid conversation.');
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Jarvis could not create a conversation.');
      }
    } finally {
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _deleteConversation(Map<String, dynamic> conversation) async {
    final id = jsonString(conversation, 'id');
    if (id == null) return;
    final title = asJsonString(conversation['title']) ?? 'this conversation';
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete conversation?',
      message:
          '“$title” and its messages, saved agent state, and memories learned from those messages will be permanently removed.',
      cancelLabel: 'Keep conversation',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed) return;
    if (!mounted) return;
    try {
      await widget.http.delete('/api/v1/conversations/$id');
      if (!mounted) return;
      if (id == widget.selectedConversationId) {
        Navigator.of(context).pop((conversationId: null, deletedCurrent: true));
      } else {
        setState(() => _conversations.removeWhere((item) => item['id'] == id));
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error = error.response?.statusCode == 409
            ? 'Task conversations are managed from the Tasks section.'
            : 'Jarvis could not delete this conversation.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Jarvis could not delete this conversation.');
    }
  }

  @override
  Widget build(BuildContext context) {
    final groups = groupConversations(_visible);
    final searching = _query.trim().isNotEmpty;
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Conversations'),
        actions: [
          IconButton(
            onPressed: _loading || _creating ? null : _load,
            tooltip: 'Refresh',
            icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
          ),
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.notePencil,
            onPressed: _createConversation,
            busy: _creating,
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: _conversations.isEmpty,
        onRetry: _load,
        onRefresh: _load,
        empty: const EmptyState(
          icon: PhosphorIconsRegular.chatsCircle,
          title: 'No conversations yet.',
          message: 'Start a new conversation and it will appear here.',
        ),
        child: CustomScrollView(
          keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
          slivers: [
            SliverToBoxAdapter(
              child: ContentWidth(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 8),
                  child: _SearchField(
                    controller: _search,
                    onChanged: (value) => setState(() => _query = value),
                    onClear: () {
                      _search.clear();
                      setState(() => _query = '');
                    },
                  ),
                ),
              ),
            ),
            if (groups.isEmpty && searching)
              SliverToBoxAdapter(
                child: Padding(
                  padding: const EdgeInsets.only(top: 48),
                  child: EmptyState(
                    icon: PhosphorIconsRegular.magnifyingGlass,
                    title: 'No matching conversations',
                    message: 'No title contains “${_query.trim()}”.',
                  ),
                ),
              ),
            for (final (label, items) in groups) ...[
              SliverToBoxAdapter(
                child: ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(20, 12, 16, 6),
                    child: Text(
                      label,
                      style: Theme.of(context).textTheme.labelMedium?.copyWith(
                        color: JarvisColors.of(context).muted,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                ),
              ),
              SliverPadding(
                padding: const EdgeInsets.symmetric(horizontal: 16),
                sliver: SliverList.builder(
                  itemCount: items.length,
                  itemBuilder: (context, index) =>
                      _row(context, items[index], index),
                ),
              ),
            ],
            const SliverToBoxAdapter(child: SizedBox(height: 32)),
          ],
        ),
      ),
    );
  }

  Widget _row(
    BuildContext context,
    Map<String, dynamic> conversation,
    int index,
  ) {
    final id = jsonString(conversation, 'id')!;
    final selected = id == widget.selectedConversationId;
    final pinned = conversation['pinned'] == true;
    final colors = JarvisColors.of(context);
    final title = asJsonString(conversation['title']) ?? 'New conversation';
    return FadeSlideIn(
      index: index,
      child: ContentWidth(
        child: SurfaceCard(
          margin: const EdgeInsets.only(bottom: 8),
          padding: const EdgeInsets.fromLTRB(14, 12, 4, 12),
          borderColor: selected ? colors.outlineStrong : colors.outline,
          onTap: () => Navigator.of(
            context,
          ).pop((conversationId: id, deletedCurrent: false)),
          child: Row(
            children: [
              IconBadge(
                icon: selected
                    ? PhosphorIconsFill.chatCircle
                    : PhosphorIconsRegular.chatCircle,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      title,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(
                        context,
                      ).textTheme.titleSmall?.copyWith(fontSize: 15),
                    ),
                    const SizedBox(height: 3),
                    Text(
                      [
                        _formatDate(conversation['updatedAt']),
                        ?asJsonString(conversation['profileName']),
                      ].where((part) => part.isNotEmpty).join(' · '),
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.bodySmall,
                    ),
                  ],
                ),
              ),
              if (pinned)
                Padding(
                  padding: const EdgeInsets.only(left: 6),
                  child: Icon(
                    PhosphorIconsFill.pushPin,
                    size: 16,
                    color: colors.muted,
                    semanticLabel: 'Pinned',
                  ),
                ),
              if (selected)
                Padding(
                  padding: const EdgeInsets.only(left: 6),
                  child: Icon(
                    PhosphorIconsFill.checkCircle,
                    size: 20,
                    color: colors.ink,
                    semanticLabel: 'Open now',
                  ),
                ),
              PopupMenuButton<String>(
                tooltip: 'Conversation actions',
                enabled: !_updating.contains(id),
                icon: const Icon(PhosphorIconsRegular.dotsThree, size: 22),
                onSelected: (value) => switch (value) {
                  'rename' => _rename(conversation),
                  'pin' => _togglePin(conversation),
                  'project' => _moveToProject(conversation),
                  'copy' => _copyAsMarkdown(conversation),
                  'delete' => _deleteConversation(conversation),
                  _ => Future<void>.value(),
                },
                itemBuilder: (_) => [
                  const PopupMenuItem(
                    value: 'rename',
                    child: _MenuRow(
                      icon: PhosphorIconsRegular.pencilSimple,
                      label: 'Rename',
                    ),
                  ),
                  PopupMenuItem(
                    value: 'pin',
                    child: _MenuRow(
                      icon: PhosphorIconsRegular.pushPin,
                      label: pinned ? 'Unpin' : 'Pin to top',
                    ),
                  ),
                  PopupMenuItem(
                    value: 'project',
                    child: _MenuRow(
                      icon: PhosphorIconsRegular.folderSimple,
                      label: conversation['projectId'] is String
                          ? 'Move to another project'
                          : 'Move to project',
                    ),
                  ),
                  const PopupMenuItem(
                    value: 'copy',
                    child: _MenuRow(
                      icon: PhosphorIconsRegular.copy,
                      label: 'Copy as Markdown',
                    ),
                  ),
                  PopupMenuItem(
                    value: 'delete',
                    child: _MenuRow(
                      icon: PhosphorIconsRegular.trash,
                      label: 'Delete',
                      color: colors.danger,
                    ),
                  ),
                ],
              ),
            ],
          ),
        ),
      ),
    );
  }

  String _formatDate(Object? value) {
    final date = jsonDate(value, local: true);
    if (date == null) return '';
    final now = DateTime.now();
    final sameDay =
        date.year == now.year && date.month == now.month && date.day == now.day;
    if (sameDay) {
      final hour = date.hour % 12 == 0 ? 12 : date.hour % 12;
      return 'Today at $hour:${date.minute.toString().padLeft(2, '0')} ${date.hour < 12 ? 'AM' : 'PM'}';
    }
    return '${date.day}/${date.month}/${date.year}';
  }
}

class _MenuRow extends StatelessWidget {
  const _MenuRow({required this.icon, required this.label, this.color});

  final IconData icon;
  final String label;
  final Color? color;

  @override
  Widget build(BuildContext context) => Row(
    children: [
      Icon(icon, size: 18, color: color),
      const SizedBox(width: 12),
      Text(label, style: color == null ? null : TextStyle(color: color)),
    ],
  );
}

class _SearchField extends StatelessWidget {
  const _SearchField({
    required this.controller,
    required this.onChanged,
    required this.onClear,
  });

  final TextEditingController controller;
  final ValueChanged<String> onChanged;
  final VoidCallback onClear;

  @override
  Widget build(BuildContext context) => TextField(
    controller: controller,
    onChanged: onChanged,
    textInputAction: TextInputAction.search,
    decoration: InputDecoration(
      hintText: 'Search conversations',
      prefixIcon: const Icon(PhosphorIconsRegular.magnifyingGlass, size: 20),
      suffixIcon: ValueListenableBuilder<TextEditingValue>(
        valueListenable: controller,
        builder: (context, value, _) => value.text.isEmpty
            ? const SizedBox.shrink()
            : IconButton(
                tooltip: 'Clear search',
                onPressed: onClear,
                icon: const Icon(PhosphorIconsRegular.xCircle, size: 20),
              ),
      ),
    ),
  );
}

class _RenameDialog extends StatefulWidget {
  const _RenameDialog({required this.initial});

  final String initial;

  @override
  State<_RenameDialog> createState() => _RenameDialogState();
}

class _RenameDialogState extends State<_RenameDialog> {
  late final _controller = TextEditingController(text: widget.initial)
    ..selection = TextSelection(
      baseOffset: 0,
      extentOffset: widget.initial.length,
    );

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  String get _title => _controller.text.trim().split(RegExp(r'\s+')).join(' ');

  void _save() {
    if (_title.isEmpty) return;
    Navigator.pop(context, _title);
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Rename conversation'),
    content: TextField(
      controller: _controller,
      autofocus: true,
      maxLength: 200,
      textCapitalization: TextCapitalization.sentences,
      textInputAction: TextInputAction.done,
      onChanged: (_) => setState(() {}),
      onSubmitted: (_) => _save(),
      decoration: const InputDecoration(hintText: 'Conversation name'),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        style: TextButton.styleFrom(
          foregroundColor: JarvisColors.of(context).inkSoft,
        ),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: _title.isEmpty ? null : _save,
        child: const Text('Save'),
      ),
    ],
  );
}
