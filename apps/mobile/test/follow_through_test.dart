import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/review/follow_through_card.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _path = '/api/v1/reviews/weekly/follow-through';

Map<String, Object?> _week({
  List<Map<String, Object?>> suggestions = const [],
  bool quiet = false,
}) => {
  'weekStart': '2026-10-12',
  'weekEnd': '2026-10-18',
  'fulfilled': quiet ? 0 : 2,
  'dropped': quiet ? 0 : 1,
  'overdue': quiet ? 0 : 2,
  'stale': quiet ? 0 : 1,
  'decisionsDueNextWeek': quiet ? 0 : 1,
  'fulfilledItems': quiet
      ? <Object>[]
      : [
          {
            'ref': 'commitment:11111111-1111-7111-8111-111111111111',
            'title': 'Sent quote',
            'detail': 'Rosa',
          },
        ],
  'staleItems': quiet
      ? <Object>[]
      : [
          {
            'ref': 'commitment:22222222-2222-7222-8222-222222222222',
            'title': 'Send tiles',
            'detail': '9 days overdue',
          },
        ],
  'nextWeekDecisions': quiet
      ? <Object>[]
      : [
          {
            'ref': 'decision:33333333-3333-7333-8333-333333333333',
            'title': 'Pick plumber',
            'detail': '2026-10-20',
          },
        ],
  'projectsNeedingStep': <Object>[],
  'suggestions': suggestions,
};

Map<String, Object?> _plan(String id) => {
  'kind': 'plan_commitment',
  'targetId': id,
  'title': 'Give “Send tiles” a new day',
  'reason': 'It has been overdue for 9 days.',
  'suggestedOn': '2026-10-19',
};

Map<String, Object?> _step(String id) => {
  'kind': 'project_next_step',
  'targetId': id,
  'title': 'Choose the next step for “Kitchen”',
  'reason': 'It has no next step yet.',
  'suggestedOn': null,
};

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: SingleChildScrollView(child: child)),
);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      _host(FollowThroughCard(http: http.client(), weekStart: '2026-10-12')),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows the counts and the items they come from', (tester) async {
    http.on('GET', _path, _week());
    await show(tester);

    expect(http.sent('GET', _path).single.query['weekStart'], '2026-10-12');
    expect(find.text('2 fulfilled'), findsOneWidget);
    expect(find.text('1 dropped'), findsOneWidget);
    expect(find.text('2 overdue'), findsOneWidget);
    expect(find.text('1 decisions next week'), findsOneWidget);
    expect(find.text('Sent quote'), findsOneWidget);
    expect(find.text('9 days overdue'), findsOneWidget);
    expect(find.text('Pick plumber'), findsOneWidget);
  });

  testWidgets('a quiet week says so and invents nothing', (tester) async {
    http.on('GET', _path, _week(quiet: true));
    await show(tester);

    expect(find.byKey(const Key('follow-through-quiet')), findsOneWidget);
    expect(find.textContaining('Worth doing next week'), findsNothing);
  });

  testWidgets('accepting a plan sends the chosen day and reloads', (
    tester,
  ) async {
    const id = '22222222-2222-7222-8222-222222222222';
    http
      ..on('GET', _path, _week(suggestions: [_plan(id)]))
      ..on('POST', '$_path/accept', {'ref': 'commitment:$id'});
    await show(tester);
    expect(find.text('Give “Send tiles” a new day'), findsOneWidget);

    await tester.tap(find.byKey(const Key('suggestion-accept-$id')));
    await tester.pumpAndSettle();
    http.on('GET', _path, _week());
    await tester.tap(find.byKey(const Key('follow-through-save')));
    await tester.pumpAndSettle();

    final sent = http.sent('POST', '$_path/accept').single.body as Map;
    expect(sent['kind'], 'plan_commitment');
    expect(sent['targetId'], id);
    expect(sent['on'], '2026-10-19');
    expect(find.text('Give “Send tiles” a new day'), findsNothing);
  });

  testWidgets('a project step needs words before it can be saved', (
    tester,
  ) async {
    const id = '44444444-4444-7444-8444-444444444444';
    http
      ..on('GET', _path, _week(suggestions: [_step(id)]))
      ..on('POST', '$_path/accept', {'ref': 'project:$id'});
    await show(tester);

    await tester.tap(find.byKey(const Key('suggestion-accept-$id')));
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('follow-through-save')))
          .onPressed,
      isNull,
    );
    await tester.enterText(
      find.byKey(const Key('follow-through-note')),
      '  Call the plumber ',
    );
    await tester.pump();
    await tester.tap(find.byKey(const Key('follow-through-save')));
    await tester.pumpAndSettle();

    final sent = http.sent('POST', '$_path/accept').single.body as Map;
    expect(sent['kind'], 'project_next_step');
    expect(sent['note'], 'Call the plumber');
    expect(sent.containsKey('on'), isFalse);
  });

  testWidgets('"Not now" puts a suggestion away without accepting it', (
    tester,
  ) async {
    const id = '22222222-2222-7222-8222-222222222222';
    http
      ..on('GET', _path, _week(suggestions: [_plan(id)]))
      ..on('POST', '$_path/dismiss', null, status: 204);
    await show(tester);

    http.on('GET', _path, _week());
    await tester.tap(find.byKey(const Key('suggestion-dismiss-$id')));
    await tester.pumpAndSettle();

    final sent = http.sent('POST', '$_path/dismiss').single.body as Map;
    expect(sent, {'kind': 'plan_commitment', 'targetId': id});
    expect(http.sent('POST', '$_path/accept'), isEmpty);
    expect(find.text('Give “Send tiles” a new day'), findsNothing);
  });

  testWidgets('a refused acceptance is shown and the suggestion stays', (
    tester,
  ) async {
    const id = '22222222-2222-7222-8222-222222222222';
    http
      ..on('GET', _path, _week(suggestions: [_plan(id)]))
      ..on('POST', '$_path/accept', {
        'detail': 'Pick today or a later day.',
      }, status: 400);
    await show(tester);

    await tester.tap(find.byKey(const Key('suggestion-accept-$id')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('follow-through-save')));
    await tester.pumpAndSettle();

    expect(find.text('Pick today or a later day.'), findsOneWidget);
    expect(find.text('Give “Send tiles” a new day'), findsOneWidget);
  });
}
