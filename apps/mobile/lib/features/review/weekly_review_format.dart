import '../journal/journal_format.dart';

const _shortMonths = [
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

const weekdayInitials = ['M', 'T', 'W', 'T', 'F', 'S', 'S'];

/// Parses a `yyyy-MM-dd` week start; null when invalid.
DateTime? parseWeekStart(Object? value) => parseJournalDate(value);

/// "28 Sep", the label under a week on the trend chart.
String shortWeekLabel(DateTime start) =>
    '${start.day} ${_shortMonths[start.month - 1]}';

/// "28 Sep – 4 Oct", with years only when the week spans two of them or is
/// not in [now]'s year.
String formatWeekRange(DateTime start, {DateTime? now}) {
  final end = start.add(const Duration(days: 6));
  final year = (now ?? DateTime.now()).year;
  final showYear = start.year != end.year || end.year != year;
  final from = showYear && start.year != end.year
      ? '${shortWeekLabel(start)} ${start.year}'
      : shortWeekLabel(start);
  final to = showYear
      ? '${shortWeekLabel(end)} ${end.year}'
      : shortWeekLabel(end);
  return '$from – $to';
}

/// "4.2" for an average, "–" when there is none.
String formatAverage(Object? value) =>
    value is num ? value.toStringAsFixed(1) : '–';

/// "+0.5 on last week", "Same as last week", or null without both numbers.
String? changeLabel(Object? current, Object? previous) {
  if (current is! num || previous is! num) return null;
  final delta = double.parse((current - previous).toStringAsFixed(1));
  if (delta.abs() < 0.1) return 'Same as last week';
  final sign = delta > 0 ? '+' : '−';
  return '$sign${delta.abs().toStringAsFixed(1)} on last week';
}

/// Spoken summary of the trend for screen readers, e.g.
/// "Mood by week: 21 Sep 3.5, 28 Sep no entries".
String trendSemantics(List<({DateTime week, double? mood})> points) {
  if (points.isEmpty) return 'No mood data yet.';
  final parts = [
    for (final point in points)
      '${shortWeekLabel(point.week)} '
          '${point.mood == null ? 'no entries' : point.mood!.toStringAsFixed(1)}',
  ];
  return 'Mood by week out of 5: ${parts.join(', ')}.';
}
