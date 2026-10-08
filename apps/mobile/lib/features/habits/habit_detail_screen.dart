import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'habit_editor.dart';
import 'habit_models.dart';

/// One habit: streaks, the last twelve weeks, and fixing the past week.
/// Pops with true when anything changed.
class HabitDetailScreen extends StatefulWidget {
  const HabitDetailScreen({required this.http, required this.habit, super.key});

  final Dio http;
  final HabitView habit;

  @override
  State<HabitDetailScreen> createState() => _HabitDetailScreenState();
}

class _HabitDetailScreenState extends State<HabitDetailScreen> {
  late HabitView _habit = widget.habit;
  bool _changed = false;
  bool _busy = false;

  void _close() => Navigator.of(context).pop(_changed);

  Future<void> _setDay(DateTime day, bool done) async {
    if (_busy) return;
    unawaited(HapticFeedback.selectionClick());
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/habits/${_habit.id}/check-ins',
        data: {'date': habitDateKey(day), 'done': done},
      );
      if (!mounted) return;
      setState(() {
        _habit = HabitView.fromJson(jsonObject(response.data) ?? const {});
        _changed = true;
      });
    } on DioException catch (error) {
      _showError(error, 'Could not update that day.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _edit() async {
    final saved = await showHabitEditor(
      context,
      http: widget.http,
      habit: _habit,
    );
    if (saved == null || !mounted) return;
    setState(() {
      _habit = saved;
      _changed = true;
    });
  }

  Future<void> _setArchived(bool archived) async {
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/habits/${_habit.id}/archived',
        data: {'archived': archived},
      );
      if (!mounted) return;
      setState(() {
        _habit = HabitView.fromJson(jsonObject(response.data) ?? const {});
        _changed = true;
      });
      if (archived) _close();
    } on DioException catch (error) {
      _showError(error, 'Could not change the habit.');
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete ${_habit.name}?',
      message:
          'This removes the habit and all ${_habit.totalCheckIns} check-ins. '
          'Archive it instead to keep its history.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<dynamic>('/api/v1/habits/${_habit.id}');
      if (!mounted) return;
      _changed = true;
      _close();
    } on DioException catch (error) {
      _showError(error, 'Could not delete the habit.');
    }
  }

  void _showError(DioException error, String fallback) {
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(firstProblemMessage(error.response?.data) ?? fallback),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final habit = _habit;
    return PopScope<bool>(
      canPop: false,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _close();
      },
      child: Scaffold(
        appBar: AppBar(
          leading: BackButton(onPressed: _close),
          title: PageTitle(habit.name),
          actions: [
            if (!habit.archived)
              IconButton(
                tooltip: 'Edit',
                onPressed: () => unawaited(_edit()),
                icon: const Icon(PhosphorIconsRegular.pencilSimple, size: 20),
              ),
            PopupMenuButton<String>(
              tooltip: 'More',
              icon: const Icon(PhosphorIconsRegular.dotsThree),
              onSelected: (value) => switch (value) {
                'archive' => unawaited(_setArchived(true)),
                'restore' => unawaited(_setArchived(false)),
                _ => unawaited(_delete()),
              },
              itemBuilder: (_) => [
                PopupMenuItem(
                  value: habit.archived ? 'restore' : 'archive',
                  child: Text(habit.archived ? 'Restore' : 'Archive'),
                ),
                const PopupMenuItem(value: 'delete', child: Text('Delete')),
              ],
            ),
          ],
        ),
        body: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            ContentWidth(
              child: Column(
                children: [
                  const SizedBox(height: 8),
                  Container(
                    width: 72,
                    height: 72,
                    alignment: Alignment.center,
                    decoration: BoxDecoration(
                      color: habit.doneToday
                          ? colors.successSoft
                          : colors.surfaceMuted,
                      borderRadius: BorderRadius.circular(22),
                    ),
                    child: Text(
                      habit.displayIcon,
                      style: const TextStyle(fontSize: 36),
                    ),
                  ),
                  const SizedBox(height: 10),
                  Text(
                    habit.archived
                        ? '${habit.cadenceLabel} · Archived'
                        : habit.isWeekly
                        ? '${habit.cadenceLabel} · ${habit.progressLabel}'
                        : habit.cadenceLabel,
                    style: TextStyle(color: colors.muted),
                  ),
                  if (!habit.archived) ...[
                    const SizedBox(height: 14),
                    FilledButton.icon(
                      key: const Key('habit-detail-toggle'),
                      onPressed: _busy
                          ? null
                          : () => unawaited(
                              _setDay(habit.today, !habit.doneToday),
                            ),
                      style: FilledButton.styleFrom(
                        minimumSize: const Size(0, 46),
                        shape: const StadiumBorder(),
                        backgroundColor: habit.doneToday
                            ? colors.successSoft
                            : null,
                        foregroundColor: habit.doneToday
                            ? colors.success
                            : null,
                      ),
                      icon: Icon(
                        habit.doneToday
                            ? PhosphorIconsRegular.checkCircle
                            : PhosphorIconsRegular.check,
                        size: 18,
                      ),
                      label: Text(
                        habit.doneToday ? 'Done today' : 'Mark done today',
                      ),
                    ),
                  ],
                  const SizedBox(height: 20),
                ],
              ),
            ),
            ContentWidth(
              child: Row(
                children: [
                  _StatTile(
                    icon: PhosphorIconsFill.fire,
                    color: colors.warning,
                    value: '${habit.currentStreak}',
                    label: habit.streakUnit == 'weeks'
                        ? 'week streak'
                        : 'day streak',
                  ),
                  const SizedBox(width: 10),
                  _StatTile(
                    icon: PhosphorIconsRegular.trophy,
                    color: colors.accent,
                    value: '${habit.bestStreak}',
                    label: 'best streak',
                  ),
                  const SizedBox(width: 10),
                  _StatTile(
                    icon: PhosphorIconsRegular.calendarCheck,
                    color: colors.success,
                    value: '${habit.totalCheckIns}',
                    label: 'check-ins',
                  ),
                ],
              ),
            ),
            if (!habit.archived) ...[
              const ContentWidth(
                child: SectionHeader(
                  'Last 7 days',
                  padding: EdgeInsets.fromLTRB(4, 22, 0, 8),
                ),
              ),
              ContentWidth(
                child: _RecentDays(
                  habit: habit,
                  enabled: !_busy,
                  onToggle: (day) =>
                      unawaited(_setDay(day, !habit.doneOn(day))),
                ),
              ),
            ],
            const ContentWidth(
              child: SectionHeader(
                'Last 12 weeks',
                padding: EdgeInsets.fromLTRB(4, 22, 0, 8),
              ),
            ),
            ContentWidth(child: HabitHeatmap(habit: habit)),
            ContentWidth(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(4, 16, 4, 0),
                child: Text(
                  'You can also tell Jarvis in chat, for example '
                  '"${habit.name}: done".',
                  style: TextStyle(fontSize: 13, color: colors.muted),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.icon,
    required this.color,
    required this.value,
    required this.label,
  });

  final IconData icon;
  final Color color;
  final String value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Expanded(
      child: SurfaceCard(
        padding: const EdgeInsets.symmetric(vertical: 14, horizontal: 10),
        borderColor: colors.outline,
        child: Column(
          children: [
            Icon(icon, size: 20, color: color),
            const SizedBox(height: 6),
            RollingNumber(
              value,
              countUp: true,
              style: Theme.of(context).textTheme.headlineSmall,
            ),
            Text(
              label,
              textAlign: TextAlign.center,
              style: TextStyle(fontSize: 12, color: colors.muted),
            ),
          ],
        ),
      ),
    );
  }
}

class _RecentDays extends StatelessWidget {
  const _RecentDays({
    required this.habit,
    required this.enabled,
    required this.onToggle,
  });

  final HabitView habit;
  final bool enabled;
  final ValueChanged<DateTime> onToggle;

  static const _weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final today = habit.today;
    return Row(
      children: [
        for (var offset = 6; offset >= 0; offset--)
          Expanded(
            child: Builder(
              builder: (context) {
                final day = DateTime(
                  today.year,
                  today.month,
                  today.day - offset,
                );
                final done = habit.doneOn(day);
                return Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 3),
                  child: Semantics(
                    button: true,
                    checked: done,
                    label: '${_weekdays[day.weekday - 1]} ${day.day}',
                    child: InkWell(
                      borderRadius: BorderRadius.circular(14),
                      onTap: enabled ? () => onToggle(day) : null,
                      child: AnimatedContainer(
                        duration: const Duration(milliseconds: 200),
                        padding: const EdgeInsets.symmetric(vertical: 10),
                        decoration: BoxDecoration(
                          color: done ? colors.success : colors.surface,
                          borderRadius: BorderRadius.circular(14),
                          border: Border.all(
                            color: offset == 0 && !done
                                ? colors.accent
                                : done
                                ? colors.success
                                : colors.outline,
                          ),
                        ),
                        child: Column(
                          children: [
                            Text(
                              _weekdays[day.weekday - 1],
                              style: TextStyle(
                                fontSize: 11,
                                color: done ? colors.onInk : colors.muted,
                              ),
                            ),
                            const SizedBox(height: 2),
                            Text(
                              '${day.day}',
                              style: TextStyle(
                                fontSize: 16,
                                fontWeight: FontWeight.w600,
                                color: done ? colors.onInk : colors.ink,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
      ],
    );
  }
}

/// Twelve weeks of check-ins, one column per week and one row per weekday.
class HabitHeatmap extends StatelessWidget {
  const HabitHeatmap({required this.habit, super.key});

  final HabitView habit;

  static const weeks = 12;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final lastWeek = habitWeekStart(habit.today);
    final firstWeek = DateTime(
      lastWeek.year,
      lastWeek.month,
      lastWeek.day - 7 * (weeks - 1),
    );
    return SurfaceCard(
      padding: const EdgeInsets.all(14),
      borderColor: colors.outline,
      child: LayoutBuilder(
        builder: (context, constraints) {
          const gap = 4.0;
          const labelWidth = 18.0;
          final cell =
              ((constraints.maxWidth - labelWidth - gap * weeks) / weeks).clamp(
                8.0,
                22.0,
              );
          return Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              SizedBox(
                width: labelWidth,
                child: Column(
                  children: [
                    for (final letter in ['M', '', 'W', '', 'F', '', 'S'])
                      SizedBox(
                        height: cell + gap,
                        child: Text(
                          letter,
                          style: TextStyle(fontSize: 10, color: colors.muted),
                        ),
                      ),
                  ],
                ),
              ),
              for (var week = 0; week < weeks; week++)
                Padding(
                  padding: const EdgeInsets.only(left: gap),
                  child: Column(
                    children: [
                      for (var weekday = 0; weekday < 7; weekday++)
                        Builder(
                          builder: (context) {
                            final day = DateTime(
                              firstWeek.year,
                              firstWeek.month,
                              firstWeek.day + week * 7 + weekday,
                            );
                            final future = day.isAfter(habit.today);
                            final done = habit.doneOn(day);
                            return Container(
                              width: cell,
                              height: cell,
                              margin: const EdgeInsets.only(bottom: gap),
                              decoration: BoxDecoration(
                                color: future
                                    ? Colors.transparent
                                    : done
                                    ? colors.success
                                    : colors.surfaceMuted,
                                borderRadius: BorderRadius.circular(cell / 3.5),
                                border: day == habit.today
                                    ? Border.all(
                                        color: colors.accent,
                                        width: 1.4,
                                      )
                                    : null,
                              ),
                            );
                          },
                        ),
                    ],
                  ),
                ),
            ],
          );
        },
      ),
    );
  }
}
