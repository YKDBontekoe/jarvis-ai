import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

/// Spending categories in the order the server lists them.
const expenseCategories = [
  'groceries',
  'dining',
  'transport',
  'shopping',
  'housing',
  'bills',
  'health',
  'entertainment',
  'travel',
  'subscriptions',
  'other',
];

String expenseCategoryLabel(String category) => switch (category) {
  'groceries' => 'Groceries',
  'dining' => 'Eating out',
  'transport' => 'Transport',
  'shopping' => 'Shopping',
  'housing' => 'Housing',
  'bills' => 'Bills',
  'health' => 'Health',
  'entertainment' => 'Going out',
  'travel' => 'Travel',
  'subscriptions' => 'Subscriptions',
  _ => 'Other',
};

IconData expenseCategoryIcon(String category) => switch (category) {
  'groceries' => PhosphorIconsRegular.shoppingCart,
  'dining' => PhosphorIconsRegular.forkKnife,
  'transport' => PhosphorIconsRegular.car,
  'shopping' => PhosphorIconsRegular.shoppingBag,
  'housing' => PhosphorIconsRegular.house,
  'bills' => PhosphorIconsRegular.lightning,
  'health' => PhosphorIconsRegular.firstAid,
  'entertainment' => PhosphorIconsRegular.filmStrip,
  'travel' => PhosphorIconsRegular.airplaneTilt,
  'subscriptions' => PhosphorIconsRegular.repeat,
  _ => PhosphorIconsRegular.dotsThree,
};

/// Icon tint per category. Charts never rely on it: every bar is labelled.
Color expenseCategoryColor(JarvisColors colors, String category) =>
    switch (category) {
      'groceries' => colors.success,
      'dining' => colors.warning,
      'transport' => colors.sky,
      'shopping' => colors.rose,
      'housing' || 'subscriptions' => colors.violet,
      'bills' => colors.info,
      'health' => colors.danger,
      'entertainment' || 'travel' => colors.accent,
      _ => colors.inkSoft,
    };

double _amount(dynamic value) => switch (value) {
  num number => number.toDouble(),
  String text => double.tryParse(text) ?? 0,
  _ => 0,
};

DateTime? _day(dynamic value) {
  final raw = asJsonString(value);
  if (raw == null) return null;
  final parsed = DateTime.tryParse(raw);
  return parsed == null
      ? null
      : DateTime(parsed.year, parsed.month, parsed.day);
}

/// "€1,234.50", "$12.00", or "12.00 CHF". Amounts are positive.
String formatMoney(double amount, String currency, {bool cents = true}) {
  final fixed = amount.abs().toStringAsFixed(cents ? 2 : 0);
  final parts = fixed.split('.');
  final whole = parts[0].replaceAllMapped(
    RegExp(r'\B(?=(\d{3})+(?!\d))'),
    (_) => ',',
  );
  final number = parts.length > 1 ? '$whole.${parts[1]}' : whole;
  return switch (currency) {
    'EUR' => '€$number',
    'USD' => '\$$number',
    'GBP' => '£$number',
    _ => '$number $currency',
  };
}

const _monthNames = [
  'January',
  'February',
  'March',
  'April',
  'May',
  'June',
  'July',
  'August',
  'September',
  'October',
  'November',
  'December',
];

String monthLabel(int year, int month, {DateTime? now}) {
  final today = now ?? DateTime.now();
  final name = _monthNames[month - 1];
  return year == today.year ? name : '$name $year';
}

String dayLabel(DateTime day, {DateTime? now}) {
  final today = now ?? DateTime.now();
  final date = DateTime(today.year, today.month, today.day);
  final difference = date.difference(day).inDays;
  if (difference == 0) return 'Today';
  if (difference == 1) return 'Yesterday';
  final name = _monthNames[day.month - 1];
  return '${day.day} $name${day.year == today.year ? '' : ' ${day.year}'}';
}

/// `YYYY-MM` as the API expects it.
String monthKey(int year, int month) =>
    '$year-${month.toString().padLeft(2, '0')}';

String dateKey(DateTime day) =>
    '${day.year}-${day.month.toString().padLeft(2, '0')}-'
    '${day.day.toString().padLeft(2, '0')}';

class ExpenseData {
  const ExpenseData({
    required this.id,
    required this.amount,
    required this.currency,
    required this.category,
    required this.spentOn,
    required this.source,
    this.merchant,
    this.note,
    this.receiptFileId,
  });

  final String id;
  final double amount;
  final String currency;
  final String? merchant;
  final String category;
  final String? note;
  final DateTime spentOn;
  final String? receiptFileId;

  /// `chat`, `receipt`, or `app`.
  final String source;

  /// The shop, else the note, else the category: what the row is called.
  String get title => merchant ?? note ?? expenseCategoryLabel(category);

  static ExpenseData? fromJson(dynamic data) {
    final map = jsonObject(data);
    final id = jsonId(map);
    final spentOn = _day(map?['spentOn']);
    if (map == null || id == null || spentOn == null) return null;
    return ExpenseData(
      id: id,
      amount: _amount(map['amount']),
      currency: jsonString(map, 'currency') ?? 'EUR',
      merchant: jsonString(map, 'merchant'),
      category: jsonString(map, 'category') ?? 'other',
      note: jsonString(map, 'note'),
      spentOn: spentOn,
      receiptFileId: jsonString(map, 'receiptFileId'),
      source: jsonString(map, 'source') ?? 'app',
    );
  }
}

typedef CategoryTotalData = ({String category, double total, int count});
typedef CurrencyTotalData = ({String currency, double total, int count});
typedef DayTotalData = ({DateTime date, double total});
typedef MerchantTotalData = ({String merchant, double total, int count});

/// One month of spending, as returned by `GET /api/v1/expenses?month=`.
class ExpenseMonthData {
  const ExpenseMonthData({
    required this.year,
    required this.month,
    required this.currency,
    required this.total,
    required this.count,
    required this.previousTotal,
    required this.categories,
    required this.days,
    required this.topMerchants,
    required this.otherCurrencies,
    required this.expenses,
  });

  final int year;
  final int month;
  final String currency;
  final double total;
  final int count;
  final double previousTotal;
  final List<CategoryTotalData> categories;
  final List<DayTotalData> days;
  final List<MerchantTotalData> topMerchants;
  final List<CurrencyTotalData> otherCurrencies;
  final List<ExpenseData> expenses;

  bool get isEmpty => expenses.isEmpty;

  /// Change against last month as a fraction (0.12 is 12% more); null without
  /// spending last month to compare with.
  double? get change =>
      previousTotal <= 0 ? null : (total - previousTotal) / previousTotal;

  static ExpenseMonthData? fromJson(dynamic data) {
    final map = jsonObject(data);
    if (map == null) return null;
    final year = asJsonInt(map['year']);
    final month = asJsonInt(map['month']);
    if (year == 0 || month < 1 || month > 12) return null;
    return ExpenseMonthData(
      year: year,
      month: month,
      currency: jsonString(map, 'currency') ?? 'EUR',
      total: _amount(map['total']),
      count: asJsonInt(map['count']),
      previousTotal: _amount(map['previousTotal']),
      categories: [
        for (final item in jsonMaps(map['categories']))
          (
            category: jsonString(item, 'category') ?? 'other',
            total: _amount(item['total']),
            count: asJsonInt(item['count']),
          ),
      ],
      days: [
        for (final item in jsonMaps(map['days']))
          if (_day(item['date']) case final date?)
            (date: date, total: _amount(item['total'])),
      ],
      topMerchants: [
        for (final item in jsonMaps(map['topMerchants']))
          if (jsonString(item, 'merchant') case final merchant?)
            (
              merchant: merchant,
              total: _amount(item['total']),
              count: asJsonInt(item['count']),
            ),
      ],
      otherCurrencies: [
        for (final item in jsonMaps(map['otherCurrencies']))
          if (jsonString(item, 'currency') case final currency?)
            (
              currency: currency,
              total: _amount(item['total']),
              count: asJsonInt(item['count']),
            ),
      ],
      expenses: [
        for (final item in jsonMaps(map['expenses']))
          ?ExpenseData.fromJson(item),
      ],
    );
  }
}

/// What `POST /api/v1/expenses/scan` read from a receipt photo.
class ReceiptScan {
  const ReceiptScan({
    required this.fileId,
    this.amount,
    this.currency,
    this.merchant,
    this.category,
    this.spentOn,
    this.note,
  });

  final String fileId;
  final double? amount;
  final String? currency;
  final String? merchant;
  final String? category;
  final DateTime? spentOn;
  final String? note;

  static ReceiptScan? fromJson(dynamic data) {
    final map = jsonObject(data);
    final fileId = map == null ? null : jsonString(map, 'fileId');
    if (map == null || fileId == null) return null;
    return ReceiptScan(
      fileId: fileId,
      amount: map['amount'] == null ? null : _amount(map['amount']),
      currency: jsonString(map, 'currency'),
      merchant: jsonString(map, 'merchant'),
      category: jsonString(map, 'category'),
      spentOn: _day(map['spentOn']),
      note: jsonString(map, 'note'),
    );
  }
}
