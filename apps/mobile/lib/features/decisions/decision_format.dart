import '../journal/journal_format.dart';

/// `0.7` becomes "70%".
String percentLabel(num probability) => '${(probability * 100).round()}%';

/// The decision's `yyyy-MM-dd` review date as a local calendar day.
DateTime? parseReviewDate(Object? value) => parseJournalDate(value);

/// When the outcome is due, relative to today: "Today", "Tomorrow", "In 5 days", "3 days overdue".
String reviewLabel(DateTime reviewOn, {DateTime? now}) {
  final current = now ?? DateTime.now();
  final today = DateTime(current.year, current.month, current.day);
  final day = DateTime(reviewOn.year, reviewOn.month, reviewOn.day);
  final days = day.difference(today).inDays;
  if (days == 0) return 'Today';
  if (days == 1) return 'Tomorrow';
  if (days == -1) return '1 day overdue';
  if (days < 0) return '${-days} days overdue';
  if (days < 14) return 'In $days days';
  if (days < 60) return 'In ${(days / 7).round()} weeks';
  return 'In ${(days / 30).round()} months';
}

/// A plain-language reading of a Brier score (0 perfect, 0.25 is always saying 50%).
String brierVerdict(double brier) {
  if (brier < 0.10) return 'Sharp';
  if (brier < 0.18) return 'Well calibrated';
  if (brier < 0.25) return 'Better than a coin flip';
  return 'Worse than always saying 50%';
}

String trendLabel(String trend) => switch (trend) {
  'improving' => 'Improving',
  'worsening' => 'Slipping',
  _ => 'Steady',
};

const reviewShortcuts = <(String, int)>[
  ('Tomorrow', 1),
  ('1 week', 7),
  ('1 month', 30),
  ('3 months', 90),
];

/// "Score 0.12 · better than before" for the weekly review; null when nothing was scored.
String? brierChangeLabel(Object? score, Object? previous) {
  if (score is! num) return null;
  final base = 'Score ${score.toStringAsFixed(2)}';
  if (previous is! num) return base;
  final delta = previous - score;
  if (delta > 0.02) return '$base · better than before';
  if (delta < -0.02) return '$base · worse than before';
  return '$base · in line with before';
}
