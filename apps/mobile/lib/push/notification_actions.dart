import 'package:dio/dio.dart';

/// Button ids registered for iOS notification categories in `ios/Runner/AppDelegate.swift`.
/// Keep both lists in sync.
const reminderDoneAction = 'jarvis.reminder.done';
const reminderSnoozeAction = 'jarvis.reminder.snooze';
const approvalOpenAction = 'jarvis.approval.open';

/// Minutes the Snooze button on a reminder notification moves it by.
const notificationSnoozeMinutes = 10;

/// The server action for a notification button, or null when the tap should open Jarvis as usual.
///
/// Approvals only offer Open: deciding one always happens inside the app.
String? notificationQuickAction(String? actionIdentifier) =>
    switch (actionIdentifier) {
      reminderDoneAction => 'done',
      reminderSnoozeAction => 'snooze',
      _ => null,
    };

/// Runs a Done or Snooze button and returns a short confirmation for the person.
Future<String> runNotificationQuickAction(
  Dio http, {
  required String notificationId,
  required String action,
}) async {
  await http.post<Object?>(
    '/api/v1/notifications/$notificationId/actions',
    data: {
      'action': action,
      if (action == 'snooze') 'minutes': notificationSnoozeMinutes,
    },
  );
  return action == 'snooze'
      ? 'Reminder snoozed for $notificationSnoozeMinutes minutes.'
      : 'Reminder marked done.';
}

/// What to say when a notification button could not reach Jarvis.
String notificationQuickActionFailure(String action) => action == 'snooze'
    ? 'Jarvis could not snooze that reminder.'
    : 'Jarvis could not mark that reminder done.';
