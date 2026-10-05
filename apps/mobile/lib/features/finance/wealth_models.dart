import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart' show formatMoney;

double _num(dynamic value) => switch (value) {
  num number => number.toDouble(),
  String text => double.tryParse(text) ?? 0,
  _ => 0,
};

double? _numOrNull(dynamic value) => value == null ? null : _num(value);

DateTime? _day(dynamic value) {
  final raw = asJsonString(value);
  final parsed = raw == null ? null : DateTime.tryParse(raw);
  return parsed == null
      ? null
      : DateTime(parsed.year, parsed.month, parsed.day);
}

/// "-€12.50" or "€12.50": [formatMoney] drops the sign, balances need it.
String formatSigned(double amount, String currency, {bool plus = false}) {
  final text = formatMoney(amount, currency);
  if (amount < 0) return '-$text';
  return plus && amount > 0 ? '+$text' : text;
}

/// Share quantities drop trailing zeros: 10, 2.5, 0.0125.
String formatQuantity(double value) {
  final text = value.toStringAsFixed(4);
  return text.replaceFirst(RegExp(r'\.?0+$'), '');
}

const accountTypes = [
  'checking',
  'savings',
  'credit_card',
  'cash',
  'brokerage',
  'other',
];

String accountTypeLabel(String type) => switch (type) {
  'checking' => 'Checking',
  'savings' => 'Savings',
  'credit_card' => 'Credit card',
  'cash' => 'Cash',
  'brokerage' => 'Brokerage',
  _ => 'Other',
};

IconData accountTypeIcon(String type) => switch (type) {
  'savings' => PhosphorIconsRegular.shieldCheck,
  'credit_card' => PhosphorIconsRegular.identificationCard,
  'cash' => PhosphorIconsRegular.receipt,
  'brokerage' => PhosphorIconsRegular.chartLine,
  'other' => PhosphorIconsRegular.folderSimple,
  _ => PhosphorIconsRegular.wallet,
};

const incomeCategories = [
  'salary',
  'freelance',
  'interest',
  'dividends',
  'refund',
  'gift',
  'other_income',
];

String incomeCategoryLabel(String category) => switch (category) {
  'salary' => 'Salary',
  'freelance' => 'Freelance',
  'interest' => 'Interest',
  'dividends' => 'Dividends',
  'refund' => 'Refund',
  'gift' => 'Gift',
  'transfer' => 'Transfer',
  _ => 'Other income',
};

const assetTypes = ['stock', 'etf', 'fund', 'crypto', 'bond', 'other'];

String assetTypeLabel(String type) => switch (type) {
  'stock' => 'Stocks',
  'etf' => 'ETFs',
  'fund' => 'Funds',
  'crypto' => 'Crypto',
  'bond' => 'Bonds',
  _ => 'Other',
};

const tradeKinds = ['buy', 'sell', 'dividend', 'fee', 'split'];

String tradeKindLabel(String kind) => switch (kind) {
  'buy' => 'Bought',
  'sell' => 'Sold',
  'dividend' => 'Dividend',
  'fee' => 'Fee',
  _ => 'Split',
};

class AccountData {
  const AccountData({
    required this.id,
    required this.name,
    required this.type,
    required this.currency,
    required this.balance,
    required this.monthIn,
    required this.monthOut,
    required this.openingBalance,
    this.institution,
    this.last4,
    this.archived = false,
    this.transactionCount = 0,
  });

  final String id;
  final String name;
  final String type;
  final String currency;
  final String? institution;
  final String? last4;
  final double balance;
  final double openingBalance;
  final double monthIn;
  final double monthOut;
  final bool archived;
  final int transactionCount;

  /// "ING · ••4300" or just the account type.
  String get subtitle => [
    institution ?? accountTypeLabel(type),
    if (last4 != null) '••$last4',
  ].join(' · ');

  static AccountData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = jsonId(map);
    final name = map == null ? null : jsonString(map, 'name');
    if (map == null || id == null || name == null) return null;
    return AccountData(
      id: id,
      name: name,
      type: jsonString(map, 'type') ?? 'checking',
      currency: jsonString(map, 'currency') ?? 'EUR',
      institution: jsonString(map, 'institution'),
      last4: jsonString(map, 'last4'),
      balance: _num(map['balance']),
      openingBalance: _num(map['openingBalance']),
      monthIn: _num(map['monthIn']),
      monthOut: _num(map['monthOut']),
      archived: asJsonBool(map['archived']),
      transactionCount: asJsonInt(map['transactionCount']),
    );
  }

  static List<AccountData> listFrom(dynamic json) => [
    for (final item in jsonMaps(json)) ?AccountData.fromJson(item),
  ];
}

typedef MonthFlowData = ({int year, int month, double income, double spending});
typedef OtherCurrencyData = ({String currency, double value});

class WealthData {
  const WealthData({
    required this.currency,
    required this.netWorth,
    required this.cashTotal,
    required this.portfolioValue,
    required this.monthIncome,
    required this.monthSpending,
    required this.cashFlow,
    required this.otherCurrencies,
    this.savingsRate,
  });

  final String currency;
  final double netWorth;
  final double cashTotal;
  final double portfolioValue;
  final double monthIncome;
  final double monthSpending;
  final double? savingsRate;
  final List<MonthFlowData> cashFlow;
  final List<OtherCurrencyData> otherCurrencies;

  static WealthData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null || jsonString(map, 'currency') == null) return null;
    return WealthData(
      currency: jsonString(map, 'currency')!,
      netWorth: _num(map['netWorth']),
      cashTotal: _num(map['cashTotal']),
      portfolioValue: _num(map['portfolioValue']),
      monthIncome: _num(map['monthIncome']),
      monthSpending: _num(map['monthSpending']),
      savingsRate: _numOrNull(map['savingsRate']),
      cashFlow: [
        for (final item in jsonMaps(map['cashFlow']))
          (
            year: asJsonInt(item['year']),
            month: asJsonInt(item['month']),
            income: _num(item['income']),
            spending: _num(item['spending']),
          ),
      ],
      otherCurrencies: [
        for (final item in jsonMaps(map['otherCurrencies']))
          (
            currency: jsonString(item, 'currency') ?? 'EUR',
            value: _num(item['value']),
          ),
      ],
    );
  }
}

class TransactionData {
  const TransactionData({
    required this.id,
    required this.kind,
    required this.amount,
    required this.currency,
    required this.category,
    required this.spentOn,
    this.merchant,
    this.note,
    this.accountId,
    this.transferAccountId,
  });

  final String id;

  /// `expense`, `income`, or `transfer`.
  final String kind;
  final double amount;
  final String currency;
  final String? merchant;
  final String category;
  final String? note;
  final DateTime spentOn;
  final String? accountId;
  final String? transferAccountId;

  bool get isIncome => kind == 'income';
  bool get isTransfer => kind == 'transfer';

  /// The amount as it moves money: negative for spending.
  double get signed => isIncome ? amount : -amount;

  String get title =>
      merchant ??
      note ??
      (isTransfer
          ? 'Transfer'
          : isIncome
          ? incomeCategoryLabel(category)
          : category);

  static TransactionData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = jsonId(map);
    final spentOn = _day(map?['spentOn']);
    if (map == null || id == null || spentOn == null) return null;
    return TransactionData(
      id: id,
      kind: jsonString(map, 'kind') ?? 'expense',
      amount: _num(map['amount']),
      currency: jsonString(map, 'currency') ?? 'EUR',
      merchant: jsonString(map, 'merchant'),
      category: jsonString(map, 'category') ?? 'other',
      note: jsonString(map, 'note'),
      spentOn: spentOn,
      accountId: jsonString(map, 'accountId'),
      transferAccountId: jsonString(map, 'transferAccountId'),
    );
  }
}

class TransactionPage {
  const TransactionPage(this.items, this.hasMore);

  final List<TransactionData> items;
  final bool hasMore;

  static TransactionPage? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    return TransactionPage([
      for (final item in jsonMaps(map['items']))
        ?TransactionData.fromJson(item),
    ], asJsonBool(map['hasMore']));
  }
}

class HoldingData {
  const HoldingData({
    required this.id,
    required this.symbol,
    required this.name,
    required this.assetType,
    required this.currency,
    required this.quantity,
    required this.costBasis,
    required this.averageCost,
    required this.realizedProfit,
    required this.dividends,
    this.lastPrice,
    this.priceSource,
    this.marketValue,
    this.unrealizedProfit,
    this.unrealizedPercent,
    this.priceIsStale = false,
  });

  final String id;
  final String symbol;
  final String name;
  final String assetType;
  final String currency;
  final double? lastPrice;
  final String? priceSource;
  final double quantity;
  final double costBasis;
  final double averageCost;
  final double realizedProfit;
  final double dividends;
  final double? marketValue;
  final double? unrealizedProfit;
  final double? unrealizedPercent;
  final bool priceIsStale;

  bool get isOpen => quantity > 0;

  static HoldingData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = jsonId(map);
    final symbol = map == null ? null : jsonString(map, 'symbol');
    if (map == null || id == null || symbol == null) return null;
    return HoldingData(
      id: id,
      symbol: symbol,
      name: jsonString(map, 'name') ?? symbol,
      assetType: jsonString(map, 'assetType') ?? 'stock',
      currency: jsonString(map, 'currency') ?? 'EUR',
      lastPrice: _numOrNull(map['lastPrice']),
      priceSource: jsonString(map, 'priceSource'),
      quantity: _num(map['quantity']),
      costBasis: _num(map['costBasis']),
      averageCost: _num(map['averageCost']),
      realizedProfit: _num(map['realizedProfit']),
      dividends: _num(map['dividends']),
      marketValue: _numOrNull(map['marketValue']),
      unrealizedProfit: _numOrNull(map['unrealizedProfit']),
      unrealizedPercent: _numOrNull(map['unrealizedPercent']),
      priceIsStale: asJsonBool(map['priceIsStale']),
    );
  }
}

typedef AllocationData = ({String label, double value, double percent});

class PortfolioData {
  const PortfolioData({
    required this.currency,
    required this.totalValue,
    required this.totalCost,
    required this.unrealizedProfit,
    required this.realizedProfit,
    required this.dividends,
    required this.holdings,
    required this.allocation,
    required this.quotesConfigured,
  });

  final String currency;
  final double totalValue;
  final double totalCost;
  final double unrealizedProfit;
  final double realizedProfit;
  final double dividends;
  final List<HoldingData> holdings;
  final List<AllocationData> allocation;
  final bool quotesConfigured;

  double? get unrealizedPercent =>
      totalCost > 0 ? unrealizedProfit / totalCost * 100 : null;

  static PortfolioData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null || jsonString(map, 'currency') == null) return null;
    return PortfolioData(
      currency: jsonString(map, 'currency')!,
      totalValue: _num(map['totalValue']),
      totalCost: _num(map['totalCost']),
      unrealizedProfit: _num(map['unrealizedProfit']),
      realizedProfit: _num(map['realizedProfit']),
      dividends: _num(map['dividends']),
      holdings: [
        for (final item in jsonMaps(map['holdings']))
          ?HoldingData.fromJson(item),
      ],
      allocation: [
        for (final item in jsonMaps(map['allocation']))
          (
            label: jsonString(item, 'label') ?? 'other',
            value: _num(item['value']),
            percent: _num(item['percent']),
          ),
      ],
      quotesConfigured: asJsonBool(map['quotesConfigured']),
    );
  }
}

class TradeData {
  const TradeData({
    required this.id,
    required this.kind,
    required this.tradedOn,
    required this.quantity,
    required this.price,
    required this.fees,
    this.note,
  });

  final String id;
  final String kind;
  final DateTime tradedOn;
  final double quantity;
  final double price;
  final double fees;
  final String? note;

  static TradeData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = jsonId(map);
    final tradedOn = _day(map?['tradedOn']);
    if (map == null || id == null || tradedOn == null) return null;
    return TradeData(
      id: id,
      kind: jsonString(map, 'kind') ?? 'buy',
      tradedOn: tradedOn,
      quantity: _num(map['quantity']),
      price: _num(map['price']),
      fees: _num(map['fees']),
      note: jsonString(map, 'note'),
    );
  }
}

typedef PricePointData = ({DateTime date, double price});

List<PricePointData> pricePointsFrom(dynamic json) => [
  for (final item in jsonMaps(json))
    if (_day(item['date']) case final date?)
      (date: date, price: _num(item['price'])),
];
