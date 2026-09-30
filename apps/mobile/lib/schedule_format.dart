import 'package:flutter/material.dart';
import 'package:flutter_timezone/flutter_timezone.dart';

/// Reads the device's IANA time zone (for example Europe/Amsterdam). Tests
/// replace this; null means the platform could not tell.
Future<String?> Function() deviceTimeZoneLookup = () async {
  try {
    final zone = (await FlutterTimezone.getLocalTimezone().timeout(
      const Duration(seconds: 3),
    )).identifier.trim();
    return zone.isEmpty ? null : zone;
  } catch (_) {
    return null;
  }
};

const weekdayNames = [
  (1, 'Mon'),
  (2, 'Tue'),
  (4, 'Wed'),
  (8, 'Thu'),
  (16, 'Fri'),
  (32, 'Sat'),
  (64, 'Sun'),
];

/// "Today 14:30", "Tomorrow 08:00", "Wed 14:30" within a week, otherwise the
/// medium date with the time.
String friendlyWhen(BuildContext context, DateTime local, {DateTime? now}) {
  final localizations = MaterialLocalizations.of(context);
  final time = localizations.formatTimeOfDay(TimeOfDay.fromDateTime(local));
  final today = _day(now ?? DateTime.now());
  final days = _day(local).difference(today).inDays;
  final day = switch (days) {
    0 => 'Today',
    1 => 'Tomorrow',
    -1 => 'Yesterday',
    > 1 && < 7 => _weekdayName(local.weekday),
    _ => localizations.formatMediumDate(local),
  };
  return '$day $time';
}

/// "in 25 min", "in 3 h", "in 2 days", "5 min ago". Empty within a minute.
String relativeFromNow(DateTime local, {DateTime? now}) {
  final difference = local.difference(now ?? DateTime.now());
  final future = !difference.isNegative;
  final span = difference.abs();
  final String amount;
  if (span.inMinutes < 1) return future ? 'now' : 'just now';
  if (span.inMinutes < 60) {
    amount = '${span.inMinutes} min';
  } else if (span.inMinutes < 23 * 60 + 30) {
    amount = '${(span.inMinutes / 60).round()} h';
  } else {
    final days = (span.inHours / 24).round();
    amount = days == 1 ? '1 day' : '$days days';
  }
  return future ? 'in $amount' : '$amount ago';
}

/// Plain-language repeat rule: "Every day", "Every weekday", "Every Mon, Wed".
String? repeatLabel(String? recurrence, int weekdays) => switch (recurrence) {
  'daily' => 'Every day',
  'weekdays' => 'Every weekday',
  'weekly' =>
    weekdays == 0
        ? 'Every week'
        : 'Every ${[for (final day in weekdayNames)
            if (weekdays & day.$1 != 0) day.$2].join(', ')}',
  _ => null,
};

String _weekdayName(int weekday) => weekdayNames[weekday - 1].$2;

DateTime _day(DateTime value) => DateTime(value.year, value.month, value.day);
