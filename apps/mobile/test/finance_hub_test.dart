import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/finance/finance_screen.dart';
import 'package:jarvis_mobile/features/finance/wealth_models.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 15, 12);

Map<String, Object?> _wealth() => {
  'currency': 'EUR',
  'netWorth': 11250.5,
  'cashTotal': 10150.5,
  'portfolioValue': 1100,
  'monthIncome': 3000,
  'monthSpending': 1200,
  'savingsRate': 60,
  'cashFlow': [
    for (var m = 5; m <= 10; m++)
      {'year': 2026, 'month': m, 'income': 2800, 'spending': 1500},
  ],
  'otherCurrencies': [
    {'currency': 'USD', 'value': 200},
  ],
};

List<Object?> _accounts() => [
  {
    'id': 'a1',
    'name': 'ING Checking',
    'type': 'checking',
    'currency': 'EUR',
    'institution': 'ING',
    'last4': '4300',
    'openingBalance': 1000,
    'openingOn': '2026-10-01',
    'archived': false,
    'balance': 2450.25,
    'monthIn': 3000,
    'monthOut': 1200,
    'transactionCount': 12,
  },
  {
    'id': 'a2',
    'name': 'Savings',
    'type': 'savings',
    'currency': 'EUR',
    'openingBalance': 7700.25,
    'openingOn': '2026-10-01',
    'archived': false,
    'balance': 7700.25,
    'monthIn': 0,
    'monthOut': 0,
    'transactionCount': 0,
  },
];

Map<String, Object?> _transactions() => {
  'items': [
    {
      'id': 't1',
      'amount': 3000,
      'currency': 'EUR',
      'merchant': 'ACME BV',
      'category': 'salary',
      'spentOn': '2026-10-14',
      'source': 'import',
      'kind': 'income',
      'accountId': 'a1',
    },
    {
      'id': 't2',
      'amount': 12.5,
      'currency': 'EUR',
      'merchant': 'Albert Heijn',
      'category': 'groceries',
      'spentOn': '2026-10-13',
      'source': 'app',
      'kind': 'expense',
      'accountId': 'a1',
    },
    {
      'id': 't3',
      'amount': 300,
      'currency': 'EUR',
      'category': 'transfer',
      'spentOn': '2026-10-12',
      'source': 'app',
      'kind': 'transfer',
      'accountId': 'a1',
      'transferAccountId': 'a2',
    },
  ],
  'hasMore': false,
};

Map<String, Object?> _portfolio({bool quotes = false}) => {
  'currency': 'EUR',
  'totalValue': 1100,
  'totalCost': 1000,
  'unrealizedProfit': 100,
  'realizedProfit': 25,
  'dividends': 10,
  'quotesConfigured': quotes,
  'allocation': [
    {'label': 'etf', 'value': 1100, 'percent': 100},
  ],
  'otherCurrencies': <Object>[],
  'holdings': [
    {
      'id': 'h1',
      'symbol': 'VWRL.AS',
      'name': 'Vanguard World',
      'assetType': 'etf',
      'currency': 'EUR',
      'lastPrice': 110,
      'priceSource': 'manual',
      'quantity': 10,
      'costBasis': 1000,
      'averageCost': 100,
      'realizedProfit': 25,
      'dividends': 10,
      'marketValue': 1100,
      'unrealizedProfit': 100,
      'unrealizedPercent': 10,
      'priceIsStale': true,
    },
  ],
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp()
      ..on('GET', '/api/v1/finance/wealth', _wealth())
      ..on('GET', '/api/v1/finance/accounts', _accounts())
      ..on('GET', '/api/v1/transactions', _transactions())
      ..on('GET', '/api/v1/finance/portfolio', _portfolio())
      ..on('GET', '/api/v1/finance/overview', {
        'year': 2026,
        'month': 10,
        'currency': 'EUR',
        'total': 0,
        'budgets': <Object>[],
        'forecast': {
          'year': 2026,
          'month': 10,
          'currency': 'EUR',
          'spentSoFar': 0,
          'expectedRecurring': 0,
          'projectedOther': 0,
          'projectedTotal': 0,
          'previousMonthTotal': 0,
          'daysLeft': 16,
          'upcoming': <Object>[],
        },
        'subscriptions': <Object>[],
        'subscriptionsPerMonth': 0,
        'alerts': <Object>[],
      });
  });

  Future<void> show(
    WidgetTester tester, {
    FinanceTab tab = FinanceTab.overview,
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
          initialTab: tab,
          pickStatement: pick,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  test('models parse accounts, transactions, and the portfolio', () {
    final account = AccountData.listFrom(_accounts()).first;
    expect(account.subtitle, 'ING · ••4300');
    expect(account.balance, 2450.25);
    final page = TransactionPage.fromJson(_transactions())!;
    expect(page.items.map((x) => x.kind), ['income', 'expense', 'transfer']);
    expect(page.items[1].signed, -12.5);
    expect(page.items[2].title, 'Transfer');
    final portfolio = PortfolioData.fromJson(_portfolio())!;
    expect(portfolio.unrealizedPercent, closeTo(10, .001));
    expect(portfolio.holdings.single.isOpen, isTrue);
    expect(WealthData.fromJson({'netWorth': 1}), isNull);
    expect(formatSigned(-12.5, 'EUR'), '-€12.50');
    expect(formatSigned(5, 'EUR', plus: true), '+€5.00');
    expect(formatQuantity(2.5), '2.5');
    expect(formatQuantity(10), '10');
  });

  testWidgets('overview shows net worth, the month, and where it comes from', (
    tester,
  ) async {
    await show(tester);

    expect(find.text('€11,250.50'), findsOneWidget);
    expect(find.text('€3,000.00'), findsOneWidget);
    expect(find.text('€1,200.00'), findsOneWidget);
    expect(
      find.textContaining('You keep €1,800.00 (60% of income)'),
      findsOneWidget,
    );
    expect(find.textContaining('Also \$200.00'), findsOneWidget);
    expect(find.text('Income and spending'), findsOneWidget);
  });

  testWidgets('the overview opens the accounts tab', (tester) async {
    await show(tester);

    await tester.tap(find.text('Cash'));
    await tester.pumpAndSettle();

    expect(find.text('ING Checking'), findsOneWidget);
  });

  testWidgets('transactions show spending, income and transfers with signs', (
    tester,
  ) async {
    await show(tester, tab: FinanceTab.transactions);

    expect(find.text('ACME BV'), findsOneWidget);
    expect(find.text('+€3,000.00'), findsOneWidget);
    expect(find.text('-€12.50'), findsOneWidget);
    expect(find.textContaining('ING Checking → Savings'), findsOneWidget);
    // A transfer is neither plus nor minus.
    expect(find.text('€300.00'), findsOneWidget);
  });

  testWidgets('filtering transactions asks the server for that kind', (
    tester,
  ) async {
    await show(tester, tab: FinanceTab.transactions);

    await tester.tap(find.byKey(const Key('transactions-filter-income')));
    await tester.pumpAndSettle();

    expect(
      http.sent('GET', '/api/v1/transactions').last.query['kind'],
      'income',
    );
  });

  testWidgets('adding income posts its kind and account', (tester) async {
    http.on('POST', '/api/v1/expenses', <String, Object>{});
    await show(tester, tab: FinanceTab.transactions);

    await tester.tap(find.byKey(const Key('transactions-add')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Received'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('transaction-amount')),
      '125,50',
    );
    await tester.enterText(
      find.byKey(const Key('transaction-merchant')),
      'Sanne',
    );
    await tester.tap(find.byKey(const Key('transaction-account')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Savings').last);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('transaction-save')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/expenses').single.body as Map;
    expect(body['kind'], 'income');
    expect(body['amount'], 125.5);
    expect(body['accountId'], 'a2');
    expect(body['merchant'], 'Sanne');
    expect(body.containsKey('transferAccountId'), isFalse);
  });

  testWidgets('a transfer needs two different accounts', (tester) async {
    http.on('POST', '/api/v1/expenses', <String, Object>{});
    await show(tester, tab: FinanceTab.transactions);

    await tester.tap(find.byKey(const Key('transactions-add')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.descendant(
        of: find.byKey(const Key('transaction-kind')),
        matching: find.text('Transfer'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('transaction-amount')), '50');
    await tester.tap(find.byKey(const Key('transaction-save')));
    await tester.pumpAndSettle();

    expect(find.text('Pick two different accounts.'), findsOneWidget);
    expect(http.sent('POST', '/api/v1/expenses'), isEmpty);
  });

  testWidgets('deleting a transaction asks the server to delete it', (
    tester,
  ) async {
    http.on('DELETE', '/api/v1/expenses/t2', null, status: 204);
    await show(tester, tab: FinanceTab.transactions);

    await tester.tap(find.byKey(const Key('transaction-t2')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('transaction-delete')));
    await tester.pumpAndSettle();

    expect(http.sent('DELETE', '/api/v1/expenses/t2'), hasLength(1));
  });

  testWidgets('accounts list balances and add posts the new account', (
    tester,
  ) async {
    http.on('POST', '/api/v1/finance/accounts', <String, Object>{});
    await show(tester, tab: FinanceTab.accounts);

    expect(find.text('€2,450.25'), findsOneWidget);
    expect(find.text('€7,700.25'), findsOneWidget);
    expect(find.text('ING · ••4300'), findsOneWidget);

    await tester.tap(find.byKey(const Key('accounts-add')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('account-name')), 'Joint');
    await tester.enterText(find.byKey(const Key('account-balance')), '-20,5');
    await tester.tap(find.byKey(const Key('account-save')));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/finance/accounts').single.body as Map;
    expect(body['name'], 'Joint');
    expect(body['openingBalance'], -20.5);
    expect(body['type'], 'checking');
    expect(body['currency'], 'EUR');
  });

  testWidgets('an account page matches the bank balance', (tester) async {
    http
      ..on('GET', '/api/v1/finance/accounts/a1', _accounts().first)
      ..on('POST', '/api/v1/finance/accounts/a1/reconcile', <String, Object>{});
    await show(tester, tab: FinanceTab.accounts);

    await tester.tap(find.byKey(const Key('account-a1')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('account-balance-value')), findsOneWidget);
    // Its own transactions load filtered to the account.
    expect(
      http
          .sent('GET', '/api/v1/transactions')
          .any((r) => r.query['accountId'] == 'a1'),
      isTrue,
    );

    await tester.tap(find.byKey(const Key('account-reconcile')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('reconcile-balance')), '2500');
    await tester.tap(find.byKey(const Key('reconcile-save')));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/finance/accounts/a1/reconcile').single.body
            as Map;
    expect(body['balance'], 2500);
  });

  testWidgets('importing into an account previews, then commits', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/finance/accounts/a1', _accounts().first)
      ..on('POST', '/api/v1/finance/accounts/a1/import', {
        'committed': false,
        'rows': 2,
        'imported': 0,
        'duplicates': 0,
      });
    await show(
      tester,
      tab: FinanceTab.accounts,
      pick: () async => 'Date,Amount\n',
    );

    await tester.tap(find.byKey(const Key('account-a1')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('account-import')));
    await tester.pumpAndSettle();
    expect(find.text('Import 2 transactions?'), findsOneWidget);
    await tester.tap(find.text('Import'));
    await tester.pumpAndSettle();

    final posts = http
        .sent('POST', '/api/v1/finance/accounts/a1/import')
        .toList();
    expect(posts.map((p) => (p.body as Map)['commit']), [false, true]);
  });

  testWidgets(
    'the portfolio shows value, profit, allocation and stale prices',
    (tester) async {
      await show(tester, tab: FinanceTab.portfolio);

      expect(find.text('€1,100.00'), findsWidgets);
      expect(
        find.textContaining('+€100.00 (+10.0%) on a cost of €1,000.00'),
        findsOneWidget,
      );
      expect(find.text('ETFs 100%'), findsOneWidget);
      expect(find.text('VWRL.AS'), findsOneWidget);
      expect(find.text('10 × €110.00'), findsOneWidget);
      expect(find.byKey(const Key('holding-stale-h1')), findsOneWidget);
      expect(find.byKey(const Key('portfolio-refresh')), findsNothing);
      expect(
        find.textContaining('Prices are the ones you enter'),
        findsOneWidget,
      );
    },
  );

  testWidgets('with quotes configured the portfolio can refresh prices', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/finance/portfolio', _portfolio(quotes: true))
      ..on('POST', '/api/v1/finance/portfolio/refresh', {'updated': 1});
    await show(tester, tab: FinanceTab.portfolio);

    await tester.tap(find.byKey(const Key('portfolio-refresh')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/finance/portfolio/refresh'),
      hasLength(1),
    );
  });

  testWidgets('adding a purchase creates the holding, then records the trade', (
    tester,
  ) async {
    http
      ..on('POST', '/api/v1/finance/portfolio/holdings', {
        'id': 'h9',
        'symbol': 'AAPL',
      })
      ..on(
        'POST',
        '/api/v1/finance/portfolio/holdings/h9/trades',
        <String, Object>{},
      );
    await show(tester, tab: FinanceTab.portfolio);

    await tester.tap(find.byKey(const Key('portfolio-add')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('trade-symbol')), 'aapl');
    await tester.enterText(find.byKey(const Key('trade-quantity')), '3');
    await tester.enterText(find.byKey(const Key('trade-price')), '190,5');
    await tester.tap(find.byKey(const Key('trade-save')));
    await tester.pumpAndSettle();

    final holding =
        http.sent('POST', '/api/v1/finance/portfolio/holdings').single.body
            as Map;
    expect(holding['symbol'], 'AAPL');
    expect(holding['assetType'], 'stock');
    final trade =
        http
                .sent('POST', '/api/v1/finance/portfolio/holdings/h9/trades')
                .single
                .body
            as Map;
    expect(trade['kind'], 'buy');
    expect(trade['quantity'], 3);
    expect(trade['price'], 190.5);
  });

  testWidgets('a holding page lists trades and saves a price you enter', (
    tester,
  ) async {
    final holding = (_portfolio()['holdings'] as List).first;
    http
      ..on('GET', '/api/v1/finance/portfolio/holdings/h1', holding)
      ..on('GET', '/api/v1/finance/portfolio/holdings/h1/trades', [
        {
          'id': 'tr1',
          'holdingId': 'h1',
          'kind': 'buy',
          'tradedOn': '2026-01-05',
          'quantity': 10,
          'price': 100,
          'fees': 2,
        },
      ])
      ..on('GET', '/api/v1/finance/portfolio/holdings/h1/history', [
        {'date': '2026-09-01', 'price': 100},
        {'date': '2026-10-01', 'price': 110},
      ])
      ..on('PUT', '/api/v1/finance/portfolio/holdings/h1/price', holding)
      ..on('DELETE', '/api/v1/finance/portfolio/trades/tr1', null, status: 204);
    await show(tester, tab: FinanceTab.portfolio);

    await tester.tap(find.byKey(const Key('holding-h1')));
    await tester.pumpAndSettle();
    expect(find.text('Bought 10 × €100.00'), findsOneWidget);
    expect(find.textContaining('fees €2.00'), findsOneWidget);
    expect(find.byKey(const Key('holding-history')), findsOneWidget);

    await tester.tap(find.byKey(const Key('holding-set-price')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('holding-price')), '112,5');
    await tester.tap(find.byKey(const Key('holding-price-save')));
    await tester.pumpAndSettle();
    final put = http
        .sent('PUT', '/api/v1/finance/portfolio/holdings/h1/price')
        .single;
    expect((put.body as Map)['price'], 112.5);

    await tester.tap(find.byKey(const Key('trade-delete-tr1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Delete').last);
    await tester.pumpAndSettle();
    expect(
      http.sent('DELETE', '/api/v1/finance/portfolio/trades/tr1'),
      hasLength(1),
    );
  });
}
