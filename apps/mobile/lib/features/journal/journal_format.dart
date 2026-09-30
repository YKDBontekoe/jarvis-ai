/// Prompt that starts a spoken or typed journaling conversation with Jarvis.
const journalTalkPrompt =
    "I'd like to journal about my day. Ask me about it one question at a time "
    '(highlights, what was hard, what I am grateful for), then save it to my '
    'journal when we are done.';

const _weekdays = [
  'Monday',
  'Tuesday',
  'Wednesday',
  'Thursday',
  'Friday',
  'Saturday',
  'Sunday',
];
const _months = [
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

/// `yyyy-MM-dd`, the wire format for an entry date.
String journalDateKey(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-'
    '${date.month.toString().padLeft(2, '0')}-'
    '${date.day.toString().padLeft(2, '0')}';

/// Parses a `yyyy-MM-dd` wire date as a local calendar day; null when invalid.
DateTime? parseJournalDate(Object? value) {
  if (value is! String) return null;
  final match = RegExp(r'^(\d{4})-(\d{2})-(\d{2})').firstMatch(value);
  if (match == null) return null;
  final year = int.parse(match.group(1)!);
  final month = int.parse(match.group(2)!);
  final day = int.parse(match.group(3)!);
  if (month < 1 || month > 12 || day < 1 || day > 31) return null;
  return DateTime(year, month, day);
}

/// "Today", "Yesterday", or e.g. "Tuesday, 30 September".
String formatJournalDate(DateTime date, {DateTime? now}) {
  final current = now ?? DateTime.now();
  final today = DateTime(current.year, current.month, current.day);
  final day = DateTime(date.year, date.month, date.day);
  final age = today.difference(day).inDays;
  if (age == 0) return 'Today';
  if (age == 1) return 'Yesterday';
  final suffix = day.year == today.year ? '' : ' ${day.year}';
  return '${_weekdays[day.weekday - 1]}, ${day.day} ${_months[day.month - 1]}$suffix';
}

/// Splits a comma or whitespace separated tag field into clean tags.
List<String> parseJournalTags(String input) {
  final seen = <String>{};
  final tags = <String>[];
  for (final raw in input.split(RegExp(r'[,\n]'))) {
    var tag = raw.trim().toLowerCase();
    while (tag.startsWith('#')) {
      tag = tag.substring(1).trim();
    }
    if (tag.isEmpty || tag.length > 32 || !seen.add(tag)) continue;
    tags.add(tag);
  }
  return tags.take(10).toList();
}

const _moodEmoji = ['😞', '😕', '😐', '🙂', '😄'];

/// Face for a 1-5 mood; empty for values outside the scale.
String moodEmoji(int mood) =>
    mood >= 1 && mood <= 5 ? _moodEmoji[mood - 1] : '';

/// One-line label for a 1-5 or 1-10 rating, e.g. "🙂 Mood 4/5".
String ratingLabel(String name, int value, int max) => '$name $value/$max';
