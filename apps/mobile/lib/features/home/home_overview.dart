import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../chat/chat_widgets.dart';

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

  @override
  State<HomeOverview> createState() => _HomeOverviewState();
}

class _HomeOverviewState extends State<HomeOverview>
    with WidgetsBindingObserver {
  List<_ActiveTask> _tasks = [];
  bool _loading = false;
  String? _error;
  int _requestRevision = 0;
  CancelToken? _request;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    if (widget.ready) unawaited(_load());
  }

  @override
  void didUpdateWidget(HomeOverview oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (!widget.ready) {
      _requestRevision++;
      _request?.cancel();
      _tasks = [];
      _loading = false;
      _error = null;
    } else if (!oldWidget.ready ||
        oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_load());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && widget.ready) {
      unawaited(_load());
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
      onRefresh: _load,
      child: ListView(
        physics: const AlwaysScrollableScrollPhysics(),
        padding: const EdgeInsets.fromLTRB(20, 12, 20, 28),
        children: [
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 620),
              child: _Entrance(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    Text(
                      '${_today.toUpperCase()}  ·  $_greeting',
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: JarvisColors.muted,
                        letterSpacing: .8,
                      ),
                    ),
                    const SizedBox(height: 8),
                    Text(
                      'What do you need?',
                      style: theme.textTheme.headlineMedium,
                    ),
                    const SizedBox(height: 20),
                    _VoiceHero(
                      mark: widget.mark,
                      onTalk: widget.onTalk,
                      voiceStarting: widget.voiceStarting,
                      onContinueConversation: widget.onContinueConversation,
                    ),
                    const SizedBox(height: 14),
                    Row(
                      children: [
                        const Expanded(child: Divider()),
                        Padding(
                          padding: const EdgeInsets.symmetric(horizontal: 12),
                          child: Text(
                            widget.onSuggestion == null
                                ? 'Or send a message below.'
                                : 'Or try one of these:',
                            style: theme.textTheme.bodySmall?.copyWith(
                              color: JarvisColors.muted,
                            ),
                          ),
                        ),
                        const Expanded(child: Divider()),
                      ],
                    ),
                    if (widget.onSuggestion != null) ...[
                      const SizedBox(height: 14),
                      SuggestionChips(onSelected: widget.onSuggestion),
                    ],
                    const SizedBox(height: 28),
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
                            onPressed: widget.ready && !_loading ? _load : null,
                            icon: const Icon(Icons.refresh_rounded, size: 20),
                          ),
                        ],
                      ),
                    ),
                    if (_loading)
                      const Padding(
                        padding: EdgeInsets.only(bottom: 12),
                        child: LinearProgressIndicator(
                          minHeight: 3,
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
                        icon: Icons.wifi_off_rounded,
                        text: 'Connect to Jarvis to see your active tasks.',
                      )
                    else if (!_loading && _error == null && _tasks.isEmpty)
                      const _TasksPlaceholder(
                        icon: Icons.task_alt_rounded,
                        text: 'No active tasks. Start one from Tasks.',
                      ),
                    for (final task in _tasks.take(3))
                      _TaskRow(
                        task: task,
                        onTap: () => unawaited(_openTask(task)),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ],
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

class _VoiceHero extends StatelessWidget {
  const _VoiceHero({
    required this.mark,
    required this.onTalk,
    required this.voiceStarting,
    required this.onContinueConversation,
  });

  final Widget mark;
  final VoidCallback? onTalk;
  final bool voiceStarting;
  final VoidCallback? onContinueConversation;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Container(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(JarvisRadii.xl),
        gradient: const LinearGradient(
          begin: Alignment.topLeft,
          end: Alignment.bottomRight,
          colors: [Color(0xfff1efff), Color(0xffffffff), Color(0xffeaf7ff)],
          stops: [0, .55, 1],
        ),
        border: Border.all(color: JarvisColors.outline),
        boxShadow: JarvisShadows.soft,
      ),
      child: Stack(
        children: [
          Positioned(
            right: -40,
            top: -50,
            child: Container(
              width: 180,
              height: 180,
              decoration: const BoxDecoration(
                shape: BoxShape.circle,
                gradient: RadialGradient(
                  colors: [Color(0x33a78bfa), Color(0x00a78bfa)],
                ),
              ),
            ),
          ),
          Padding(
            padding: const EdgeInsets.fromLTRB(22, 22, 22, 20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    mark,
                    const SizedBox(width: 18),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            'Hands-free mode',
                            style: theme.textTheme.titleMedium,
                          ),
                          const SizedBox(height: 4),
                          Text(
                            'Speak naturally — voice picks up this conversation and everything Jarvis remembers.',
                            style: theme.textTheme.bodySmall?.copyWith(
                              fontSize: 13.5,
                            ),
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 18),
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  crossAxisAlignment: WrapCrossAlignment.center,
                  children: [
                    FilledButton.icon(
                      onPressed: onTalk,
                      style: FilledButton.styleFrom(
                        backgroundColor: JarvisColors.ink,
                        minimumSize: const Size(0, 48),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(40),
                        ),
                      ),
                      icon: voiceStarting
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(Icons.graphic_eq_rounded),
                      label: Text(
                        voiceStarting ? 'Connecting…' : 'Talk to Jarvis',
                      ),
                    ),
                    if (onContinueConversation != null)
                      TextButton.icon(
                        onPressed: onContinueConversation,
                        style: TextButton.styleFrom(
                          foregroundColor: JarvisColors.ink,
                          minimumSize: const Size(0, 48),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(40),
                          ),
                        ),
                        icon: const Icon(Icons.forum_outlined, size: 18),
                        label: const Text('Continue conversation'),
                      ),
                  ],
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
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
      border: Border.all(color: JarvisColors.outlineStrong),
      color: JarvisColors.surface.withValues(alpha: .5),
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
  Widget build(BuildContext context) => SurfaceCard(
    margin: const EdgeInsets.only(bottom: 8),
    padding: const EdgeInsets.fromLTRB(14, 12, 10, 12),
    onTap: onTap,
    child: Row(
      children: [
        IconBadge(icon: task.icon, color: task.color),
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
              const SizedBox(height: 6),
              StatusPill(label: task.statusLabel, color: task.color),
            ],
          ),
        ),
        const Icon(Icons.chevron_right_rounded, color: JarvisColors.muted),
      ],
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
    if (value is! Map<String, dynamic>) return null;
    final id = value['id'];
    final title = value['title'];
    final status = value['status'];
    final createdAt = value['createdAt'];
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
    'needs_approval' => Icons.gpp_maybe_outlined,
    'running' => Icons.autorenew_rounded,
    'waiting' => Icons.pause_circle_outline,
    _ => Icons.schedule,
  };

  Color get color => statusStyle(status).color;
}
