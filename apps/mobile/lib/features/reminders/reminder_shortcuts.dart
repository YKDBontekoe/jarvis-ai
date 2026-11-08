import 'package:flutter/material.dart';

import '../../schedule_format.dart';
import '../../ui/phosphor_icons.dart';

/// Quick ways to pick when a reminder fires, resolved on the clock of the
/// reminder's own time zone.
enum ReminderShortcut {
  inAnHour('In an hour'),
  thisEvening('This evening'),
  tomorrowMorning('Tomorrow morning');

  const ReminderShortcut(this.label);

  final String label;
}

/// The date and time [shortcut] means when it is [nowInZone] on the reminder's
/// clock, or null when it does not apply (it is already too late for "this
/// evening"). Both are wall-clock values in that zone.
DateTime? resolveReminderShortcut(
  ReminderShortcut shortcut,
  DateTime nowInZone,
) {
  switch (shortcut) {
    case ReminderShortcut.inAnHour:
      final later = nowInZone.add(const Duration(hours: 1));
      // Round up to the next 5 minutes so the time reads cleanly.
      final extra = (5 - later.minute % 5) % 5;
      final rounded = later.add(Duration(minutes: extra));
      return DateTime(
        rounded.year,
        rounded.month,
        rounded.day,
        rounded.hour,
        rounded.minute,
      );
    case ReminderShortcut.thisEvening:
      final minutes = nowInZone.hour * 60 + nowInZone.minute;
      // Early enough: 18:00. Later in the evening: 21:00. After that it is night, not "this evening".
      final hour = minutes < 17 * 60 + 30
          ? 18
          : minutes < 20 * 60 + 30
          ? 21
          : null;
      return hour == null
          ? null
          : DateTime(nowInZone.year, nowInZone.month, nowInZone.day, hour);
    case ReminderShortcut.tomorrowMorning:
      return DateTime(nowInZone.year, nowInZone.month, nowInZone.day + 1, 9);
  }
}

/// What a clock set to [zoneId] shows right now (the phone's clock when the
/// zone is unknown), as plain date and time fields.
DateTime nowOnClock(String? zoneId, {DateTime? now}) {
  final moment = now ?? DateTime.now();
  return wallClockInZone(moment, zoneId) ?? moment;
}

/// A way to push a reminder back: by a length of time, or to a set moment.
class SnoozeChoice {
  const SnoozeChoice.by(Duration this.by) : until = null;
  const SnoozeChoice.until(DateTime this.until) : by = null;

  final Duration? by;

  /// A UTC instant.
  final DateTime? until;
}

/// One sheet for snoozing everywhere: +10 min, +1 hour, tomorrow morning, or a
/// time of the owner's choosing. Each row shows the moment it would fire, on the
/// reminder's own clock (and the phone's, when they differ).
Future<SnoozeChoice?> showSnoozeSheet(
  BuildContext context, {
  String? zoneId,
  DateTime Function()? clock,
}) => showModalBottomSheet<SnoozeChoice>(
  context: context,
  showDragHandle: true,
  isScrollControlled: true,
  builder: (sheet) =>
      _SnoozeSheet(zoneId: zoneId, clock: clock ?? DateTime.now),
);

class _SnoozeSheet extends StatelessWidget {
  const _SnoozeSheet({required this.zoneId, required this.clock});

  final String? zoneId;
  final DateTime Function() clock;

  String _when(BuildContext context, DateTime instant) {
    final described = describeScheduledInstant(
      context,
      instant,
      zoneId,
      now: clock(),
    );
    return described.other == null
        ? described.primary
        : '${described.primary} (${described.other})';
  }

  Future<void> _choose(BuildContext context) async {
    final now = nowOnClock(zoneId, now: clock());
    final date = await showDatePicker(
      context: context,
      initialDate: now,
      firstDate: DateTime(now.year, now.month, now.day),
      lastDate: now.add(const Duration(days: 730)),
    );
    if (date == null || !context.mounted) return;
    final time = await showTimePicker(
      context: context,
      initialTime: TimeOfDay(hour: (now.hour + 1) % 24, minute: 0),
    );
    if (time == null || !context.mounted) return;
    final wall = DateTime(
      date.year,
      date.month,
      date.day,
      time.hour,
      time.minute,
    );
    final instant = instantInZone(wall, zoneId) ?? wall.toUtc();
    if (!instant.isAfter(clock())) {
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          const SnackBar(content: Text('Choose a time in the future.')),
        );
      return;
    }
    Navigator.of(context).pop(SnoozeChoice.until(instant));
  }

  @override
  Widget build(BuildContext context) {
    final now = clock();
    final tomorrowWall = resolveReminderShortcut(
      ReminderShortcut.tomorrowMorning,
      nowOnClock(zoneId, now: now),
    )!;
    final tomorrow =
        instantInZone(tomorrowWall, zoneId) ?? tomorrowWall.toUtc();
    return SafeArea(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final (key, label, icon, instant, choice) in [
            (
              'snooze-10',
              '+10 min',
              PhosphorIconsRegular.clockCounterClockwise,
              now.add(const Duration(minutes: 10)),
              const SnoozeChoice.by(Duration(minutes: 10)),
            ),
            (
              'snooze-60',
              '+1 hour',
              PhosphorIconsRegular.clock,
              now.add(const Duration(hours: 1)),
              const SnoozeChoice.by(Duration(hours: 1)),
            ),
            (
              'snooze-tomorrow',
              'Tomorrow morning',
              PhosphorIconsRegular.sunHorizon,
              tomorrow,
              SnoozeChoice.until(tomorrow),
            ),
          ])
            ListTile(
              key: Key(key),
              leading: Icon(icon),
              title: Text(label),
              subtitle: Text(_when(context, instant)),
              onTap: () => Navigator.of(context).pop(choice),
            ),
          ListTile(
            key: const Key('snooze-pick'),
            leading: const Icon(PhosphorIconsRegular.calendarBlank),
            title: const Text('Choose time…'),
            onTap: () => _choose(context),
          ),
          const SizedBox(height: 8),
        ],
      ),
    );
  }
}
