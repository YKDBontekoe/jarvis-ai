import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/missions/mission_result_screen.dart';
import 'package:jarvis_mobile/features/missions/missions_screen.dart';

import 'support/fixture_http.dart';

final templates = [
  {
    'id': 'decision',
    'title': 'Research a decision',
    'purpose': 'Weigh the options.',
    'deliverable': 'A short report.',
    'questions': [
      {
        'key': 'topic',
        'label': 'What are you deciding?',
        'hint': 'x',
        'required': true,
      },
      {
        'key': 'constraints',
        'label': 'Anything it must respect?',
        'hint': 'y',
        'required': false,
      },
    ],
  },
];

Map<String, Object?> mission() => {
  'id': 'm9',
  'title': 'Research a decision: laptop',
  'goal': 'g',
  'status': 'ready',
  'summary': null,
  'failureReason': null,
  'createdAt': '2026-10-17T09:00:00Z',
  'steps': <Object>[],
  'notes': <Object>[],
};

Map<String, Object?> result({String? library}) => {
  'missionId': 'm1',
  'title': 'Pick a laptop',
  'status': 'completed',
  'headline': 'Buy the light one.',
  'sections': [
    {
      'stepKey': 'a',
      'title': 'Step a',
      'role': 'researcher',
      'text': 'Found it.',
    },
  ],
  'evidence': [
    {
      'url': 'https://example.com/x',
      'host': 'example.com',
      'stepTitle': 'Step a',
    },
  ],
  'checks': [
    {'id': 'finished', 'label': 'Every step has finished', 'ok': true},
    {'id': 'sources', 'label': 'At least one source is named', 'ok': false},
  ],
  'limitations': ['“Step b” did not finish, so its part is missing.'],
  'nextDecisions': ['Retry or skip “Step b”.'],
  'effort': 'Took about 12 minutes.',
  'reviewNote': 'These checks only show that the work exists.',
  'libraryItemId': library,
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget home) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: home));
    await tester.pumpAndSettle();
  }

  testWidgets(
    'a starter asks its questions, composes the goal and plans with it',
    (tester) async {
      http.on('GET', '/api/v1/missions', <Object>[]);
      http.on('GET', '/api/v1/missions/templates', templates);
      http.on('POST', '/api/v1/missions/templates/decision/compose', {
        'title': 'Research a decision: laptop',
        'goal': 'The composed goal',
      });
      http.on('POST', '/api/v1/missions', mission(), status: 201);
      http.on('GET', '/api/v1/missions/m9', mission());
      await show(tester, MissionsScreen(http: http.client(), poll: false));

      await tester.tap(find.byKey(const Key('mission-new')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('starter-decision')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('starter-field-topic')),
        'which laptop',
      );
      await tester.tap(find.byKey(const Key('starter-compose')));
      await tester.pumpAndSettle();

      final compose =
          http
                  .sent('POST', '/api/v1/missions/templates/decision/compose')
                  .single
                  .body
              as Map;
      expect((compose['answers'] as Map)['topic'], 'which laptop');
      final plan = http.sent('POST', '/api/v1/missions').single.body as Map;
      expect(plan['goal'], 'The composed goal');
      expect(plan['title'], 'Research a decision: laptop');
    },
  );

  testWidgets('a missing required answer is explained and nothing is planned', (
    tester,
  ) async {
    http.on('GET', '/api/v1/missions', <Object>[]);
    http.on('GET', '/api/v1/missions/templates', templates);
    http.on('POST', '/api/v1/missions/templates/decision/compose', {
      'detail': 'What are you deciding? is needed to plan this.',
    }, status: 400);
    await show(tester, MissionsScreen(http: http.client(), poll: false));

    await tester.tap(find.byKey(const Key('mission-new')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('starter-decision')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('starter-compose')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('starter-problem')), findsOneWidget);
    expect(http.sent('POST', '/api/v1/missions'), isEmpty);
  });

  testWidgets(
    'the result separates what was checked, what is missing and what is left to decide',
    (tester) async {
      http.on('GET', '/api/v1/missions/m1/deliverable', result());
      await show(
        tester,
        MissionResultScreen(http: http.client(), missionId: 'm1'),
      );

      expect(find.text('PARTLY DONE'), findsOneWidget);
      expect(find.text('Buy the light one.'), findsOneWidget);
      expect(find.text('At least one source is named'), findsOneWidget);
      expect(find.textContaining('did not finish'), findsOneWidget);
      expect(find.text('Retry or skip “Step b”.'), findsOneWidget);
      expect(find.text('https://example.com/x'), findsOneWidget);
      expect(find.byKey(const Key('result-review-note')), findsOneWidget);
    },
  );

  testWidgets('saving goes to the Library once', (tester) async {
    http.on('GET', '/api/v1/missions/m1/deliverable', result());
    http.on(
      'POST',
      '/api/v1/missions/m1/deliverable/save',
      result(library: 'item-1'),
    );
    await show(
      tester,
      MissionResultScreen(http: http.client(), missionId: 'm1'),
    );

    await tester.tap(find.byKey(const Key('result-save')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/missions/m1/deliverable/save'),
      hasLength(1),
    );
    expect(find.text('Saved to your Library'), findsOneWidget);
    final button = tester.widget<FilledButton>(
      find
          .descendant(
            of: find.byKey(const Key('result-save')),
            matching: find.byType(FilledButton),
          )
          .first,
    );
    expect(button.onPressed, isNull);
  });
}
