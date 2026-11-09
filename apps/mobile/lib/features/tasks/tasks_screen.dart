import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../ui/phosphor_icons.dart';
import 'task_details_screen.dart';
import '../watches/condition_watches_screen.dart';
import '../approvals/approvals_screen.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/plain_text.dart';
import '../../ui/submit_state.dart';

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
  String? _error;
  int _requestRevision = 0;

  /// Which summary tile narrows the list: 'active', 'review' or 'done'; null shows everything.
  String? _filter;

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

  /// True when started, null when cancelled. A failed start keeps the dialog
  /// open with what was typed, so this never returns false.
  Future<bool?> _createTask() async {
    final created = await showJarvisDialog<bool>(
      context: context,
      builder: (_) => _NewTaskDialog(http: widget.http, onSubmit: _startTask),
    );
    return created == true ? true : null;
  }

  Future<String?> _startTask(_NewTask created) async {
    try {
      await widget.http.post(
        '/api/v1/tasks',
        data: {
          'title': created.title,
          'prompt': created.prompt,
          if (created.profileId != null) 'profileId': created.profileId,
        },
      );
      if (mounted) unawaited(_load());
      return null;
    } on DioException catch (error) {
      return error.response?.statusCode == 503
          ? 'The durable task service is unavailable. Try again shortly.'
          : 'Jarvis could not start that task.';
    } catch (_) {
      return 'Jarvis could not start that task.';
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
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _date(dynamic value) {
    final date = jsonDate(value, local: true);
    if (date == null) return '';
    // Recent tasks read as "Today 10:15" or "Wed 09:00"; older ones by date.
    return DateTime.now().difference(date).inDays < 6
        ? friendlyWhen(context, date)
        : MaterialLocalizations.of(context).formatMediumDate(date);
  }

  static const _activeStatuses = {
    'queued',
    'running',
    'waiting',
    'needs_approval',
  };

  void _openWatches() => Navigator.of(context).push<void>(
    MaterialPageRoute<void>(
      builder: (_) => ConditionWatchesScreen(http: widget.http),
    ),
  );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Tasks'),
      actions: [
        // Narrow phones fold the secondary buttons into one menu, so the
        // title keeps its room.
        if (MediaQuery.sizeOf(context).width < 400)
          PopupMenuButton<String>(
            tooltip: 'More',
            icon: const Icon(PhosphorIconsRegular.dotsThree),
            onSelected: (value) => switch (value) {
              'approvals' => _openApprovals(),
              'watches' => _openWatches(),
              'refresh' => _load(),
              _ => null,
            },
            itemBuilder: (_) => [
              const PopupMenuItem(value: 'approvals', child: Text('Approvals')),
              const PopupMenuItem(
                value: 'watches',
                child: Text('Condition watches'),
              ),
              PopupMenuItem(
                value: 'refresh',
                enabled: !_loading,
                child: const Text('Refresh tasks'),
              ),
            ],
          )
        else ...[
          IconButton(
            tooltip: 'Approvals',
            onPressed: _openApprovals,
            icon: const Icon(PhosphorIconsRegular.shieldCheck),
          ),
          IconButton(
            tooltip: 'Condition watches',
            onPressed: _openWatches,
            icon: const Icon(PhosphorIconsRegular.pulse),
          ),
          IconButton(
            tooltip: 'Refresh tasks',
            onPressed: _loading ? null : _load,
            icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
          ),
        ],
        HeaderAction(
          label: 'New task',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createTask,
          busy: false,
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
        message: 'Hand Jarvis something to work on in the background, like research or a summary. It reports back here.',
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
                  for (final (index, task) in _visibleTasks.indexed)
                    FadeSlideIn(
                      key: ValueKey(jsonId(task)),
                      index: index + 1,
                      child: _taskCard(task),
                    ),
                  if (_filter != null && _visibleTasks.isEmpty)
                    Padding(
                      key: const Key('tasks-filter-empty'),
                      padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
                      child: Wrap(
                        crossAxisAlignment: WrapCrossAlignment.center,
                        spacing: 12,
                        runSpacing: 8,
                        children: [
                          Text(
                            _filterEmptyMessage,
                            style: Theme.of(context).textTheme.bodyMedium
                                ?.copyWith(
                                  color: JarvisColors.of(context).inkSoft,
                                ),
                          ),
                          PillAction(
                            label: 'Show all tasks',
                            compact: true,
                            onPressed: () => setState(() => _filter = null),
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
          ],
        ),
      ),
    ),
  );

  static bool _matches(String? filter, String status) => switch (filter) {
    'active' => _activeStatuses.contains(status),
    'review' => status == 'needs_approval',
    'done' => status == 'completed',
    _ => true,
  };

  List<Map<String, dynamic>> get _visibleTasks => [
    for (final task in _tasks)
      if (_matches(_filter, asJsonString(task['status']) ?? 'queued')) task,
  ];

  String get _filterEmptyMessage => switch (_filter) {
    'active' => 'Nothing is running right now.',
    'review' => 'Nothing needs your review.',
    _ => 'Nothing finished yet.',
  };

  /// Three counts that double as filters: tap one to narrow the list, tap it again to show everything.
  Widget _summary() {
    final colors = JarvisColors.of(context);
    int count(String filter) => _tasks
        .where(
          (task) => _matches(filter, asJsonString(task['status']) ?? 'queued'),
        )
        .length;
    const stats = [
      ('active', 'Active'),
      ('review', 'Needs review'),
      ('done', 'Done'),
    ];
    // At large text three columns break words, so the counts stack as rows.
    final stacked = MediaQuery.textScalerOf(context).scale(1) >= 1.5;
    Widget tile(String id, String label) => Semantics(
      button: true,
      selected: _filter == id,
      label: '$label, ${count(id)}',
      hint: _filter == id ? 'Show all tasks' : 'Show only these',
      excludeSemantics: true,
      child: PressFeedback(
        scale: .97,
        builder: (context, onHighlight) => InkWell(
          key: Key('tasks-filter-$id'),
          onHighlightChanged: onHighlight,
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          onTap: () {
            HapticFeedback.selectionClick();
            setState(() => _filter = _filter == id ? null : id);
          },
          child: AnimatedContainer(
            duration: JarvisMotion.of(context, JarvisMotion.fast),
            constraints: const BoxConstraints(minHeight: 48),
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 10),
            decoration: BoxDecoration(
              color: _filter == id
                  ? colors.accentSoft
                  : colors.accentSoft.withValues(alpha: 0),
              borderRadius: BorderRadius.circular(JarvisRadii.md),
            ),
            child: Flex(
              direction: stacked ? Axis.horizontal : Axis.vertical,
              crossAxisAlignment: stacked
                  ? CrossAxisAlignment.center
                  : CrossAxisAlignment.start,
              children: [
                RollingNumber(
                  '${count(id)}',
                  style: Theme.of(context).textTheme.headlineSmall?.copyWith(
                    color: _filter == id ? colors.accentDeep : null,
                    fontFeatures: const [FontFeature.tabularFigures()],
                  ),
                ),
                const SizedBox(width: 12, height: 2),
                Flexible(
                  flex: stacked ? 1 : 0,
                  child: Text(
                    label,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                      color: _filter == id ? colors.accentDeep : null,
                      fontWeight: _filter == id ? FontWeight.w600 : null,
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
    return SurfaceCard(
      padding: const EdgeInsets.all(4),
      child: stacked
          ? Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [for (final (id, label) in stats) tile(id, label)],
            )
          : IntrinsicHeight(
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  for (final (id, label) in stats)
                    Expanded(child: tile(id, label)),
                ],
              ),
            ),
    );
  }

  Widget _taskCard(Map<String, dynamic> task) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(task['status']) ?? 'queued';
    final style = statusStyle(status, JarvisColors.of(context));
    final canCancel = ['queued', 'running', 'needs_approval'].contains(status);
    final needsYou = status == 'needs_approval';
    final summary = asJsonString(task['summary']);
    final date = _date(task['createdAt']);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(14, 14, 4, 14),
      onTap: () => _openTask(task),
      // A task waiting on you stands out; finished ones step back.
      borderColor: needsYou ? colors.warning.withValues(alpha: .45) : null,
      color: status == 'completed' || status == 'cancelled'
          ? Color.lerp(colors.surface, colors.canvas, .5)
          : null,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 38,
            height: 38,
            decoration: BoxDecoration(
              color: style.color.withValues(alpha: colors.isDark ? .2 : .1),
              borderRadius: BorderRadius.circular(12),
            ),
            child: Icon(style.icon, size: 19, color: style.color),
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  asJsonString(task['title']) ?? 'Task',
                  maxLines: 3,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleSmall
                      ?.copyWith(fontSize: 15, height: 1.3),
                ),
                const SizedBox(height: 5),
                // One quiet line of status and time, in the status colour.
                Text.rich(
                  TextSpan(
                    children: [
                      TextSpan(
                        text: style.label,
                        style: TextStyle(
                          color: style.color,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      if (date.isNotEmpty) TextSpan(text: '  ·  $date'),
                    ],
                  ),
                  style: TextStyle(fontSize: 12.5, color: colors.muted),
                ),
                if (summary?.isNotEmpty == true) ...[
                  const SizedBox(height: 8),
                  Text(
                    plainPreview(summary!),
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: Theme.of(context).textTheme.bodyMedium
                        ?.copyWith(color: colors.inkSoft, fontSize: 13.5),
                  ),
                ],
                // What it is waiting for is one tap away, not behind the menu.
                if (needsYou) ...[
                  const SizedBox(height: 10),
                  PillAction(
                    key: Key('task-review-${jsonId(task)}'),
                    label: 'Review',
                    icon: PhosphorIconsRegular.shieldCheck,
                    primary: true,
                    compact: true,
                    onPressed: _openApprovals,
                  ),
                ],
              ],
            ),
          ),
          if (canCancel)
            PopupMenuButton<String>(
              tooltip: 'Task actions',
              icon: Icon(
                PhosphorIconsRegular.dotsThree,
                size: 20,
                color: colors.muted,
              ),
              onSelected: (_) => _cancel(task),
              itemBuilder: (_) => const [
                PopupMenuItem(value: 'cancel', child: Text('Cancel task')),
              ],
            )
          else
            Padding(
              padding: const EdgeInsets.all(12),
              child: Icon(
                PhosphorIconsRegular.caretRight,
                size: 16,
                color: colors.muted,
              ),
            ),
        ],
      ),
    );
  }
}
