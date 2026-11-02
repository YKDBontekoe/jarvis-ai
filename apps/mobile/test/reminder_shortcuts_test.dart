import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/reminders/reminder_shortcuts.dart';
import 'package:jarvis_mobile/features/reminders/reminders_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

DateTime? resolve(ReminderShortcut shortcut, DateTime now) =>
    resolveReminderShortcut(shortcut, now);

void main() {
  group('shortcut resolution', () {
    test('in an hour rounds up to the next five minutes', () {
      expect(
        resolve(ReminderShortcut.inAnHour, DateTime(2026, 11, 2, 14, 12)),
        DateTime(2026, 11, 2, 15, 15),
      );
      expect(
        resolve(ReminderShortcut.inAnHour, DateTime(2026, 11, 2, 14, 15)),
        DateTime(2026, 11, 2, 15, 15),
      );
    });

    test('in an hour crosses midnight into the next day', () {
      expect(
        resolve(ReminderShortcut.inAnHour, DateTime(2026, 11, 2, 23, 30)),
        DateTime(2026, 11, 3, 0, 30),
      );
    });

    test('this evening is 18:00, then 21:00, then not offered', () {
      expect(
        resolve(ReminderShortcut.thisEvening, DateTime(2026, 11, 2, 9)),
        DateTime(2026, 11, 2, 18),
      );
      expect(
        resolve(ReminderShortcut.thisEvening, DateTime(2026, 11, 2, 18)),
        DateTime(2026, 11, 2, 21),
      );
      expect(
        resolve(ReminderShortcut.thisEvening, DateTime(2026, 11, 2, 22)),
        isNull,
      );
    });

    test('tomorrow morning is 09:00 tomorrow, month end included', () {
      expect(
        resolve(ReminderShortcut.tomorrowMorning, DateTime(2026, 11, 30, 23)),
        DateTime(2026, 12, 1, 9),
      );
    });

    test(
      'shortcuts are read on the reminder zone clock, not the phone clock',
      () {
        // 20:30 UTC is already 05:30 the next day in Tokyo.
        final now = DateTime.utc(2026, 11, 2, 20, 30);
        final tokyo = nowOnClock('Asia/Tokyo', now: now);
        expect(tokyo, DateTime(2026, 11, 3, 5, 30));
        expect(
          resolve(ReminderShortcut.tomorrowMorning, tokyo),
          DateTime(2026, 11, 4, 9),
        );
        // And that wall clock converts back to the right UTC instant.
        expect(
          instantInZone(DateTime(2026, 11, 4, 9), 'Asia/Tokyo'),
          DateTime.utc(2026, 11, 4),
        );
      },
    );
  });

  group('reminders screen', () {
    late FixtureHttp http;

    setUp(() {
      http = FixtureHttp();
      deviceTimeZoneLookup = () async => 'Asia/Tokyo';
      http.on('GET', '/api/v1/reminders', <Object>[]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      http.on('GET', '/api/v1/briefings/daily', {
        'enabled': false,
        'localTime': '08:00:00',
        'timeZoneId': 'UTC',
      });
      http.on('PUT', '/api/v1/briefings/daily', <String, Object>{});
      http.on('POST', '/api/v1/reminders', {'id': 'r1', 'status': 'pending'});
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

    testWidgets('a shortcut fills the time, shows the exact moment and saves '
        'it', (tester) async {
      await show(tester);
      await tester.tap(find.text('New'));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('shortcut-tomorrowMorning')), findsOneWidget);
      expect(find.byKey(const Key('shortcut-pick')), findsOneWidget);
      await tester.tap(find.byKey(const Key('shortcut-inAnHour')));
      await tester.pumpAndSettle();

      final when = tester.widget<Text>(find.byKey(const Key('reminder-when')));
      expect(when.data, startsWith('Reminds you '));
      await tester.enterText(find.byType(TextFormField), 'Stretch');
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();

      final body =
          http.sent('POST', '/api/v1/reminders').single.body
              as Map<String, dynamic>;
      final due = DateTime.parse(body['dueAt'] as String);
      final fromNow = due.difference(DateTime.now().toUtc());
      expect(fromNow.inMinutes, inInclusiveRange(59, 66));
      expect(body['timeZoneId'], 'Asia/Tokyo');
    });

    testWidgets('Pick a time leaves the shortcuts', (tester) async {
      await show(tester);
      await tester.tap(find.text('New'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('shortcut-inAnHour')));
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<ChoiceChip>(find.byKey(const Key('shortcut-inAnHour')))
            .selected,
        isTrue,
      );

      await tester.tap(find.byKey(const Key('shortcut-pick')));
      await tester.pumpAndSettle();
      // The time picker is open; closing it keeps the previous time.
      await tester.tap(find.text('Cancel').last);
      await tester.pumpAndSettle();
      expect(
        tester
            .widget<ChoiceChip>(find.byKey(const Key('shortcut-inAnHour')))
            .selected,
        isFalse,
      );
    });

    testWidgets('the snooze sheet shows when each choice fires and sends it', (
      tester,
    ) async {
      final due = DateTime.now().add(const Duration(days: 1)).toUtc();
      http.on('GET', '/api/v1/reminders', [
        {
          'id': 'r1',
          'title': 'Call the office',
          'dueAt': due.toIso8601String(),
          'status': 'pending',
          'timeZoneId': 'Asia/Tokyo',
          'createdAt': '2026-10-01T12:00:00Z',
        },
      ]);
      http.on('POST', '/api/v1/reminders/r1/snooze', {
        'id': 'r1',
        'status': 'pending',
        'dueAt': DateTime.now()
            .add(const Duration(days: 1))
            .toUtc()
            .toIso8601String(),
      });
      await show(tester);

      await tester.tap(find.byTooltip('Reminder actions'));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Snooze…'));
      await tester.pumpAndSettle();

      for (final key in [
        'snooze-10',
        'snooze-60',
        'snooze-tomorrow',
        'snooze-pick',
      ]) {
        expect(find.byKey(Key(key)), findsOneWidget, reason: key);
      }
      // The reminder runs on Tokyo's clock, which is not this phone's.
      expect(find.textContaining('in Tokyo'), findsWidgets);

      await tester.tap(find.byKey(const Key('snooze-tomorrow')));
      await tester.pumpAndSettle();
      final body =
          http.sent('POST', '/api/v1/reminders/r1/snooze').single.body as Map;
      final until = DateTime.parse(body['until'] as String);
      // Tomorrow 09:00 in Tokyo is 00:00 UTC.
      expect(until.isUtc, isTrue);
      expect(until.hour, 0);
      expect(until.minute, 0);
    });
  });
}
