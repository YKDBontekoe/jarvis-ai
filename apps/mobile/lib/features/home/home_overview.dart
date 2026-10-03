import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../ui/phosphor_icons.dart';

import '../../json_maps.dart';
import '../tasks/task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../chat/chat_widgets.dart';
import '../chat/mcp_setup.dart';
import '../chat/tool_catalog.dart';
import '../planner/day_planner_screen.dart';
import '../habits/habits_home_card.dart';
import '../usage/usage_screen.dart';
import '../settings/codex_sign_in_card.dart';

part 'home_overview_widgets.dart';

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
    this.onOpenApprovals,
    this.onOpenReminders,
    this.onOpenIntegrations,
    this.onOpenCoding,
    this.onOpenHabits,
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

  /// Opens pending approvals from the home briefing.
  final VoidCallback? onOpenApprovals;

  /// Opens reminders from the home briefing.
  final VoidCallback? onOpenReminders;

  /// Opens guided integration packs.
  final VoidCallback? onOpenIntegrations;

  /// Opens coding-run review.
  final VoidCallback? onOpenCoding;

  /// Opens habits; today's habits are shown when this is set.
  final VoidCallback? onOpenHabits;

  @override
  State<HomeOverview> createState() => _HomeOverviewState();
}

class _HomeOverviewState extends State<HomeOverview>
    with WidgetsBindingObserver {
  List<_ActiveTask> _tasks = [];
  Map<String, dynamic>? _usage;
  Map<String, dynamic>? _briefing;
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
      unawaited(_loadBriefing());
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
      _briefing = null;
      _loading = false;
      _error = null;
    } else if (!oldWidget.ready ||
        oldWidget.refreshRevision != widget.refreshRevision) {
      unawaited(_load());
      unawaited(_loadUsage());
      unawaited(_loadBriefing());
    } else if (oldWidget.onOpenUsage == null && widget.onOpenUsage != null) {
      unawaited(_loadUsage());
    }
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && widget.ready) {
      unawaited(_load());
      unawaited(_loadUsage());
      unawaited(_loadBriefing());
    }
  }

  Future<void> _refresh() async {
    await Future.wait([_load(), _loadUsage(), _loadBriefing()]);
  }

  Future<void> _loadBriefing() async {
    if (!widget.ready || !mounted) return;
    try {
      final response = await widget.http.get<dynamic>('/api/v1/home');
      if (!mounted || !widget.ready) return;
      setState(() => _briefing = jsonObject(response.data));
    } on DioException {
      if (mounted) setState(() => _briefing = null);
    } catch (_) {
      if (mounted) setState(() => _briefing = null);
    }
  }

  Future<void> _loadUsage() async {
    if (!widget.ready || widget.onOpenUsage == null || !mounted) return;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/usage',
        queryParameters: const {'period': '7d'},
      );
      if (!mounted || !widget.ready || widget.onOpenUsage == null) return;
      setState(() => _usage = jsonObject(response.data));
    } on DioException {
      if (mounted) setState(() => _usage = null);
    } catch (_) {
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
      final response = await widget.http.get<dynamic>(
        '/api/v1/tasks',
        cancelToken: request,
      );
      final tasks =
          jsonMaps(
              response.data,
            ).map(_ActiveTask.fromJson).whereType<_ActiveTask>().toList()
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
    } catch (_) {
      if (mounted && revision == _requestRevision) {
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

  Future<void> _openToday() async {
    final ask = widget.onSuggestion;
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => DayPlannerScreen(http: widget.http, onAskInChat: ask),
      ),
    );
    if (mounted) unawaited(_loadBriefing());
  }

  Widget _briefingSections() {
    final briefing = _briefing;
    if (briefing == null) return const SizedBox.shrink();
    final portrait = asJsonString(briefing['portrait']);
    final reminders = jsonMaps(briefing['reminders']);
    final approvals = jsonMaps(briefing['approvals']);
    final calendar = jsonObject(briefing['calendar']) ?? const {};
    final events = jsonMaps(calendar['events']);
    final device = jsonObject(briefing['device']);
    final packs = jsonMaps(briefing['packs']);
    final missingPacks = packs
        .where((pack) => asJsonBool(pack['installed']) == false)
        .toList();
    return Column(
      key: const Key('home-briefing'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        CodexSignInCard(http: widget.http),
        if (portrait != null && portrait.isNotEmpty) ...[
          SurfaceCard(
            child: Text(
              portrait,
              style: TextStyle(
                color: JarvisColors.of(context).inkSoft,
                height: 1.45,
              ),
            ),
          ),
          const SizedBox(height: 12),
        ],
        if (approvals.isNotEmpty)
          _BriefingListCard(
            key: const Key('home-approvals'),
            icon: PhosphorIconsRegular.shieldWarning,
            title:
                '${approvals.length} ${approvals.length == 1 ? 'approval' : 'approvals'} waiting',
            subtitle: approvals
                .take(3)
                .map((item) => _toolLabel(asJsonString(item['toolName'])))
                .join(' · '),
            onTap: widget.onOpenApprovals,
          ),
        if (reminders.isNotEmpty) ...[
          if (approvals.isNotEmpty) const SizedBox(height: 12),
          _BriefingListCard(
            key: const Key('home-reminders'),
            icon: PhosphorIconsRegular.bell,
            title: reminders.length == 1
                ? (asJsonString(reminders.first['title']) ?? 'Reminder')
                : '${reminders.length} upcoming reminders',
            subtitle: reminders
                .take(3)
                .map((item) {
                  final due = jsonDate(item['dueAt'], local: true);
                  final when = due == null ? null : _shortWhen(due);
                  if (reminders.length == 1) return when ?? '';
                  final title = asJsonString(item['title']) ?? 'Reminder';
                  return when == null ? title : '$title · $when';
                })
                .join('\n'),
            onTap: widget.onOpenReminders,
          ),
        ],
        if (events.isNotEmpty) ...[
          const SizedBox(height: 12),
          _BriefingListCard(
            key: const Key('home-calendar'),
            icon: PhosphorIconsRegular.sunHorizon,
            title: asJsonBool(calendar['connected'])
                ? 'Today’s calendar'
                : 'Upcoming events',
            subtitle: events
                .take(3)
                .map((item) {
                  final start = jsonDate(item['startAt'], local: true);
                  final title = asJsonString(item['title']) ?? 'Event';
                  return start == null
                      ? title
                      : '$title · ${_shortWhen(start)}';
                })
                .join('\n'),
            onTap: _openToday,
          ),
        ] else if (asJsonBool(calendar['connected']) == false &&
            (widget.onSuggestion != null ||
                widget.onOpenIntegrations != null)) ...[
          const SizedBox(height: 12),
          _BriefingListCard(
            icon: PhosphorIconsRegular.calendarBlank,
            title: 'Connect a calendar',
            subtitle:
                'Ask Jarvis to add your calendar so today’s events show up here.',
            onTap: widget.onSuggestion == null
                ? widget.onOpenIntegrations
                : () => widget.onSuggestion!(mcpCalendarPrompt),
          ),
        ],
        if (events.isEmpty) ...[
          const SizedBox(height: 12),
          _BriefingListCard(
            key: const Key('home-today'),
            icon: PhosphorIconsRegular.sunHorizon,
            title: 'Plan your day',
            subtitle:
                'Add what you want to get done and Jarvis fits it into your free time.',
            onTap: _openToday,
          ),
        ],
        if (device != null) ...[
          const SizedBox(height: 12),
          _BriefingListCard(
            icon: PhosphorIconsRegular.deviceMobile,
            title: device['batteryPercent'] is num
                ? 'This device · ${asJsonInt(device['batteryPercent'])}%'
                : 'This device',
            subtitle: [
              if (asJsonBool(device['charging'])) 'Charging',
              if (asJsonBool(device['hasLocation'])) 'Location available',
            ].join(' · '),
          ),
        ],
        if (missingPacks.isNotEmpty &&
            (widget.onSuggestion != null ||
                widget.onOpenIntegrations != null)) ...[
          const SizedBox(height: 12),
          _BriefingListCard(
            icon: PhosphorIconsRegular.plugsConnected,
            title: 'Suggested connections',
            subtitle: missingPacks
                .map((pack) => asJsonString(pack['name']) ?? 'Pack')
                .join(' · '),
            onTap: widget.onSuggestion == null
                ? widget.onOpenIntegrations
                : () => widget.onSuggestion!(
                    'Help me connect ${missingPacks.map((pack) => asJsonString(pack['name']) ?? 'this').join(', ')} in this chat. Show the setup card.',
                  ),
          ),
        ],
        if (widget.onOpenCoding != null) ...[
          const SizedBox(height: 12),
          _BriefingListCard(
            icon: PhosphorIconsRegular.code,
            title: 'Coding runs',
            subtitle: 'Review isolated worktrees and diffs from coding tasks.',
            onTap: widget.onOpenCoding,
          ),
        ],
      ],
    );
  }

  String _shortWhen(DateTime time) {
    final local = time.toLocal();
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    final day = DateTime(local.year, local.month, local.day);
    final hour =
        '${local.hour.toString().padLeft(2, '0')}:${local.minute.toString().padLeft(2, '0')}';
    if (day == today) return hour;
    if (day == today.add(const Duration(days: 1))) return 'tomorrow $hour';
    return '${local.month}/${local.day} $hour';
  }

  /// First-run steps, or null once the account has settled in (or usage is unknown).
  /// The card only nudges new accounts: it disappears after a handful of messages.
  List<_StartStep>? get _checklist {
    final usage = _usage;
    if (usage == null || widget.onSuggestion == null) return null;
    final activity = jsonObject(usage['activity']) ?? const {};
    final sent = asJsonInt(jsonObject(activity['messagesSent'])?['total']);
    if (sent >= 20) return null;
    final memories = asJsonInt(
      jsonObject(usage['personalization'])?['activeMemories'],
    );
    final packs = jsonMaps(_briefing?['packs']);
    final connected = packs.any((pack) => asJsonBool(pack['installed']));
    final steps = [
      _StartStep(
        label: 'Say hello',
        hint: 'Ask what Jarvis can do',
        done: sent > 0,
        onTap: () =>
            widget.onSuggestion!('Hi Jarvis! What can you help me with?'),
      ),
      _StartStep(
        label: 'Connect an app',
        hint: 'Calendar, mail, GitHub, and more',
        done: connected,
        onTap: () => widget.onSuggestion!(mcpSetupPrompt),
      ),
      _StartStep(
        label: 'Help Jarvis know you',
        hint: 'It remembers what matters to you',
        done: memories > 0,
        onTap: () => widget.onSuggestion!(
          'Ask me a few questions so you can get to know me, and remember my answers.',
        ),
      ),
    ];
    return steps.every((step) => step.done) ? const [] : steps;
  }

  Widget _usageCard() {
    final usage = _usage;
    if (usage == null) return const SizedBox.shrink();
    final personalization = jsonObject(usage['personalization']) ?? const {};
    final codex = jsonObject(usage['codex']) ?? const {};
    final openRouter = jsonObject(usage['openRouter']) ?? const {};
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
                  style: TextStyle(
                    color: JarvisColors.of(context).inkSoft,
                    fontSize: 13,
                  ),
                ),
              ],
            ),
          ),
          Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: JarvisColors.of(context).muted,
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
                child: HeroGlow(
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
                            color: JarvisColors.of(context).muted,
                          ),
                        ),
                        const SizedBox(height: 6),
                        Text(
                          'What do you need?',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontFamily: 'InstrumentSerif',
                            fontSize: 40,
                            height: 1.1,
                            letterSpacing: -.6,
                            color: JarvisColors.of(context).ink,
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
                        if (_checklist case final steps?
                            when steps.isNotEmpty) ...[
                          _GetStartedCard(steps: steps),
                          const SizedBox(height: 20),
                        ],
                        // Sections that load later ease in rather than pop.
                        if (_briefing != null) ...[
                          FadeSlideIn(child: _briefingSections()),
                          const SizedBox(height: 28),
                        ],
                        if (widget.onOpenHabits case final openHabits?
                            when widget.ready)
                          HabitsHomeCard(
                            http: widget.http,
                            onOpen: openHabits,
                            refreshRevision: widget.refreshRevision,
                          ),
                        if (_usage != null && widget.onOpenUsage != null) ...[
                          FadeSlideIn(index: 1, child: _usageCard()),
                          const SizedBox(height: 28),
                        ],
                        if (widget.onSuggestion != null) ...[
                          Padding(
                            padding: const EdgeInsets.only(left: 4, bottom: 10),
                            child: Text(
                              'Or try one of these:',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: JarvisColors.of(context).muted,
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
                          FadeSlideIn(
                            index: 2,
                            child: GroupedSection(
                              dividerIndent: 64,
                              children: [
                                for (final task in _tasks.take(3))
                                  _TaskRow(
                                    task: task,
                                    onTap: () => unawaited(_openTask(task)),
                                  ),
                              ],
                            ),
                          ),
                      ],
                    ),
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

class _StartStep {
  const _StartStep({
    required this.label,
    required this.hint,
    required this.done,
    required this.onTap,
  });

  final String label;
  final String hint;
  final bool done;
  final VoidCallback onTap;
}

/// A short checklist that guides a new account through its first minutes.
class _GetStartedCard extends StatelessWidget {
  const _GetStartedCard({required this.steps});

  final List<_StartStep> steps;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final done = steps.where((step) => step.done).length;
    return SurfaceCard(
      key: const Key('home-get-started'),
      elevated: true,
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Get started',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              Text(
                '$done of ${steps.length}',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
          const SizedBox(height: 8),
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: TweenAnimationBuilder<double>(
              tween: Tween(end: done / steps.length),
              duration: const Duration(milliseconds: 500),
              curve: Curves.easeOutCubic,
              builder: (context, value, _) => LinearProgressIndicator(
                value: value,
                minHeight: 4,
                backgroundColor: colors.surfaceMuted,
              ),
            ),
          ),
          const SizedBox(height: 6),
          for (final step in steps)
            InkWell(
              borderRadius: BorderRadius.circular(JarvisRadii.md),
              onTap: step.done ? null : step.onTap,
              child: Padding(
                padding: const EdgeInsets.symmetric(vertical: 10),
                child: Row(
                  children: [
                    AnimatedSwitcher(
                      duration: const Duration(milliseconds: 250),
                      child: Icon(
                        step.done
                            ? PhosphorIconsRegular.checkCircle
                            : PhosphorIconsRegular.circle,
                        key: ValueKey(step.done),
                        size: 22,
                        color: step.done ? colors.success : colors.muted,
                      ),
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            step.label,
                            style: TextStyle(
                              fontWeight: FontWeight.w500,
                              color: step.done ? colors.muted : colors.ink,
                              decoration: step.done
                                  ? TextDecoration.lineThrough
                                  : null,
                            ),
                          ),
                          if (!step.done)
                            Text(
                              step.hint,
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                        ],
                      ),
                    ),
                    if (!step.done)
                      Icon(
                        PhosphorIconsRegular.caretRight,
                        size: 16,
                        color: colors.muted,
                      ),
                  ],
                ),
              ),
            ),
        ],
      ),
    );
  }
}

/// "CreateCalendarEvent" → "Create calendar event".
String _toolLabel(String? tool) {
  if (tool == null) return 'Tool';
  final name = humanizeToolName(tool);
  return name[0].toUpperCase() + name.substring(1);
}
