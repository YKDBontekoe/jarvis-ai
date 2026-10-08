import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../ui/phosphor_icons.dart';
import 'task_details_screen.dart';
import '../watches/condition_watches_screen.dart';
import '../approvals/approvals_screen.dart';
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/plain_text.dart';

part 'task_editor.dart';

class TasksScreen extends StatefulWidget {
  const TasksScreen({
    required this.http,
    this.startCreating = false,
    this.onCreateDone,
    super.key,
  });

  final Dio http;

  /// Opens the "new" editor as soon as the page has settled.
  final bool startCreating;

  /// Called after a [startCreating] editor closes: with a confirmation when
  /// it saved, or null when it was cancelled.
  final ValueChanged<String?>? onCreateDone;

  @override
  State<TasksScreen> createState() => _TasksScreenState();
}

class _TasksScreenState extends State<TasksScreen> {
  List<Map<String, dynamic>> _tasks = [];
  bool _loading = true;
  bool _creating = false;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.startCreating) afterRouteSettles(this, _quickCreate);
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/tasks');
      if (mounted && revision == _requestRevision) {
        setState(() => _tasks = jsonMaps(response.data));
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load tasks.');
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load tasks.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _quickCreate() async {
    final created = await _createTask();
    if (created == false || !mounted) return;
    widget.onCreateDone?.call(created == true ? 'Task started' : null);
  }

  /// True when started, null when cancelled, false when it failed.
  Future<bool?> _createTask() async {
    final created = await showJarvisDialog<_NewTask>(
      context: context,
      builder: (_) => _NewTaskDialog(http: widget.http),
    );
    if (created == null || !mounted) return null;

    setState(() => _creating = true);
    try {
      await widget.http.post(
        '/api/v1/tasks',
        data: {
          'title': created.title,
          'prompt': created.prompt,
          if (created.profileId != null) 'profileId': created.profileId,
        },
      );
      await _load();
      return true;
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The durable task service is unavailable. Try again shortly.'
            : 'Jarvis could not start that task.';
        _showError(message);
      }
      return false;
    } catch (_) {
      if (mounted) _showError('Jarvis could not start that task.');
      return false;
    } finally {
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
    if (!mounted) return;
    final id = jsonId(task);
    if (id == null) return;
    try {
      await widget.http.delete('/api/v1/tasks/$id');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not cancel that task.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not cancel that task.');
    }
  }

  Future<void> _openTask(Map<String, dynamic> task) async {
    final taskId = asJsonString(task['id']);
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
    final date = jsonDate(value, local: true);
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
          collapsesWhenNarrow: true,
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _tasks.isEmpty,
      onRetry: _load,
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.checkCircle,
        title: 'No tasks yet',
        message: 'No tasks yet. Give Jarvis something to work on.',
      ),
      child: OrbRefresh(
        onRefresh: _load,
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            4,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            ContentWidth(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  _summary(),
                  const SizedBox(height: 18),
                  for (final (index, task) in _tasks.indexed)
                    FadeSlideIn(index: index + 1, child: _taskCard(task)),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );

  Widget _summary() {
    int count(bool Function(String status) test) => _tasks
        .where((task) => test(asJsonString(task['status']) ?? 'queued'))
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
                        maxLines: 2,
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
    final status = asJsonString(task['status']) ?? 'queued';
    final style = statusStyle(status);
    final canCancel = ['queued', 'running', 'needs_approval'].contains(status);
    final summary = asJsonString(task['summary']);
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
                  asJsonString(task['title']) ?? 'Task',
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
                    plainPreview(summary!),
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodyMedium?.copyWith(
                      color: JarvisColors.of(context).inkSoft,
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
            Padding(
              padding: EdgeInsets.all(10),
              child: Icon(
                PhosphorIconsRegular.caretRight,
                size: 16,
                color: JarvisColors.of(context).muted,
              ),
            ),
        ],
      ),
    );
  }
}
