import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'habit_models.dart';
import 'habits_screen.dart';

/// Today's habits on the home screen, checkable in place. Hidden until the
/// user has a habit, and whenever habits cannot be loaded.
class HabitsHomeCard extends StatefulWidget {
  const HabitsHomeCard({
    required this.http,
    required this.onOpen,
    required this.refreshRevision,
    super.key,
  });

  final Dio http;
  final VoidCallback onOpen;
  final int refreshRevision;

  @override
  State<HabitsHomeCard> createState() => _HabitsHomeCardState();
}

class _HabitsHomeCardState extends State<HabitsHomeCard> {
  List<HabitView> _habits = const [];
  final Set<String> _pending = {};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(HabitsHomeCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.refreshRevision != widget.refreshRevision) unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/habits');
      if (!mounted) return;
      final data = jsonObject(response.data);
      setState(
        () => _habits = jsonMaps(
          data?['habits'],
        ).map(HabitView.fromJson).toList(),
      );
    } on DioException {
      if (mounted) setState(() => _habits = const []);
    }
  }

  void _replace(HabitView habit) => setState(() {
    _habits = [for (final x in _habits) x.id == habit.id ? habit : x];
  });

  Future<void> _toggle(HabitView habit) async {
    if (!_pending.add(habit.id)) return;
    unawaited(HapticFeedback.lightImpact());
    _replace(habit.withToday(!habit.doneToday));
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/habits/${habit.id}/check-ins',
        data: {'done': !habit.doneToday},
      );
      if (mounted) {
        _replace(HabitView.fromJson(jsonObject(response.data) ?? const {}));
      }
    } on DioException {
      if (mounted) _replace(habit);
    } finally {
      _pending.remove(habit.id);
    }
  }

  @override
  Widget build(BuildContext context) {
    if (_habits.isEmpty) return const SizedBox.shrink();
    final colors = JarvisColors.of(context);
    final done = _habits.where((x) => x.doneToday || x.weekGoalMet).length;
    final shown = [
      ..._habits.where((x) => !x.doneToday && !x.weekGoalMet),
      ..._habits.where((x) => x.doneToday || x.weekGoalMet),
    ].take(4).toList();
    return Padding(
      padding: const EdgeInsets.only(bottom: 28),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(
            'Habits today · $done of ${_habits.length}',
            padding: const EdgeInsets.only(left: 4, bottom: 6),
            trailing: TextButton(
              onPressed: widget.onOpen,
              child: const Text('View all'),
            ),
          ),
          GroupedSection(
            dividerIndent: 60,
            children: [
              for (final habit in shown)
                InkWell(
                  key: ValueKey('home-habit-${habit.id}'),
                  onTap: widget.onOpen,
                  child: Padding(
                    padding: const EdgeInsets.fromLTRB(14, 10, 12, 10),
                    child: Row(
                      children: [
                        Text(
                          habit.displayIcon,
                          style: const TextStyle(fontSize: 22),
                        ),
                        const SizedBox(width: 14),
                        Expanded(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                habit.name,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                  fontSize: 15,
                                  fontWeight: FontWeight.w500,
                                ),
                              ),
                              if (habit.streakLabel != null || habit.isWeekly)
                                Row(
                                  children: [
                                    if (habit.streakLabel != null) ...[
                                      Icon(
                                        PhosphorIconsFill.fire,
                                        size: 12,
                                        color: colors.warning,
                                      ),
                                      const SizedBox(width: 3),
                                    ],
                                    Flexible(
                                      child: Text(
                                        habit.isWeekly
                                            ? habit.progressLabel
                                            : habit.streakLabel!,
                                        overflow: TextOverflow.ellipsis,
                                        style: TextStyle(
                                          fontSize: 12.5,
                                          color: colors.muted,
                                        ),
                                      ),
                                    ),
                                  ],
                                ),
                            ],
                          ),
                        ),
                        HabitCheckButton(
                          size: 34,
                          done: habit.doneToday,
                          onPressed: () => unawaited(_toggle(habit)),
                        ),
                      ],
                    ),
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}
