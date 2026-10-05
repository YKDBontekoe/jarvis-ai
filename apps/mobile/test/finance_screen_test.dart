import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/finance/finance_models.dart';
import 'package:jarvis_mobile/features/finance/finance_screen.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 15, 12);

Map<String, Object?> _overview() => {
  'year': 2026,
  'month': 10,
  'currency': 'EUR',
  'total': 265,
  'budgets': [
    {
      'id': 'b1',
      'category': 'groceries',
      'limit': 300,
      'currency': 'EUR',
      'spent': 240,
      'remaining': 60,
      'percent': 80,
      'projected': 512.5,
      'state': 'warning',
    },
  ],
  'forecast': {
    'year': 2026,
    'month': 10,
    'currency': 'EUR',
    'spentSoFar': 265,
    'expectedRecurring': 10,
    'projectedOther': 256,
    'projectedTotal': 531,
    'previousMonthTotal': 400,
    'daysLeft': 16,
    'upcoming': <Object>[],
  },
  'subscriptions': [
    {
      'id': 's1',
      'merchant': 'Netflix',
      'amount': 15.99,
      'currency': 'EUR',
      'cadence': 'monthly',
      'lastChargedOn': '2026-10-03',
      'nextDueOn': '2026-11-03',
      'chargeCount': 5,
      'previousAmount': 13.99,
      'status': 'active',
      'remindDaysBefore': null,
      'perMonth': 15.99,
    },
  ],
  'subscriptionsPerMonth': 15.99,
  'alerts': [
    {'title': 'Netflix got more expensive', 'detail': '€13.99 to €15.99.'},
  ],
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/finance/overview', _overview());
  });

  Future<void> show(
    WidgetTester tester, {
    Future<String?> Function()? pick,
  }) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: FinanceScreen(
          http: http.client(),
          now: _now,
          initialTab: FinanceTab.budgets,
          pickStatement: pick,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  test('overview parses budgets, subscriptions, and forecast', () {
    final data = FinanceOverviewData.fromJson(_overview())!;
    expect(data.budgets.single.label, 'Groceries');
    expect(data.subscriptions.single.priceWentUp, isTrue);
    expect(data.forecast.projectedTotal, 531);
    expect(data.alerts, hasLength(1));
    expect(FinanceOverviewData.fromJson({'currency': 'EUR'}), isNull);
  });

  testWidgets('shows the forecast, budgets, alerts, and subscriptions', (
    tester,
  ) async {
    await show(tester);

    expect(find.text('€531.00'), findsOneWidget);
    expect(find.textContaining('€131.00 more than last month'), findsOneWidget);
    expect(find.text('80%'), findsOneWidget);
    expect(find.textContaining('on pace for €512.50'), findsOneWidget);
    expect(find.textContaining('Netflix got more expensive'), findsOneWidget);
    expect(find.text('Netflix'), findsOneWidget);
    expect(find.textContaining('Was €13.99'), findsOneWidget);
  });

  testWidgets('toggling the reminder patches the subscription', (tester) async {
    http.on('PATCH', '/api/v1/finance/subscriptions/s1', <String, Object>{});
    await show(tester);

    await tester.tap(find.byKey(const Key('subscription-remind-s1')));
    await tester.pumpAndSettle();

    final patch = http.sent('PATCH', '/api/v1/finance/subscriptions/s1').single;
    expect((patch.body as Map)['remindDaysBefore'], 3);
  });

  testWidgets('adding a budget sends the category and limit', (tester) async {
    http.on('PUT', '/api/v1/finance/budgets', <String, Object>{});
    await show(tester);

    await tester.tap(find.byKey(const Key('finance-add-budget')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('budget-limit')), '2500');
    await tester.tap(find.byKey(const Key('budget-save')));
    await tester.pumpAndSettle();

    final put = http.sent('PUT', '/api/v1/finance/budgets').single.body as Map;
    expect(put['category'], 'total');
    expect(put['limit'], 2500);
  });

  testWidgets('importing previews first and commits after confirming', (
    tester,
  ) async {
    http.on('POST', '/api/v1/finance/import', {
      'committed': false,
      'rows': 2,
      'imported': 0,
      'duplicates': 0,
      'incomeSkipped': 1,
      'unreadable': 0,
      'problem': null,
      'preview': <Object>[],
    });
    await show(tester, pick: () async => 'Date,Description,Amount\n');

    await tester.tap(find.byKey(const Key('finance-import')));
    await tester.pumpAndSettle();
    expect(find.text('Import 2 expenses?'), findsOneWidget);
    await tester.tap(find.text('Import'));
    await tester.pumpAndSettle();

    final posts = http.sent('POST', '/api/v1/finance/import').toList();
    expect(posts, hasLength(2));
    expect((posts[0].body as Map)['commit'], false);
    expect((posts[1].body as Map)['commit'], true);
  });
}
