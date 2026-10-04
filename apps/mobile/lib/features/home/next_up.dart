import '../../json_maps.dart';

/// The next thing on the person's day: a calendar event or a reminder.
class UpNext {
  const UpNext({
    required this.title,
    required this.start,
    this.detail,
    this.reminder = false,
  });

  final String title;
  final DateTime start;

  /// A place or a note shown under the title.
  final String? detail;
  final bool reminder;
}

/// Calendar events and reminders from the home briefing that have not passed,
/// soonest first. An event that has begun but not ended still counts.
List<UpNext> upcomingItems(Map<String, dynamic>? briefing, DateTime now) {
  if (briefing == null) return const [];
  final items = <UpNext>[];
  final calendar = jsonObject(briefing['calendar']) ?? const {};
  for (final event in jsonMaps(calendar['events'])) {
    final start = jsonDate(event['startAt'], local: true);
    final title = asJsonString(event['title']);
    if (start == null || title == null || title.isEmpty) continue;
    final end = jsonDate(event['endAt'], local: true);
    final over = (end ?? start.add(const Duration(minutes: 30))).isBefore(now);
    if (over) continue;
    items.add(
      UpNext(
        title: title,
        start: start,
        detail: asJsonString(event['location']),
      ),
    );
  }
  for (final reminder in jsonMaps(briefing['reminders'])) {
    final due = jsonDate(reminder['dueAt'], local: true);
    final title = asJsonString(reminder['title']);
    if (due == null || title == null || title.isEmpty || due.isBefore(now)) {
      continue;
    }
    items.add(UpNext(title: title, start: due, reminder: true));
  }
  items.sort((a, b) => a.start.compareTo(b.start));
  return items;
}

/// "19:45".
String clockTime(DateTime time) =>
    '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}';

/// "now", "in 25 min", "in 1 h 29 min", "in 3 h", or the weekday when it is a
/// day or more away.
String countdownLabel(DateTime start, DateTime now) {
  final gap = start.difference(now);
  if (gap.inMinutes <= 0) return 'now';
  if (gap.inHours >= 24) {
    final today = DateTime(now.year, now.month, now.day);
    final day = DateTime(start.year, start.month, start.day);
    if (day.difference(today).inDays == 1) return 'tomorrow';
    return const [
      'Monday',
      'Tuesday',
      'Wednesday',
      'Thursday',
      'Friday',
      'Saturday',
      'Sunday',
    ][start.weekday - 1];
  }
  final hours = gap.inHours;
  final minutes = gap.inMinutes % 60;
  if (hours == 0) return 'in $minutes min';
  if (minutes == 0) return 'in $hours h';
  return 'in $hours h $minutes min';
}

/// "Saturday 3 October".
String longDate(DateTime now) {
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
  return '${weekdays[now.weekday - 1]} ${now.day} ${months[now.month - 1]}';
}

/// "Good morning", "Good afternoon" or "Good evening".
String greetingFor(DateTime now) => switch (now.hour) {
  < 12 => 'Good morning',
  < 18 => 'Good afternoon',
  _ => 'Good evening',
};
