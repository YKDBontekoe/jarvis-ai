import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/conversations',
      );
      if (mounted) {
        setState(
          () => _conversations = jsonMaps(response.data),
        );
      }
    } on DioException {
      if (mounted) {
        setState(() => _error = 'Jarvis could not load conversations.');
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _createConversation() async {
    setState(() => _creating = true);
    try {
      final response = await widget.http.post<Map<String, dynamic>>(
        '/api/v1/conversations',
        data: const {'title': 'New conversation'},
      );
      final id = response.data?['id'];
      if (id is! String || id.isEmpty) {
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
    } finally {
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _deleteConversation(Map<String, dynamic> conversation) async {
    final id = jsonString(conversation, 'id');
    if (id == null) return;
    final title = conversation['title'] as String? ?? 'this conversation';
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
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Conversations'),
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
    body: _loading
        ? const LoadingState()
        : _error != null
        ? ErrorState(message: _error!, onRetry: _load)
        : _conversations.isEmpty
        ? const EmptyState(
            icon: PhosphorIconsRegular.chatsCircle,
            title: 'No conversations yet.',
            message: 'Start a new conversation and it will appear here.',
          )
        : ListView.builder(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            itemCount: _conversations.length,
            itemBuilder: (context, index) {
              final conversation = _conversations[index];
              final id = jsonString(conversation, 'id');
              if (id == null) return const SizedBox.shrink();
              final selected = id == widget.selectedConversationId;
              return ContentWidth(
                child: SurfaceCard(
                  margin: const EdgeInsets.only(bottom: 8),
                  padding: const EdgeInsets.fromLTRB(14, 12, 6, 12),
                  borderColor: selected
                      ? JarvisColors.outlineStrong
                      : JarvisColors.outline,
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
                              conversation['title'] as String? ??
                                  'New conversation',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(
                                context,
                              ).textTheme.titleSmall?.copyWith(fontSize: 15),
                            ),
                            const SizedBox(height: 3),
                            Text(
                              _formatDate(conversation['updatedAt']),
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ],
                        ),
                      ),
                      if (selected)
                        const Padding(
                          padding: EdgeInsets.symmetric(horizontal: 4),
                          child: Icon(
                            PhosphorIconsFill.checkCircle,
                            size: 20,
                            color: JarvisColors.ink,
                          ),
                        ),
                      IconButton(
                        tooltip: 'Delete conversation',
                        onPressed: () => _deleteConversation(conversation),
                        icon: const Icon(PhosphorIconsRegular.trash, size: 20),
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
  );

  String _formatDate(Object? value) {
    final date = value is String ? DateTime.tryParse(value)?.toLocal() : null;
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
