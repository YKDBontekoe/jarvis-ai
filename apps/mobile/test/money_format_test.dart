import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/expenses/expense_models.dart';
import 'package:jarvis_mobile/features/finance/finance_screen.dart';
import 'package:jarvis_mobile/features/finance/wealth_models.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _account(
  String id,
  String name,
  String currency,
  num balance,
) => {
  'id': id,
  'name': name,
  'type': 'checking',
  'currency': currency,
  'openingBalance': 0,
  'openingOn': '2026-10-01',
  'archived': false,
  'balance': balance,
  'monthIn': 0,
  'monthOut': 0,
  'transactionCount': 0,
};

void main() {
  tearDown(() => moneyLocale = 'en');

  group('formatMoney', () {
    test('English keeps the familiar shapes', () {
      expect(formatMoney(1234.5, 'EUR'), '€1,234.50');
      expect(formatMoney(12, 'USD'), r'$12.00');
      expect(formatMoney(3, 'CHF'), '3.00 CHF');
      expect(formatMoney(1234.5, 'EUR', cents: false), '€1,235');
      expect(formatMoney(-5, 'GBP'), '£5.00');
    });

    test('Dutch uses comma decimals, dot grouping and a spaced symbol', () {
      moneyLocale = 'nl';
      expect(formatMoney(1234.5, 'EUR'), '€ 1.234,50');
      expect(formatMoney(12, 'USD'), r'$ 12,00');
      expect(formatMoney(3, 'CHF'), '3,00 CHF');
      expect(formatSigned(-1234.5, 'EUR'), '-€ 1.234,50');
    });

    test('with the code, any currency reads unambiguously', () {
      expect(formatMoney(1234.5, 'EUR', code: true), 'EUR 1,234.50');
      expect(formatMoney(10, 'USD', code: true), 'USD 10.00');
      moneyLocale = 'nl';
      expect(formatMoney(1234.5, 'EUR', code: true), 'EUR 1.234,50');
      expect(formatSigned(-5, 'USD', code: true), '-USD 5,00');
    });

    test('mixed currencies are detected', () {
      expect(hasMixedCurrencies(['EUR', 'EUR']), isFalse);
      expect(hasMixedCurrencies(['EUR', 'USD']), isTrue);
      expect(hasMixedCurrencies(const []), isFalse);
    });
  });

  group('accounts list', () {
    late FixtureHttp http;
    setUp(() => http = FixtureHttp());

    Future<void> show(WidgetTester tester) async {
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          home: FinanceScreen(
            http: http.client(),
            now: DateTime(2026, 10, 15, 12),
            initialTab: FinanceTab.accounts,
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('one currency keeps symbols', (tester) async {
      http.on('GET', '/api/v1/finance/accounts', [
        _account('a1', 'Checking', 'EUR', 100),
        _account('a2', 'Savings', 'EUR', 2000),
      ]);
      await show(tester);
      expect(find.text('€100.00'), findsOneWidget);
      expect(find.text('€2,000.00'), findsOneWidget);
    });

    testWidgets('different currencies are written with their codes', (
      tester,
    ) async {
      http.on('GET', '/api/v1/finance/accounts', [
        _account('a1', 'Checking', 'EUR', 100),
        _account('a2', 'Dollars', 'USD', 2000),
      ]);
      await show(tester);
      expect(find.text('EUR 100.00'), findsOneWidget);
      expect(find.text('USD 2,000.00'), findsOneWidget);
    });
  });
}
