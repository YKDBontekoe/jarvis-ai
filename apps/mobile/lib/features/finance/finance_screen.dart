import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../ui/jarvis_ui.dart';
import 'accounts_tab.dart';
import 'budgets_tab.dart';
import 'overview_tab.dart';
import 'portfolio_tab.dart';
import 'transactions_tab.dart';

/// The tabs of the finance hub, in the order they appear.
enum FinanceTab {
  overview('Overview'),
  transactions('Transactions'),
  accounts('Accounts'),
  portfolio('Portfolio'),
  budgets('Budgets & bills');

  const FinanceTab(this.label);

  final String label;
}

/// Net worth, the transaction ledger, bank accounts, the stock portfolio, and budgets with subscriptions.
class FinanceScreen extends StatefulWidget {
  const FinanceScreen({
    required this.http,
    this.now,
    this.pickStatement,
    this.onAskInChat,
    this.initialTab = FinanceTab.overview,
    super.key,
  });

  final Dio http;
  final DateTime? now;
  final FinanceTab initialTab;

  /// Starts a chat with a prompt, used to cancel in the browser together with Jarvis. Null hides that route.
  final ValueChanged<String>? onAskInChat;

  /// Returns the text of a bank CSV; replaced in tests.
  final Future<String?> Function()? pickStatement;

  @override
  State<FinanceScreen> createState() => _FinanceScreenState();
}

class _FinanceScreenState extends State<FinanceScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(
    length: FinanceTab.values.length,
    vsync: this,
    initialIndex: widget.initialTab.index,
  );

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Finance'),
      bottom: TabBar(
        controller: _tabs,
        isScrollable: true,
        tabAlignment: TabAlignment.start,
        tabs: [for (final tab in FinanceTab.values) Tab(text: tab.label)],
      ),
    ),
    body: ContentWidth(
      child: TabBarView(
        controller: _tabs,
        children: [
          OverviewTab(
            http: widget.http,
            now: widget.now,
            onOpenTab: _tabs.animateTo,
          ),
          TransactionsTab(http: widget.http, now: widget.now),
          AccountsTab(
            http: widget.http,
            now: widget.now,
            pickStatement: widget.pickStatement,
          ),
          PortfolioTab(http: widget.http, now: widget.now),
          BudgetsTab(
            http: widget.http,
            now: widget.now,
            pickStatement: widget.pickStatement,
            onAskInChat: widget.onAskInChat,
          ),
        ],
      ),
    ),
  );
}
