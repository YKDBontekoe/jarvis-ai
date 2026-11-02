import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/reminders/reminders_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => 'Asia/Tokyo';
    http.on('GET', '/api/v1/notifications', <Object>[]);
    http.on('GET', '/api/v1/briefings/daily', {
      'enabled': false,
      'localTime': '08:00:00',
      'timeZoneId': 'UTC',
    });
  });

  group('zone helpers', () {
    test('a wall-clock time maps to one UTC instant and back', () {
      final instant = instantInZone(DateTime(2026, 11, 3, 9), 'Asia/Tokyo')!;
      expect(instant, DateTime.utc(2026, 11, 3, 0));
      expect(wallClockInZone(instant, 'Asia/Tokyo'), DateTime(2026, 11, 3, 9));
    });

    test('daylight saving is applied for the date asked about', () {
      // Amsterdam is UTC+1 in winter and UTC+2 in summer.
      expect(
        instantInZone(DateTime(2026, 1, 15, 9), 'Europe/Amsterdam'),
        DateTime.utc(2026, 1, 15, 8),
      );
      expect(
        instantInZone(DateTime(2026, 7, 15, 9), 'Europe/Amsterdam'),
        DateTime.utc(2026, 7, 15, 7),
      );
    });

    test('unknown zones are reported as unknown, not guessed', () {
      expect(instantInZone(DateTime(2026, 1, 1), 'Mars/Olympus'), isNull);
      expect(wallClockInZone(DateTime.utc(2026, 1, 1), null), isNull);
      expect(zoneCity('America/Argentina/Buenos_Aires'), 'Buenos Aires');
    });
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RemindersScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('a reminder on another clock says what time that is there', (
    tester,
  ) async {
    final due = DateTime.now().toUtc().add(const Duration(days: 3));
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Call the office',
        'dueAt': DateTime.utc(
          due.year,
          due.month,
          due.day,
          12,
        ).toIso8601String(),
        'status': 'pending',
        'timeZoneId': 'Asia/Tokyo',
        'createdAt': '2026-10-01T12:00:00Z',
      },
    ]);
    await show(tester);

    // Noon UTC is 21:00 in Tokyo: the line names the city and its clock.
    final line = find.byKey(const Key('reminder-zone-line'));
    expect(line, findsOneWidget);
    expect(tester.widget<Text>(line).data, contains('in Tokyo'));
    expect(tester.widget<Text>(line).data, contains('9:00 PM'));
  });

  testWidgets('a reminder on the phone clock gets no extra zone line', (
    tester,
  ) async {
    final local = DateTime.now().add(const Duration(days: 3));
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Water plants',
        'dueAt': local.toUtc().toIso8601String(),
        'status': 'pending',
        'timeZoneId': DateTime.now().timeZoneName == 'UTC' ? 'UTC' : null,
        'createdAt': '2026-10-01T12:00:00Z',
      },
    ]);
    await show(tester);

    expect(find.byKey(const Key('reminder-zone-line')), findsNothing);
  });

  testWidgets('creating shows the exact moment on both clocks and sends it', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('POST', '/api/v1/reminders', {'id': 'r1', 'status': 'pending'});
    http.on('PUT', '/api/v1/briefings/daily', <String, Object>{});
    // The editor's clock is Tokyo's; the phone shows the same instant in UTC.
    await show(tester);

    await tester.tap(find.text('New'));
    await tester.pumpAndSettle();
    expect(find.text('Time zone: Asia/Tokyo'), findsOneWidget);
    expect(
      tester.widget<Text>(find.byKey(const Key('reminder-when'))).data,
      allOf(startsWith('Reminds you '), contains('in Tokyo')),
    );
    expect(
      tester.widget<Text>(find.byKey(const Key('reminder-when-phone'))).data,
      contains('on your phone'),
    );

    await tester.enterText(find.byType(TextFormField), 'Call the office');
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/reminders').single.body
            as Map<String, dynamic>;
    // Tomorrow 09:00 in Tokyo is 00:00 UTC: the picked clock is the zone's.
    expect(body['timeZoneId'], 'Asia/Tokyo');
    expect(DateTime.parse(body['dueAt'] as String).hour, 0);
  });
}
