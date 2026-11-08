import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/planner/day_planner_format.dart';
import 'package:jarvis_mobile/features/planner/day_planner_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

import 'support/fixture_http.dart';

const _base = '/api/v1/planner/today';

String _iso(DateTime time) => time.toUtc().toIso8601String();

Map<String, Object?> _day({
  List<Map<String, Object?>> entries = const [],
  List<Map<String, Object?>> items = const [],
  bool connected = true,
  bool unavailable = false,
  int freeMinutes = 150,
}) {
  final now = DateTime.now();
  final date =
      '${now.year}-${now.month.toString().padLeft(2, '0')}-${now.day.toString().padLeft(2, '0')}';
  return {
    'date': date,
    'timeZoneId': 'Europe/Amsterdam',
    'dayStart': '08:00:00',
    'dayEnd': '18:00:00',
    'calendarConnected': connected,
    'calendarUnavailable': unavailable,
    'entries': entries,
    'items': items,
    'freeSlots': const [],
    'freeMinutes': freeMinutes,
    'plannedAt': null,
  };
}

Map<String, Object?> _item(
  String id,
  String title, {
  int minutes = 30,
  bool done = false,
  DateTime? startAt,
  String? sourceRef,
}) => {
  'id': id,
  'title': title,
  'minutes': minutes,
  'done': done,
  'sourceRef': sourceRef,
  'startAt': startAt == null ? null : _iso(startAt),
  'endAt': startAt == null
      ? null
      : _iso(startAt.add(Duration(minutes: minutes))),
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  test('formats durations, clock values and plan results', () {
    expect(durationLabel(45), '45 min');
    expect(durationLabel(60), '1 h');
    expect(durationLabel(150), '2 h 30 min');
    expect(minutesOfDay('08:30:00'), 510);
    expect(minutesOfDay('nope'), isNull);
    expect(timeOfDayValue(1050), '17:30:00');
    expect(longDayLabel(DateTime(2026, 10, 2)), 'Friday 2 October');
    expect(
      planResultMessage(0, const []),
      'Nothing to plan. Add a to-do first.',
    );
    expect(planResultMessage(1, const []), '1 to-do planned.');
    expect(
      planResultMessage(2, const ['Shed']),
      '2 to-dos planned. Shed did not fit today.',
    );
  });

  testWidgets('shows the timeline with events, focus blocks and to-dos', (
    tester,
  ) async {
    final soon = DateTime.now().add(const Duration(hours: 1));
    http.on(
      'GET',
      _base,
      _day(
        entries: [
          {
            'kind': 'event',
            'id': null,
            'title': 'Design review',
            'startAt': _iso(soon),
            'endAt': _iso(soon.add(const Duration(minutes: 30))),
            'done': false,
          },
          {
            'kind': 'focus',
            'id': 'a',
            'title': 'Write report',
            'startAt': _iso(soon.add(const Duration(minutes: 35))),
            'endAt': _iso(soon.add(const Duration(minutes: 125))),
            'done': false,
          },
        ],
        items: [
          _item(
            'a',
            'Write report',
            minutes: 90,
            startAt: soon.add(const Duration(minutes: 35)),
          ),
          _item('b', 'Call plumber', minutes: 15),
        ],
      ),
    );

    await show(tester, DayPlannerScreen(http: http.client()));

    expect(find.text('Design review'), findsOneWidget);
    expect(find.text('Calendar'), findsWidgets);
    expect(find.text('Focus · 1 h 30 min'), findsOneWidget);
    expect(find.text('Call plumber'), findsOneWidget);
    expect(find.textContaining('Not planned yet'), findsOneWidget);
    expect(find.textContaining('1 event'), findsOneWidget);
    expect(find.textContaining('2 h 30 min free'), findsOneWidget);
  });

  testWidgets('adds a to-do with the chosen length and plans the day', (
    tester,
  ) async {
    http.on('GET', _base, _day(items: [_item('b', 'Call plumber')]));
    http.on('POST', '$_base/items', _item('c', 'Groceries', minutes: 45));
    http.on('POST', '$_base/plan', {
      'today': _day(),
      'scheduled': 1,
      'didNotFit': [_item('d', 'Rebuild shed', minutes: 480)],
    });

    await show(tester, DayPlannerScreen(http: http.client()));
    await tester.tap(find.byKey(const Key('planner-minutes-45')));
    await tester.enterText(
      find.byKey(const Key('planner-new-title')),
      'Groceries',
    );
    await tester.tap(find.byKey(const Key('planner-add')));
    await tester.pumpAndSettle();

    final added = http.sent('POST', '$_base/items').single.body as Map;
    expect(added['title'], 'Groceries');
    expect(added['minutes'], 45);

    await tester.tap(find.byKey(const Key('planner-plan')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_base/plan'), hasLength(1));
    expect(
      find.text('1 to-do planned. Rebuild shed did not fit today.'),
      findsOneWidget,
    );
  });

  testWidgets('a to-do that came from a promise says so', (tester) async {
    http.on(
      'GET',
      _base,
      _day(
        items: [
          _item('p', 'Send Rosa the deck', sourceRef: 'commitment:c1'),
          _item('q', 'Call plumber'),
        ],
      ),
    );

    await show(tester, DayPlannerScreen(http: http.client()));

    expect(find.textContaining('From a promise'), findsOneWidget);
  });

  testWidgets('checks off and removes to-dos', (tester) async {
    http.on('GET', _base, _day(items: [_item('b', 'Call plumber')]));
    http.on('PATCH', '$_base/items/b', _item('b', 'Call plumber', done: true));
    http.on('DELETE', '$_base/items/b', null, status: 204);

    await show(tester, DayPlannerScreen(http: http.client()));
    await tester.tap(find.text('Call plumber'));
    await tester.pumpAndSettle();
    expect(
      (http.sent('PATCH', '$_base/items/b').single.body as Map)['done'],
      isTrue,
    );

    await tester.tap(find.byTooltip('Remove Call plumber'));
    await tester.pumpAndSettle();
    expect(http.sent('DELETE', '$_base/items/b'), hasLength(1));

    // Undo adds it back with its length.
    http.on('POST', '$_base/items', _item('c', 'Call plumber'));
    await tester.tap(find.text('Undo'));
    await tester.pumpAndSettle();
    final undo = http.sent('POST', '$_base/items').single.body as Map;
    expect(undo['title'], 'Call plumber');
    expect(undo['minutes'], isNotNull);
  });

  testWidgets('asks chat to connect a calendar or copy focus blocks to it', (
    tester,
  ) async {
    final asked = <String>[];
    final later = DateTime.now().add(const Duration(hours: 2));
    http.on(
      'GET',
      _base,
      _day(
        connected: false,
        entries: [
          {
            'kind': 'focus',
            'id': 'a',
            'title': 'Deep work',
            'startAt': _iso(later),
            'endAt': _iso(later.add(const Duration(hours: 1))),
            'done': false,
          },
        ],
        items: [_item('a', 'Deep work', minutes: 60, startAt: later)],
      ),
    );

    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => TextButton(
            onPressed: () => Navigator.of(context).push(
              MaterialPageRoute<void>(
                builder: (_) => DayPlannerScreen(
                  http: http.client(),
                  onAskInChat: asked.add,
                ),
              ),
            ),
            child: const Text('open'),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('planner-connect')), findsOneWidget);
    expect(
      find.text(
        'Jarvis asks for your approval before it changes your calendar.',
      ),
      findsOneWidget,
    );
    await tester.tap(find.byKey(const Key('planner-to-calendar')));
    await tester.pumpAndSettle();

    expect(asked, [addPlanToCalendarPrompt]);
    expect(find.byType(DayPlannerScreen), findsNothing);
  });

  testWidgets('warns when the calendar could not be read', (tester) async {
    http.on('GET', _base, _day(unavailable: true));

    await show(tester, DayPlannerScreen(http: http.client()));

    expect(find.byKey(const Key('planner-calendar-down')), findsOneWidget);
    expect(find.textContaining('Nothing scheduled yet'), findsOneWidget);
  });

  testWidgets('the next day opens a read-only agenda and back to today returns', (
    tester,
  ) async {
    final tomorrow = DateTime.now().add(const Duration(days: 1));
    final key =
        '${tomorrow.year}-${tomorrow.month.toString().padLeft(2, '0')}-${tomorrow.day.toString().padLeft(2, '0')}';
    http.on('GET', _base, _day(items: [_item('t1', 'Write report')]));
    http.on('GET', '/api/v1/planner/day', {
      ..._day(
        items: [_item('later1', 'File taxes', minutes: 45)],
        entries: [
          {
            'kind': 'event',
            'id': null,
            'title': 'Dentist',
            'startAt': _iso(tomorrow),
            'endAt': _iso(tomorrow.add(const Duration(hours: 1))),
            'done': false,
          },
        ],
      ),
      'date': key,
    });
    await show(tester, DayPlannerScreen(http: http.client()));
    expect(find.text('To-dos'), findsOneWidget);

    await tester.tap(find.byKey(const Key('planner-next-day')));
    await tester.pumpAndSettle();

    final request = http.sent('GET', '/api/v1/planner/day').single;
    expect(request.query['date'], key);
    expect(find.text('Dentist'), findsOneWidget);
    expect(find.text('Kept for this day'), findsOneWidget);
    expect(find.text('File taxes'), findsOneWidget);
    expect(find.byKey(const Key('planner-readonly-note')), findsOneWidget);
    expect(find.text('To-dos'), findsNothing);
    expect(find.byKey(const Key('planner-today')), findsOneWidget);

    await tester.tap(find.byKey(const Key('planner-today')));
    await tester.pumpAndSettle();

    expect(find.text('To-dos'), findsOneWidget);
    expect(find.byKey(const Key('planner-readonly-note')), findsNothing);
  });

  testWidgets('you cannot go past a week back from the previous-day button', (
    tester,
  ) async {
    http.on('GET', _base, _day());
    http.on('GET', '/api/v1/planner/day', _day());
    await show(tester, DayPlannerScreen(http: http.client()));

    for (var i = 0; i < 9; i++) {
      await tester.tap(
        find.byKey(const Key('planner-prev-day')),
        warnIfMissed: false,
      );
      await tester.pumpAndSettle();
    }

    expect(http.sent('GET', '/api/v1/planner/day').length, 7);
    final button = tester.widget<CircleIconButton>(
      find.byKey(const Key('planner-prev-day')),
    );
    expect(button.onPressed, isNull);
  });
}
