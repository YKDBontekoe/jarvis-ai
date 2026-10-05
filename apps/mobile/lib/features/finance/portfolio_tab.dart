import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart' show formatMoney;
import 'holding_detail_screen.dart';
import 'trade_editor.dart';
import 'wealth_models.dart';

/// Stocks and ETFs the owner holds: value, profit, allocation, and a way to record trades.
class PortfolioTab extends StatefulWidget {
  const PortfolioTab({required this.http, this.now, super.key});

  final Dio http;
  final DateTime? now;

  @override
  State<PortfolioTab> createState() => _PortfolioTabState();
}

class _PortfolioTabState extends State<PortfolioTab>
    with AutomaticKeepAliveClientMixin {
  PortfolioData? _data;
  bool _loading = true;
  bool _refreshing = false;
  String? _error;

  @override
  bool get wantKeepAlive => true;

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
        '/api/v1/finance/portfolio',
      );
      if (!mounted) return;
      final data = PortfolioData.fromJson(response.data);
      setState(() {
        _data = data;
        _loading = false;
        _error = data == null ? 'Could not load your portfolio.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your portfolio.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _refreshPrices() async {
    setState(() => _refreshing = true);
    try {
      await widget.http.post<dynamic>('/api/v1/finance/portfolio/refresh');
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ??
              'Could not refresh prices.',
        );
      }
    } finally {
      if (mounted) setState(() => _refreshing = false);
    }
  }

  /// Adds the holding when it is new, then records the first trade on it.
  Future<void> _addTrade() async {
    final body = await showTradeEditor(context, now: _now, askSymbol: true);
    if (body == null || !mounted) return;
    try {
      final holding = await widget.http.post<dynamic>(
        '/api/v1/finance/portfolio/holdings',
        data: {
          'symbol': body['symbol'],
          'name': body['name'],
          'assetType': body['assetType'],
          'currency': body['currency'],
        },
      );
      final id = jsonId(jsonObject(holding.data));
      if (id == null) return;
      await widget.http.post<dynamic>(
        '/api/v1/finance/portfolio/holdings/$id/trades',
        data: {
          'kind': body['kind'],
          'quantity': body['quantity'],
          'price': body['price'],
          'fees': body['fees'],
          'tradedOn': body['tradedOn'],
        },
      );
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      // The holding may already exist; the owner then adds the trade from its page.
      _toast(firstProblemMessage(error.response?.data) ?? 'That did not work.');
      await _load();
    }
  }

  Future<void> _open(HoldingData holding) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => HoldingDetailScreen(
          http: widget.http,
          holdingId: holding.id,
          now: widget.now,
        ),
      ),
    );
    if (mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    super.build(context);
    final data = _data;
    return ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: data == null || data.holdings.isEmpty,
      onRetry: () => unawaited(_load()),
      onRefresh: _load,
      empty: EmptyState(
        icon: PhosphorIconsRegular.chartLine,
        title: 'No holdings yet',
        message:
            'Add the stocks and ETFs you own to follow their value and '
            'profit.',
        action: FilledButton.icon(
          key: const Key('portfolio-add'),
          onPressed: () => unawaited(_addTrade()),
          icon: const Icon(PhosphorIconsRegular.plus, size: 18),
          label: const Text('Add a purchase'),
        ),
      ),
      child: data == null
          ? const SizedBox.shrink()
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
              children: [
                Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    FilledButton.icon(
                      key: const Key('portfolio-add'),
                      onPressed: () => unawaited(_addTrade()),
                      icon: const Icon(PhosphorIconsRegular.plus, size: 18),
                      label: const Text('Add a purchase'),
                    ),
                    if (data.quotesConfigured)
                      FilledButton.tonalIcon(
                        key: const Key('portfolio-refresh'),
                        onPressed: _refreshing
                            ? null
                            : () => unawaited(_refreshPrices()),
                        icon: const Icon(
                          PhosphorIconsRegular.arrowsClockwise,
                          size: 18,
                        ),
                        label: Text(
                          _refreshing ? 'Refreshing…' : 'Refresh prices',
                        ),
                      ),
                  ],
                ),
                const SizedBox(height: 12),
                _summary(context, data),
                if (data.allocation.isNotEmpty) _allocation(context, data),
                if (!data.quotesConfigured)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: Text(
                      'Prices are the ones you enter. Set Finance:Quotes:ApiKey '
                      'on the server to fetch them automatically.',
                      style: TextStyle(
                        color: JarvisColors.of(context).muted,
                        fontSize: 13,
                      ),
                    ),
                  ),
                const SectionHeader('Holdings'),
                for (final holding in data.holdings) _holding(context, holding),
              ],
            ),
    );
  }

  Color _profitColor(JarvisColors colors, double? value) => switch (value) {
    null => colors.inkSoft,
    > 0 => colors.success,
    < 0 => colors.danger,
    _ => colors.inkSoft,
  };

  Widget _summary(BuildContext context, PortfolioData data) {
    final colors = JarvisColors.of(context);
    final percent = data.unrealizedPercent;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Portfolio value', style: TextStyle(color: colors.inkSoft)),
          const SizedBox(height: 4),
          Text(
            formatMoney(data.totalValue, data.currency),
            key: const Key('portfolio-value'),
            style: Theme.of(context).textTheme.headlineMedium,
          ),
          const SizedBox(height: 4),
          Text(
            '${formatSigned(data.unrealizedProfit, data.currency, plus: true)}'
            '${percent == null ? '' : ' (${percent >= 0 ? '+' : ''}${percent.toStringAsFixed(1)}%)'}'
            ' on a cost of ${formatMoney(data.totalCost, data.currency)}',
            key: const Key('portfolio-profit'),
            style: TextStyle(
              color: _profitColor(colors, data.unrealizedProfit),
            ),
          ),
          if (data.realizedProfit != 0 || data.dividends != 0)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                'Realized ${formatSigned(data.realizedProfit, data.currency, plus: true)}'
                ' · dividends ${formatMoney(data.dividends, data.currency)}',
                style: TextStyle(color: colors.muted, fontSize: 13),
              ),
            ),
        ],
      ),
    );
  }

  /// A stacked bar with a labelled legend, so no information depends on colour alone.
  Widget _allocation(BuildContext context, PortfolioData data) {
    final colors = JarvisColors.of(context);
    final palette = [
      colors.accent,
      colors.success,
      colors.warning,
      colors.violet,
      colors.sky,
      colors.rose,
    ];
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Allocation', style: TextStyle(color: colors.inkSoft)),
          const SizedBox(height: 10),
          ClipRRect(
            borderRadius: BorderRadius.circular(6),
            child: SizedBox(
              height: 12,
              child: Row(
                children: [
                  for (final (i, slice) in data.allocation.indexed)
                    Expanded(
                      flex: (slice.percent * 10).round().clamp(1, 1000),
                      child: ColoredBox(color: palette[i % palette.length]),
                    ),
                ],
              ),
            ),
          ),
          const SizedBox(height: 10),
          Wrap(
            spacing: 14,
            runSpacing: 4,
            children: [
              for (final (i, slice) in data.allocation.indexed)
                Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Container(
                      width: 10,
                      height: 10,
                      decoration: BoxDecoration(
                        color: palette[i % palette.length],
                        shape: BoxShape.circle,
                      ),
                    ),
                    const SizedBox(width: 6),
                    Text(
                      '${assetTypeLabel(slice.label)} ${slice.percent.toStringAsFixed(0)}%',
                      style: const TextStyle(fontSize: 13),
                    ),
                  ],
                ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _holding(BuildContext context, HoldingData holding) {
    final colors = JarvisColors.of(context);
    final percent = holding.unrealizedPercent;
    return SurfaceCard(
      key: Key('holding-${holding.id}'),
      margin: const EdgeInsets.only(bottom: 10),
      onTap: () => unawaited(_open(holding)),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  holding.symbol,
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
                Text(
                  holding.isOpen
                      ? '${formatQuantity(holding.quantity)} × '
                            '${holding.lastPrice == null ? 'no price' : formatMoney(holding.lastPrice!, holding.currency)}'
                      : 'Sold',
                  style: TextStyle(color: colors.muted, fontSize: 13),
                ),
                if (holding.priceIsStale)
                  Text(
                    'Price may be out of date',
                    key: Key('holding-stale-${holding.id}'),
                    style: TextStyle(color: colors.warning, fontSize: 12.5),
                  ),
              ],
            ),
          ),
          if (holding.isOpen)
            Column(
              crossAxisAlignment: CrossAxisAlignment.end,
              children: [
                Text(
                  formatMoney(
                    holding.marketValue ?? holding.costBasis,
                    holding.currency,
                  ),
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                if (percent != null)
                  Text(
                    '${percent >= 0 ? '+' : ''}${percent.toStringAsFixed(1)}%',
                    style: TextStyle(
                      color: _profitColor(colors, holding.unrealizedProfit),
                      fontSize: 13,
                    ),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}
