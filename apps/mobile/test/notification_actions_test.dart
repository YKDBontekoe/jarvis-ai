import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/push/notification_actions.dart';

import 'support/fixture_http.dart';

void main() {
  test('only Done and Snooze run without opening the app', () {
    expect(notificationQuickAction(reminderDoneAction), 'done');
    expect(notificationQuickAction(reminderSnoozeAction), 'snooze');
    expect(notificationQuickAction(approvalOpenAction), isNull);
    expect(
      notificationQuickAction(
        'com.apple.UNNotificationDefaultActionIdentifier',
      ),
      isNull,
    );
    expect(notificationQuickAction(null), isNull);
  });

  test('Snooze asks the server for ten minutes', () async {
    final http = FixtureHttp()
      ..on('POST', '/api/v1/notifications/n1/actions', {'action': 'snooze'});

    final message = await runNotificationQuickAction(
      http.client(),
      notificationId: 'n1',
      action: 'snooze',
    );

    expect(message, 'Reminder snoozed for 10 minutes.');
    expect(http.sent('POST', '/api/v1/notifications/n1/actions').single.body, {
      'action': 'snooze',
      'minutes': 10,
    });
  });

  test('Done sends only the action', () async {
    final http = FixtureHttp()
      ..on('POST', '/api/v1/notifications/n1/actions', {'action': 'done'});

    final message = await runNotificationQuickAction(
      http.client(),
      notificationId: 'n1',
      action: 'done',
    );

    expect(message, 'Reminder marked done.');
    expect(http.sent('POST', '/api/v1/notifications/n1/actions').single.body, {
      'action': 'done',
    });
  });

  test('a rejected action surfaces as an error', () async {
    final http = FixtureHttp()
      ..on('POST', '/api/v1/notifications/n1/actions', {}, status: 400);

    await expectLater(
      runNotificationQuickAction(
        http.client(),
        notificationId: 'n1',
        action: 'snooze',
      ),
      throwsA(isA<DioException>()),
    );
    expect(
      notificationQuickActionFailure('snooze'),
      'Jarvis could not snooze that reminder.',
    );
  });
}
