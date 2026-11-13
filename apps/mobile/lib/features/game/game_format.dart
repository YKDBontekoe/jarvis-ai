import '../journal/journal_format.dart';

const _weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
const _months = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

/// A `yyyy-MM-dd` value from the game API as a calendar day.
DateTime? parseGameDate(Object? value) => parseJournalDate(value);

/// "Wed 5 Nov".
String shortDay(DateTime day) =>
    '${_weekdays[day.weekday - 1]} ${day.day} ${_months[day.month - 1]}';

/// When an open bet settles, from the owner's side: "Settles today",
/// "Settles tomorrow", "Until Sun 15 Nov".
String betDeadline(DateTime endsOn, DateTime today) {
  final days = DateTime(
    endsOn.year,
    endsOn.month,
    endsOn.day,
  ).difference(DateTime(today.year, today.month, today.day)).inDays;
  if (days <= 0) return 'Settles today';
  if (days == 1) return 'Settles tomorrow';
  return 'Until ${shortDay(endsOn)}';
}

/// How far into the current level the owner is, as filled segments of [segments].
int levelSegments(
  int points,
  int levelStart,
  int nextLevelAt, {
  int segments = 10,
}) {
  final span = nextLevelAt - levelStart;
  if (span <= 0) return segments;
  return (((points - levelStart) / span) * segments).floor().clamp(0, segments);
}

/// A ghost measurement for display: money without decimals, counts as whole
/// numbers, mood with one decimal.
String ghostValue(num value, String unit) {
  final symbol = switch (unit.toUpperCase()) {
    'EUR' => '€',
    'USD' => r'$',
    'GBP' => '£',
    _ => null,
  };
  if (symbol != null) return '$symbol${value.round()}';
  if (unit.length == 3 && unit.toUpperCase() == unit) {
    return '${value.round()} $unit';
  }
  return value == value.roundToDouble()
      ? '${value.round()}'
      : value.toStringAsFixed(1);
}

/// "Ahead on 2, behind on 1" — or a calmer line when it is level.
String ghostSummary(int ahead, int behind) {
  if (ahead == 0 && behind == 0) return 'Neck and neck with your ghost';
  if (behind == 0) return 'Ahead of your ghost on $ahead';
  if (ahead == 0) return 'Behind your ghost on $behind';
  return 'Ahead on $ahead, behind on $behind';
}
