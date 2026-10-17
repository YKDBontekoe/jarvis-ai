import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/goals/goal_screen.dart';
import 'package:jarvis_mobile/features/goals/goals_review_card.dart';
import 'package:jarvis_mobile/features/goals/goals_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _id = 'g1';
const _path = '/api/v1/goals/$_id';

Map<String, Object?> _view({
  String status = 'active',
  bool due = true,
  List<Map<String, Object?>> items = const [],
  Map<String, Object?>? review,
  List<Map<String, Object?>> experiments = const [],
  List<Map<String, Object?>> reflections = const [],
}) => {
  'goal': {
    'id': _id,
    'title': 'Run a 10k',
    'motivation': 'Feel fit.',
    'status': status,
    'weeklyMinutes': 90,
    'plan': items.isEmpty
        ? null
        : {'weekStart': '2026-10-12', 'omittedForTime': 1, 'items': items},
    'experiments': experiments,
  },
  'checkInDue': due,
  'habits': [
    {'id': 'h1', 'name': 'Run', 'thisWeekCount': 1, 'targetPerWeek': 3},
  ],
  'project': {'id': 'p1', 'name': 'Marathon', 'nextAction': 'Pick a plan'},
  'commitments': <Object>[],
  'progress': {
    'measured': ['Run: 1 of 3 this week'],
    'reflections': reflections,
    'interpretation': review,
  },
};

Map<String, Object?> _step(String id, String status, {int minutes = 30}) => {
  'id': id,
  'title': 'Step $id',
  'minutes': minutes,
  'status': status,
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 4200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      KeyedSubtree(key: UniqueKey(), child: _host(child)),
    );
    await tester.pumpAndSettle();
  }

  Future<void> detail(WidgetTester tester) =>
      show(tester, GoalScreen(http: http.client(), goalId: _id));

  testWidgets('the list shows state without pressure and starts empty kindly', (
    tester,
  ) async {
    http.on('GET', '/api/v1/goals', <Object>[]);
    await show(tester, GoalsScreen(http: http.client()));
    expect(find.byKey(const Key('goal-start')), findsOneWidget);

    http.on('GET', '/api/v1/goals', [
      {
        'id': _id,
        'title': 'Run a 10k',
        'status': 'active',
        'checkInDue': true,
        'feeling': 'slipping',
        'openPlanItems': 2,
        'review': {'reason': 'What would make it easier?'},
      },
    ]);
    await show(tester, GoalsScreen(http: http.client()));
    expect(find.text('Run a 10k'), findsOneWidget);
    expect(find.textContaining('Check-in due'), findsOneWidget);
    expect(find.text('What would make it easier?'), findsOneWidget);
  });

  testWidgets('creating a goal posts its links and opens it', (tester) async {
    http
      ..on('GET', '/api/v1/goals', <Object>[])
      ..on('GET', '/api/v1/projects', [
        {'id': 'p1', 'name': 'Marathon'},
      ])
      ..on('GET', '/api/v1/habits', {
        'habits': [
          {'id': 'h1', 'name': 'Run'},
        ],
      })
      ..on('POST', '/api/v1/goals', _view(), status: 201)
      ..on('GET', _path, _view());
    await show(tester, GoalsScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('goal-start')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('goal-title-field')),
      'Run a 10k',
    );
    await tester.tap(find.byKey(const Key('goal-habit-h1')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('goal-create')));
    await tester.pumpAndSettle();

    final post = http.sent('POST', '/api/v1/goals').single.body as Map;
    expect(post['title'], 'Run a 10k');
    expect(post['habitIds'], ['h1']);
    expect(post['weeklyMinutes'], 90);
    expect(find.byType(GoalScreen), findsOneWidget);
  });

  testWidgets('the detail labels measured, own words and the suggestion', (
    tester,
  ) async {
    http.on(
      'GET',
      _path,
      _view(
        reflections: [
          {'feeling': 'slipping', 'progress': 'Ran once', 'friction': 'Rain'},
        ],
        review: {'verdict': 'change', 'reason': 'What would make it easier?'},
      ),
    );
    await detail(tester);

    expect(find.text('Measured'), findsOneWidget);
    expect(find.text('In your words'), findsOneWidget);
    expect(find.textContaining('Suggestion'), findsOneWidget);
    expect(find.textContaining('CHANGE:'), findsOneWidget);
    expect(find.textContaining('In the way: Rain'), findsOneWidget);
    expect(find.byKey(const Key('goal-check-in-due')), findsOneWidget);
  });

  testWidgets('a check-in posts what the owner wrote and the feeling', (
    tester,
  ) async {
    http
      ..on('GET', _path, _view())
      ..on('POST', '$_path/check-ins', _view(due: false));
    await detail(tester);

    await tester.tap(find.byKey(const Key('goal-check-in')));
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('checkin-save')))
          .onPressed,
      isNull,
    );
    await tester.enterText(
      find.byKey(const Key('checkin-progress')),
      'Ran twice',
    );
    await tester.tap(find.byKey(const Key('checkin-feeling-stuck')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('checkin-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '$_path/check-ins').single.body as Map;
    expect(body['progress'], 'Ran twice');
    expect(body['feeling'], 'stuck');
    expect(find.byKey(const Key('goal-check-in-due')), findsNothing);
  });

  testWidgets('plan steps can be accepted, shrunk, declined and put on today', (
    tester,
  ) async {
    http
      ..on(
        'GET',
        _path,
        _view(
          items: [
            _step('s1', 'proposed'),
            _step('s2', 'accepted', minutes: 40),
          ],
        ),
      )
      ..on(
        'POST',
        '$_path/plan/items/s1',
        _view(
          items: [
            _step('s1', 'accepted'),
            _step('s2', 'accepted', minutes: 40),
          ],
        ),
      )
      ..on(
        'POST',
        '$_path/plan/items/s2',
        _view(
          items: [
            _step('s1', 'accepted'),
            _step('s2', 'accepted', minutes: 40),
          ],
        ),
      );
    await detail(tester);

    await tester.tap(find.byKey(const Key('goal-accept-s1')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '$_path/plan/items/s1').single.body, {
      'action': 'accept',
    });

    await tester.tap(find.byKey(const Key('goal-smaller-s2')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('goal-number-ok')));
    await tester.pumpAndSettle();
    final reduce = http.sent('POST', '$_path/plan/items/s2').first.body as Map;
    expect(reduce['action'], 'reduce');
    expect(reduce['minutes'], 20);

    await tester.tap(find.byKey(const Key('goal-schedule-s2')));
    await tester.pumpAndSettle();
    final schedule = http.sent('POST', '$_path/plan/items/s2').last;
    expect((schedule.body as Map)['action'], 'schedule');
    expect(schedule.query['timeZone'], 'Europe/Amsterdam');
    expect(find.textContaining('Added to today'), findsOneWidget);

    await tester.tap(find.byKey(const Key('goal-decline-s2')));
    await tester.pumpAndSettle();
    expect(
      (http.sent('POST', '$_path/plan/items/s2').last.body as Map)['action'],
      'decline',
    );
  });

  testWidgets('"Not now" on the suggestion posts a dismissal', (tester) async {
    http
      ..on(
        'GET',
        _path,
        _view(review: {'verdict': 'keep', 'reason': 'Going well.'}),
      )
      ..on('POST', '$_path/review/dismiss', _view());
    await detail(tester);

    await tester.tap(find.byKey(const Key('goal-review-dismiss')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/review/dismiss'), hasLength(1));
    expect(find.byKey(const Key('goal-review')), findsNothing);
  });

  testWidgets('a paused goal says nothing is planned and can be resumed', (
    tester,
  ) async {
    http
      ..on('GET', _path, _view(status: 'paused', due: true))
      ..on('PATCH', _path, _view());
    await detail(tester);

    expect(find.byKey(const Key('goal-status-notice')), findsOneWidget);
    expect(find.byKey(const Key('goal-check-in-due')), findsNothing);
    expect(find.byKey(const Key('goal-plan')), findsNothing);

    await tester.tap(find.byKey(const Key('goal-menu')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Resume'));
    await tester.pumpAndSettle();
    expect((http.sent('PATCH', _path).single.body as Map)['status'], 'active');
  });

  testWidgets('a refused change shows the reason', (tester) async {
    http
      ..on('GET', _path, _view())
      ..on('POST', '$_path/plan', {
        'message': 'Resume the goal before planning its week.',
      }, status: 409);
    await detail(tester);

    await tester.tap(find.byKey(const Key('goal-plan')));
    await tester.pumpAndSettle();

    expect(find.textContaining('Resume the goal'), findsOneWidget);
  });

  testWidgets('an experiment takes notes and is kept or stopped', (
    tester,
  ) async {
    final running = {
      'id': 'e1',
      'change': 'Run before breakfast',
      'expectedBenefit': 'More energy',
      'status': 'running',
      'reviewOn': '2026-10-31',
      'notes': <Object>[],
    };
    http
      ..on('GET', _path, _view(experiments: [running]))
      ..on('POST', '$_path/experiments/e1/notes', _view(experiments: [running]))
      ..on(
        'POST',
        '$_path/experiments/e1/finish',
        _view(
          experiments: [
            {...running, 'status': 'kept'},
          ],
        ),
      );
    await detail(tester);

    await tester.tap(find.byKey(const Key('experiment-note')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('experiment-note-field')),
      'Slept badly',
    );
    await tester.tap(find.byKey(const Key('experiment-note-ok')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '$_path/experiments/e1/notes').single.body, {
      'text': 'Slept badly',
    });

    await tester.tap(find.byKey(const Key('experiment-keep')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '$_path/experiments/e1/finish').single.body, {
      'keep': true,
    });
    expect(find.byKey(const Key('goal-experiment')), findsNothing);
  });

  testWidgets(
    'the weekly review card lists only goals that have something to say',
    (tester) async {
      http.on('GET', '/api/v1/goals', [
        {
          'id': 'g1',
          'title': 'Run a 10k',
          'status': 'active',
          'checkInDue': true,
        },
        {
          'id': 'g2',
          'title': 'Quiet goal',
          'status': 'active',
          'checkInDue': false,
        },
        {
          'id': 'g3',
          'title': 'Paused goal',
          'status': 'paused',
          'checkInDue': true,
        },
      ]);
      await show(
        tester,
        Scaffold(
          body: ListView(children: [GoalsReviewCard(http: http.client())]),
        ),
      );

      expect(find.byKey(const Key('goals-review-g1')), findsOneWidget);
      expect(find.text('Quiet goal'), findsNothing);
      expect(find.text('Paused goal'), findsNothing);
    },
  );

  testWidgets('the weekly review card is absent when there are no goals', (
    tester,
  ) async {
    http.on('GET', '/api/v1/goals', <Object>[]);
    await show(
      tester,
      Scaffold(
        body: ListView(children: [GoalsReviewCard(http: http.client())]),
      ),
    );

    expect(find.byKey(const Key('goals-review')), findsNothing);
  });
}
