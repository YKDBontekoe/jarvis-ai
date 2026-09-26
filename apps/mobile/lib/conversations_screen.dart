import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

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
          () => _conversations = (response.data ?? [])
              .cast<Map<String, dynamic>>(),
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
      final id = response.data?['id'] as String?;
      if (id == null) throw const FormatException('Missing conversation ID.');
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
    final id = conversation['id'] as String;
    final title = conversation['title'] as String? ?? 'this conversation';
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Delete conversation?'),
        content: Text(
          '“$title” and its messages, saved agent state, and memories learned from those messages will be permanently removed.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Keep conversation'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
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
          icon: const Icon(Icons.refresh),
        ),
        IconButton(
          onPressed: _creating ? null : _createConversation,
          tooltip: 'New conversation',
          icon: _creating
              ? const SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Icon(Icons.add_comment_outlined),
        ),
      ],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!),
                TextButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : _conversations.isEmpty
        ? const Center(child: Text('No conversations yet.'))
        : ListView.separated(
            itemCount: _conversations.length,
            separatorBuilder: (context, index) => const Divider(height: 1),
            itemBuilder: (context, index) {
              final conversation = _conversations[index];
              final id = conversation['id'] as String;
              return ListTile(
                selected: id == widget.selectedConversationId,
                leading: const Icon(Icons.forum_outlined),
                title: Text(
                  conversation['title'] as String? ?? 'New conversation',
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                ),
                subtitle: Text(_formatDate(conversation['updatedAt'])),
                trailing: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    if (id == widget.selectedConversationId)
                      const Icon(Icons.check, size: 19),
                    IconButton(
                      tooltip: 'Delete conversation',
                      onPressed: () => _deleteConversation(conversation),
                      icon: const Icon(Icons.delete_outline),
                    ),
                  ],
                ),
                onTap: () => Navigator.of(context).pop((
                  conversationId: id,
                  deletedCurrent: false,
                )),
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
