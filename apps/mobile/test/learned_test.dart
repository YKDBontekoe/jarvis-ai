import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/outcomes/learned_screen.dart';
import 'package:jarvis_mobile/features/outcomes/outcome_feedback_row.dart';

import 'support/fixture_http.dart';

Map<String, Object?> change(
  String id,
  String status, {
  String kind = 'style',
}) => {
  'change': {
    'id': id,
    'kind': kind,
    'title': 'Keep project update short',
    'reason': 'You said it was too long 3 times.',
    'before': 'A long paragraph.',
    'after': 'A short line.',
    'status': status,
  },
  'effect': status == 'applied'
      ? {'statement': 'Too early to say whether this helped.'}
      : null,
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/outcomes/learned', {
      'summary': 'Nothing changes until you say so.',
    });
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets(
    'a pending change shows why and a made-up example, and applies on request',
    (tester) async {
      http.on('GET', '/api/v1/outcomes/changes', [change('c1', 'pending')]);
      http.on(
        'POST',
        '/api/v1/outcomes/changes/c1/apply',
        change('c1', 'applied'),
      );
      await show(tester, LearnedScreen(http: http.client()));

      expect(find.textContaining('too long 3 times'), findsOneWidget);
      expect(find.text('Example (made up)'), findsOneWidget);
      expect(find.byKey(const Key('change-before-c1')), findsOneWidget);
      expect(http.sent('POST', '/api/v1/outcomes/changes/c1/apply'), isEmpty);

      await tester.tap(find.byKey(const Key('change-apply-c1')));
      await tester.pumpAndSettle();
      expect(
        http.sent('POST', '/api/v1/outcomes/changes/c1/apply'),
        hasLength(1),
      );
    },
  );

  testWidgets('declining sends reject and an applied change can be undone', (
    tester,
  ) async {
    http.on('GET', '/api/v1/outcomes/changes', [
      change('c1', 'pending'),
      change('c2', 'applied', kind: 'quiet'),
    ]);
    http.on(
      'POST',
      '/api/v1/outcomes/changes/c1/reject',
      change('c1', 'rejected'),
    );
    http.on('POST', '/api/v1/outcomes/changes/c2/undo', change('c2', 'undone'));
    await show(tester, LearnedScreen(http: http.client()));

    expect(find.textContaining('Too early to say'), findsOneWidget);
    await tester.tap(find.byKey(const Key('change-reject-c1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('change-undo-c2')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/outcomes/changes/c1/reject'),
      hasLength(1),
    );
    expect(http.sent('POST', '/api/v1/outcomes/changes/c2/undo'), hasLength(1));
  });

  testWidgets('with nothing to review the hero says so', (tester) async {
    http.on('GET', '/api/v1/outcomes/changes', <Object>[]);
    await show(tester, LearnedScreen(http: http.client()));

    expect(find.text('Nothing new this week'), findsOneWidget);
  });

  testWidgets('feedback sends exactly the chosen signal and aspect', (
    tester,
  ) async {
    http.on('POST', '/api/v1/outcomes/feedback', {
      'recorded': true,
      'message': 'Noted.',
    });
    await show(
      tester,
      OutcomeFeedbackRow(
        http: http.client(),
        artifactKind: 'project_update',
        artifactId: 'd1',
      ),
    );

    await tester.tap(find.byKey(const Key('feedback-long')));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/outcomes/feedback').single.body as Map;
    expect(body['artifactKind'], 'project_update');
    expect(body['artifactId'], 'd1');
    expect(body['signal'], 'revised');
    expect(body['aspect'], 'too_long');
    expect(find.text('Noted.'), findsOneWidget);
    expect(find.byKey(const Key('feedback-long')), findsNothing);
  });

  testWidgets(
    'a profile kept out of learning explains why nothing was stored',
    (tester) async {
      http.on('POST', '/api/v1/outcomes/feedback', {
        'recorded': false,
        'message':
            'This profile is kept out of learning, so nothing was stored.',
      });
      await show(
        tester,
        OutcomeFeedbackRow(
          http: http.client(),
          artifactKind: 'trip',
          profileId: 'p1',
        ),
      );

      await tester.tap(find.byKey(const Key('feedback-useful')));
      await tester.pumpAndSettle();

      expect(find.textContaining('kept out of learning'), findsOneWidget);
    },
  );
}
