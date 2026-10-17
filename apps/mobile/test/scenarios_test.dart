import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/scenarios/scenarios_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _result({String per = '3.50'}) => {
  'kind': 'savings',
  'baseline': {'summary': '12.99 EUR a month for 24 months.'},
  'alternative': {'summary': '9.49 EUR a month for 24 months.'},
  'deltas': [
    {
      'label': 'Per month',
      'value': 3.5,
      'unit': 'EUR',
      'explanation': '12.99 − 9.49 = $per a month',
    },
  ],
  'notes': ['Nothing here changes a budget.'],
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: ScenariosScreen(http: http.client(), now: DateTime(2026, 10, 17)),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'shows the consequence at once and recalculates when a number changes',
    (tester) async {
      http.on('POST', '/api/v1/scenarios/evaluate', _result());
      await show(tester);

      expect(find.byKey(const Key('scenario-result')), findsOneWidget);
      expect(find.textContaining('12.99 − 9.49 = 3.50'), findsOneWidget);
      final first =
          http.sent('POST', '/api/v1/scenarios/evaluate').first.body as Map;
      expect((first['savingsAlternative'] as Map)['monthlyCost'], 9.49);

      http.on('POST', '/api/v1/scenarios/evaluate', _result(per: '5.00'));
      await tester.enterText(
        find.byKey(const Key('scenario-nextMonthly')),
        '7.99',
      );
      await tester.pumpAndSettle(const Duration(milliseconds: 400));

      final last =
          http.sent('POST', '/api/v1/scenarios/evaluate').last.body as Map;
      expect((last['savingsAlternative'] as Map)['monthlyCost'], 7.99);
      expect(find.textContaining('= 5.00 a month'), findsOneWidget);
    },
  );

  testWidgets('a refused scenario shows the reason and no result', (
    tester,
  ) async {
    http.on('POST', '/api/v1/scenarios/evaluate', {
      'detail': 'The two sides use different currencies.',
    }, status: 400);
    await show(tester);

    expect(find.byKey(const Key('scenario-problem')), findsOneWidget);
    expect(find.textContaining('different currencies'), findsOneWidget);
    expect(find.byKey(const Key('scenario-result')), findsNothing);
  });

  testWidgets(
    'an unreadable box asks for a number instead of calling the server',
    (tester) async {
      http.on('POST', '/api/v1/scenarios/evaluate', _result());
      await show(tester);
      final before = http.sent('POST', '/api/v1/scenarios/evaluate').length;

      await tester.enterText(find.byKey(const Key('scenario-months')), 'abc');
      await tester.pumpAndSettle(const Duration(milliseconds: 400));

      expect(find.text('Enter a number in every box.'), findsOneWidget);
      expect(http.sent('POST', '/api/v1/scenarios/evaluate').length, before);
    },
  );

  testWidgets('the time scenario sends its own typed assumptions', (
    tester,
  ) async {
    http.on('POST', '/api/v1/scenarios/evaluate', _result());
    await show(tester);

    await tester.tap(find.byKey(const Key('scenario-kind-time')));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/scenarios/evaluate').last.body as Map;
    expect(body['kind'], 'time');
    expect((body['timeBaseline'] as Map)['hoursPerWeek'], 5.0);
    expect((body['timeAlternative'] as Map)['taskHours'], 40.0);
  });

  testWidgets('saving can also log a decision and says nothing else changed', (
    tester,
  ) async {
    http
      ..on('POST', '/api/v1/scenarios/evaluate', _result())
      ..on('POST', '/api/v1/scenarios/save', {
        'libraryItemId': 'l1',
        'decisionId': 'd1',
      });
    await show(tester);

    await tester.tap(find.byKey(const Key('scenario-save')));
    await tester.pumpAndSettle();
    var body = http.sent('POST', '/api/v1/scenarios/save').single.body as Map;
    expect(body.containsKey('decisionProbability'), isFalse);
    expect(find.textContaining('Nothing else was changed'), findsOneWidget);

    await tester.tap(find.byKey(const Key('scenario-decision')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('scenario-save')));
    await tester.pumpAndSettle();
    body = http.sent('POST', '/api/v1/scenarios/save').last.body as Map;
    expect(body['decisionProbability'], 0.7);
    expect(body['decisionReviewOn'], '2027-01-15');
  });
}
