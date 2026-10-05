import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart'
    show dayLabel, expenseCategoryIcon, expenseCategoryLabel, formatMoney;
import 'transaction_editor.dart';
import 'wealth_models.dart';

/// The ledger of everything that moved: spending, income and transfers, across every account.
class TransactionsTab extends StatefulWidget {
  const TransactionsTab({
    required this.http,
    this.now,
    this.accountId,
    this.showToolbar = true,
    this.onChanged,
    super.key,
  });

  final Dio http;
  final DateTime? now;

  /// Limits the list to one account, as on the account page.
  final String? accountId;
  final bool showToolbar;

  /// Called after a transaction was added, changed or deleted, so a page showing balances can refresh.
  final VoidCallback? onChanged;

  @override
  State<TransactionsTab> createState() => _TransactionsTabState();
}

class _TransactionsTabState extends State<TransactionsTab>
    with AutomaticKeepAliveClientMixin {
  static const _pageSize = 40;

  final _search = TextEditingController();
  final List<TransactionData> _items = [];
  List<AccountData> _accounts = const [];
  String? _kind;
  String? _accountId;
  bool _hasMore = false;
  bool _loading = true;
  bool _loadingMore = false;
  String? _error;

  @override
  bool get wantKeepAlive => true;

  DateTime get _now => widget.now ?? DateTime.now();

  String? get _account => widget.accountId ?? _accountId;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({bool more = false}) async {
    setState(() {
      if (more) {
        _loadingMore = true;
      } else {
        _loading = true;
      }
    });
    try {
      final accounts = more
          ? null
          : await widget.http.get<dynamic>('/api/v1/finance/accounts');
      final response = await widget.http.get<dynamic>(
        '/api/v1/transactions',
        queryParameters: {
          'offset': more ? _items.length : 0,
          'limit': _pageSize,
          'kind': ?_kind,
          'accountId': ?_account,
          if (_search.text.trim().isNotEmpty) 'q': _search.text.trim(),
        },
      );
      if (!mounted) return;
      final page = TransactionPage.fromJson(response.data);
      setState(() {
        if (accounts != null) _accounts = AccountData.listFrom(accounts.data);
        if (!more) _items.clear();
        _items.addAll(page?.items ?? const []);
        _hasMore = page?.hasMore ?? false;
        _loading = false;
        _loadingMore = false;
        _error = page == null ? 'Could not load your transactions.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _loadingMore = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your transactions.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _edit([TransactionData? existing]) async {
    final result = await showTransactionEditor(
      context,
      accounts: _accounts,
      now: _now,
      existing: existing,
      initialAccountId: widget.accountId,
    );
    if (result == null || !mounted) return;
    try {
      if (result.delete) {
        await widget.http.delete<dynamic>('/api/v1/expenses/${existing!.id}');
      } else if (existing == null) {
        await widget.http.post<dynamic>('/api/v1/expenses', data: result.body);
      } else {
        await widget.http.put<dynamic>(
          '/api/v1/expenses/${existing.id}',
          data: result.body,
        );
      }
      await _load();
      widget.onChanged?.call();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  String? _accountName(String? id) {
    for (final account in _accounts) {
      if (account.id == id) return account.name;
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final colors = JarvisColors.of(context);
    return Column(
      children: [
        if (widget.showToolbar)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 0),
            child: Column(
              children: [
                Row(
                  children: [
                    Expanded(
                      child: TextField(
                        key: const Key('transactions-search'),
                        controller: _search,
                        textInputAction: TextInputAction.search,
                        onSubmitted: (_) => unawaited(_load()),
                        decoration: const InputDecoration(
                          hintText: 'Search shop or note',
                          prefixIcon: Icon(
                            PhosphorIconsRegular.magnifyingGlass,
                            size: 18,
                          ),
                          isDense: true,
                        ),
                      ),
                    ),
                    const SizedBox(width: 8),
                    FilledButton.icon(
                      key: const Key('transactions-add'),
                      onPressed: () => unawaited(_edit()),
                      icon: const Icon(PhosphorIconsRegular.plus, size: 18),
                      label: const Text('Add'),
                    ),
                  ],
                ),
                const SizedBox(height: 8),
                SingleChildScrollView(
                  scrollDirection: Axis.horizontal,
                  child: Row(
                    spacing: 8,
                    children: [
                      for (final (kind, label) in const [
                        (null, 'All'),
                        ('expense', 'Spending'),
                        ('income', 'Income'),
                        ('transfer', 'Transfers'),
                      ])
                        ChoiceChip(
                          key: Key('transactions-filter-${kind ?? 'all'}'),
                          label: Text(label),
                          selected: _kind == kind,
                          onSelected: (_) {
                            setState(() => _kind = kind);
                            unawaited(_load());
                          },
                        ),
                      if (widget.accountId == null && _accounts.length > 1)
                        PopupMenuButton<String?>(
                          key: const Key('transactions-account-filter'),
                          tooltip: 'Filter by account',
                          onSelected: (id) {
                            setState(() => _accountId = id);
                            unawaited(_load());
                          },
                          itemBuilder: (_) => [
                            const PopupMenuItem(
                              value: null,
                              child: Text('All accounts'),
                            ),
                            for (final account in _accounts)
                              PopupMenuItem(
                                value: account.id,
                                child: Text(account.name),
                              ),
                          ],
                          child: Chip(
                            avatar: const Icon(
                              PhosphorIconsRegular.wallet,
                              size: 16,
                            ),
                            label: Text(_accountName(_accountId) ?? 'Account'),
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        Expanded(
          child: ListScreenBody(
            loading: _loading,
            error: _error,
            isEmpty: _items.isEmpty,
            onRetry: () => unawaited(_load()),
            onRefresh: _load,
            empty: EmptyState(
              icon: PhosphorIconsRegular.receipt,
              title: 'No transactions',
              message: _kind != null || _search.text.isNotEmpty
                  ? 'Nothing matches these filters.'
                  : 'Add spending, income or transfers, or import a bank statement from an account.',
            ),
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
              children: [
                for (final item in _items) _row(context, item, colors),
                if (_hasMore)
                  Center(
                    child: TextButton(
                      key: const Key('transactions-more'),
                      onPressed: _loadingMore
                          ? null
                          : () => unawaited(_load(more: true)),
                      child: Text(_loadingMore ? 'Loading…' : 'Load more'),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ],
    );
  }

  Widget _row(BuildContext context, TransactionData item, JarvisColors colors) {
    final account = _accountName(item.accountId);
    final to = _accountName(item.transferAccountId);
    final detail = <String?>[
      item.isTransfer && account != null && to != null
          ? '$account → $to'
          : account,
      if (!item.isTransfer)
        item.isIncome
            ? incomeCategoryLabel(item.category)
            : expenseCategoryLabel(item.category),
      dayLabel(item.spentOn, now: _now),
    ].nonNulls.join(' · ');
    return SurfaceCard(
      key: Key('transaction-${item.id}'),
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
      onTap: () => unawaited(_edit(item)),
      child: Row(
        children: [
          Icon(
            item.isTransfer
                ? PhosphorIconsRegular.arrowsClockwise
                : item.isIncome
                ? PhosphorIconsRegular.trendUp
                : expenseCategoryIcon(item.category),
            size: 20,
            color: item.isIncome ? colors.success : colors.inkSoft,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  item.title,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                Text(
                  detail,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(color: colors.muted, fontSize: 13),
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Text(
            item.isTransfer
                ? formatMoney(item.amount, item.currency)
                : formatSigned(item.signed, item.currency, plus: true),
            style: TextStyle(
              fontWeight: FontWeight.w600,
              color: item.isIncome ? colors.success : null,
            ),
          ),
        ],
      ),
    );
  }
}
