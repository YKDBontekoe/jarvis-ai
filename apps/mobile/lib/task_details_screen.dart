import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'approvals_screen.dart';

class TaskDetailsScreen extends StatefulWidget {
  const TaskDetailsScreen({
    required this.http,
    required this.taskId,
    super.key,
  });

  final Dio http;
  final String taskId;

  @override
  State<TaskDetailsScreen> createState() => _TaskDetailsScreenState();
}

class _TaskDetailsScreenState extends State<TaskDetailsScreen> {
  Map<String, dynamic>? _task;
  List<Map<String, dynamic>> _messages = [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final responses = await Future.wait<Response<dynamic>>([
        widget.http.get<Map<String, dynamic>>('/api/v1/tasks/${widget.taskId}'),
        widget.http.get<List<dynamic>>(
          '/api/v1/tasks/${widget.taskId}/messages',
        ),
      ]);
      if (!mounted) return;
      setState(() {
        _task = responses[0].data as Map<String, dynamic>?;
        _messages = (responses[1].data as List<dynamic>? ?? [])
            .cast<Map<String, dynamic>>();
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _error = error.response?.statusCode == 404
            ? 'This task is no longer available.'
            : 'Jarvis could not load the task details.';
      });
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  String _date(dynamic value) {
    if (value is! String) return '';
    final date = DateTime.tryParse(value)?.toLocal();
    if (date == null) return '';
    final dateText = MaterialLocalizations.of(context).formatMediumDate(date);
    final timeText = MaterialLocalizations.of(
      context,
    ).formatTimeOfDay(TimeOfDay.fromDateTime(date));
    return '$dateText · $timeText';
  }

  Future<void> _openApprovals(String conversationId) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) =>
            ApprovalsScreen(http: widget.http, conversationId: conversationId),
      ),
    );
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(_task?['title'] as String? ?? 'Task'),
      actions: [
        IconButton(
          tooltip: 'Refresh task',
          onPressed: _loading ? null : _load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: _loading && _task == null
        ? const Center(child: CircularProgressIndicator())
        : _error != null && _task == null
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!, textAlign: TextAlign.center),
                const SizedBox(height: 12),
                OutlinedButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : _buildDetails(),
  );

  Widget _buildDetails() {
    final task = _task!;
    final status = task['status'] as String? ?? 'unknown';
    final conversationId = task['conversationId'] as String?;
    final summary = task['summary'] as String?;
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(18, 16, 18, 32),
        children: [
          Wrap(
            spacing: 10,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              Chip(label: Text(status.replaceAll('_', ' '))),
              Text(_date(task['createdAt'])),
            ],
          ),
          if (status == 'needs_approval' && conversationId != null) ...[
            const SizedBox(height: 12),
            Card(
              child: ListTile(
                leading: const Icon(Icons.gpp_maybe_outlined),
                title: const Text('Jarvis needs your approval'),
                subtitle: const Text(
                  'Review the actions before this task continues.',
                ),
                trailing: const Icon(Icons.chevron_right),
                onTap: () => _openApprovals(conversationId),
              ),
            ),
          ],
          if (_error != null) ...[
            const SizedBox(height: 8),
            Text(
              _error!,
              style: TextStyle(color: Theme.of(context).colorScheme.error),
            ),
          ],
          if (summary?.isNotEmpty == true &&
              !_messages.any((message) => message['role'] == 'assistant')) ...[
            const SizedBox(height: 12),
            Text(summary!, style: Theme.of(context).textTheme.bodyLarge),
          ],
          const SizedBox(height: 20),
          for (final message in _messages) _TaskMessage(message: message),
          if (_loading)
            const Padding(
              padding: EdgeInsets.all(12),
              child: Center(child: CircularProgressIndicator()),
            ),
        ],
      ),
    );
  }
}

class _TaskMessage extends StatelessWidget {
  const _TaskMessage({required this.message});

  final Map<String, dynamic> message;

  @override
  Widget build(BuildContext context) {
    final role = message['role'] as String? ?? 'assistant';
    final isUser = role == 'user';
    final content = message['content'] as String? ?? '';
    if (content.isEmpty) return const SizedBox.shrink();
    return Container(
      margin: const EdgeInsets.only(bottom: 14),
      padding: const EdgeInsets.all(16),
      decoration: BoxDecoration(
        color: isUser ? const Color(0xff282638) : const Color(0xff1c1e28),
        borderRadius: BorderRadius.circular(18),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            isUser ? 'You' : 'Jarvis',
            style: Theme.of(context).textTheme.labelLarge,
          ),
          const SizedBox(height: 8),
          SelectableText(content),
        ],
      ),
    );
  }
}
