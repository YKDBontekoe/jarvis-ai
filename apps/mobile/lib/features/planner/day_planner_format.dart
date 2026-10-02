/// Pure helpers for the Today screen, kept apart so they are easy to test.
library;

/// "09:05" in the device's local time.
String clockLabel(DateTime time) {
  final local = time.toLocal();
  return '${local.hour.toString().padLeft(2, '0')}:'
      '${local.minute.toString().padLeft(2, '0')}';
}

/// "45 min", "1 h", "2 h 30 min".
String durationLabel(int minutes) {
  if (minutes < 60) return '$minutes min';
  final hours = minutes ~/ 60;
  final rest = minutes % 60;
  return rest == 0 ? '$hours h' : '$hours h $rest min';
}

/// "Friday, October 2" for the screen header.
String longDayLabel(DateTime day) {
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
  return '${weekdays[day.weekday - 1]}, ${months[day.month - 1]} ${day.day}';
}

/// Parses "08:00:00" or "08:00" from the API into minutes since midnight.
int? minutesOfDay(Object? value) {
  if (value is! String) return null;
  final parts = value.split(':');
  if (parts.length < 2) return null;
  final hours = int.tryParse(parts[0]);
  final minutes = int.tryParse(parts[1]);
  if (hours == null || minutes == null) return null;
  if (hours < 0 || hours > 24 || minutes < 0 || minutes > 59) return null;
  return hours * 60 + minutes;
}

/// "08:00:00" for the API from minutes since midnight.
String timeOfDayValue(int minutes) {
  final hours = (minutes ~/ 60).toString().padLeft(2, '0');
  final rest = (minutes % 60).toString().padLeft(2, '0');
  return '$hours:$rest:00';
}

/// Snack bar text after "Plan my day".
String planResultMessage(int scheduled, List<String> didNotFit) {
  if (scheduled == 0 && didNotFit.isEmpty) {
    return 'Nothing to plan. Add a to-do first.';
  }
  final planned = scheduled == 1
      ? '1 to-do planned'
      : '$scheduled to-dos planned';
  if (didNotFit.isEmpty) return '$planned.';
  final names = didNotFit.length <= 2
      ? didNotFit.join(' and ')
      : '${didNotFit.length} to-dos';
  return '$planned. $names did not fit today.';
}

/// Prompt that asks Jarvis to copy the planned focus blocks to the calendar.
/// Jarvis asks for approval before it writes anything.
const addPlanToCalendarPrompt =
    'Add the focus blocks from my day plan to my calendar.';
