import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'task_details_screen.dart';
import 'condition_watches_screen.dart';
import 'approvals_screen.dart';

class TasksScreen extends StatefulWidget {
  const TasksScreen({required this.http, super.key});

  final Dio http;

  @override
  State<TasksScreen> createState() => _TasksScreenState();
}

class _TasksScreenState extends State<TasksScreen> {
  List<Map<String, dynamic>> _tasks = [];
  bool _loading = true;
  bool _creating = false;
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
      final response = await widget.http.get<List<dynamic>>('/api/v1/tasks');
      if (mounted) {
        setState(
          () => _tasks = (response.data ?? []).cast<Map<String, dynamic>>(),
        );
      }
    } on DioException {
      if (mounted) setState(() => _error = 'Jarvis could not load tasks.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _createTask() async {
    final title = TextEditingController();
    final prompt = TextEditingController();
    final formKey = GlobalKey<FormState>();
    final create = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Give Jarvis a task'),
        content: Form(
          key: formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                controller: title,
                autofocus: true,
                maxLength: 200,
                decoration: const InputDecoration(labelText: 'Task name'),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Enter a task name.'
                    : null,
              ),
              const SizedBox(height: 12),
              TextFormField(
                controller: prompt,
                minLines: 2,
                maxLines: 5,
                maxLength: 32000,
                decoration: const InputDecoration(
                  labelText: 'What should Jarvis do?',
                  alignLabelWithHint: true,
                ),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Describe the task.'
                    : null,
              ),
            ],
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () {
              if (formKey.currentState?.validate() ?? false) {
                Navigator.pop(dialogContext, true);
              }
            },
            child: const Text('Start task'),
          ),
        ],
      ),
    );
    if (create != true) {
      title.dispose();
      prompt.dispose();
      return;
    }

    setState(() => _creating = true);
    try {
      await widget.http.post(
        '/api/v1/tasks',
        data: {'title': title.text.trim(), 'prompt': prompt.text.trim()},
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The durable task service is unavailable. Try again shortly.'
            : 'Jarvis could not start that task.';
        _showError(message);
      }
    } finally {
      title.dispose();
      prompt.dispose();
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _cancel(Map<String, dynamic> task) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Cancel this task?'),
        content: Text('“${task['title']}” will stop running.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Keep task'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Cancel task'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    try {
      await widget.http.delete('/api/v1/tasks/${task['id']}');
      await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not cancel that task.');
    }
  }

  Future<void> _openTask(Map<String, dynamic> task) async {
    final taskId = task['id'] as String?;
    if (taskId == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => TaskDetailsScreen(http: widget.http, taskId: taskId),
      ),
    );
    if (mounted) await _load();
  }

  Future<void> _openApprovals() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => ApprovalsScreen(http: widget.http),
      ),
    );
    if (mounted) await _load();
  }

  void _showError(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  String _date(dynamic value) {
    if (value is! String) return '';
    final date = DateTime.tryParse(value)?.toLocal();
    if (date == null) return '';
    return MaterialLocalizations.of(context).formatMediumDate(date);
  }

  Color _statusColor(String status) => switch (status) {
    'running' => const Color(0xff68d6a8),
    'needs_approval' => const Color(0xffffcb6b),
    'completed' => const Color(0xff91a9ff),
    'failed' => const Color(0xffff7185),
    _ => const Color(0xffa1a2b0),
  };

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Tasks'),
      actions: [
        IconButton(
          tooltip: 'Approvals',
          onPressed: _openApprovals,
          icon: const Icon(Icons.gpp_maybe_outlined),
        ),
        IconButton(
          tooltip: 'Condition watches',
          onPressed: () => Navigator.of(context).push<void>(
            MaterialPageRoute<void>(
              builder: (_) => ConditionWatchesScreen(http: widget.http),
            ),
          ),
          icon: const Icon(Icons.visibility_outlined),
        ),
        IconButton(
          tooltip: 'Refresh tasks',
          onPressed: _loading ? null : _load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: _creating ? null : _createTask,
      icon: _creating
          ? const SizedBox(
              width: 18,
              height: 18,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Icon(Icons.add),
      label: const Text('New task'),
    ),
    body: _loading && _tasks.isEmpty
        ? const Center(child: CircularProgressIndicator())
        : _error != null && _tasks.isEmpty
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!),
                const SizedBox(height: 12),
                OutlinedButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : _tasks.isEmpty
        ? const Center(
            child: Text('No tasks yet. Give Jarvis something to work on.'),
          )
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 96),
              itemCount: _tasks.length,
              itemBuilder: (context, index) {
                final task = _tasks[index];
                final status = task['status'] as String? ?? 'queued';
                final canCancel = [
                  'queued',
                  'running',
                  'needs_approval',
                ].contains(status);
                final summary = task['summary'] as String?;
                return Card(
                  child: ListTile(
                    onTap: () => _openTask(task),
                    isThreeLine: summary?.isNotEmpty == true,
                    leading: Icon(Icons.task_alt, color: _statusColor(status)),
                    title: Text(task['title'] as String? ?? 'Task'),
                    subtitle: Text(
                      [
                        status.replaceAll('_', ' '),
                        _date(task['createdAt']),
                        if (summary?.isNotEmpty == true) summary!,
                      ].join(' · '),
                      maxLines: 4,
                      overflow: TextOverflow.ellipsis,
                    ),
                    trailing: canCancel
                        ? IconButton(
                            tooltip: 'Cancel task',
                            onPressed: () => _cancel(task),
                            icon: const Icon(Icons.cancel_outlined),
                          )
                        : null,
                  ),
                );
              },
            ),
          ),
  );
}
