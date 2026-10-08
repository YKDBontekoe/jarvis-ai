import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'account_detail_screen.dart';
import 'wealth_models.dart';

/// The owner's bank accounts, cards, savings and cash, each with its balance.
class AccountsTab extends StatefulWidget {
  const AccountsTab({
    required this.http,
    this.now,
    this.pickStatement,
    super.key,
  });

  final Dio http;
  final DateTime? now;
  final Future<String?> Function()? pickStatement;

  @override
  State<AccountsTab> createState() => _AccountsTabState();
}

class _AccountsTabState extends State<AccountsTab>
    with AutomaticKeepAliveClientMixin {
  List<AccountData> _accounts = const [];
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
      final response = await widget.http.get<dynamic>(
        '/api/v1/finance/accounts',
      );
      if (!mounted) return;
      setState(() {
        _accounts = AccountData.listFrom(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your accounts.';
      });
    }
  }

  Future<void> _add() async {
    final body = await showAccountEditor(context);
    if (body == null || !mounted) return;
    try {
      await widget.http.post<dynamic>('/api/v1/finance/accounts', data: body);
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ??
                'Could not add the account.',
          ),
        ),
      );
    }
  }

  Future<void> _open(AccountData account) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => AccountDetailScreen(
          http: widget.http,
          accountId: account.id,
          now: widget.now,
          pickStatement: widget.pickStatement,
        ),
      ),
    );
    if (mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final colors = JarvisColors.of(context);
    return ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _accounts.isEmpty,
      onRetry: () => unawaited(_load()),
      onRefresh: _load,
      empty: EmptyState(
        icon: PhosphorIconsRegular.wallet,
        title: 'No accounts yet',
        message:
            'Add your bank accounts to see balances, income and where '
            'your money goes.',
        action: FilledButton.icon(
          key: const Key('accounts-add'),
          onPressed: () => unawaited(_add()),
          icon: const Icon(PhosphorIconsRegular.plus, size: 18),
          label: const Text('Add account'),
        ),
      ),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
        children: [
          Align(
            alignment: Alignment.centerLeft,
            child: FilledButton.icon(
              key: const Key('accounts-add'),
              onPressed: () => unawaited(_add()),
              icon: const Icon(PhosphorIconsRegular.plus, size: 18),
              label: const Text('Add account'),
            ),
          ),
          const SizedBox(height: 12),
          for (final account in _accounts)
            SurfaceCard(
              key: Key('account-${account.id}'),
              margin: const EdgeInsets.only(bottom: 10),
              onTap: () => unawaited(_open(account)),
              child: Row(
                children: [
                  Icon(
                    accountTypeIcon(account.type),
                    color: colors.inkSoft,
                    size: 22,
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          account.name,
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                        Text(
                          account.subtitle,
                          style: TextStyle(color: colors.muted, fontSize: 13),
                        ),
                      ],
                    ),
                  ),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      Text(
                        formatSigned(account.balance, account.currency),
                        style: TextStyle(
                          fontWeight: FontWeight.w600,
                          fontSize: 16,
                          color: account.balance < 0 ? colors.danger : null,
                        ),
                      ),
                      Text(
                        '${formatSigned(account.monthIn - account.monthOut, account.currency, plus: true)} this month',
                        style: TextStyle(color: colors.muted, fontSize: 12.5),
                      ),
                    ],
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }
}

/// Asks for an account's details; returns the request body, or null when cancelled.
Future<Map<String, Object?>?> showAccountEditor(
  BuildContext context, {
  AccountData? existing,
}) => showJarvisDialog<Map<String, Object?>>(
  context: context,
  builder: (_) => _AccountDialog(existing: existing),
);

class _AccountDialog extends StatefulWidget {
  const _AccountDialog({this.existing});

  final AccountData? existing;

  @override
  State<_AccountDialog> createState() => _AccountDialogState();
}

class _AccountDialogState extends State<_AccountDialog> {
  late final _name = TextEditingController(text: widget.existing?.name);
  late final _bank = TextEditingController(text: widget.existing?.institution);
  late final _currency = TextEditingController(
    text: widget.existing?.currency ?? 'EUR',
  );
  late final _balance = TextEditingController(
    text: widget.existing == null
        ? ''
        : widget.existing!.openingBalance.toString(),
  );
  late String _type = widget.existing?.type ?? 'checking';

  @override
  void dispose() {
    _name.dispose();
    _bank.dispose();
    _currency.dispose();
    _balance.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final editing = widget.existing != null;
    return AlertDialog(
      title: Text(editing ? 'Edit account' : 'New account'),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(
              key: const Key('account-name'),
              controller: _name,
              autofocus: !editing,
              decoration: const InputDecoration(labelText: 'Name'),
            ),
            DropdownButtonFormField<String>(
              key: const Key('account-type'),
              initialValue: _type,
              decoration: const InputDecoration(labelText: 'Type'),
              items: [
                for (final type in accountTypes)
                  DropdownMenuItem(
                    value: type,
                    child: Text(accountTypeLabel(type)),
                  ),
              ],
              onChanged: (value) => setState(() => _type = value ?? _type),
            ),
            TextField(
              key: const Key('account-bank'),
              controller: _bank,
              decoration: const InputDecoration(labelText: 'Bank (optional)'),
            ),
            TextField(
              key: const Key('account-currency'),
              controller: _currency,
              textCapitalization: TextCapitalization.characters,
              maxLength: 3,
              decoration: const InputDecoration(
                labelText: 'Currency',
                counterText: '',
              ),
            ),
            TextField(
              key: const Key('account-balance'),
              controller: _balance,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
                signed: true,
              ),
              decoration: InputDecoration(
                labelText: editing ? 'Opening balance' : 'Balance today',
              ),
            ),
          ],
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(context),
          child: const Text('Cancel'),
        ),
        FilledButton(
          key: const Key('account-save'),
          onPressed: () {
            final name = _name.text.trim();
            if (name.isEmpty) return;
            final balance = _balance.text.trim().isEmpty
                ? 0.0
                : double.tryParse(_balance.text.trim().replaceAll(',', '.'));
            if (balance == null) return;
            Navigator.pop(context, {
              'name': name,
              'type': _type,
              'currency': _currency.text.trim().toUpperCase(),
              'institution': _bank.text.trim(),
              'openingBalance': balance,
            });
          },
          child: const Text('Save'),
        ),
      ],
    );
  }
}
