import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/habits/habit_detail_screen.dart';
import 'package:jarvis_mobile/features/habits/habit_models.dart';
import 'package:jarvis_mobile/features/habits/habits_home_card.dart';
import 'package:jarvis_mobile/features/habits/habits_screen.dart';
import 'package:jarvis_mobile/features/notifications/notification_routing.dart';
import 'package:jarvis_mobile/schedule_format.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _habit(
  String id,
  String name, {
  String icon = '🏃',
  String cadence = 'daily',
  int target = 1,
  bool doneToday = false,
  int streak = 0,
  int best = 0,
  int week = 0,
  bool archived = false,
  List<String> dates = const [],
}) => {
  'id': id,
  'name': name,
  'icon': icon,
  'cadence': cadence,
  'targetPerWeek': target,
  'archived': archived,
  'archivedAt': archived ? '2026-09-30T10:00:00Z' : null,
  'stats': {
    'today': '2026-10-01',
    'currentStreak': streak,
    'bestStreak': best,
    'streakUnit': cadence == 'weekly' ? 'weeks' : 'days',
    'doneToday': doneToday,
    'thisWeekCount': week,
    'totalCheckIns': dates.length,
    'openToday': !doneToday,
    'recentDates': dates,
  },
  'createdAt': '2026-09-01T10:00:00Z',
  'updatedAt': '2026-09-01T10:00:00Z',
};

Map<String, Object?> _overview(List<Map<String, Object?>> habits) => {
  'today': '2026-10-01',
  'habits': habits,
  'settings': {
    'eveningCheckIn': true,
    'checkInTime': '20:30',
    'timeZoneId': 'Europe/Amsterdam',
  },
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  test('checking today in updates streaks before the server answers', () {
    final habit = HabitView.fromJson(
      _habit('1', 'Run', streak: 3, best: 3, dates: ['2026-09-30']),
    );
    final done = habit.withToday(true);

    expect(habit.today, DateTime(2026, 10, 1));
    expect(done.doneToday, isTrue);
    expect(done.currentStreak, 4);
    expect(done.bestStreak, 4);
    expect(done.doneOn(DateTime(2026, 10, 1)), isTrue);
    expect(done.withToday(false).currentStreak, 3);
    expect(habitWeekStart(DateTime(2026, 10, 1)), DateTime(2026, 9, 28));
    expect(habitDateKey(DateTime(2026, 1, 5)), '2026-01-05');
    expect(opensHabits('habit.checkin'), isTrue);
  });

  testWidgets('lists open and done habits with streaks and progress', (
    tester,
  ) async {
    http.on(
      'GET',
      '/api/v1/habits',
      _overview([
        _habit('1', 'Run', streak: 5, best: 9),
        _habit('2', 'Read', icon: '📚', doneToday: true, streak: 2),
        _habit('3', 'Gym', icon: '💪', cadence: 'weekly', target: 3, week: 1),
        _habit('4', 'Old', archived: true),
      ]),
    );
    await show(tester, HabitsScreen(http: http.client()));

    expect(find.text('To do today'), findsOneWidget);
    expect(find.text('Done'), findsOneWidget);
    expect(find.text('5-day streak'), findsOneWidget);
    expect(find.text('1 of 3 this week'), findsOneWidget);
    expect(find.text('1/2'), findsOneWidget);
    expect(find.text('Jarvis checks in at 20:30'), findsOneWidget);
    expect(find.text('Archived (1)'), findsOneWidget);
    expect(find.text('Old'), findsNothing);
  });

  testWidgets('tapping the check marks a habit done today', (tester) async {
    http.on('GET', '/api/v1/habits', _overview([_habit('1', 'Run')]));
    http.on(
      'POST',
      '/api/v1/habits/1/check-ins',
      _habit('1', 'Run', doneToday: true, streak: 1, dates: ['2026-10-01']),
    );
    await show(tester, HabitsScreen(http: http.client()));

    await tester.tap(find.byTooltip('Done today'));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/habits/1/check-ins').single.body;
    expect((body as Map)['done'], isTrue);
    expect(find.text('All done today 🎉'), findsOneWidget);
    expect(find.text('1-day streak'), findsOneWidget);
  });

  testWidgets('empty screen starts a habit from a template', (tester) async {
    http.on('GET', '/api/v1/habits', _overview([]));
    http.on('POST', '/api/v1/habits', _habit('9', 'Read', icon: '📚'));
    await show(tester, HabitsScreen(http: http.client()));

    expect(find.text('Build habits that stick'), findsOneWidget);
    await tester.tap(find.text('Read'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('habit-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/habits').single.body as Map;
    expect(body['name'], 'Read');
    expect(body['icon'], '📚');
    expect(body['cadence'], 'daily');
    expect(body['timeZoneId'], 'Europe/Amsterdam');
    expect(find.text('To do today'), findsOneWidget);
  });

  testWidgets('weekly habits send how many times a week', (tester) async {
    http.on('GET', '/api/v1/habits', _overview([]));
    http.on('POST', '/api/v1/habits', _habit('9', 'Gym', cadence: 'weekly'));
    await show(tester, HabitsScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('habit-new')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('habit-name')), 'Gym');
    await tester.tap(find.text('Times a week'));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('More'));
    await tester.pump();
    await tester.tap(find.byKey(const Key('habit-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/habits').single.body as Map;
    expect(body['cadence'], 'weekly');
    expect(body['targetPerWeek'], 4);
  });

  testWidgets('evening check-in can be turned off', (tester) async {
    http.on('GET', '/api/v1/habits', _overview([_habit('1', 'Run')]));
    http.on('PUT', '/api/v1/habits/settings', {
      'eveningCheckIn': false,
      'checkInTime': '20:30',
      'timeZoneId': 'Europe/Amsterdam',
    });
    await show(tester, HabitsScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('habit-settings')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('habit-checkin-switch')));
    await tester.pump();
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    final body = http.sent('PUT', '/api/v1/habits/settings').single.body as Map;
    expect(body['eveningCheckIn'], isFalse);
    expect(find.text('Evening check-in is off'), findsOneWidget);
  });

  testWidgets('detail shows streaks and fixes an earlier day', (tester) async {
    final habit = HabitView.fromJson(
      _habit(
        '1',
        'Run',
        streak: 2,
        best: 6,
        dates: ['2026-09-30', '2026-09-29'],
      ),
    );
    http.on(
      'POST',
      '/api/v1/habits/1/check-ins',
      _habit(
        '1',
        'Run',
        streak: 3,
        best: 6,
        dates: ['2026-09-30', '2026-09-29', '2026-09-28'],
      ),
    );
    await show(tester, HabitDetailScreen(http: http.client(), habit: habit));

    expect(find.text('day streak'), findsOneWidget);
    expect(find.text('6'), findsOneWidget);
    expect(find.text('Last 12 weeks'), findsOneWidget);
    await tester.tap(find.bySemanticsLabel(RegExp(r'^Mon 28')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/habits/1/check-ins').single.body;
    expect((body as Map)['date'], '2026-09-28');
    expect(body['done'], isTrue);
    expect(find.text('3'), findsWidgets);
  });

  testWidgets('home card shows habits and hides without any', (tester) async {
    http.on(
      'GET',
      '/api/v1/habits',
      _overview([_habit('1', 'Run', streak: 4)]),
    );
    var opened = false;
    await show(
      tester,
      Scaffold(
        body: HabitsHomeCard(
          http: http.client(),
          onOpen: () => opened = true,
          refreshRevision: 0,
        ),
      ),
    );

    expect(find.text('Habits today · 0 of 1'), findsOneWidget);
    expect(find.text('4-day streak'), findsOneWidget);
    await tester.tap(find.text('View all'));
    expect(opened, isTrue);

    http.on('GET', '/api/v1/habits', _overview([]));
    await show(
      tester,
      Scaffold(
        body: HabitsHomeCard(
          http: http.client(),
          onOpen: () {},
          refreshRevision: 1,
        ),
      ),
    );
    expect(find.textContaining('Habits today'), findsNothing);
  });
}
