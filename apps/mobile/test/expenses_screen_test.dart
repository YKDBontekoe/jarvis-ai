import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/expenses/expense_models.dart';
import 'package:jarvis_mobile/features/expenses/expenses_screen.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 2, 12);

Map<String, Object?> _expense(
  String id,
  double amount,
  String category,
  String date, {
  String? merchant,
  String? note,
  String? receipt,
}) => {
  'id': id,
  'amount': amount,
  'currency': 'EUR',
  'merchant': merchant,
  'category': category,
  'note': note,
  'spentOn': date,
  'receiptFileId': receipt,
  'source': receipt == null ? 'chat' : 'receipt',
  'createdAt': '2026-10-02T10:00:00Z',
  'updatedAt': '2026-10-02T10:00:00Z',
};

Map<String, Object?> _october() => {
  'year': 2026,
  'month': 10,
  'currency': 'EUR',
  'total': 64.9,
  'count': 3,
  'previousTotal': 100,
  'categories': [
    {'category': 'groceries', 'total': 52.9, 'count': 2},
    {'category': 'dining', 'total': 12, 'count': 1},
  ],
  'days': [
    {'date': '2026-10-01', 'total': 40.9},
    {'date': '2026-10-02', 'total': 24},
  ],
  'topMerchants': [
    {'merchant': 'Albert Heijn', 'total': 52.9, 'count': 2},
  ],
  'otherCurrencies': [
    {'currency': 'USD', 'total': 20, 'count': 1},
  ],
  'expenses': [
    _expense('e1', 12, 'dining', '2026-10-02', note: 'lunch'),
    _expense(
      'e2',
      12,
      'groceries',
      '2026-10-02',
      merchant: 'Albert Heijn',
      receipt: 'f1',
    ),
    _expense('e3', 40.9, 'groceries', '2026-10-01', merchant: 'Albert Heijn'),
  ],
};

Map<String, Object?> _emptyMonth(int month) => {
  'year': 2026,
  'month': month,
  'currency': 'EUR',
  'total': 0,
  'count': 0,
  'previousTotal': 0,
  'categories': <Object>[],
  'days': <Object>[],
  'topMerchants': <Object>[],
  'otherCurrencies': <Object>[],
  'expenses': <Object>[],
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: ExpensesScreen(http: http.client(), now: _now),
      ),
    );
    await tester.pumpAndSettle();
  }

  test('money and months are formatted for people', () {
    expect(formatMoney(1234.5, 'EUR'), '€1,234.50');
    expect(formatMoney(12, 'USD'), r'$12.00');
    expect(formatMoney(3, 'CHF'), '3.00 CHF');
    expect(monthLabel(2026, 10, now: _now), 'October');
    expect(monthLabel(2025, 12, now: _now), 'December 2025');
    expect(dayLabel(DateTime(2026, 10, 1), now: _now), 'Yesterday');
    expect(dayLabel(DateTime(2026, 9, 14), now: _now), '14 September');
    expect(expenseCategoryLabel('dining'), 'Eating out');
  });

  test('month data parses and computes the change', () {
    final month = ExpenseMonthData.fromJson(_october())!;
    expect(month.expenses, hasLength(3));
    expect(month.expenses.first.title, 'lunch');
    expect(month.expenses[1].receiptFileId, 'f1');
    expect(month.change, closeTo(-0.351, 0.001));
    expect(ExpenseData.fromJson({'id': 'x'}), isNull);
  });

  testWidgets('overview shows the total, change, categories, and days', (
    tester,
  ) async {
    http.on('GET', '/api/v1/expenses', _october());
    await show(tester);

    expect(http.requests.first.path, '/api/v1/expenses');
    expect(find.text('€64.90'), findsOneWidget);
    expect(find.text('35% less'), findsOneWidget);
    expect(find.textContaining('Plus \$20.00 in USD'), findsOneWidget);
    expect(find.text('Groceries'), findsWidgets);
    expect(find.text('82%'), findsOneWidget);
    expect(find.text('Today'), findsOneWidget);
    expect(find.text('Yesterday'), findsOneWidget);
    expect(find.text('Albert Heijn · €52.90'), findsOneWidget);
    expect(find.textContaining('Busiest: 1 October'), findsOneWidget);
  });

  testWidgets('tapping a category filters the list', (tester) async {
    http.on('GET', '/api/v1/expenses', _october());
    await show(tester);

    await tester.tap(find.byKey(const Key('expenses-category-dining')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('expense-e1')), findsOneWidget);
    expect(find.byKey(const Key('expense-e2')), findsNothing);
    await tester.tap(find.byKey(const Key('expenses-clear-filter')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('expense-e2')), findsOneWidget);
  });

  testWidgets('adding an expense posts it and reloads', (tester) async {
    http.on('GET', '/api/v1/expenses', _emptyMonth(10));
    http.on(
      'POST',
      '/api/v1/expenses',
      _expense('n1', 12.5, 'dining', '2026-10-02'),
    );
    await show(tester);

    expect(find.text('Nothing spent yet this month'), findsOneWidget);
    await tester.tap(find.byKey(const Key('expenses-empty-add')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('expense-amount')), '12,50');
    await tester.enterText(find.byKey(const Key('expense-merchant')), 'Cafe');
    await tester.tap(find.byKey(const Key('expense-category-dining')));
    await tester.pump();
    await tester.tap(find.byKey(const Key('expense-save')));
    await tester.pumpAndSettle();

    final sent = http.sent('POST', '/api/v1/expenses').single.body! as Map;
    expect(sent['amount'], 12.5);
    expect(sent['merchant'], 'Cafe');
    expect(sent['category'], 'dining');
    expect(sent['spentOn'], isA<String>());
    expect(http.sent('GET', '/api/v1/expenses'), hasLength(2));
  });

  testWidgets('a missing amount is explained instead of sent', (tester) async {
    http.on('GET', '/api/v1/expenses', _emptyMonth(10));
    await show(tester);

    await tester.tap(find.byKey(const Key('expenses-empty-add')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('expense-save')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('expense-error')), findsOneWidget);
    expect(http.sent('POST', '/api/v1/expenses'), isEmpty);
  });

  testWidgets('editing an expense can delete it after confirming', (
    tester,
  ) async {
    http.on('GET', '/api/v1/expenses', _october());
    http.on('DELETE', '/api/v1/expenses/e1', null, status: 204);
    await show(tester);

    await tester.tap(find.byKey(const Key('expense-e1')));
    await tester.pumpAndSettle();
    expect(find.text('Edit expense'), findsOneWidget);
    await tester.tap(find.byKey(const Key('expense-delete')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Delete'));
    await tester.pumpAndSettle();

    expect(http.sent('DELETE', '/api/v1/expenses/e1'), hasLength(1));
  });

  testWidgets('months can be browsed back but not into the future', (
    tester,
  ) async {
    http.on('GET', '/api/v1/expenses', _emptyMonth(10));
    await show(tester);

    final next = tester.widget<IconButton>(
      find.byKey(const Key('expenses-next-month')),
    );
    expect(next.onPressed, isNull);
    await tester.tap(find.byKey(const Key('expenses-previous-month')));
    await tester.pumpAndSettle();

    expect(find.text('September'), findsOneWidget);
    expect(find.text('No expenses in September'), findsOneWidget);
  });
}
