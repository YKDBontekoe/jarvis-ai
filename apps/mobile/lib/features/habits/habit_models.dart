import '../../json_maps.dart';

/// Emoji offered when creating a habit; the first is the default.
const habitIconSuggestions = [
  '✅',
  '🏃',
  '💪',
  '🧘',
  '📚',
  '💧',
  '🥗',
  '😴',
  '🚶',
  '✍️',
  '🎸',
  '🧹',
];

/// Quick starts shown on the empty screen.
const habitTemplates = [
  (name: 'Exercise', icon: '🏃', cadence: 'weekly', target: 3),
  (name: 'Read', icon: '📚', cadence: 'daily', target: 1),
  (name: 'Meditate', icon: '🧘', cadence: 'daily', target: 1),
  (name: 'Drink water', icon: '💧', cadence: 'daily', target: 1),
];

DateTime? parseHabitDate(Object? value) {
  final text = asJsonString(value);
  if (text == null) return null;
  final parsed = DateTime.tryParse(text);
  return parsed == null
      ? null
      : DateTime(parsed.year, parsed.month, parsed.day);
}

String habitDateKey(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-'
    '${date.month.toString().padLeft(2, '0')}-'
    '${date.day.toString().padLeft(2, '0')}';

DateTime habitWeekStart(DateTime date) =>
    DateTime(date.year, date.month, date.day - (date.weekday - 1));

/// A habit as the API returns it, with its streak figures for [today].
class HabitView {
  const HabitView({
    required this.id,
    required this.name,
    required this.icon,
    required this.cadence,
    required this.targetPerWeek,
    required this.archived,
    required this.today,
    required this.currentStreak,
    required this.bestStreak,
    required this.streakUnit,
    required this.doneToday,
    required this.thisWeekCount,
    required this.totalCheckIns,
    required this.openToday,
    required this.recentDates,
  });

  factory HabitView.fromJson(Map<String, dynamic> json) {
    final stats = jsonObject(json['stats']) ?? const <String, dynamic>{};
    final today = parseHabitDate(stats['today']) ?? _localToday();
    final dates = jsonStrings(
      stats['recentDates'],
    ).map(parseHabitDate).whereType<DateTime>().toSet();
    return HabitView(
      id: asJsonString(json['id']) ?? '',
      name: asJsonString(json['name']) ?? 'Habit',
      icon: asJsonString(json['icon']),
      cadence: asJsonString(json['cadence']) ?? 'daily',
      targetPerWeek: asJsonInt(json['targetPerWeek'], 1),
      archived: json['archived'] == true,
      today: today,
      currentStreak: asJsonInt(stats['currentStreak']),
      bestStreak: asJsonInt(stats['bestStreak']),
      streakUnit: asJsonString(stats['streakUnit']) ?? 'days',
      doneToday: stats['doneToday'] == true,
      thisWeekCount: asJsonInt(stats['thisWeekCount']),
      totalCheckIns: asJsonInt(stats['totalCheckIns']),
      openToday: stats['openToday'] == true,
      recentDates: dates,
    );
  }

  final String id;
  final String name;
  final String? icon;
  final String cadence;
  final int targetPerWeek;
  final bool archived;
  final DateTime today;
  final int currentStreak;
  final int bestStreak;
  final String streakUnit;
  final bool doneToday;
  final int thisWeekCount;
  final int totalCheckIns;
  final bool openToday;
  final Set<DateTime> recentDates;

  bool get isWeekly => cadence == 'weekly';
  String get displayIcon => (icon == null || icon!.isEmpty) ? '✅' : icon!;
  bool get weekGoalMet => isWeekly && thisWeekCount >= targetPerWeek;

  bool doneOn(DateTime day) =>
      recentDates.contains(DateTime(day.year, day.month, day.day));

  /// "Daily", "3× a week".
  String get cadenceLabel => isWeekly ? '$targetPerWeek× a week' : 'Daily';

  /// "12-day streak", "2-week streak", or null without a streak.
  String? get streakLabel {
    if (currentStreak <= 0) return null;
    final unit = streakUnit == 'weeks' ? 'week' : 'day';
    return '$currentStreak-$unit streak';
  }

  String get progressLabel => isWeekly
      ? '$thisWeekCount of $targetPerWeek this week'
      : doneToday
      ? 'Done today'
      : 'Not done yet';

  /// The habit after checking today in or out, before the server confirms.
  HabitView withToday(bool done) {
    if (done == doneToday) return this;
    final delta = done ? 1 : -1;
    final dates = {...recentDates};
    done ? dates.add(today) : dates.remove(today);
    final week = thisWeekCount + delta;
    var streak = currentStreak;
    if (!isWeekly) {
      streak = (currentStreak + delta).clamp(0, 1 << 30);
    } else if ((thisWeekCount >= targetPerWeek) != (week >= targetPerWeek)) {
      streak = (currentStreak + delta).clamp(0, 1 << 30);
    }
    return HabitView(
      id: id,
      name: name,
      icon: icon,
      cadence: cadence,
      targetPerWeek: targetPerWeek,
      archived: archived,
      today: today,
      currentStreak: streak,
      bestStreak: streak > bestStreak ? streak : bestStreak,
      streakUnit: streakUnit,
      doneToday: done,
      thisWeekCount: week,
      totalCheckIns: totalCheckIns + delta,
      openToday: !done && (!isWeekly || week < targetPerWeek),
      recentDates: dates,
    );
  }

  static DateTime _localToday() {
    final now = DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }
}

class HabitSettingsView {
  const HabitSettingsView({
    this.eveningCheckIn = true,
    this.checkInTime = '20:30',
    this.timeZoneId,
  });

  factory HabitSettingsView.fromJson(Map<String, dynamic>? json) =>
      HabitSettingsView(
        eveningCheckIn: json?['eveningCheckIn'] != false,
        checkInTime: asJsonString(json?['checkInTime']) ?? '20:30',
        timeZoneId: asJsonString(json?['timeZoneId']),
      );

  final bool eveningCheckIn;
  final String checkInTime;
  final String? timeZoneId;

  Map<String, dynamic> toJson() => {
    'eveningCheckIn': eveningCheckIn,
    'checkInTime': checkInTime,
    'timeZoneId': timeZoneId,
  };
}
