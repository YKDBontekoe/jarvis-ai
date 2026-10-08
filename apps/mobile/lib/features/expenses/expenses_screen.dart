import 'dart:async';
import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../finance/finance_screen.dart';
import 'expense_editor.dart';
import 'expense_models.dart';

/// Monthly spending: the total against last month, spending per day and per
/// category, and every expense. Jarvis fills it from chat ("€12 lunch") and
/// receipt photos; the owner can scan, add, and correct expenses here too.
class ExpensesScreen extends StatefulWidget {
  const ExpensesScreen({required this.http, this.now, super.key});

  final Dio http;

  /// Fixed clock for tests.
  final DateTime? now;

  @override
  State<ExpensesScreen> createState() => _ExpensesScreenState();
}

class _ExpensesScreenState extends State<ExpensesScreen>
    with WidgetsBindingObserver {
  late DateTime _month = _currentMonth;
  ExpenseMonthData? _data;
  String? _category;
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  DateTime get _now => widget.now ?? DateTime.now();
  DateTime get _currentMonth => DateTime(_now.year, _now.month);
  bool get _isCurrentMonth => _month == _currentMonth;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_load());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  // Jarvis may have logged a receipt from WhatsApp while the app was away.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/expenses',
        queryParameters: {'month': monthKey(_month.year, _month.month)},
      );
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _data = ExpenseMonthData.fromJson(response.data);
        _loading = false;
        _error = _data == null ? 'Could not load your expenses.' : null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your expenses.';
      });
    }
  }

  void _shiftMonth(int delta) {
    final next = DateTime(_month.year, _month.month + delta);
    if (next.isAfter(_currentMonth)) return;
    setState(() {
      _month = next;
      _data = null;
      _category = null;
    });
    unawaited(_load());
  }

  Future<void> _afterEdit(Future<bool> edit) async {
    if (await edit && mounted) unawaited(_load());
  }

  void _add() =>
      unawaited(_afterEdit(showExpenseEditor(context, http: widget.http)));

  void _scan() =>
      unawaited(_afterEdit(scanReceipt(context, http: widget.http)));

  void _edit(ExpenseData expense) => unawaited(
    _afterEdit(
      showExpenseEditor(
        context,
        http: widget.http,
        seed: ExpenseEditorSeed(expense: expense),
      ),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final data = _data;
    final monthName = monthLabel(_month.year, _month.month, now: _now);
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Expenses'),
        actions: [
          HeaderAction(
            key: const Key('expenses-finance'),
            label: 'Budgets & bills',
            icon: PhosphorIconsRegular.chartLine,
            collapsesWhenNarrow: true,
            onPressed: () => unawaited(
              Navigator.of(context).push<void>(
                MaterialPageRoute(
                  builder: (_) => FinanceScreen(http: widget.http),
                ),
              ),
            ),
          ),
          const SizedBox(width: 8),
          HeaderAction(
            key: const Key('expenses-scan'),
            label: 'Scan receipt',
            icon: PhosphorIconsRegular.scan,
            collapsesWhenNarrow: true,
            onPressed: _scan,
          ),
          const SizedBox(width: 8),
          HeaderAction(
            key: const Key('expenses-add'),
            label: 'Add',
            icon: PhosphorIconsRegular.plus,
            onPressed: _add,
          ),
        ],
      ),
      body: Column(
        children: [
          ContentWidth(
            child: _MonthSwitcher(
              label: monthName,
              canGoForward: !_isCurrentMonth,
              onBack: () => _shiftMonth(-1),
              onForward: () => _shiftMonth(1),
            ),
          ),
          Expanded(
            child: ListScreenBody(
              loading: _loading,
              error: _error,
              isEmpty: data == null || data.isEmpty,
              onRetry: () => unawaited(_load()),
              onRefresh: _load,
              empty: EmptyState(
                icon: PhosphorIconsRegular.wallet,
                title: _isCurrentMonth
                    ? 'Nothing spent yet this month'
                    : 'No expenses in $monthName',
                message:
                    'Tell Jarvis “€12 lunch” or send a photo of a receipt '
                    'in chat, and it lands here with its category.',
                action: Wrap(
                  alignment: WrapAlignment.center,
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    FilledButton.icon(
                      key: const Key('expenses-empty-scan'),
                      onPressed: _scan,
                      icon: const Icon(PhosphorIconsRegular.scan, size: 18),
                      label: const Text('Scan a receipt'),
                    ),
                    OutlinedButton.icon(
                      key: const Key('expenses-empty-add'),
                      onPressed: _add,
                      icon: const Icon(PhosphorIconsRegular.plus, size: 18),
                      label: const Text('Add expense'),
                    ),
                  ],
                ),
              ),
              child: data == null ? const SizedBox.shrink() : _overview(data),
            ),
          ),
        ],
      ),
    );
  }

  Widget _overview(ExpenseMonthData data) {
    final colors = JarvisColors.of(context);
    final visible = _category == null
        ? data.expenses
        : data.expenses.where((x) => x.category == _category).toList();
    final days = <DateTime, List<ExpenseData>>{};
    for (final expense in visible) {
      (days[expense.spentOn] ??= []).add(expense);
    }
    var index = 0;
    return ListView(
      padding: EdgeInsets.fromLTRB(
        16,
        4,
        16,
        32 + MediaQuery.paddingOf(context).bottom,
      ),
      children: [
        ContentWidth(
          child: FadeSlideIn(
            index: index++,
            child: _TotalCard(data: data, now: _now),
          ),
        ),
        const SizedBox(height: 12),
        if (data.days.isNotEmpty)
          ContentWidth(
            child: FadeSlideIn(
              index: index++,
              child: _DailyChartCard(data: data),
            ),
          ),
        const SizedBox(height: 12),
        if (data.categories.isNotEmpty)
          ContentWidth(
            child: FadeSlideIn(
              index: index++,
              child: _CategoryCard(
                data: data,
                selected: _category,
                onSelect: (category) => setState(
                  () => _category = _category == category ? null : category,
                ),
              ),
            ),
          ),
        const SizedBox(height: 24),
        ContentWidth(
          child: SectionHeader(
            _category == null
                ? 'All expenses'
                : expenseCategoryLabel(_category!),
            trailing: _category == null
                ? null
                : TextButton.icon(
                    key: const Key('expenses-clear-filter'),
                    onPressed: () => setState(() => _category = null),
                    icon: const Icon(PhosphorIconsRegular.x, size: 14),
                    label: const Text('Show all'),
                  ),
          ),
        ),
        if (visible.isEmpty)
          ContentWidth(
            child: Padding(
              padding: const EdgeInsets.symmetric(vertical: 16),
              child: Text(
                'No ${data.currency} expenses in this category.',
                textAlign: TextAlign.center,
                style: TextStyle(color: colors.muted),
              ),
            ),
          ),
        for (final MapEntry(key: day, value: expenses) in days.entries)
          ContentWidth(
            child: _DayGroup(
              day: day,
              now: _now,
              expenses: expenses,
              onTap: _edit,
            ),
          ),
      ],
    );
  }
}

class _MonthSwitcher extends StatelessWidget {
  const _MonthSwitcher({
    required this.label,
    required this.canGoForward,
    required this.onBack,
    required this.onForward,
  });

  final String label;
  final bool canGoForward;
  final VoidCallback onBack;
  final VoidCallback onForward;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(8, 4, 8, 4),
    child: Row(
      children: [
        IconButton(
          key: const Key('expenses-previous-month'),
          tooltip: 'Previous month',
          onPressed: onBack,
          icon: const Icon(PhosphorIconsRegular.caretLeft, size: 18),
        ),
        Expanded(
          child: AnimatedSwitcher(
            duration: const Duration(milliseconds: 200),
            child: Text(
              label,
              key: ValueKey(label),
              textAlign: TextAlign.center,
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ),
        ),
        IconButton(
          key: const Key('expenses-next-month'),
          tooltip: 'Next month',
          onPressed: canGoForward ? onForward : null,
          icon: const Icon(PhosphorIconsRegular.caretRight, size: 18),
        ),
      ],
    ),
  );
}

/// The month's total as a hero number, with the change against last month.
class _TotalCard extends StatelessWidget {
  const _TotalCard({required this.data, required this.now});

  final ExpenseMonthData data;
  final DateTime now;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final change = data.change;
    final previousMonth = DateTime(data.year, data.month - 1);
    final previousName = monthLabel(
      previousMonth.year,
      previousMonth.month,
      now: now,
    );
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(20, 18, 20, 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Spent in ${monthLabel(data.year, data.month, now: now)}',
            style: TextStyle(fontSize: 13.5, color: colors.inkSoft),
          ),
          const SizedBox(height: 6),
          TweenAnimationBuilder<double>(
            tween: Tween(end: data.total),
            duration: const Duration(milliseconds: 600),
            curve: Curves.easeOutCubic,
            builder: (context, value, _) => Text(
              formatMoney(value, data.currency),
              key: const Key('expenses-total'),
              style: theme.textTheme.displaySmall?.copyWith(
                fontWeight: FontWeight.w600,
                letterSpacing: -0.5,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            crossAxisAlignment: WrapCrossAlignment.center,
            children: [
              if (change != null) _ChangePill(change: change),
              Text(
                change == null
                    ? '${data.count} ${data.count == 1 ? 'expense' : 'expenses'}'
                    : 'vs ${formatMoney(data.previousTotal, data.currency)} in '
                          '$previousName · ${data.count} '
                          '${data.count == 1 ? 'expense' : 'expenses'}',
                style: TextStyle(fontSize: 13, color: colors.inkSoft),
              ),
            ],
          ),
          for (final other in data.otherCurrencies) ...[
            const SizedBox(height: 8),
            Text(
              'Plus ${formatMoney(other.total, other.currency)} in '
              '${other.currency} (${other.count})',
              style: TextStyle(fontSize: 13, color: colors.muted),
            ),
          ],
        ],
      ),
    );
  }
}

class _ChangePill extends StatelessWidget {
  const _ChangePill({required this.change});

  final double change;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final percent = (change.abs() * 100).round();
    final less = change < 0;
    final (fg, bg) = less
        ? (colors.success, colors.successSoft)
        : (colors.warning, colors.warningSoft);
    final text = percent == 0
        ? 'Same as before'
        : '$percent% ${less ? 'less' : 'more'}';
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 9, vertical: 4),
      decoration: BoxDecoration(
        color: bg,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(
            less
                ? PhosphorIconsRegular.trendDown
                : PhosphorIconsRegular.trendUp,
            size: 14,
            color: fg,
          ),
          const SizedBox(width: 4),
          Text(
            text,
            key: const Key('expenses-change'),
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w600,
              color: fg,
            ),
          ),
        ],
      ),
    );
  }
}

/// One bar per day of the month in a single hue. Tapping a bar shows that
/// day's total; the busiest day is labelled from the start.
class _DailyChartCard extends StatefulWidget {
  const _DailyChartCard({required this.data});

  final ExpenseMonthData data;

  @override
  State<_DailyChartCard> createState() => _DailyChartCardState();
}

class _DailyChartCardState extends State<_DailyChartCard> {
  int? _selected;

  int get _daysInMonth =>
      DateTime(widget.data.year, widget.data.month + 1, 0).day;

  List<double> get _totals {
    final totals = List<double>.filled(_daysInMonth, 0);
    for (final day in widget.data.days) {
      if (day.date.day <= totals.length) totals[day.date.day - 1] = day.total;
    }
    return totals;
  }

  @override
  void didUpdateWidget(_DailyChartCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.data != widget.data) _selected = null;
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final totals = _totals;
    var peak = 0;
    for (var i = 1; i < totals.length; i++) {
      if (totals[i] > totals[peak]) peak = i;
    }
    final focus = _selected ?? peak;
    final focusLabel =
        '${focus + 1} ${monthLabel(widget.data.year, widget.data.month).split(' ').first}'
        ' · ${formatMoney(totals[focus], widget.data.currency)}';
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  'Per day',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              Text(
                _selected == null ? 'Busiest: $focusLabel' : focusLabel,
                key: const Key('expenses-day-focus'),
                style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Semantics(
            label:
                'Spending per day. Busiest day: $focusLabel. Tap a bar to see '
                'that day.',
            child: LayoutBuilder(
              builder: (context, constraints) {
                void select(Offset position) {
                  final slot = constraints.maxWidth / totals.length;
                  final day = (position.dx / slot).floor().clamp(
                    0,
                    totals.length - 1,
                  );
                  setState(() => _selected = _selected == day ? null : day);
                }

                return GestureDetector(
                  behavior: HitTestBehavior.opaque,
                  onTapDown: (details) => select(details.localPosition),
                  onHorizontalDragUpdate: (details) {
                    final slot = constraints.maxWidth / totals.length;
                    final day = (details.localPosition.dx / slot).floor().clamp(
                      0,
                      totals.length - 1,
                    );
                    if (day != _selected) setState(() => _selected = day);
                  },
                  child: TweenAnimationBuilder<double>(
                    tween: Tween(begin: 0, end: 1),
                    duration: const Duration(milliseconds: 550),
                    curve: Curves.easeOutCubic,
                    builder: (context, progress, _) => CustomPaint(
                      size: Size(constraints.maxWidth, 96),
                      painter: _DailyBarsPainter(
                        totals: totals,
                        selected: focus,
                        progress: progress,
                        colors: colors,
                      ),
                    ),
                  ),
                );
              },
            ),
          ),
          const SizedBox(height: 6),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              for (final day in [1, 15, totals.length])
                Text(
                  '$day',
                  style: TextStyle(fontSize: 11, color: colors.muted),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DailyBarsPainter extends CustomPainter {
  _DailyBarsPainter({
    required this.totals,
    required this.selected,
    required this.progress,
    required this.colors,
  });

  final List<double> totals;
  final int selected;
  final double progress;
  final JarvisColors colors;

  @override
  void paint(Canvas canvas, Size size) {
    final maximum = totals.fold<double>(0, math.max);
    final slot = size.width / totals.length;
    final barWidth = math.max(2.0, slot - 2);
    final baseline = Paint()
      ..color = colors.outline
      ..strokeWidth = 1;
    canvas.drawLine(
      Offset(0, size.height - 0.5),
      Offset(size.width, size.height - 0.5),
      baseline,
    );
    if (maximum <= 0) return;
    for (final (index, total) in totals.indexed) {
      if (total <= 0) continue;
      final height = math.max(
        3.0,
        total / maximum * (size.height - 6) * progress,
      );
      final rect = Rect.fromLTWH(
        slot * index + (slot - barWidth) / 2,
        size.height - height,
        barWidth,
        height,
      );
      final paint = Paint()
        ..color = index == selected
            ? colors.accent
            : colors.accent.withValues(alpha: 0.38);
      canvas.drawRRect(
        RRect.fromRectAndCorners(
          rect,
          topLeft: Radius.circular(math.min(4, barWidth / 2)),
          topRight: Radius.circular(math.min(4, barWidth / 2)),
        ),
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(_DailyBarsPainter oldDelegate) =>
      oldDelegate.totals != totals ||
      oldDelegate.selected != selected ||
      oldDelegate.progress != progress ||
      oldDelegate.colors != colors;
}

/// Categories ranked by spending, each with its share as a thin bar. Tapping
/// one filters the list below.
class _CategoryCard extends StatelessWidget {
  const _CategoryCard({
    required this.data,
    required this.selected,
    required this.onSelect,
  });

  final ExpenseMonthData data;
  final String? selected;
  final ValueChanged<String> onSelect;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final top = data.categories.isEmpty ? 1.0 : data.categories.first.total;
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(8, 16, 8, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(12, 0, 12, 6),
            child: Text(
              'By category',
              style: Theme.of(context).textTheme.titleMedium,
            ),
          ),
          for (final item in data.categories)
            _CategoryRow(
              key: Key('expenses-category-${item.category}'),
              item: item,
              share: data.total <= 0 ? 0 : item.total / data.total,
              relative: top <= 0 ? 0 : item.total / top,
              currency: data.currency,
              selected: selected == item.category,
              dimmed: selected != null && selected != item.category,
              onTap: () => onSelect(item.category),
              colors: colors,
            ),
          if (data.topMerchants.isNotEmpty) ...[
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 14, 12, 8),
              child: Text(
                'Top places',
                style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w600,
                  color: colors.inkSoft,
                ),
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(12, 0, 12, 10),
              child: Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final merchant in data.topMerchants.take(4))
                    Container(
                      padding: const EdgeInsets.symmetric(
                        horizontal: 10,
                        vertical: 6,
                      ),
                      decoration: BoxDecoration(
                        color: colors.surfaceMuted,
                        borderRadius: BorderRadius.circular(999),
                      ),
                      child: Text(
                        '${merchant.merchant} · '
                        '${formatMoney(merchant.total, data.currency)}',
                        style: TextStyle(fontSize: 12.5, color: colors.ink),
                      ),
                    ),
                ],
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _CategoryRow extends StatelessWidget {
  const _CategoryRow({
    required this.item,
    required this.share,
    required this.relative,
    required this.currency,
    required this.selected,
    required this.dimmed,
    required this.onTap,
    required this.colors,
    super.key,
  });

  final CategoryTotalData item;
  final double share;
  final double relative;
  final String currency;
  final bool selected;
  final bool dimmed;
  final VoidCallback onTap;
  final JarvisColors colors;

  @override
  Widget build(BuildContext context) {
    final label = expenseCategoryLabel(item.category);
    final percent = (share * 100).round();
    return Semantics(
      button: true,
      selected: selected,
      label:
          '$label, ${formatMoney(item.total, currency)}, $percent percent, '
          '${item.count} ${item.count == 1 ? 'expense' : 'expenses'}',
      excludeSemantics: true,
      child: Material(
        color: selected ? colors.accentSoft : Colors.transparent,
        borderRadius: BorderRadius.circular(JarvisRadii.md),
        child: InkWell(
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          onTap: onTap,
          child: AnimatedOpacity(
            duration: const Duration(milliseconds: 180),
            opacity: dimmed ? 0.45 : 1,
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 9, 12, 9),
              child: Row(
                children: [
                  IconBadge(
                    icon: expenseCategoryIcon(item.category),
                    color: expenseCategoryColor(colors, item.category),
                    size: 34,
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text(
                                label,
                                maxLines: 1,
                                overflow: TextOverflow.ellipsis,
                                style: const TextStyle(
                                  fontSize: 14.5,
                                  fontWeight: FontWeight.w500,
                                ),
                              ),
                            ),
                            Text(
                              formatMoney(item.total, currency),
                              style: const TextStyle(
                                fontSize: 14.5,
                                fontWeight: FontWeight.w600,
                                fontFeatures: [FontFeature.tabularFigures()],
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 6),
                        Row(
                          children: [
                            Expanded(
                              child: ClipRRect(
                                borderRadius: BorderRadius.circular(2),
                                child: TweenAnimationBuilder<double>(
                                  tween: Tween(end: relative),
                                  duration: const Duration(milliseconds: 500),
                                  curve: Curves.easeOutCubic,
                                  builder: (context, value, _) =>
                                      LinearProgressIndicator(
                                        value: value,
                                        minHeight: 4,
                                        color: colors.accent,
                                        backgroundColor: colors.surfaceMuted,
                                      ),
                                ),
                              ),
                            ),
                            const SizedBox(width: 10),
                            SizedBox(
                              width: 36,
                              child: Text(
                                '$percent%',
                                textAlign: TextAlign.right,
                                style: TextStyle(
                                  fontSize: 12,
                                  color: colors.inkSoft,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _DayGroup extends StatelessWidget {
  const _DayGroup({
    required this.day,
    required this.now,
    required this.expenses,
    required this.onTap,
  });

  final DateTime day;
  final DateTime now;
  final List<ExpenseData> expenses;
  final ValueChanged<ExpenseData> onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final currency = expenses.first.currency;
    final sameCurrency = expenses.every((x) => x.currency == currency);
    final total = expenses.fold<double>(0, (sum, x) => sum + x.amount);
    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 0, 4, 6),
            child: Row(
              children: [
                Expanded(
                  child: Text(
                    dayLabel(day, now: now),
                    style: TextStyle(
                      fontSize: 12.5,
                      fontWeight: FontWeight.w600,
                      color: colors.inkSoft,
                    ),
                  ),
                ),
                if (sameCurrency)
                  Text(
                    formatMoney(total, currency),
                    style: TextStyle(fontSize: 12.5, color: colors.muted),
                  ),
              ],
            ),
          ),
          SurfaceCard(
            padding: EdgeInsets.zero,
            child: Column(
              children: [
                for (final (index, expense) in expenses.indexed) ...[
                  if (index > 0)
                    Divider(height: 1, indent: 62, color: colors.outline),
                  _ExpenseRow(expense: expense, onTap: () => onTap(expense)),
                ],
              ],
            ),
          ),
        ],
      ),
    );
  }
}

class _ExpenseRow extends StatelessWidget {
  const _ExpenseRow({required this.expense, required this.onTap});

  final ExpenseData expense;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final subtitle = [
      expenseCategoryLabel(expense.category),
      if (expense.merchant != null && expense.note != null) expense.note!,
    ].join(' · ');
    return InkWell(
      key: Key('expense-${expense.id}'),
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 12, 16, 12),
        child: Row(
          children: [
            IconBadge(
              icon: expenseCategoryIcon(expense.category),
              color: expenseCategoryColor(colors, expense.category),
              size: 36,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    expense.title,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    subtitle,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                  ),
                ],
              ),
            ),
            if (expense.receiptFileId != null) ...[
              Tooltip(
                message: 'Has a receipt photo',
                child: Icon(
                  PhosphorIconsRegular.receipt,
                  size: 16,
                  color: colors.muted,
                ),
              ),
              const SizedBox(width: 8),
            ],
            Text(
              formatMoney(expense.amount, expense.currency),
              style: const TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                fontFeatures: [FontFeature.tabularFigures()],
              ),
            ),
          ],
        ),
      ),
    );
  }
}
