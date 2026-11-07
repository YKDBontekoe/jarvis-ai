import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/planner/day_planner_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'harness.dart';

/// The Today screen on the day itself and one day ahead (read-only agenda).
/// Run: flutter test test/screenshots/planner_lookahead.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
  });

  String key(DateTime d) =>
      '${d.year}-${d.month.toString().padLeft(2, '0')}-${d.day.toString().padLeft(2, '0')}';

  Map<String, Object?> day(DateTime date, List<Map<String, Object?>> entries,
          List<Map<String, Object?>> items) =>
      {
        'date': key(date),
        'timeZoneId': 'Europe/Amsterdam',
        'dayStart': '08:00:00',
        'dayEnd': '18:00:00',
        'calendarConnected': true,
        'calendarUnavailable': false,
        'entries': entries,
        'items': items,
        'freeSlots': const <Object>[],
        'freeMinutes': 210,
        'plannedAt': null,
      };

  for (final dark in [false, true]) {
    screenshotTest('planner look-ahead ${dark ? 'dark' : 'light'}', (tester) async {
      usePhone(tester);
      final today = DateTime.now();
      final tomorrow = today.add(const Duration(days: 1));
      DateTime at(DateTime d, int h) => DateTime(d.year, d.month, d.day, h).toUtc();
      Map<String, Object?> entry(DateTime d, int h, String title, String kind) => {
            'kind': kind,
            'id': null,
            'title': title,
            'startAt': at(d, h).toIso8601String(),
            'endAt': at(d, h + 1).toIso8601String(),
            'done': false,
          };
      final routes = <String, Object?>{
        'GET /api/v1/planner/today': day(today, [entry(today, 10, 'Team stand-up', 'event')], [
          {'id': 't1', 'title': 'Write the report', 'minutes': 60, 'done': false, 'sourceRef': null, 'startAt': null, 'endAt': null},
        ]),
        'GET /api/v1/planner/day': day(tomorrow, [
          entry(tomorrow, 9, 'Dentist', 'event'),
          entry(tomorrow, 14, 'Call Sam', 'reminder'),
        ], const []),
      };
      final dio = Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
        ..httpClientAdapter = ScreenshotHttp(routes);
      await tester.pumpWidget(RepaintBoundary(
        key: screenshotKey,
        child: MaterialApp(
          debugShowCheckedModeBanner: false,
          theme: buildJarvisTheme(brightness: dark ? Brightness.dark : Brightness.light),
          home: DayPlannerScreen(http: dio),
        ),
      ));
      for (var i = 0; i < 10; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await tester.tap(find.byKey(const Key('planner-next-day')));
      for (var i = 0; i < 10; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, 'planner-lookahead-${dark ? 'dark' : 'light'}');
    });
  }
}
