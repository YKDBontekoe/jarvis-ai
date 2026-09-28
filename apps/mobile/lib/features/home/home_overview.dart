import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import '../../ui/phosphor_icons.dart';

import '../../json_maps.dart';
import '../../task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../chat/chat_widgets.dart';
import '../usage/usage_screen.dart';

class HomeOverview extends StatefulWidget {
  const HomeOverview({
    required this.http,
    required this.mark,
    required this.ready,
    required this.voiceStarting,
    required this.onTalk,
    required this.onOpenTasks,
    required this.refreshRevision,
    this.onContinueConversation,
    this.onSuggestion,
    this.onOpenUsage,
    super.key,
  });

  final Dio http;
  final Widget mark;
  final bool ready;
  final bool voiceStarting;
  final VoidCallback? onTalk;
  final VoidCallback onOpenTasks;
  final int refreshRevision;
  final VoidCallback? onContinueConversation;

  /// Sends a suggested prompt; hidden when null.
  final ValueChanged<String>? onSuggestion;

  /// Opens the usage dashboard. The summary is hidden when this is null.
  final VoidCallback? onOpenUsage;

  @override
  State<HomeOverview> createState() => _HomeOverviewState();
}

class _HomeOverviewState extends State<HomeOverview>
    with WidgetsBindingObserver {
  List<_ActiveTask> _tasks = [];
  Map<String, dynamic>? _usage;
  bool _loading = false;
  String? _error;
  int _requestRevision = 0;
  CancelToken? _request;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.ready) {
      unawaited(_load());
      unawaited(_loadUsage());
    }
  }

  @override
  void didUpdateWidget(HomeOverview oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.ready) {
      _requestRevision++;
      _request?.cancel();
      _tasks = [];
      _usage = null;
      _loading = false;
      _error = null;
    } else if (!oldWidget.ready ||
        oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_load());
      unawaited(_loadUsage());
    } else if (oldWidget.onOpenUsage == null && widget.onOpenUsage != null) {
      unawaited(_loadUsage());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && widget.ready) {
      unawaited(_load());
      unawaited(_loadUsage());
    }
  }

  Future<void> _refresh() async {
    await Future.wait([_load(), _loadUsage()]);
  }

  Future<void> _loadUsage() async {
    if (!widget.ready || widget.onOpenUsage == null || !mounted) return;
    try {
      final response = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/usage',
        queryParameters: const {'period': '7d'},
      );
      if (!mounted || !widget.ready || widget.onOpenUsage == null) return;
      final data = response.data;
      setState(
        () => _usage = data == null ? null : Map<String, dynamic>.of(data),
      );
    } on DioException {
      if (mounted) setState(() => _usage = null);
    }
  }

  Future<void> _load() async {
    if (!widget.ready || !mounted) return;
    final revision = ++_requestRevision;
    _request?.cancel();
    final request = CancelToken();
    _request = request;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/tasks',
        cancelToken: request,
      );
      final tasks =
          (response.data ?? [])
              .map(_ActiveTask.fromJson)
              .whereType<_ActiveTask>()
              .toList()
            ..sort((a, b) {
              final priority = a.priority.compareTo(b.priority);
              return priority != 0
                  ? priority
                  : b.createdAt.compareTo(a.createdAt);
            });
      if (mounted && revision == _requestRevision) {
        setState(() => _tasks = tasks);
      }
    } on DioException catch (error) {
      if (!CancelToken.isCancel(error) &&
          mounted &&
          revision == _requestRevision) {
        setState(() => _error = 'Could not load active tasks.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _openTask(_ActiveTask task) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => TaskDetailsScreen(http: widget.http, taskId: task.id),
      ),
    );
    if (mounted) await _load();
  }

  Widget _usageCard() {
    final usage = _usage;
    if (usage == null) return const SizedBox.shrink();
    final persona = usage['personalization'];
    final personalization = persona is Map
        ? Map<String, dynamic>.from(persona)
        : const <String, dynamic>{};
    final codex = usage['codex'] is Map
        ? Map<String, dynamic>.from(usage['codex'] as Map)
        : const <String, dynamic>{};
    final openRouter = usage['openRouter'] is Map
        ? Map<String, dynamic>.from(usage['openRouter'] as Map)
        : const <String, dynamic>{};
    final tokens =
        asJsonInt(codex['totalTokens']) + asJsonInt(openRouter['totalTokens']);
    final band = asJsonString(personalization['band']) ?? 'New';
    final score = asJsonInt(personalization['score']);
    final memories = asJsonInt(personalization['activeMemories']);
    final cost = openRouter['estimatedCostUsd'];
    return SurfaceCard(
      key: const Key('home-usage'),
      onTap: widget.onOpenUsage,
      child: Row(
        children: [
          const IconBadge(icon: PhosphorIconsRegular.chartBar, size: 40),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  '$band · $score',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
                const SizedBox(height: 2),
                Text(
                  '$memories ${memories == 1 ? 'memory' : 'memories'} · ${formatTokenCount(tokens)} tokens this week'
                  '${cost is num ? ' · ${formatUsd(cost)}' : ''}',
                  style: const TextStyle(color: JarvisColors.inkSoft, fontSize: 13),
                ),
              ],
            ),
          ),
          const Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: JarvisColors.muted,
          ),
        ],
      ),
    );
  }

  String get _greeting => switch (DateTime.now().hour) {
    < 12 => 'Good morning.',
    < 18 => 'Good afternoon.',
    _ => 'Good evening.',
  };

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _requestRevision++;
    _request?.cancel();
    super.dispose();
  }

  String get _today {
    final now = DateTime.now();
    const weekdays = [
      'Monday',
      'Tuesday',
      'Wednesday',
      'Thursday',
      'Friday',
      'Saturday',
      'Sunday',
    ];
    const months = [
      'January',
      'February',
      'March',
      'April',
      'May',
      'June',
      'July',
      'August',
      'September',
      'October',
      'November',
      'December',
    ];
    return '${weekdays[now.weekday - 1]}, ${months[now.month - 1]} ${now.day}';
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return RefreshIndicator(
      onRefresh: _refresh,
      child: LayoutBuilder(
        builder: (context, constraints) => ListView(
          physics: const AlwaysScrollableScrollPhysics(),
          padding: EdgeInsets.fromLTRB(
            20,
            constraints.maxHeight > 700 ? 48 : 20,
            20,
            28,
          ),
          children: [
            Center(
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 620),
                child: _Entrance(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Center(child: widget.mark),
                      const SizedBox(height: 22),
                      Text(
                        '$_today  ·  $_greeting',
                        textAlign: TextAlign.center,
                        style: theme.textTheme.bodySmall?.copyWith(
                          color: JarvisColors.muted,
                        ),
                      ),
                      const SizedBox(height: 6),
                      const Text(
                        'What do you need?',
                        textAlign: TextAlign.center,
                        style: TextStyle(
                          fontFamily: 'InstrumentSerif',
                          fontSize: 40,
                          height: 1.1,
                          letterSpacing: -.6,
                          color: JarvisColors.ink,
                        ),
                      ),
                      const SizedBox(height: 22),
                      Wrap(
                        alignment: WrapAlignment.center,
                        spacing: 6,
                        runSpacing: 6,
                        children: [
                          FilledButton.icon(
                            onPressed: widget.onTalk,
                            style: FilledButton.styleFrom(
                              minimumSize: const Size(0, 44),
                              padding: const EdgeInsets.symmetric(
                                horizontal: 20,
                              ),
                              shape: const StadiumBorder(),
                            ),
                            icon: widget.voiceStarting
                                ? const SizedBox.square(
                                    dimension: 16,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 1.8,
                                    ),
                                  )
                                : const Icon(
                                    PhosphorIconsRegular.waveform,
                                    size: 18,
                                  ),
                            label: Text(
                              widget.voiceStarting
                                  ? 'Connecting…'
                                  : 'Talk to Jarvis',
                            ),
                          ),
                          if (widget.onContinueConversation != null)
                            TextButton.icon(
                              onPressed: widget.onContinueConversation,
                              style: TextButton.styleFrom(
                                minimumSize: const Size(0, 44),
                                shape: const StadiumBorder(),
                              ),
                              icon: const Icon(
                                PhosphorIconsRegular.arrowUpRight,
                                size: 16,
                              ),
                              label: const Text('Continue conversation'),
                            ),
                        ],
                      ),
                      const SizedBox(height: 36),
                      if (_usage != null && widget.onOpenUsage != null) ...[
                        _usageCard(),
                        const SizedBox(height: 28),
                      ],
                      if (widget.onSuggestion != null) ...[
                        Padding(
                          padding: const EdgeInsets.only(left: 4, bottom: 10),
                          child: Text(
                            'Or try one of these:',
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: JarvisColors.muted,
                            ),
                          ),
                        ),
                        SuggestionChips(onSelected: widget.onSuggestion),
                        const SizedBox(height: 28),
                      ],
                      SectionHeader(
                        _tasks.isEmpty
                            ? 'Active tasks'
                            : 'Active tasks (${_tasks.length})',
                        padding: const EdgeInsets.only(left: 4, bottom: 6),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            TextButton(
                              onPressed: widget.onOpenTasks,
                              child: const Text('View all'),
                            ),
                            IconButton(
                              tooltip: 'Refresh active tasks',
                              onPressed: widget.ready && !_loading
                                  ? () => unawaited(_refresh())
                                  : null,
                              icon: const Icon(
                                PhosphorIconsRegular.arrowsClockwise,
                                size: 18,
                              ),
                            ),
                          ],
                        ),
                      ),
                      if (_loading)
                        const Padding(
                          padding: EdgeInsets.only(bottom: 12),
                          child: LinearProgressIndicator(
                            minHeight: 2,
                            semanticsLabel: 'Loading active tasks',
                          ),
                        ),
                      if (_error != null)
                        InlineNotice(
                          message: '$_error Pull down to retry.',
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(bottom: 12),
                        ),
                      if (!widget.ready)
                        const _TasksPlaceholder(
                          icon: PhosphorIconsRegular.wifiSlash,
                          text: 'Connect to Jarvis to see your active tasks.',
                        )
                      else if (!_loading && _error == null && _tasks.isEmpty)
                        const _TasksPlaceholder(
                          icon: PhosphorIconsRegular.checkCircle,
                          text: 'No active tasks. Start one from Tasks.',
                        ),
                      if (_tasks.isNotEmpty)
                        GroupedSection(
                          dividerIndent: 64,
                          children: [
                            for (final task in _tasks.take(3))
                              _TaskRow(
                                task: task,
                                onTap: () => unawaited(_openTask(task)),
                              ),
                          ],
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Fades and lifts the home content in once when it first appears.
class _Entrance extends StatelessWidget {
  const _Entrance({required this.child});

  final Widget child;

  @override
  Widget build(BuildContext context) => TweenAnimationBuilder<double>(
    tween: Tween(begin: 0, end: 1),
    duration: const Duration(milliseconds: 450),
    curve: Curves.easeOutCubic,
    builder: (context, value, child) => Opacity(
      opacity: value,
      child: Transform.translate(
        offset: Offset(0, 12 * (1 - value)),
        child: child,
      ),
    ),
    child: child,
  );
}

class _TasksPlaceholder extends StatelessWidget {
  const _TasksPlaceholder({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 18),
    decoration: BoxDecoration(
      borderRadius: BorderRadius.circular(JarvisRadii.lg),
      border: Border.all(color: JarvisColors.outline),
      color: JarvisColors.surface,
    ),
    child: Row(
      children: [
        Icon(icon, size: 20, color: JarvisColors.muted),
        const SizedBox(width: 12),
        Expanded(
          child: Text(
            text,
            style: const TextStyle(color: JarvisColors.inkSoft),
          ),
        ),
      ],
    ),
  );
}

class _TaskRow extends StatelessWidget {
  const _TaskRow({required this.task, required this.onTap});

  final _ActiveTask task;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    child: Padding(
      padding: const EdgeInsets.fromLTRB(14, 12, 10, 12),
      child: Row(
        children: [
          IconBadge(icon: task.icon),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  task.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 5),
                StatusPill(label: task.statusLabel, color: task.color),
              ],
            ),
          ),
          const Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: JarvisColors.muted,
          ),
        ],
      ),
    ),
  );
}

class _ActiveTask {
  const _ActiveTask({
    required this.id,
    required this.title,
    required this.status,
    required this.createdAt,
  });

  final String id;
  final String title;
  final String status;
  final DateTime createdAt;

  static _ActiveTask? fromJson(dynamic value) {
    if (value is! Map) return null;
    final map = Map<String, dynamic>.from(value);
    final id = map['id'];
    final title = map['title'];
    final status = map['status'];
    final createdAt = map['createdAt'];
    if (id is! String ||
        id.isEmpty ||
        title is! String ||
        status is! String ||
        createdAt is! String ||
        !const {
          'queued',
          'running',
          'waiting',
          'needs_approval',
        }.contains(status)) {
      return null;
    }
    final date = DateTime.tryParse(createdAt);
    if (date == null) return null;
    return _ActiveTask(id: id, title: title, status: status, createdAt: date);
  }

  int get priority => switch (status) {
    'needs_approval' => 0,
    'running' => 1,
    'waiting' => 2,
    _ => 3,
  };

  String get statusLabel => switch (status) {
    'needs_approval' => 'Needs your approval',
    'running' => 'In progress',
    'waiting' => 'Waiting',
    _ => 'Queued',
  };

  IconData get icon => switch (status) {
    'needs_approval' => PhosphorIconsRegular.shieldWarning,
    'running' => PhosphorIconsRegular.hourglassMedium,
    'waiting' => PhosphorIconsRegular.pauseCircle,
    _ => PhosphorIconsRegular.clock,
  };

  Color get color => statusStyle(status).color;
}
