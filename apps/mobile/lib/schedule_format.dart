import 'package:flutter/material.dart';
import 'package:flutter_timezone/flutter_timezone.dart';
import 'package:timezone/data/latest_all.dart' as tz_data;
import 'package:timezone/timezone.dart' as tz;

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
  'biweekly' =>
    weekdays == 0
        ? 'Every other week'
        : 'Every other ${[for (final day in weekdayNames)
            if (weekdays & day.$1 != 0) day.$2].join(', ')}',
  _ => null,
};

String _weekdayName(int weekday) => weekdayNames[weekday - 1].$2;

DateTime _day(DateTime value) => DateTime(value.year, value.month, value.day);

bool _zonesReady = false;
final Map<String, tz.Location?> _zones = {};

tz.Location? _zone(String? id) {
  if (id == null || id.isEmpty) return null;
  if (!_zonesReady) {
    tz_data.initializeTimeZones();
    _zonesReady = true;
  }
  return _zones.putIfAbsent(id, () {
    try {
      return tz.getLocation(id);
    } catch (_) {
      return null;
    }
  });
}

/// The moment a clock set to [zoneId] shows [wallClock] (its year, month, day,
/// hour and minute), as a UTC instant. Null when the zone is not known.
DateTime? instantInZone(DateTime wallClock, String? zoneId) {
  final location = _zone(zoneId);
  if (location == null) return null;
  return tz.TZDateTime(
    location,
    wallClock.year,
    wallClock.month,
    wallClock.day,
    wallClock.hour,
    wallClock.minute,
  ).toUtc();
}

/// What a clock set to [zoneId] shows at [instant], as plain date and time
/// fields. Null when the zone is not known.
DateTime? wallClockInZone(DateTime instant, String? zoneId) {
  final location = _zone(zoneId);
  if (location == null) return null;
  final zoned = tz.TZDateTime.from(instant.toUtc(), location);
  return DateTime(zoned.year, zoned.month, zoned.day, zoned.hour, zoned.minute);
}

/// "Lisbon" for `Europe/Lisbon`, "Buenos Aires" for `America/Argentina/Buenos_Aires`.
String zoneCity(String zoneId) => zoneId.split('/').last.replaceAll('_', ' ');

/// How a scheduled [instant] reads on the clock of its own [zoneId], for example
/// "Tomorrow 9:00 AM in Lisbon". Null when that is the same time the phone
/// shows (so there is nothing to explain) or the zone is unknown.
String? zoneEquivalentLine(
  BuildContext context,
  DateTime instant,
  String? zoneId, {
  DateTime? now,
}) {
  final there = wallClockInZone(instant, zoneId);
  if (there == null || zoneId == null) return null;
  final here = instant.toLocal();
  final same =
      there.year == here.year &&
      there.month == here.month &&
      there.day == here.day &&
      there.hour == here.hour &&
      there.minute == here.minute;
  if (same) return null;
  final nowThere = wallClockInZone(now ?? DateTime.now(), zoneId);
  return '${friendlyWhen(context, there, now: nowThere)} in ${zoneCity(zoneId)}';
}

/// One scheduled [instant] described on both clocks when they differ: the
/// reminder's own zone first, then the phone. When they read the same (or the
/// zone is unknown) there is only the phone's reading and [other] is null.
({String primary, String? other}) describeScheduledInstant(
  BuildContext context,
  DateTime instant,
  String? zoneId, {
  DateTime? now,
}) {
  final zoneLine = zoneEquivalentLine(context, instant, zoneId, now: now);
  final phone = friendlyWhen(context, instant.toLocal(), now: now);
  if (zoneLine == null) return (primary: phone, other: null);
  return (primary: zoneLine, other: '$phone on your phone');
}
