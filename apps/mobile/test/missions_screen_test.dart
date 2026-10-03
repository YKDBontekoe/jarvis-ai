import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/missions/mission_models.dart';
import 'package:jarvis_mobile/features/missions/missions_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _step(String key, String title, int stage, String status,
        {List<String> after = const [], String? result, String? error, String? taskStatus}) =>
    {
      'id': 'id-$key',
      'key': key,
      'ordinal': stage,
      'stage': stage,
      'role': 'researcher',
      'title': title,
      'instruction': 'Do $title',
      'dependsOn': after,
      'status': status,
      'taskStatus': taskStatus,
      'taskId': null,
      'result': result,
      'error': error,
    };

Map<String, Object?> _mission(String status, List<Map<String, Object?>> steps,
        {String? summary}) =>
    {
      'id': 'm1',
      'title': 'Lisbon trip',
      'goal': 'Plan a three day trip to Lisbon.',
      'status': status,
      'summary': summary,
      'failureReason': null,
      'steps': steps,
      'notes': [
        {'key': 'hotel_price', 'value': '€90/night', 'stepKey': 's2'},
      ],
    };

List<Map<String, Object?>> _planned() => [
  _step('s1', 'Find flights', 0, 'pending'),
  _step('s2', 'Find hotels', 0, 'pending'),
  _step('s3', 'Write itinerary', 1, 'pending', after: ['s1', 's2']),
];

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/missions', [
      {'id': 'm1', 'title': 'Lisbon trip', 'status': 'ready', 'createdAt': '2026-10-03T10:00:00Z'},
    ]);
    http.on('GET', '/api/v1/missions/m1', _mission('ready', _planned()));
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: MissionsScreen(http: http.client(), poll: false)),
    );
    await tester.pumpAndSettle();
  }

  test('detail data groups steps into parallel stages', () {
    final detail = MissionDetailData.fromJson(_mission('ready', _planned()))!;
    expect(detail.stages, hasLength(2));
    expect(detail.stages.first, hasLength(2));
    expect(detail.notes.single.$1, 'hotel_price');
    expect(stepStatusLabel('running', 'needs_approval'), 'Waiting for your approval');
    expect(stepStatusLabel('cancelled', null), 'Stopped');
    expect(MissionDetailData.fromJson({'id': 'x'}), isNull);
  });

  testWidgets('a planned mission shows its stages and starts', (tester) async {
    http.on('POST', '/api/v1/missions/m1/start', _mission('running', [
      _step('s1', 'Find flights', 0, 'running'),
      _step('s2', 'Find hotels', 0, 'running', taskStatus: 'needs_approval'),
      _step('s3', 'Write itinerary', 1, 'pending', after: ['s1', 's2']),
    ]));
    await show(tester);

    expect(find.text('Lisbon trip'), findsOneWidget);
    await tester.tap(find.byKey(const Key('mission-m1')));
    await tester.pumpAndSettle();
    expect(find.text('Stage 1 · in parallel'), findsOneWidget);
    expect(find.text('Stage 2'), findsOneWidget);
    expect(find.text('After s1, s2'), findsOneWidget);
    expect(find.text('hotel_price: '), findsNothing);

    await tester.tap(find.byKey(const Key('mission-start')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/missions/m1/start'), hasLength(1));
    expect(find.text('Waiting for your approval'), findsOneWidget);
    expect(find.byKey(const Key('mission-pause')), findsOneWidget);
    expect(find.byKey(const Key('mission-start')), findsNothing);
  });

  testWidgets('a waiting step can be edited', (tester) async {
    http.on('PUT', '/api/v1/missions/steps/id-s1', _mission('ready', _planned()));
    await show(tester);
    await tester.tap(find.byKey(const Key('mission-m1')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('step-edit-s1')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('step-instruction')), 'Flights under €250');
    await tester.tap(find.byKey(const Key('step-save')));
    await tester.pumpAndSettle();

    final put = http.sent('PUT', '/api/v1/missions/steps/id-s1').single.body as Map;
    expect(put['instruction'], 'Flights under €250');
  });

  testWidgets('a failed step can be retried or skipped and results can be read', (tester) async {
    http.on('GET', '/api/v1/missions/m1', _mission('failed', [
      _step('s1', 'Find flights', 0, 'completed', result: 'TAP, €220.'),
      _step('s2', 'Find hotels', 0, 'failed', error: 'No hotels found'),
      _step('s3', 'Write itinerary', 1, 'cancelled', after: ['s1', 's2'], error: 'An earlier step did not finish.'),
    ]));
    http.on('POST', '/api/v1/missions/steps/id-s2/retry', _mission('running', _planned()));
    await show(tester);
    await tester.tap(find.byKey(const Key('mission-m1')));
    await tester.pumpAndSettle();

    expect(find.text('No hotels found'), findsOneWidget);
    await tester.tap(find.byKey(const Key('step-result-s1')));
    await tester.pumpAndSettle();
    expect(find.text('TAP, €220.'), findsOneWidget);
    expect(find.byKey(const Key('step-skip-s2')), findsOneWidget);
    await tester.tap(find.byKey(const Key('step-retry-s2')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/missions/steps/id-s2/retry'), hasLength(1));
  });

  testWidgets('creating a mission posts the goal and opens the plan', (tester) async {
    http.on('POST', '/api/v1/missions', _mission('ready', _planned()), status: 201);
    await show(tester);

    await tester.tap(find.byKey(const Key('mission-new')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('mission-goal')),
      'Plan a three day trip to Lisbon.',
    );
    await tester.tap(find.byKey(const Key('mission-plan')));
    await tester.pumpAndSettle();

    final post = http.sent('POST', '/api/v1/missions').single.body as Map;
    expect(post['goal'], 'Plan a three day trip to Lisbon.');
    expect(find.text('Stage 1 · in parallel'), findsOneWidget);
  });

  testWidgets('the final result is shown once the mission is done', (tester) async {
    http.on('GET', '/api/v1/missions/m1', _mission('completed', [
      _step('s1', 'Write itinerary', 0, 'completed', result: 'Day 1: Belém.'),
    ], summary: 'Day 1: Belém.'));
    await show(tester);
    await tester.tap(find.byKey(const Key('mission-m1')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('mission-summary')), findsOneWidget);
    expect(find.text('Done'), findsWidgets);
    expect(find.byKey(const Key('mission-pause')), findsNothing);
  });
}
