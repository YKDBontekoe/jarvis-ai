import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/attention/attention_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> item(
  String id,
  String kind,
  String title, {
  String urgency = 'today',
  bool locked = false,
  int overdue = 0,
}) => {
  'id': id,
  'kind': kind,
  'title': title,
  'detail': 'detail',
  'urgency': urgency,
  'destination': kind == 'approval' ? 'approvals' : 'decision:d1',
  'daysOverdue': overdue,
  'locked': locked,
};

Map<String, Object?> queue({
  List<Map<String, Object?>>? items,
  int snoozed = 0,
}) => {
  'items':
      items ??
      [
        item(
          'approval:a1',
          'approval',
          'Approve or decline: SendEmail',
          urgency: 'now',
          locked: true,
        ),
        item(
          'commitment:c1',
          'commitment',
          'Send the deck',
          urgency: 'late',
          overdue: 3,
        ),
      ],
  'snoozed': snoozed,
  'summary': 'Two need you.',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: AttentionScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'items show how urgent they are and locked ones cannot be put away',
    (tester) async {
      http.on('GET', '/api/v1/attention', queue());
      await show(tester);

      expect(find.text('WAITING ON YOU'), findsOneWidget);
      expect(find.text('3 DAYS LATE'), findsOneWidget);
      expect(
        find.byKey(const Key('attention-snooze-approval:a1')),
        findsNothing,
      );
      expect(
        find.byKey(const Key('attention-snooze-commitment:c1')),
        findsOneWidget,
      );
    },
  );

  testWidgets(
    'putting an item away until tomorrow sends the days and updates the list',
    (tester) async {
      http.on('GET', '/api/v1/attention', queue());
      http.on(
        'POST',
        '/api/v1/attention/commitment%3Ac1/snooze',
        queue(
          items: [
            item(
              'approval:a1',
              'approval',
              'Approve or decline: SendEmail',
              urgency: 'now',
              locked: true,
            ),
          ],
          snoozed: 1,
        ),
      );
      await show(tester);

      await tester.tap(find.byKey(const Key('attention-snooze-commitment:c1')));
      await tester.pumpAndSettle();

      final body =
          http
                  .sent('POST', '/api/v1/attention/commitment%3Ac1/snooze')
                  .single
                  .body
              as Map;
      expect(body['days'], 1);
      expect(find.text('Send the deck'), findsNothing);
      expect(find.byKey(const Key('attention-snoozed')), findsOneWidget);
    },
  );

  testWidgets('an empty queue says all clear', (tester) async {
    http.on('GET', '/api/v1/attention', queue(items: <Map<String, Object?>>[]));
    await show(tester);

    expect(find.text('All clear'), findsOneWidget);
  });

  testWidgets('a failure to load offers a retry', (tester) async {
    await show(tester);

    expect(find.textContaining('Could not load'), findsOneWidget);
  });
}
