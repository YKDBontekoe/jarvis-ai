import 'package:flutter/widgets.dart';

import '../../json_maps.dart';
import '../../theme.dart';

const _monthNames = [
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

String monthName(int month) => _monthNames[(month - 1).clamp(0, 11)];

String shortMonthName(int month) => monthName(month).substring(0, 3);

/// Someone in the owner's life, as returned by /api/v1/people. Date maths
/// (days until the birthday, whether a check-in is due) comes from the server
/// so it matches the owner's time zone and the notifications.
class PersonData {
  const PersonData({
    required this.id,
    required this.name,
    this.relationship,
    this.birthdayMonth,
    this.birthdayDay,
    this.birthYear,
    this.notes,
    this.contactEveryDays,
    this.lastContactedAt,
    this.daysUntilBirthday,
    this.turningAge,
    this.daysSinceContact,
    this.contactDue = false,
    this.linkedToMemory = false,
  });

  final String id;
  final String name;
  final String? relationship;
  final int? birthdayMonth;
  final int? birthdayDay;
  final int? birthYear;
  final String? notes;
  final int? contactEveryDays;
  final DateTime? lastContactedAt;
  final int? daysUntilBirthday;
  final int? turningAge;
  final int? daysSinceContact;
  final bool contactDue;
  final bool linkedToMemory;

  bool get hasBirthday => birthdayMonth != null && birthdayDay != null;

  bool get birthdayToday => daysUntilBirthday == 0;

  static PersonData? fromJson(dynamic data) {
    final json = jsonObject(data);
    if (json == null) return null;
    final id = jsonString(json, 'id');
    final name = jsonString(json, 'name');
    if (id == null || name == null) return null;
    int? optionalInt(String key) =>
        json[key] is num ? asJsonInt(json[key]) : null;
    return PersonData(
      id: id,
      name: name,
      relationship: jsonString(json, 'relationship'),
      birthdayMonth: optionalInt('birthdayMonth'),
      birthdayDay: optionalInt('birthdayDay'),
      birthYear: optionalInt('birthYear'),
      notes: jsonString(json, 'notes'),
      contactEveryDays: optionalInt('contactEveryDays'),
      lastContactedAt: jsonDate(json['lastContactedAt'], local: true),
      daysUntilBirthday: optionalInt('daysUntilBirthday'),
      turningAge: optionalInt('turningAge'),
      daysSinceContact: optionalInt('daysSinceContact'),
      contactDue: asJsonBool(json['contactDue']),
      linkedToMemory: jsonString(json, 'graphEntityId') != null,
    );
  }

  static List<PersonData> listFromJson(dynamic data) =>
      jsonMaps(data).map(PersonData.fromJson).whereType<PersonData>().toList();
}

/// Someone Jarvis learned about in memory who is not on the list yet.
class PersonSuggestionData {
  const PersonSuggestionData({
    required this.graphEntityId,
    required this.name,
    this.relationship,
    this.birthdayMonth,
    this.birthdayDay,
    this.birthYear,
  });

  final String graphEntityId;
  final String name;
  final String? relationship;
  final int? birthdayMonth;
  final int? birthdayDay;
  final int? birthYear;

  static PersonSuggestionData? fromJson(Map<String, dynamic> json) {
    final id = jsonString(json, 'graphEntityId');
    final name = jsonString(json, 'name');
    if (id == null || name == null) return null;
    int? optionalInt(String key) =>
        json[key] is num ? asJsonInt(json[key]) : null;
    return PersonSuggestionData(
      graphEntityId: id,
      name: name,
      relationship: jsonString(json, 'relationship'),
      birthdayMonth: optionalInt('birthdayMonth'),
      birthdayDay: optionalInt('birthdayDay'),
      birthYear: optionalInt('birthYear'),
    );
  }

  static List<PersonSuggestionData> listFromJson(dynamic data) => jsonMaps(data)
      .map(PersonSuggestionData.fromJson)
      .whereType<PersonSuggestionData>()
      .toList();
}

/// "Anna de Vries" → "AV", "mama" → "M".
String personInitials(String name) {
  final words = name
      .trim()
      .split(RegExp(r'\s+'))
      .where((word) => word.isNotEmpty)
      .toList();
  if (words.isEmpty) return '?';
  final first = words.first.characters.first.toUpperCase();
  if (words.length == 1) return first;
  return first + words.last.characters.first.toUpperCase();
}

/// A stable tint per name so each person keeps the same avatar colour.
Color personTint(JarvisColors colors, String name) {
  final palette = [
    colors.accent,
    colors.violet,
    colors.sky,
    colors.rose,
    colors.success,
    colors.warning,
  ];
  final hash = name.toLowerCase().codeUnits.fold<int>(
    0,
    (sum, unit) => (sum * 31 + unit) & 0x7fffffff,
  );
  return palette[hash % palette.length];
}

/// "14 March" or "14 March 1990".
String birthdayDateLabel(int month, int day, [int? year]) => year == null
    ? '$day ${monthName(month)}'
    : '$day ${monthName(month)} $year';

/// "Today", "Tomorrow", "In 5 days", "In 3 weeks", "14 March".
String birthdayCountdown(PersonData person) {
  final days = person.daysUntilBirthday;
  if (days == null) return '';
  if (days == 0) return 'Today';
  if (days == 1) return 'Tomorrow';
  if (days < 14) return 'In $days days';
  if (days < 45) return 'In ${days ~/ 7} weeks';
  if (days < 335) return 'In ${(days / 30).round()} months';
  return birthdayDateLabel(person.birthdayMonth!, person.birthdayDay!);
}

/// "every day", "every week", "every 2 weeks", "every month", "every 10 days".
String cadenceLabel(int days) {
  if (days == 1) return 'every day';
  if (days == 7) return 'every week';
  if (days % 7 == 0 && days <= 56) return 'every ${days ~/ 7} weeks';
  if (days == 30 || days == 31) return 'every month';
  if (days % 30 == 0 && days <= 330) return 'every ${days ~/ 30} months';
  if (days == 365) return 'every year';
  return 'every $days days';
}

/// "today", "yesterday", "3 days ago", "5 weeks ago".
String daysAgoLabel(int days) {
  if (days <= 0) return 'today';
  if (days == 1) return 'yesterday';
  if (days < 14) return '$days days ago';
  if (days < 60) return '${days ~/ 7} weeks ago';
  if (days < 730) return '${days ~/ 30} months ago';
  return '${days ~/ 365} years ago';
}

/// "Talked 3 days ago", or "No contact logged" when nothing was recorded.
String lastTalkedLabel(PersonData person) {
  final days = person.daysSinceContact;
  if (days == null) return 'No contact logged';
  return days == 0 ? 'Talked today' : 'Talked ${daysAgoLabel(days)}';
}

/// Check-in choices offered in the editor, in days. Null means off.
const cadenceChoices = <int?>[null, 7, 14, 30, 90];

String cadenceChoiceLabel(int? days) => switch (days) {
  null => 'Off',
  7 => 'Weekly',
  14 => '2 weeks',
  30 => 'Monthly',
  90 => '3 months',
  _ => cadenceLabel(days),
};
