import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';
import 'task_details_screen.dart';
import 'condition_watches_screen.dart';
import 'approvals_screen.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

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
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Cancel this task?',
      message: '“${task['title']}” will stop running.',
      cancelLabel: 'Keep task',
      confirmLabel: 'Cancel task',
      destructive: true,
      icon: PhosphorIconsRegular.stopCircle,
    );
    if (!confirmed) return;
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

  static const _activeStatuses = {
    'queued',
    'running',
    'waiting',
    'needs_approval',
  };

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Tasks'),
      actions: [
        IconButton(
          tooltip: 'Approvals',
          onPressed: _openApprovals,
          icon: const Icon(PhosphorIconsRegular.shieldCheck),
        ),
        IconButton(
          tooltip: 'Condition watches',
          onPressed: () => Navigator.of(context).push<void>(
            MaterialPageRoute<void>(
              builder: (_) => ConditionWatchesScreen(http: widget.http),
            ),
          ),
          icon: const Icon(PhosphorIconsRegular.pulse),
        ),
        IconButton(
          tooltip: 'Refresh tasks',
          onPressed: _loading ? null : _load,
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'New task',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createTask,
          busy: _creating,
        ),
      ],
    ),
    body: _loading && _tasks.isEmpty
        ? const LoadingState()
        : _error != null && _tasks.isEmpty
        ? ErrorState(message: _error!, onRetry: _load)
        : _tasks.isEmpty
        ? const EmptyState(
            icon: PhosphorIconsRegular.checkCircle,
            title: 'No tasks yet',
            message: 'No tasks yet. Give Jarvis something to work on.',
          )
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _summary(),
                      const SizedBox(height: 18),
                      for (final task in _tasks) _taskCard(task),
                    ],
                  ),
                ),
              ],
            ),
          ),
  );

  Widget _summary() {
    int count(bool Function(String status) test) => _tasks
        .where((task) => test(task['status'] as String? ?? 'queued'))
        .length;
    final stats = [
      ('Active', count(_activeStatuses.contains)),
      ('Needs review', count((status) => status == 'needs_approval')),
      ('Done', count((status) => status == 'completed')),
    ];
    return SurfaceCard(
      padding: const EdgeInsets.symmetric(vertical: 14),
      child: IntrinsicHeight(
        child: Row(
          children: [
            for (final (index, (label, value)) in stats.indexed) ...[
              if (index > 0) const VerticalDivider(width: 1),
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '$value',
                        style: Theme.of(context).textTheme.headlineSmall,
                      ),
                      const SizedBox(height: 2),
                      Text(
                        label,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              ),
            ],
          ],
        ),
      ),
    );
  }

  Widget _taskCard(Map<String, dynamic> task) {
    final status = task['status'] as String? ?? 'queued';
    final style = statusStyle(status);
    final canCancel = ['queued', 'running', 'needs_approval'].contains(status);
    final summary = task['summary'] as String?;
    final date = _date(task['createdAt']);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 6, 14),
      onTap: () => _openTask(task),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          IconBadge(icon: style.icon),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  task['title'] as String? ?? 'Task',
                  style: Theme.of(
                    context,
                  ).textTheme.titleSmall?.copyWith(fontSize: 15),
                ),
                const SizedBox(height: 8),
                Wrap(
                  spacing: 8,
                  runSpacing: 6,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    StatusPill(label: style.label, color: style.color),
                    if (date.isNotEmpty)
                      Text(date, style: Theme.of(context).textTheme.bodySmall),
                  ],
                ),
                if (summary?.isNotEmpty == true) ...[
                  const SizedBox(height: 8),
                  Text(
                    summary!,
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: JarvisColors.inkSoft,
                      fontSize: 13.5,
                    ),
                  ),
                ],
              ],
            ),
          ),
          if (canCancel)
            IconButton(
              tooltip: 'Cancel task',
              onPressed: () => _cancel(task),
              icon: const Icon(PhosphorIconsRegular.x, size: 20),
            )
          else
            const Padding(
              padding: EdgeInsets.all(10),
              child: Icon(
                PhosphorIconsRegular.caretRight,
                size: 16,
                color: JarvisColors.muted,
              ),
            ),
        ],
      ),
    );
  }
}
