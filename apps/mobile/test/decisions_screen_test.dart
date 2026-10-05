import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/decisions/decision_format.dart';
import 'package:jarvis_mobile/features/decisions/decisions_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _decision(
  String id, {
  String status = 'open',
  String title = 'Take the new job',
  double probability = 0.7,
  String reviewOn = '2099-01-01',
  bool? outcome,
  String? note,
}) => {
  'id': id,
  'title': title,
  'context': null,
  'prediction': 'I will still enjoy it in a year',
  'probability': probability,
  'reviewOn': reviewOn,
  'status': status,
  'outcome': outcome,
  'outcomeNote': note,
  'resolvedAt': outcome == null ? null : '2026-10-01T10:00:00Z',
  'createdAt': '2026-09-01T10:00:00Z',
};

Map<String, Object?> _calibration({String? trend = 'improving'}) => {
  'resolved': 12,
  'brier': 0.16,
  'meanPredicted': 0.72,
  'hitRate': 0.58,
  'buckets': [
    {
      'label': 'Under 30%',
      'count': 0,
      'meanPredicted': null,
      'actualRate': null,
    },
    {'label': '30–50%', 'count': 0, 'meanPredicted': null, 'actualRate': null},
    {'label': '50–70%', 'count': 4, 'meanPredicted': 0.6, 'actualRate': 0.5},
    {'label': '70–90%', 'count': 8, 'meanPredicted': 0.8, 'actualRate': 0.625},
    {'label': '90%+', 'count': 0, 'meanPredicted': null, 'actualRate': null},
  ],
  'recentBrier': 0.12,
  'previousBrier': 0.2,
  'trend': trend,
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: DecisionsScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();
  }

  test('formats confidence, review dates and scores in plain words', () {
    final now = DateTime(2026, 10, 5, 15);
    expect(percentLabel(0.7), '70%');
    expect(percentLabel(0.005), '1%');
    expect(reviewLabel(DateTime(2026, 10, 5), now: now), 'Today');
    expect(reviewLabel(DateTime(2026, 10, 6), now: now), 'Tomorrow');
    expect(reviewLabel(DateTime(2026, 10, 9), now: now), 'In 4 days');
    expect(reviewLabel(DateTime(2026, 10, 26), now: now), 'In 3 weeks');
    expect(reviewLabel(DateTime(2027, 1, 5), now: now), 'In 3 months');
    expect(reviewLabel(DateTime(2026, 10, 4), now: now), '1 day overdue');
    expect(reviewLabel(DateTime(2026, 10, 1), now: now), '4 days overdue');
    expect(brierVerdict(0.05), 'Sharp');
    expect(brierVerdict(0.15), 'Well calibrated');
    expect(brierVerdict(0.22), 'Better than a coin flip');
    expect(brierVerdict(0.30), 'Worse than always saying 50%');
    expect(trendLabel('improving'), 'Improving');
    expect(trendLabel('worsening'), 'Slipping');
    expect(trendLabel('steady'), 'Steady');
    expect(brierChangeLabel(0.12, 0.3), 'Score 0.12 · better than before');
    expect(brierChangeLabel(0.4, 0.1), 'Score 0.40 · worse than before');
    expect(brierChangeLabel(0.2, 0.21), 'Score 0.20 · in line with before');
    expect(brierChangeLabel(0.2, null), 'Score 0.20');
    expect(brierChangeLabel(null, 0.2), isNull);
  });

  testWidgets('decisions are grouped by what they are waiting for', (
    tester,
  ) async {
    http.on('GET', '/api/v1/decisions', [
      _decision(
        'd1',
        status: 'due',
        title: 'Ship by Friday',
        reviewOn: '2020-01-01',
      ),
      _decision('d2', title: 'Move to Utrecht'),
      _decision('d3', status: 'resolved', title: 'Buy the bike', outcome: true),
      _decision(
        'd4',
        status: 'resolved',
        title: 'Skip the gym',
        outcome: false,
      ),
    ]);
    http.on('GET', '/api/v1/decisions/calibration', _calibration());
    await show(tester);

    expect(find.text('Waiting for your answer'), findsOneWidget);
    expect(find.text('Open'), findsOneWidget);
    expect(find.text('Resolved'), findsOneWidget);
    expect(find.text('Ship by Friday'), findsOneWidget);
    expect(find.textContaining('Answer now'), findsOneWidget);
    expect(
      find.descendant(
        of: find.byKey(const Key('decision-d3')),
        matching: find.text('Happened'),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byKey(const Key('decision-d4')),
        matching: find.text('Did not happen'),
      ),
      findsOneWidget,
    );
    expect(find.text('70% sure'), findsNWidgets(4));
  });

  testWidgets(
    'the calibration card reads the score, trend and confidence bands',
    (tester) async {
      http.on('GET', '/api/v1/decisions', [_decision('d1')]);
      http.on('GET', '/api/v1/decisions/calibration', _calibration());
      await show(tester);

      expect(find.byKey(const Key('calibration-card')), findsOneWidget);
      expect(find.text('0.16'), findsOneWidget);
      expect(find.text('Well calibrated'), findsOneWidget);
      expect(find.text('Improving'), findsOneWidget);
      expect(find.textContaining('12 answered'), findsOneWidget);
      expect(
        find.textContaining('72% sure on average and 58% came true'),
        findsOneWidget,
      );
      // Only bands with answers are drawn.
      expect(find.byKey(const Key('calibration-bucket-0')), findsOneWidget);
      expect(find.byKey(const Key('calibration-bucket-1')), findsOneWidget);
      expect(find.byKey(const Key('calibration-bucket-2')), findsNothing);
      expect(find.textContaining('8 calls · 80% vs 63%'), findsOneWidget);
    },
  );

  testWidgets('no calibration card until something has been answered', (
    tester,
  ) async {
    http.on('GET', '/api/v1/decisions', [_decision('d1')]);
    http.on('GET', '/api/v1/decisions/calibration', {
      'resolved': 0,
      'brier': null,
      'buckets': <Object?>[],
    });
    await show(tester);

    expect(find.byKey(const Key('calibration-card')), findsNothing);
  });

  testWidgets('the list still loads when the score cannot', (tester) async {
    http.on('GET', '/api/v1/decisions', [_decision('d1')]);
    http.on('GET', '/api/v1/decisions/calibration', null, status: 500);
    await show(tester);

    expect(find.text('Take the new job'), findsOneWidget);
    expect(find.byKey(const Key('calibration-card')), findsNothing);
  });

  testWidgets('answering a decision posts the outcome and the note', (
    tester,
  ) async {
    http.on('GET', '/api/v1/decisions', [
      _decision('d1', status: 'due', reviewOn: '2020-01-01'),
    ]);
    http.on('GET', '/api/v1/decisions/calibration', _calibration());
    http.on(
      'POST',
      '/api/v1/decisions/d1/resolve',
      _decision('d1', status: 'resolved', outcome: true),
    );
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-d1')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('decision-note')),
      '  went well  ',
    );
    await tester.tap(find.byKey(const Key('decision-happened')));
    await tester.pumpAndSettle();

    final sent = http.sent('POST', '/api/v1/decisions/d1/resolve').single;
    expect(sent.body, {'outcome': true, 'note': 'went well'});
    expect(find.text('Recorded: it happened.'), findsOneWidget);
    // The list is reloaded afterwards.
    expect(http.sent('GET', '/api/v1/decisions'), hasLength(2));
  });

  testWidgets('"It did not" records false', (tester) async {
    http.on('GET', '/api/v1/decisions', [_decision('d1', status: 'due')]);
    http.on('GET', '/api/v1/decisions/calibration', _calibration());
    http.on('POST', '/api/v1/decisions/d1/resolve', _decision('d1'));
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-d1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('decision-did-not-happen')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/decisions/d1/resolve').single.body, {
      'outcome': false,
      'note': '',
    });
  });

  testWidgets('a resolved decision can be re-answered but not edited', (
    tester,
  ) async {
    http.on('GET', '/api/v1/decisions', [
      _decision('d1', status: 'resolved', outcome: true, note: 'it did'),
    ]);
    http.on('GET', '/api/v1/decisions/calibration', _calibration());
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-d1')));
    await tester.pumpAndSettle();

    expect(find.text('Change your answer'), findsOneWidget);
    expect(find.byKey(const Key('decision-edit')), findsNothing);
    expect(find.text('it did'), findsOneWidget);
  });

  testWidgets('deleting asks first and then removes the decision', (
    tester,
  ) async {
    http.on('GET', '/api/v1/decisions', [_decision('d1')]);
    http.on('GET', '/api/v1/decisions/calibration', _calibration());
    http.on('DELETE', '/api/v1/decisions/d1', null, status: 204);
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-d1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('decision-delete')));
    await tester.pumpAndSettle();
    expect(http.sent('DELETE', '/api/v1/decisions/d1'), isEmpty);

    await tester.tap(find.text('Delete').last);
    await tester.pumpAndSettle();

    expect(http.sent('DELETE', '/api/v1/decisions/d1'), hasLength(1));
  });

  testWidgets('the empty state offers to log the first decision', (
    tester,
  ) async {
    http.on('GET', '/api/v1/decisions', <Object?>[]);
    http.on('GET', '/api/v1/decisions/calibration', {
      'resolved': 0,
      'buckets': <Object?>[],
    });
    await show(tester);

    expect(find.text('No decisions logged'), findsOneWidget);
    await tester.tap(find.byKey(const Key('decision-log-empty')));
    await tester.pumpAndSettle();

    expect(find.text('Log a decision'), findsWidgets);
    expect(find.byKey(const Key('decision-title')), findsOneWidget);
  });

  testWidgets(
    'logging a decision posts the chance as a fraction and the review date',
    (tester) async {
      http.on('GET', '/api/v1/decisions', <Object?>[]);
      http.on('GET', '/api/v1/decisions/calibration', {
        'resolved': 0,
        'buckets': <Object?>[],
      });
      http.on('POST', '/api/v1/decisions', _decision('new'), status: 201);
      await show(tester);

      await tester.tap(find.byKey(const Key('decision-log')));
      await tester.pumpAndSettle();
      await tester.enterText(
        find.byKey(const Key('decision-title')),
        'Ship by Friday',
      );
      await tester.enterText(
        find.byKey(const Key('decision-prediction')),
        'We ship on time',
      );
      expect(find.text('How sure are you? 70%'), findsOneWidget);
      await tester.tap(find.byKey(const Key('decision-in-30')));
      await tester.pump();
      await tester.tap(find.byKey(const Key('decision-save')));
      await tester.pumpAndSettle();

      final body = http.sent('POST', '/api/v1/decisions').single.body as Map;
      expect(body['title'], 'Ship by Friday');
      expect(body['prediction'], 'We ship on time');
      expect(body['probability'], 0.7);
      final expected = DateTime.now().add(const Duration(days: 30));
      expect(
        body['reviewOn'],
        '${expected.year.toString().padLeft(4, '0')}-'
        '${expected.month.toString().padLeft(2, '0')}-'
        '${expected.day.toString().padLeft(2, '0')}',
      );
      // Saving closes the editor and reloads the list.
      expect(find.byKey(const Key('decision-title')), findsNothing);
      expect(http.sent('GET', '/api/v1/decisions'), hasLength(2));
    },
  );

  testWidgets('an unfinished decision is not sent', (tester) async {
    http.on('GET', '/api/v1/decisions', <Object?>[]);
    http.on('GET', '/api/v1/decisions/calibration', {
      'resolved': 0,
      'buckets': <Object?>[],
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-log')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('decision-title')),
      'Only a title',
    );
    await tester.tap(find.byKey(const Key('decision-save')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/decisions'), isEmpty);
    expect(
      find.text('Give the decision a name and a prediction.'),
      findsOneWidget,
    );
  });

  testWidgets('a server rejection is shown in the editor', (tester) async {
    http.on('GET', '/api/v1/decisions', <Object?>[]);
    http.on('GET', '/api/v1/decisions/calibration', {
      'resolved': 0,
      'buckets': <Object?>[],
    });
    http.on('POST', '/api/v1/decisions', {
      'title': 'Validation failed',
      'errors': {
        'probability': ['Probability must be between 1% and 99%.'],
      },
    }, status: 400);
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-log')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('decision-title')), 'A');
    await tester.enterText(find.byKey(const Key('decision-prediction')), 'B');
    await tester.tap(find.byKey(const Key('decision-save')));
    await tester.pumpAndSettle();

    expect(find.textContaining('between 1% and 99%'), findsOneWidget);
    expect(find.byKey(const Key('decision-title')), findsOneWidget);
  });

  testWidgets('editing prefills the form and updates in place', (tester) async {
    http.on('GET', '/api/v1/decisions', [_decision('d1', probability: 0.4)]);
    http.on('GET', '/api/v1/decisions/calibration', {
      'resolved': 0,
      'buckets': <Object?>[],
    });
    http.on('PUT', '/api/v1/decisions/d1', _decision('d1'));
    await show(tester);

    await tester.tap(find.byKey(const Key('decision-d1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('decision-edit')));
    await tester.pumpAndSettle();

    expect(find.text('Edit decision'), findsOneWidget);
    expect(find.text('How sure are you? 40%'), findsOneWidget);
    await tester.tap(find.byKey(const Key('decision-save')));
    await tester.pumpAndSettle();

    final body = http.sent('PUT', '/api/v1/decisions/d1').single.body as Map;
    expect(body['title'], 'Take the new job');
    expect(body['probability'], 0.4);
  });
}
