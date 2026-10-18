import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/notifications/notification_delivery_sheet.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: NotificationDeliverySheet(
            http: http.client(),
            notificationId: 'n1',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'each step says what happened and does not claim a phone showed it',
    (tester) async {
      http.on('GET', '/api/v1/notifications/n1/delivery', {
        'steps': [
          {
            'state': 'created',
            'text': 'Created in your notifications.',
            'at': '2026-10-19T08:00:00Z',
          },
          {
            'state': 'provider_accepted',
            'text': 'Handed to the push service. Jarvis cannot see when a phone shows it.',
            'at': '2026-10-19T08:01:00Z',
          },
          {'state': 'unread', 'text': 'Not opened in the app yet.', 'at': null},
        ],
      });
      await show(tester);

      expect(find.text('Created in your notifications.'), findsOneWidget);
      expect(
        find.textContaining('cannot see when a phone shows it'),
        findsOneWidget,
      );
      expect(find.text('Not opened in the app yet.'), findsOneWidget);
    },
  );

  testWidgets('a held push and a failure are shown as what they are', (
    tester,
  ) async {
    http.on('GET', '/api/v1/notifications/n1/delivery', {
      'steps': [
        {
          'state': 'created',
          'text': 'Created in your notifications.',
          'at': null,
        },
        {
          'state': 'held',
          'text': 'Held back by your current mode.',
          'at': null,
        },
        {
          'state': 'failed',
          'text': 'Could not be sent after 10 tries.',
          'at': null,
        },
      ],
    });
    await show(tester);

    expect(
      find.textContaining('Held back by your current mode'),
      findsOneWidget,
    );
    expect(find.textContaining('after 10 tries'), findsOneWidget);
  });

  testWidgets('a notification that is gone says so instead of guessing', (
    tester,
  ) async {
    await show(tester);

    expect(find.byKey(const Key('delivery-error')), findsOneWidget);
  });
}
