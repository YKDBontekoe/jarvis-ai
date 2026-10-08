import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'habit_detail_screen.dart';
import 'habit_editor.dart';
import 'habit_models.dart';

/// Daily and weekly habits with streaks. Habits can also be checked off in
/// chat ("I went for a run"), and Jarvis asks about open ones in the evening.
class HabitsScreen extends StatefulWidget {
  const HabitsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<HabitsScreen> createState() => _HabitsScreenState();
}

class _HabitsScreenState extends State<HabitsScreen> {
  List<HabitView> _habits = const [];
  HabitSettingsView _settings = const HabitSettingsView();
  bool _loading = true;
  bool _showArchived = false;
  String? _error;
  int _requestRevision = 0;
  final Set<String> _pending = {};

  List<HabitView> get _active => _habits.where((x) => !x.archived).toList();
  List<HabitView> get _archived => _habits.where((x) => x.archived).toList();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = _habits.isEmpty);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/habits',
        queryParameters: {'includeArchived': true},
      );
      if (!mounted || revision != _requestRevision) return;
      final data = jsonObject(response.data) ?? const <String, dynamic>{};
      setState(() {
        _habits = jsonMaps(data['habits']).map(HabitView.fromJson).toList();
        _settings = HabitSettingsView.fromJson(jsonObject(data['settings']));
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your habits.';
      });
    }
  }

  void _replace(HabitView habit) {
    setState(() {
      final next = [..._habits];
      final index = next.indexWhere((x) => x.id == habit.id);
      index < 0 ? next.add(habit) : next[index] = habit;
      _habits = next;
    });
  }

  Future<void> _toggle(HabitView habit) async {
    if (_pending.contains(habit.id)) return;
    final done = !habit.doneToday;
    unawaited(HapticFeedback.lightImpact());
    _pending.add(habit.id);
    _replace(habit.withToday(done));
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/habits/${habit.id}/check-ins',
        data: {'done': done},
      );
      if (!mounted) return;
      _replace(HabitView.fromJson(jsonObject(response.data) ?? const {}));
    } on DioException catch (error) {
      if (!mounted) return;
      _replace(habit);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ??
                'Could not update ${habit.name}.',
          ),
        ),
      );
    } finally {
      _pending.remove(habit.id);
    }
  }

  Future<void> _create([
    ({String name, String icon, String cadence, int target})? template,
  ]) async {
    final habit = await showHabitEditor(
      context,
      http: widget.http,
      template: template,
    );
    if (habit != null && mounted) _replace(habit);
  }

  Future<void> _open(HabitView habit) async {
    final changed = await Navigator.of(context).push<bool>(
      MaterialPageRoute(
        builder: (_) => HabitDetailScreen(http: widget.http, habit: habit),
      ),
    );
    if (changed == true && mounted) unawaited(_load());
  }

  Future<void> _openSettings() async {
    final saved = await showHabitSettings(
      context,
      http: widget.http,
      settings: _settings,
    );
    if (saved != null && mounted) setState(() => _settings = saved);
  }

  @override
  Widget build(BuildContext context) {
    final active = _active;
    final open = active.where((x) => !x.doneToday && !x.weekGoalMet).toList();
    final done = active.where((x) => x.doneToday || x.weekGoalMet).toList();
    final archived = _archived;
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Habits'),
        actions: [
          IconButton(
            key: const Key('habit-settings'),
            tooltip: 'Evening check-in',
            onPressed: () => unawaited(_openSettings()),
            icon: Icon(
              _settings.eveningCheckIn
                  ? PhosphorIconsRegular.bellRinging
                  : PhosphorIconsRegular.bellSlash,
              size: 20,
            ),
          ),
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_create()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: active.isEmpty && archived.isEmpty,
        onRetry: () => unawaited(_load()),
        empty: _EmptyHabits(
          onCreate: (template) => unawaited(_create(template)),
        ),
        child: OrbRefresh(
          onRefresh: _load,
          child: ListView(
            padding: EdgeInsets.fromLTRB(
              16,
              8,
              16,
              32 + MediaQuery.paddingOf(context).bottom,
            ),
            children: [
              if (active.isNotEmpty)
                ContentWidth(
                  child: _TodayHero(habits: active, settings: _settings),
                ),
              if (active.isEmpty)
                ContentWidth(
                  child: InlineNotice(
                    message:
                        'All your habits are archived. Start a new one '
                        'or restore one below.',
                    tone: NoticeTone.info,
                    margin: const EdgeInsets.only(bottom: 14),
                  ),
                ),
              if (open.isNotEmpty) ...[
                const ContentWidth(
                  child: SectionHeader(
                    'To do today',
                    padding: EdgeInsets.fromLTRB(4, 6, 0, 8),
                  ),
                ),
                for (final habit in open)
                  ContentWidth(
                    child: _HabitTile(
                      key: ValueKey('habit-${habit.id}'),
                      habit: habit,
                      onToggle: () => unawaited(_toggle(habit)),
                      onTap: () => unawaited(_open(habit)),
                    ),
                  ),
              ],
              if (done.isNotEmpty) ...[
                const ContentWidth(
                  child: SectionHeader(
                    'Done',
                    padding: EdgeInsets.fromLTRB(4, 10, 0, 8),
                  ),
                ),
                for (final habit in done)
                  ContentWidth(
                    child: _HabitTile(
                      key: ValueKey('habit-${habit.id}'),
                      habit: habit,
                      onToggle: () => unawaited(_toggle(habit)),
                      onTap: () => unawaited(_open(habit)),
                    ),
                  ),
              ],
              if (archived.isNotEmpty)
                ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.only(top: 12),
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: TextButton.icon(
                        onPressed: () =>
                            setState(() => _showArchived = !_showArchived),
                        icon: Icon(
                          _showArchived
                              ? PhosphorIconsRegular.caretDown
                              : PhosphorIconsRegular.caretRight,
                          size: 16,
                        ),
                        label: Text('Archived (${archived.length})'),
                      ),
                    ),
                  ),
                ),
              if (_showArchived)
                for (final habit in archived)
                  ContentWidth(
                    child: _HabitTile(
                      key: ValueKey('habit-${habit.id}'),
                      habit: habit,
                      onTap: () => unawaited(_open(habit)),
                    ),
                  ),
              if (active.isNotEmpty)
                ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(4, 18, 4, 0),
                    child: Text(
                      'Tip: tell Jarvis "I went for a run" or "ik heb gelezen" '
                      'and it checks the habit off for you.',
                      style: TextStyle(
                        fontSize: 13,
                        height: 1.4,
                        color: JarvisColors.of(context).muted,
                      ),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _TodayHero extends StatelessWidget {
  const _TodayHero({required this.habits, required this.settings});

  final List<HabitView> habits;
  final HabitSettingsView settings;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final daily = habits.where((x) => !x.isWeekly).toList();
    final counted = daily.isEmpty ? habits : daily;
    final doneCount = counted
        .where((x) => x.isWeekly ? x.weekGoalMet || x.doneToday : x.doneToday)
        .length;
    final progress = counted.isEmpty ? 0.0 : doneCount / counted.length;
    final allDone = doneCount == counted.length;
    final best = habits.fold<HabitView?>(
      null,
      (best, x) =>
          x.currentStreak > (best?.currentStreak ?? 0) && x.streakUnit == 'days'
          ? x
          : best,
    );
    // Light glances across the card the moment the last habit is ticked.
    return Sheen(
      trigger: allDone,
      delay: Duration.zero,
      borderRadius: BorderRadius.circular(JarvisRadii.lg),
      child: SurfaceCard(
        margin: const EdgeInsets.only(bottom: 8),
        padding: const EdgeInsets.all(18),
        borderColor: colors.outline,
        child: Row(
          children: [
            SizedBox.square(
              dimension: 76,
              child: TweenAnimationBuilder<double>(
                tween: Tween(end: progress),
                duration: const Duration(milliseconds: 520),
                curve: Curves.easeOutCubic,
                builder: (context, value, _) => Stack(
                  fit: StackFit.expand,
                  children: [
                    CircularProgressIndicator(
                      value: value,
                      strokeWidth: 7,
                      strokeCap: StrokeCap.round,
                      backgroundColor: colors.surfaceMuted,
                      color: allDone ? colors.success : colors.accent,
                    ),
                    Center(
                      child: Text(
                        '$doneCount/${counted.length}',
                        style: theme.textTheme.titleMedium?.copyWith(
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
            const SizedBox(width: 18),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    allDone
                        ? 'All done today 🎉'
                        : doneCount == 0
                        ? 'A fresh day'
                        : 'Keep it going',
                    style: TextStyle(
                      fontFamily: 'Geist',
                      fontSize: 26,
                      fontWeight: FontWeight.w600,
                      letterSpacing: -.9,
                      height: 1.1,
                      color: colors.ink,
                    ),
                  ),
                  const SizedBox(height: 6),
                  if (best != null)
                    Row(
                      children: [
                        Icon(
                          PhosphorIconsFill.fire,
                          size: 15,
                          color: colors.warning,
                        ),
                        const SizedBox(width: 4),
                        Flexible(
                          child: Text(
                            '${best.currentStreak}-day streak · ${best.name}',
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 13,
                              color: colors.inkSoft,
                            ),
                          ),
                        ),
                      ],
                    ),
                  const SizedBox(height: 2),
                  Text(
                    settings.eveningCheckIn
                        ? 'Jarvis checks in at ${settings.checkInTime}'
                        : 'Evening check-in is off',
                    style: TextStyle(fontSize: 13, color: colors.muted),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _HabitTile extends StatelessWidget {
  const _HabitTile({
    required this.habit,
    required this.onTap,
    this.onToggle,
    super.key,
  });

  final HabitView habit;
  final VoidCallback onTap;
  final VoidCallback? onToggle;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final complete = habit.doneToday || habit.weekGoalMet;
    final streak = habit.streakLabel;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(14, 12, 10, 12),
      borderColor: complete
          ? colors.success.withValues(alpha: .35)
          : colors.outline,
      onTap: onTap,
      child: Row(
        children: [
          AnimatedContainer(
            duration: const Duration(milliseconds: 220),
            width: 46,
            height: 46,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: complete ? colors.successSoft : colors.surfaceMuted,
              borderRadius: BorderRadius.circular(14),
            ),
            child: Text(
              habit.displayIcon,
              style: const TextStyle(fontSize: 22),
            ),
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  habit.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleMedium?.copyWith(
                    color: habit.archived ? colors.muted : colors.ink,
                  ),
                ),
                const SizedBox(height: 3),
                Row(
                  children: [
                    if (streak != null) ...[
                      Icon(
                        PhosphorIconsFill.fire,
                        size: 13,
                        color: colors.warning,
                      ),
                      const SizedBox(width: 3),
                      Text(
                        streak,
                        style: TextStyle(
                          fontSize: 12.5,
                          fontWeight: FontWeight.w600,
                          color: colors.inkSoft,
                        ),
                      ),
                      Text(
                        '  ·  ',
                        style: TextStyle(fontSize: 12.5, color: colors.muted),
                      ),
                    ],
                    Flexible(
                      child: Text(
                        habit.archived
                            ? 'Archived'
                            : habit.isWeekly
                            ? habit.progressLabel
                            : habit.cadenceLabel,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 12.5, color: colors.muted),
                      ),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                HabitWeekStrip(habit: habit),
              ],
            ),
          ),
          if (onToggle != null) ...[
            const SizedBox(width: 8),
            HabitCheckButton(done: habit.doneToday, onPressed: onToggle!),
          ],
        ],
      ),
    );
  }
}

/// Seven small dots for Monday to Sunday of the current week.
class HabitWeekStrip extends StatelessWidget {
  const HabitWeekStrip({required this.habit, super.key});

  final HabitView habit;

  static const _letters = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final start = habitWeekStart(habit.today);
    // Narrow phones shrink the strip rather than overflow the card.
    return FittedBox(
      fit: BoxFit.scaleDown,
      alignment: Alignment.centerLeft,
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (var i = 0; i < 7; i++)
            Builder(
              builder: (context) {
                final day = DateTime(start.year, start.month, start.day + i);
                final done = habit.doneOn(day);
                final isToday = day == habit.today;
                final future = day.isAfter(habit.today);
                return Padding(
                  padding: const EdgeInsets.only(right: 5),
                  child: Semantics(
                    label: '${_letters[i]} ${done ? 'done' : 'not done'}',
                    child: AnimatedContainer(
                      duration: const Duration(milliseconds: 200),
                      width: 18,
                      height: 18,
                      alignment: Alignment.center,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: done
                            ? colors.success
                            : future
                            ? Colors.transparent
                            : colors.surfaceMuted,
                        border: Border.all(
                          color: isToday && !done
                              ? colors.accent
                              : future
                              ? colors.outline
                              : Colors.transparent,
                          width: 1.4,
                        ),
                      ),
                      child: Text(
                        _letters[i],
                        style: TextStyle(
                          fontSize: 9,
                          fontWeight: FontWeight.w600,
                          color: done ? Colors.white : colors.muted,
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
        ],
      ),
    );
  }
}

/// Large round check for today, filled once done.
class HabitCheckButton extends StatelessWidget {
  const HabitCheckButton({
    required this.done,
    required this.onPressed,
    this.size = 44,
    super.key,
  });

  final bool done;
  final VoidCallback onPressed;
  final double size;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Tooltip(
      message: done ? 'Undo today' : 'Done today',
      child: Semantics(
        button: true,
        checked: done,
        label: done ? 'Undo today' : 'Mark done today',
        excludeSemantics: true,
        child: CelebrationBurst(
          trigger: done,
          radius: size * .85,
          child: InkResponse(
            onTap: onPressed,
            radius: size * .7,
            child: AnimatedContainer(
              duration: JarvisMotion.of(
                context,
                const Duration(milliseconds: 420),
              ),
              curve: JarvisSprings.pop,
              width: size,
              height: size,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: done ? colors.success : Colors.transparent,
                border: Border.all(
                  color: done ? colors.success : colors.outlineStrong,
                  width: 2,
                ),
              ),
              child: AnimatedSwitcher(
                duration: JarvisMotion.of(
                  context,
                  const Duration(milliseconds: 480),
                ),
                reverseDuration: JarvisMotion.of(context, JarvisMotion.fast),
                transitionBuilder: (child, animation) => ScaleTransition(
                  scale: CurvedAnimation(
                    parent: animation,
                    curve: JarvisSprings.pop,
                    reverseCurve: Curves.easeIn,
                  ),
                  child: RotationTransition(
                    turns: Tween(begin: -.12, end: 0.0).animate(animation),
                    child: child,
                  ),
                ),
                child: done
                    ? Icon(
                        PhosphorIconsRegular.check,
                        key: const ValueKey('done'),
                        size: size * .45,
                        color: colors.onInk,
                      )
                    : const SizedBox.shrink(key: ValueKey('open')),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _EmptyHabits extends StatelessWidget {
  const _EmptyHabits({required this.onCreate});

  final ValueChanged<({String name, String icon, String cadence, int target})?>
  onCreate;

  @override
  Widget build(BuildContext context) => EmptyState(
    icon: PhosphorIconsRegular.target,
    title: 'Build habits that stick',
    message:
        'Track daily or weekly habits and keep your streaks going. Check them '
        'off here or just tell Jarvis, and it asks how it went each evening.',
    action: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        FilledButton.icon(
          key: const Key('habit-new'),
          onPressed: () => onCreate(null),
          icon: const Icon(PhosphorIconsRegular.plus, size: 18),
          label: const Text('New habit'),
        ),
        const SizedBox(height: 14),
        Wrap(
          alignment: WrapAlignment.center,
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final template in habitTemplates)
              ActionChip(
                avatar: Text(template.icon),
                label: Text(template.name),
                onPressed: () => onCreate(template),
              ),
          ],
        ),
      ],
    ),
  );
}
