import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart' show formatMoney, monthLabel;
import '../expenses/expenses_screen.dart';
import 'wealth_models.dart';

/// Net worth, this month's income against spending, and the last six months of cash flow.
class OverviewTab extends StatefulWidget {
  const OverviewTab({
    required this.http,
    required this.onOpenTab,
    this.now,
    super.key,
  });

  final Dio http;
  final DateTime? now;

  /// Switches the hub to another tab by index.
  final ValueChanged<int> onOpenTab;

  @override
  State<OverviewTab> createState() => _OverviewTabState();
}

class _OverviewTabState extends State<OverviewTab>
    with AutomaticKeepAliveClientMixin {
  WealthData? _data;
  bool _loading = true;
  String? _error;

  @override
  bool get wantKeepAlive => true;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/finance/wealth');
      if (!mounted) return;
      final data = WealthData.fromJson(response.data);
      setState(() {
        _data = data;
        _loading = false;
        _error = data == null ? 'Could not load your finances.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your finances.';
      });
    }
  }

  void _openSpending() => Navigator.of(context).push<void>(
    MaterialPageRoute<void>(builder: (_) => ExpensesScreen(http: widget.http)),
  );

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final data = _data;
    return ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: data == null,
      onRetry: () => unawaited(_load()),
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.wallet,
        title: 'No finance data yet',
        message: 'Add an account or log an expense to get started.',
      ),
      child: data == null
          ? const SizedBox.shrink()
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
              children: [
                _netWorth(context, data),
                _month(context, data),
                _flow(context, data),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    key: const Key('finance-open-spending'),
                    onPressed: _openSpending,
                    icon: const Icon(PhosphorIconsRegular.chartBar, size: 18),
                    label: const Text('Spending by category'),
                  ),
                ),
              ],
            ),
    );
  }

  Widget _netWorth(BuildContext context, WealthData data) {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Net worth', style: TextStyle(color: colors.inkSoft)),
          const SizedBox(height: 4),
          Text(
            formatSigned(data.netWorth, data.currency),
            key: const Key('finance-net-worth'),
            style: Theme.of(context).textTheme.headlineMedium,
          ),
          const SizedBox(height: 12),
          Row(
            children: [
              Expanded(
                child: _part(
                  context,
                  'Cash',
                  formatSigned(data.cashTotal, data.currency),
                  PhosphorIconsRegular.wallet,
                  FinanceTabLink.accounts,
                ),
              ),
              Expanded(
                child: _part(
                  context,
                  'Portfolio',
                  formatSigned(data.portfolioValue, data.currency),
                  PhosphorIconsRegular.chartLine,
                  FinanceTabLink.portfolio,
                ),
              ),
            ],
          ),
          for (final other in data.otherCurrencies)
            Padding(
              padding: const EdgeInsets.only(top: 8),
              child: Text(
                'Also ${formatSigned(other.value, other.currency)}',
                style: TextStyle(color: colors.muted, fontSize: 13),
              ),
            ),
        ],
      ),
    );
  }

  Widget _part(
    BuildContext context,
    String label,
    String value,
    IconData icon,
    FinanceTabLink tab,
  ) {
    final colors = JarvisColors.of(context);
    return InkWell(
      onTap: () => widget.onOpenTab(tab.target),
      borderRadius: BorderRadius.circular(8),
      child: Padding(
        padding: const EdgeInsets.symmetric(vertical: 4),
        child: Row(
          children: [
            Icon(icon, size: 18, color: colors.inkSoft),
            const SizedBox(width: 8),
            Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  label,
                  style: TextStyle(color: colors.muted, fontSize: 13),
                ),
                Text(
                  value,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  Widget _month(BuildContext context, WealthData data) {
    final colors = JarvisColors.of(context);
    final saved = data.monthIncome - data.monthSpending;
    final rate = data.savingsRate;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('This month', style: TextStyle(color: colors.inkSoft)),
          const SizedBox(height: 8),
          Row(
            children: [
              Expanded(
                child: _figure(
                  'Income',
                  formatMoney(data.monthIncome, data.currency),
                  colors.success,
                  const Key('finance-month-income'),
                ),
              ),
              Expanded(
                child: _figure(
                  'Spending',
                  formatMoney(data.monthSpending, data.currency),
                  colors.ink,
                  const Key('finance-month-spending'),
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            rate == null
                ? 'Log income to see how much you save.'
                : saved >= 0
                ? 'You keep ${formatMoney(saved, data.currency)} (${rate.toStringAsFixed(0)}% of income).'
                : 'You spend ${formatMoney(-saved, data.currency)} more than you earn.',
            key: const Key('finance-savings'),
            style: TextStyle(
              color: saved < 0 && rate != null
                  ? colors.warning
                  : colors.inkSoft,
              fontSize: 13.5,
            ),
          ),
        ],
      ),
    );
  }

  Widget _figure(String label, String value, Color color, Key key) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(
        label,
        style: TextStyle(color: JarvisColors.of(context).muted, fontSize: 13),
      ),
      Text(
        value,
        key: key,
        style: TextStyle(
          fontSize: 20,
          fontWeight: FontWeight.w600,
          color: color,
        ),
      ),
    ],
  );

  /// Income and spending bars for each month; every bar's value is in its semantics label.
  Widget _flow(BuildContext context, WealthData data) {
    final colors = JarvisColors.of(context);
    final peak = data.cashFlow.fold<double>(
      0,
      (max, m) => [max, m.income, m.spending].reduce((a, b) => a > b ? a : b),
    );
    if (peak <= 0) return const SizedBox.shrink();
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Income and spending',
                  style: TextStyle(color: colors.inkSoft),
                ),
              ),
              _legend(colors.success, 'Income'),
              const SizedBox(width: 12),
              _legend(colors.accent, 'Spending'),
            ],
          ),
          const SizedBox(height: 12),
          SizedBox(
            height: 120,
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                for (final m in data.cashFlow)
                  Expanded(
                    child: Semantics(
                      label:
                          '${monthLabel(m.year, m.month, now: widget.now)}: '
                          'income ${formatMoney(m.income, data.currency)}, '
                          'spending ${formatMoney(m.spending, data.currency)}',
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.end,
                        mainAxisAlignment: MainAxisAlignment.center,
                        children: [
                          _bar(m.income / peak, colors.success),
                          const SizedBox(width: 3),
                          _bar(m.spending / peak, colors.accent),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          ),
          const SizedBox(height: 6),
          Row(
            children: [
              for (final m in data.cashFlow)
                Expanded(
                  child: Text(
                    monthLabel(
                      m.year,
                      m.month,
                      now: widget.now,
                    ).substring(0, 3),
                    textAlign: TextAlign.center,
                    style: TextStyle(color: colors.muted, fontSize: 12),
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _bar(double fraction, Color color) => Container(
    width: 12,
    height: (fraction * 110).clamp(2, 110).toDouble(),
    decoration: BoxDecoration(
      color: color,
      borderRadius: const BorderRadius.vertical(top: Radius.circular(3)),
    ),
  );

  Widget _legend(Color color, String label) => Row(
    children: [
      Container(
        width: 10,
        height: 10,
        decoration: BoxDecoration(color: color, shape: BoxShape.circle),
      ),
      const SizedBox(width: 4),
      Text(label, style: const TextStyle(fontSize: 12)),
    ],
  );
}

/// Which hub tab an overview shortcut opens. The hub maps it to its own tab enum.
enum FinanceTabLink {
  accounts(2),
  portfolio(3);

  const FinanceTabLink(this.target);

  final int target;
}
