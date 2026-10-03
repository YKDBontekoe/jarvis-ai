import '../../json_maps.dart';

double _num(dynamic value) => switch (value) {
  num number => number.toDouble(),
  String text => double.tryParse(text) ?? 0,
  _ => 0,
};

DateTime? _day(dynamic value) {
  final raw = asJsonString(value);
  final parsed = raw == null ? null : DateTime.tryParse(raw);
  return parsed == null
      ? null
      : DateTime(parsed.year, parsed.month, parsed.day);
}

class BudgetStatusData {
  const BudgetStatusData({
    required this.id,
    required this.category,
    required this.limit,
    required this.currency,
    required this.spent,
    required this.percent,
    required this.state,
    this.projected,
  });

  final String id;
  final String category;
  final double limit;
  final String currency;
  final double spent;
  final int percent;
  final String state;
  final double? projected;

  String get label => category == 'total'
      ? 'Everything'
      : '${category[0].toUpperCase()}${category.substring(1)}';

  static BudgetStatusData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final category = map == null ? null : jsonString(map, 'category');
    if (map == null || id == null || category == null) return null;
    return BudgetStatusData(
      id: id,
      category: category,
      limit: _num(map['limit']),
      currency: jsonString(map, 'currency') ?? 'EUR',
      spent: _num(map['spent']),
      percent: asJsonInt(map['percent']),
      state: jsonString(map, 'state') ?? 'ok',
      projected: map['projected'] == null ? null : _num(map['projected']),
    );
  }
}

class SubscriptionData {
  const SubscriptionData({
    required this.id,
    required this.merchant,
    required this.amount,
    required this.currency,
    required this.cadence,
    required this.nextDueOn,
    required this.status,
    required this.perMonth,
    this.previousAmount,
    this.remindDaysBefore,
  });

  final String id;
  final String merchant;
  final double amount;
  final String currency;
  final String cadence;
  final DateTime? nextDueOn;
  final String status;
  final double perMonth;
  final double? previousAmount;
  final int? remindDaysBefore;

  bool get priceWentUp => previousAmount != null && amount > previousAmount!;

  static SubscriptionData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final merchant = map == null ? null : jsonString(map, 'merchant');
    if (map == null || id == null || merchant == null) return null;
    return SubscriptionData(
      id: id,
      merchant: merchant,
      amount: _num(map['amount']),
      currency: jsonString(map, 'currency') ?? 'EUR',
      cadence: jsonString(map, 'cadence') ?? 'monthly',
      nextDueOn: _day(map['nextDueOn']),
      status: jsonString(map, 'status') ?? 'active',
      perMonth: _num(map['perMonth']),
      previousAmount: map['previousAmount'] == null
          ? null
          : _num(map['previousAmount']),
      remindDaysBefore: map['remindDaysBefore'] is int
          ? map['remindDaysBefore'] as int
          : null,
    );
  }
}

class FinanceAlertData {
  const FinanceAlertData({required this.title, required this.detail});

  final String title;
  final String detail;
}

class ForecastData {
  const ForecastData({
    required this.currency,
    required this.spentSoFar,
    required this.expectedRecurring,
    required this.projectedTotal,
    required this.previousMonthTotal,
    required this.daysLeft,
  });

  final String currency;
  final double spentSoFar;
  final double expectedRecurring;
  final double projectedTotal;
  final double previousMonthTotal;
  final int daysLeft;
}

class FinanceOverviewData {
  const FinanceOverviewData({
    required this.currency,
    required this.forecast,
    required this.budgets,
    required this.subscriptions,
    required this.subscriptionsPerMonth,
    required this.alerts,
  });

  final String currency;
  final ForecastData forecast;
  final List<BudgetStatusData> budgets;
  final List<SubscriptionData> subscriptions;
  final double subscriptionsPerMonth;
  final List<FinanceAlertData> alerts;

  static FinanceOverviewData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final forecast = map == null ? null : jsonObject(map['forecast']);
    if (map == null || forecast == null) return null;
    final currency = jsonString(map, 'currency') ?? 'EUR';
    return FinanceOverviewData(
      currency: currency,
      forecast: ForecastData(
        currency: currency,
        spentSoFar: _num(forecast['spentSoFar']),
        expectedRecurring: _num(forecast['expectedRecurring']),
        projectedTotal: _num(forecast['projectedTotal']),
        previousMonthTotal: _num(forecast['previousMonthTotal']),
        daysLeft: asJsonInt(forecast['daysLeft']),
      ),
      budgets: [
        for (final item in jsonMaps(map['budgets']))
          ?BudgetStatusData.fromJson(item),
      ],
      subscriptions: [
        for (final item in jsonMaps(map['subscriptions']))
          ?SubscriptionData.fromJson(item),
      ],
      subscriptionsPerMonth: _num(map['subscriptionsPerMonth']),
      alerts: [
        for (final item in jsonMaps(map['alerts']))
          if (jsonString(item, 'title') case final title?)
            FinanceAlertData(
              title: title,
              detail: jsonString(item, 'detail') ?? '',
            ),
      ],
    );
  }
}

class ImportReportData {
  const ImportReportData({
    required this.rows,
    required this.imported,
    required this.duplicates,
    required this.incomeSkipped,
    required this.unreadable,
    this.problem,
  });

  final int rows;
  final int imported;
  final int duplicates;
  final int incomeSkipped;
  final int unreadable;
  final String? problem;

  static ImportReportData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    return ImportReportData(
      rows: asJsonInt(map['rows']),
      imported: asJsonInt(map['imported']),
      duplicates: asJsonInt(map['duplicates']),
      incomeSkipped: asJsonInt(map['incomeSkipped']),
      unreadable: asJsonInt(map['unreadable']),
      problem: jsonString(map, 'problem'),
    );
  }
}
