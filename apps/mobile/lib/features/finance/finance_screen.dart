import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart'
    show dayLabel, expenseCategories, expenseCategoryLabel, formatMoney;
import '../tasks/task_details_screen.dart';
import 'finance_models.dart';
import 'negotiate_sheet.dart';

Future<String?> _pickStatement() async {
  final file = await FilePicker.pickFile(
    type: FileType.custom,
    allowedExtensions: const ['csv', 'txt'],
  );
  if (file == null) return null;
  return utf8.decode(await file.readAsBytes(), allowMalformed: true);
}

/// Budgets, the month forecast, recurring charges, and bank-statement import.
class FinanceScreen extends StatefulWidget {
  const FinanceScreen({
    required this.http,
    this.now,
    this.pickStatement,
    this.onAskInChat,
    super.key,
  });

  final Dio http;
  final DateTime? now;

  /// Starts a chat with a prompt, used to cancel in the browser together with Jarvis. Null hides that route.
  final ValueChanged<String>? onAskInChat;

  /// Returns the text of a bank CSV; replaced in tests.
  final Future<String?> Function()? pickStatement;

  @override
  State<FinanceScreen> createState() => _FinanceScreenState();
}

class _FinanceScreenState extends State<FinanceScreen> {
  FinanceOverviewData? _data;
  bool _loading = true;
  String? _error;

  DateTime get _now => widget.now ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/finance/overview',
      );
      if (!mounted) return;
      final data = FinanceOverviewData.fromJson(response.data);
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

  void _toast(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _call(Future<void> Function() action) async {
    try {
      await action();
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  Future<void> _negotiate(SubscriptionData item) async {
    final ask = widget.onAskInChat;
    final choice = await showNegotiateSheet(
      context,
      merchant: item.merchant,
      savedCancelUrl: item.cancelUrl,
      browserAvailable: ask != null,
    );
    if (choice == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/finance/subscriptions/${item.id}/negotiate',
        data: {
          'goal': choice.goal,
          'mode': choice.mode,
          'cancelUrl': choice.cancelUrl,
        },
      );
      if (!mounted) return;
      final prompt = jsonString(
        jsonObject(response.data) ?? const {},
        'prompt',
      );
      if (choice.mode == 'browser' && prompt != null && ask != null) {
        // The browser runs in a chat, where navigation, clicks and typing need the owner's approval.
        Navigator.of(context).pop();
        ask(prompt);
        return;
      }
      _toast('Jarvis is drafting the message. You will find it in Tasks.');
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  Future<void> _openTask(String taskId) => Navigator.of(context).push<void>(
    MaterialPageRoute<void>(
      builder: (_) => TaskDetailsScreen(http: widget.http, taskId: taskId),
    ),
  );

  Future<void> _addBudget() async {
    final result = await showDialog<Map<String, Object>>(
      context: context,
      builder: (_) => const _BudgetDialog(),
    );
    if (result == null) return;
    await _call(
      () => widget.http.put<dynamic>('/api/v1/finance/budgets', data: result),
    );
  }

  Future<void> _import() async {
    final csv = await (widget.pickStatement ?? _pickStatement)();
    if (csv == null || !mounted) return;
    try {
      final preview = await widget.http.post<dynamic>(
        '/api/v1/finance/import',
        data: {'csv': csv, 'commit': false},
      );
      final report = ImportReportData.fromJson(preview.data);
      if (!mounted || report == null) return;
      if (report.rows == 0) {
        _toast(report.problem ?? 'No spending found in that file.');
        return;
      }
      final confirmed = await showJarvisConfirm(
        context,
        title: 'Import ${report.rows} expenses?',
        message:
            '${report.incomeSkipped} incoming payments are skipped'
            '${report.unreadable > 0 ? ', ${report.unreadable} rows could not be read' : ''}. '
            'Payments you already logged are not added twice.',
        confirmLabel: 'Import',
      );
      if (!confirmed) return;
      final done = await widget.http.post<dynamic>(
        '/api/v1/finance/import',
        data: {'csv': csv, 'commit': true},
      );
      final result = ImportReportData.fromJson(done.data);
      if (!mounted) return;
      _toast(
        'Imported ${result?.imported ?? 0} expenses'
        '${(result?.duplicates ?? 0) > 0 ? ', ${result!.duplicates} were already logged' : ''}.',
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Import did not work.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final data = _data;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Finance'),
        actions: [
          HeaderAction(
            key: const Key('finance-import'),
            label: 'Import statement',
            icon: PhosphorIconsRegular.uploadSimple,
            collapsesWhenNarrow: true,
            onPressed: () => unawaited(_import()),
          ),
          const SizedBox(width: 8),
          HeaderAction(
            key: const Key('finance-add-budget'),
            label: 'Budget',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_addBudget()),
          ),
        ],
      ),
      body: ContentWidth(
        child: ListScreenBody(
          loading: _loading,
          error: _error,
          isEmpty: data == null,
          onRetry: () => unawaited(_load()),
          empty: const EmptyState(
            icon: PhosphorIconsRegular.wallet,
            title: 'No finance data yet',
            message: 'Log expenses first, then set budgets here.',
          ),
          child: data == null
              ? const SizedBox.shrink()
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  children: [
                    _forecast(context, data),
                    for (final alert in data.alerts)
                      InlineNotice(
                        message: '${alert.title}. ${alert.detail}',
                        tone: NoticeTone.warning,
                        margin: const EdgeInsets.only(bottom: 8),
                      ),
                    const SizedBox(height: 8),
                    const SectionHeader('Budgets'),
                    if (data.budgets.isEmpty)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 12),
                        child: Text(
                          'Set a monthly limit and Jarvis warns you at 80% '
                          'and 100%.',
                          style: TextStyle(
                            color: JarvisColors.of(context).inkSoft,
                          ),
                        ),
                      ),
                    for (final budget in data.budgets) _budget(context, budget),
                    const SizedBox(height: 12),
                    SectionHeader(
                      'Subscriptions',
                      trailing: Text(
                        '${formatMoney(data.subscriptionsPerMonth, data.currency)} / month',
                        style: TextStyle(
                          color: JarvisColors.of(context).inkSoft,
                        ),
                      ),
                    ),
                    if (data.subscriptions.isEmpty)
                      Text(
                        'Recurring charges appear once they have repeated a '
                        'few times.',
                        style: TextStyle(
                          color: JarvisColors.of(context).inkSoft,
                        ),
                      ),
                    for (final subscription in data.subscriptions)
                      _subscription(context, subscription),
                  ],
                ),
        ),
      ),
    );
  }

  Widget _forecast(BuildContext context, FinanceOverviewData data) {
    final colors = JarvisColors.of(context);
    final f = data.forecast;
    final change = f.previousMonthTotal > 0
        ? f.projectedTotal - f.previousMonthTotal
        : null;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Expected this month', style: TextStyle(color: colors.inkSoft)),
          const SizedBox(height: 4),
          Text(
            formatMoney(f.projectedTotal, f.currency),
            key: const Key('finance-projected'),
            style: Theme.of(context).textTheme.headlineSmall,
          ),
          const SizedBox(height: 6),
          Text(
            '${formatMoney(f.spentSoFar, f.currency)} spent so far, '
            '${formatMoney(f.expectedRecurring, f.currency)} still to be '
            'charged, ${f.daysLeft} days left.',
            style: TextStyle(color: colors.inkSoft),
          ),
          if (change != null)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                change >= 0
                    ? '${formatMoney(change, f.currency)} more than last month'
                    : '${formatMoney(-change, f.currency)} less than last month',
                style: TextStyle(
                  color: change > 0 ? colors.warning : colors.success,
                  fontSize: 13,
                ),
              ),
            ),
        ],
      ),
    );
  }

  Widget _budget(BuildContext context, BudgetStatusData budget) {
    final colors = JarvisColors.of(context);
    final color = switch (budget.state) {
      'over' => colors.danger,
      'warning' || 'projected_over' => colors.warning,
      _ => colors.success,
    };
    return SurfaceCard(
      key: Key('budget-${budget.id}'),
      margin: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  budget.category == 'total'
                      ? 'Everything'
                      : expenseCategoryLabel(budget.category),
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              Text('${budget.percent}%', style: TextStyle(color: color)),
              IconButton(
                tooltip: 'Remove budget',
                icon: const Icon(PhosphorIconsRegular.x, size: 18),
                onPressed: () => unawaited(
                  _call(
                    () => widget.http.delete<dynamic>(
                      '/api/v1/finance/budgets/${budget.id}',
                    ),
                  ),
                ),
              ),
            ],
          ),
          ClipRRect(
            borderRadius: BorderRadius.circular(4),
            child: LinearProgressIndicator(
              value: (budget.percent / 100).clamp(0, 1).toDouble(),
              minHeight: 8,
              color: color,
              backgroundColor: colors.surfaceMuted,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            '${formatMoney(budget.spent, budget.currency)} of '
            '${formatMoney(budget.limit, budget.currency)}'
            '${budget.projected != null ? ' · on pace for ${formatMoney(budget.projected!, budget.currency)}' : ''}',
            style: TextStyle(color: colors.inkSoft, fontSize: 13),
          ),
        ],
      ),
    );
  }

  Widget _subscription(BuildContext context, SubscriptionData item) {
    final colors = JarvisColors.of(context);
    final active = item.status == 'active';
    return SurfaceCard(
      key: Key('subscription-${item.id}'),
      margin: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: Text(
                  item.merchant,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              Text(
                '${formatMoney(item.amount, item.currency)} / ${_noun(item.cadence)}',
              ),
            ],
          ),
          if (item.priceWentUp)
            Text(
              'Was ${formatMoney(item.previousAmount!, item.currency)}',
              style: TextStyle(color: colors.warning, fontSize: 13),
            ),
          if (item.nextDueOn != null)
            Text(
              active
                  ? 'Next charge ${dayLabel(item.nextDueOn!, now: _now)}'
                  : item.status == 'cancelled'
                  ? 'Looks cancelled'
                  : 'Dismissed',
              style: TextStyle(color: colors.muted, fontSize: 13),
            ),
          if (active)
            Row(
              children: [
                Expanded(
                  child: Text(
                    'Remind me 3 days before',
                    style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
                  ),
                ),
                Switch(
                  key: Key('subscription-remind-${item.id}'),
                  value: item.remindDaysBefore != null,
                  onChanged: (value) => unawaited(
                    _call(
                      () => widget.http.patch<dynamic>(
                        '/api/v1/finance/subscriptions/${item.id}',
                        data: value
                            ? {'remindDaysBefore': 3}
                            : {'clearReminder': true},
                      ),
                    ),
                  ),
                ),
                TextButton(
                  key: Key('subscription-dismiss-${item.id}'),
                  onPressed: () => unawaited(
                    _call(
                      () => widget.http.patch<dynamic>(
                        '/api/v1/finance/subscriptions/${item.id}',
                        data: {'status': 'dismissed'},
                      ),
                    ),
                  ),
                  child: const Text('Not a subscription'),
                ),
              ],
            )
          else
            TextButton(
              onPressed: () => unawaited(
                _call(
                  () => widget.http.patch<dynamic>(
                    '/api/v1/finance/subscriptions/${item.id}',
                    data: {'status': 'active'},
                  ),
                ),
              ),
              child: const Text('Track again'),
            ),
          if (active) ..._negotiation(context, item),
        ],
      ),
    );
  }

  /// Cancel or ask for a better price, with where an earlier drafted message ended up.
  List<Widget> _negotiation(BuildContext context, SubscriptionData item) {
    final colors = JarvisColors.of(context);
    final taskId = item.negotiationTaskId;
    final started = item.negotiationStartedAt;
    final host = item.cancelUrl == null
        ? null
        : Uri.tryParse(item.cancelUrl!)?.host;
    return [
      if (host != null && host.isNotEmpty)
        Padding(
          padding: const EdgeInsets.only(top: 2),
          child: Text(
            'Cancel page: $host',
            key: Key('subscription-cancel-url-${item.id}'),
            style: TextStyle(color: colors.muted, fontSize: 13),
          ),
        ),
      if (taskId != null)
        Row(
          key: Key('subscription-negotiation-${item.id}'),
          children: [
            Expanded(
              child: Text(
                '${item.negotiationGoal == 'lower_price' ? 'Price' : 'Cancel'} '
                'message asked'
                '${started == null ? '' : ' ${dayLabel(started, now: _now)}'}',
                style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
              ),
            ),
            TextButton(
              key: Key('subscription-task-${item.id}'),
              onPressed: () => unawaited(_openTask(taskId)),
              child: const Text('Open task'),
            ),
          ],
        ),
      Align(
        alignment: Alignment.centerLeft,
        child: TextButton.icon(
          key: Key('subscription-negotiate-${item.id}'),
          onPressed: () => unawaited(_negotiate(item)),
          icon: const Icon(PhosphorIconsRegular.chatCircle, size: 16),
          label: const Text('Cancel or get a better price'),
        ),
      ),
    ];
  }

  static String _noun(String cadence) => switch (cadence) {
    'weekly' => 'week',
    'quarterly' => 'quarter',
    'yearly' => 'year',
    _ => 'month',
  };
}

class _BudgetDialog extends StatefulWidget {
  const _BudgetDialog();

  @override
  State<_BudgetDialog> createState() => _BudgetDialogState();
}

class _BudgetDialogState extends State<_BudgetDialog> {
  final _limit = TextEditingController();
  String _category = 'total';

  @override
  void dispose() {
    _limit.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Monthly budget'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        DropdownButtonFormField<String>(
          key: const Key('budget-category'),
          initialValue: _category,
          items: [
            const DropdownMenuItem(value: 'total', child: Text('Everything')),
            for (final category in expenseCategories)
              DropdownMenuItem(
                value: category,
                child: Text(expenseCategoryLabel(category)),
              ),
          ],
          onChanged: (value) => setState(() => _category = value ?? 'total'),
        ),
        TextField(
          key: const Key('budget-limit'),
          controller: _limit,
          keyboardType: const TextInputType.numberWithOptions(decimal: true),
          decoration: const InputDecoration(labelText: 'Limit per month'),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('budget-save'),
        onPressed: () {
          final limit = double.tryParse(
            _limit.text.trim().replaceAll(',', '.'),
          );
          if (limit == null || limit <= 0) return;
          Navigator.pop(context, {'category': _category, 'limit': limit});
        },
        child: const Text('Save'),
      ),
    ],
  );
}
