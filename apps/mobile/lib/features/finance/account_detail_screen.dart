import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'accounts_tab.dart';
import 'number_dialog.dart';
import 'statement_picker.dart';
import 'transactions_tab.dart';
import 'wealth_models.dart';

/// One account: its balance, reconcile and statement import, and its own transactions.
class AccountDetailScreen extends StatefulWidget {
  const AccountDetailScreen({
    required this.http,
    required this.accountId,
    this.now,
    this.pickStatement,
    super.key,
  });

  final Dio http;
  final String accountId;
  final DateTime? now;
  final Future<String?> Function()? pickStatement;

  @override
  State<AccountDetailScreen> createState() => _AccountDetailScreenState();
}

class _AccountDetailScreenState extends State<AccountDetailScreen> {
  AccountData? _account;
  bool _loading = true;
  String? _error;
  int _version = 0;

  String get _path => '/api/v1/finance/accounts/${widget.accountId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load({bool rebuild = false}) async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      final account = AccountData.fromJson(response.data);
      setState(() {
        _account = account;
        _loading = false;
        if (rebuild) _version++;
        _error = account == null ? 'Could not load this account.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this account.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _call(Future<void> Function() action) async {
    try {
      await action();
      await _load(rebuild: true);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  Future<void> _edit() async {
    final account = _account;
    if (account == null) return;
    final body = await showAccountEditor(context, existing: account);
    if (body == null) return;
    await _call(() => widget.http.put<dynamic>(_path, data: body));
  }

  Future<void> _reconcile() async {
    final account = _account;
    if (account == null) return;
    final value = await showNumberDialog(
      context,
      title: 'Match your bank',
      message:
          'Jarvis shows ${formatSigned(account.balance, account.currency)}. '
          'Enter the balance your bank shows today and Jarvis adjusts '
          'the starting balance to match.',
      label: 'Balance',
      confirmLabel: 'Update',
      fieldKey: const Key('reconcile-balance'),
      confirmKey: const Key('reconcile-save'),
      allowNegative: true,
    );
    if (value == null) return;
    await _call(
      () => widget.http.post<dynamic>(
        '$_path/reconcile',
        data: {'balance': value},
      ),
    );
  }

  Future<void> _import() async {
    final csv = await (widget.pickStatement ?? pickBankStatement)();
    if (csv == null || !mounted) return;
    try {
      final preview = await widget.http.post<dynamic>(
        '$_path/import',
        data: {'csv': csv, 'commit': false},
      );
      final report = jsonObject(preview.data);
      final rows = asJsonInt(report?['rows']);
      if (!mounted) return;
      if (rows == 0) {
        _toast(
          jsonString(report ?? const {}, 'problem') ??
              'No transactions found in that file.',
        );
        return;
      }
      final confirmed = await showJarvisConfirm(
        context,
        title: 'Import $rows transactions?',
        message:
            'Spending and incoming payments are added to this account. '
            'Payments you already logged are not added twice.',
        confirmLabel: 'Import',
      );
      if (!confirmed) return;
      final done = await widget.http.post<dynamic>(
        '$_path/import',
        data: {'csv': csv, 'commit': true},
      );
      final result = jsonObject(done.data);
      if (!mounted) return;
      final duplicates = asJsonInt(result?['duplicates']);
      _toast(
        'Imported ${asJsonInt(result?['imported'])} transactions'
        '${duplicates > 0 ? ', $duplicates were already logged' : ''}.',
      );
      await _load(rebuild: true);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'Import did not work.',
        );
      }
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this account?',
      message:
          'Its transactions stay in your log but are no longer linked to an '
          'account.',
      confirmLabel: 'Delete',
      destructive: true,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<dynamic>(_path);
      if (mounted) Navigator.of(context).pop();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final account = _account;
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(account?.name ?? 'Account'),
        actions: [
          if (account != null)
            PopupMenuButton<String>(
              key: const Key('account-menu'),
              onSelected: (value) => switch (value) {
                'edit' => unawaited(_edit()),
                'archive' => unawaited(
                  _call(
                    () => widget.http.put<dynamic>(
                      _path,
                      data: {'archived': !account.archived},
                    ),
                  ),
                ),
                _ => unawaited(_delete()),
              },
              itemBuilder: (_) => [
                const PopupMenuItem(value: 'edit', child: Text('Edit')),
                PopupMenuItem(
                  value: 'archive',
                  child: Text(account.archived ? 'Unarchive' : 'Archive'),
                ),
                const PopupMenuItem(value: 'delete', child: Text('Delete')),
              ],
            ),
        ],
      ),
      body: ContentWidth(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : account == null
            ? ErrorState(
                message: _error ?? 'Could not load this account.',
                onRetry: () => unawaited(_load()),
              )
            : Column(
                children: [
                  SurfaceCard(
                    margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          account.subtitle,
                          style: TextStyle(color: colors.inkSoft),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          formatSigned(account.balance, account.currency),
                          key: const Key('account-balance-value'),
                          style: Theme.of(context).textTheme.headlineSmall,
                        ),
                        const SizedBox(height: 4),
                        Text(
                          'This month: '
                          '${formatSigned(account.monthIn, account.currency, plus: true)} in, '
                          '${formatSigned(-account.monthOut, account.currency)} out',
                          style: TextStyle(color: colors.muted, fontSize: 13),
                        ),
                        const SizedBox(height: 8),
                        Wrap(
                          spacing: 8,
                          children: [
                            OutlinedButton.icon(
                              key: const Key('account-reconcile'),
                              onPressed: () => unawaited(_reconcile()),
                              icon: const Icon(
                                PhosphorIconsRegular.check,
                                size: 16,
                              ),
                              label: const Text('Match bank balance'),
                            ),
                            OutlinedButton.icon(
                              key: const Key('account-import'),
                              onPressed: () => unawaited(_import()),
                              icon: const Icon(
                                PhosphorIconsRegular.uploadSimple,
                                size: 16,
                              ),
                              label: const Text('Import statement'),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  Expanded(
                    child: TransactionsTab(
                      key: ValueKey(_version),
                      http: widget.http,
                      now: widget.now,
                      accountId: account.id,
                      onChanged: () => unawaited(_load()),
                    ),
                  ),
                ],
              ),
      ),
    );
  }
}
