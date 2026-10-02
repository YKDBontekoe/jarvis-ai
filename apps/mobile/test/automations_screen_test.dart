import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/automations/automations_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: AutomationsScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();
  }

  Map<String, Object?> rule({
    String status = 'enabled',
    Map<String, Object?>? lastRun,
    List<Map<String, Object?>>? actions,
  }) => {
    'id': 'a1',
    'name': 'Morning coffee',
    'status': status,
    'definition': {
      'schemaVersion': 1,
      'trigger': {
        'kind': 'schedule',
        'localTime': '07:30:00',
        'timeZoneId': 'Europe/Amsterdam',
        'weekdays': 31,
      },
      'actions':
          actions ??
          [
            {'kind': 'notification', 'title': 'Coffee', 'body': 'Brew it'},
          ],
    },
    'nextRunAt': DateTime.now()
        .add(const Duration(days: 1))
        .toUtc()
        .toIso8601String(),
    'lastRun': lastRun,
  };

  testWidgets('a rule reads as when, then, next run and last outcome', (
    tester,
  ) async {
    http.on('GET', '/api/v1/automations', [
      rule(
        lastRun: {
          'status': 'skipped',
          'failureSummary': 'Its conditions were not met.',
          'startedAt': DateTime.now().toUtc().toIso8601String(),
        },
      ),
    ]);
    await show(tester);

    expect(find.text('Every weekday at 07:30'), findsOneWidget);
    expect(find.text('Notify you “Coffee”'), findsOneWidget);
    expect(find.textContaining('Next run Tomorrow'), findsOneWidget);
    expect(find.textContaining('Skipped Today').first, findsOneWidget);
    expect(find.textContaining('its conditions were not met'), findsOneWidget);
  });

  testWidgets('switching a rule off disables it', (tester) async {
    http.on('GET', '/api/v1/automations', [rule()]);
    http.on('POST', '/api/v1/automations/a1/disable', rule(status: 'disabled'));
    await show(tester);

    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/automations/a1/disable'), hasLength(1));
  });

  testWidgets('a run waiting on approval links to the approval inbox', (
    tester,
  ) async {
    http.on('GET', '/api/v1/automations', [
      rule(
        actions: [
          {'kind': 'agent_run', 'title': 'Summarise inbox', 'prompt': 'x'},
        ],
        lastRun: {
          'status': 'waiting_approval',
          'startedAt': DateTime.now().toUtc().toIso8601String(),
        },
      ),
    ]);
    await show(tester);

    expect(find.textContaining('Asks for your approval'), findsOneWidget);
    expect(find.textContaining('Waiting for your approval'), findsOneWidget);
    expect(find.text('Review approval'), findsOneWidget);
  });

  testWidgets('a new automation is saved on the device clock and switched on', (
    tester,
  ) async {
    http.on('GET', '/api/v1/automations', <Object>[]);
    http.on('POST', '/api/v1/automations', {'id': 'a1', 'status': 'draft'});
    http.on('POST', '/api/v1/automations/a1/enable', rule());
    await show(tester);

    expect(find.text('No automations yet.'), findsOneWidget);
    await tester.tap(find.text('New'));
    await tester.pumpAndSettle();
    expect(find.text('Time zone: Europe/Amsterdam'), findsOneWidget);
    await tester.enterText(
      find.byKey(const Key('automation-name')),
      'Stand up',
    );
    await tester.enterText(find.byKey(const Key('automation-body')), 'Stretch');
    await tester.tap(find.byKey(const Key('automation-day-32')));
    await tester.tap(find.byKey(const Key('automation-day-64')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Create'));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/automations').single.body
            as Map<String, dynamic>;
    final trigger = (body['definition'] as Map)['trigger'] as Map;
    expect(trigger['timeZoneId'], 'Europe/Amsterdam');
    expect(trigger['weekdays'], 31);
    expect(trigger['localTime'], '09:00:00');
    final action =
        ((body['definition'] as Map)['actions'] as List).single as Map;
    expect(action['title'], 'Stand up');
    expect(http.sent('POST', '/api/v1/automations/a1/enable'), hasLength(1));
  });

  test('schedule helpers describe triggers and repeat rules plainly', () {
    expect(
      describeTrigger({'kind': 'schedule', 'localTime': '09:00:00'}),
      'Every day at 09:00',
    );
    expect(
      describeTrigger({
        'kind': 'schedule',
        'localTime': '09:00:00',
        'weekdays': 5,
        'timeZoneId': 'America/New_York',
      }, deviceZone: 'Europe/Amsterdam'),
      'Every Mon, Wed at 09:00 (America/New York time)',
    );
    expect(
      describeTrigger({'kind': 'reminder_due'}),
      'Whenever a reminder goes off',
    );
    expect(repeatLabel('weekly', 1 | 16), 'Every Mon, Fri');
    final now = DateTime(2030, 1, 1, 12);
    expect(
      relativeFromNow(now.add(const Duration(minutes: 25)), now: now),
      'in 25 min',
    );
    expect(
      relativeFromNow(now.subtract(const Duration(hours: 2)), now: now),
      '2 h ago',
    );
  });
}
