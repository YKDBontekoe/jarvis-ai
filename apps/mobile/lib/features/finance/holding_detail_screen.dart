import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart' show dayLabel, formatMoney;
import 'number_dialog.dart';
import 'trade_editor.dart';
import 'wealth_models.dart';

/// One holding: position, profit, price history, and its trades.
class HoldingDetailScreen extends StatefulWidget {
  const HoldingDetailScreen({
    required this.http,
    required this.holdingId,
    this.now,
    super.key,
  });

  final Dio http;
  final String holdingId;
  final DateTime? now;

  @override
  State<HoldingDetailScreen> createState() => _HoldingDetailScreenState();
}

class _HoldingDetailScreenState extends State<HoldingDetailScreen> {
  HoldingData? _holding;
  List<TradeData> _trades = const [];
  List<PricePointData> _history = const [];
  bool _loading = true;
  String? _error;

  String get _path => '/api/v1/finance/portfolio/holdings/${widget.holdingId}';
  DateTime get _now => widget.now ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final holding = await widget.http.get<dynamic>(_path);
      final trades = await widget.http.get<dynamic>('$_path/trades');
      List<PricePointData> history = const [];
      try {
        history = pricePointsFrom(
          (await widget.http.get<dynamic>('$_path/history')).data,
        );
      } on DioException {
        // The chart is a bonus; the position and trades still show.
      }
      if (!mounted) return;
      final parsed = HoldingData.fromJson(holding.data);
      setState(() {
        _holding = parsed;
        _trades = [
          for (final item in jsonMaps(trades.data)) ?TradeData.fromJson(item),
        ];
        _history = history;
        _loading = false;
        _error = parsed == null ? 'Could not load this holding.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this holding.';
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

  Future<void> _addTrade() async {
    final holding = _holding;
    if (holding == null) return;
    final body = await showTradeEditor(
      context,
      now: _now,
      symbol: holding.symbol,
    );
    if (body == null) return;
    await _call(() => widget.http.post<dynamic>('$_path/trades', data: body));
  }

  Future<void> _setPrice() async {
    final holding = _holding;
    if (holding == null) return;
    final price = await showNumberDialog(
      context,
      title: 'Price of ${holding.symbol}',
      label: 'Price per share (${holding.currency})',
      confirmLabel: 'Save',
      fieldKey: const Key('holding-price'),
      confirmKey: const Key('holding-price-save'),
      initial: holding.lastPrice?.toString() ?? '',
    );
    if (price == null) return;
    await _call(
      () => widget.http.put<dynamic>('$_path/price', data: {'price': price}),
    );
  }

  Future<void> _deleteTrade(TradeData trade) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this ${tradeKindLabel(trade.kind).toLowerCase()}?',
      message: 'The position is worked out again without it.',
      confirmLabel: 'Delete',
      destructive: true,
    );
    if (!confirmed) return;
    await _call(
      () => widget.http.delete<dynamic>(
        '/api/v1/finance/portfolio/trades/${trade.id}',
      ),
    );
  }

  Future<void> _deleteHolding() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Stop tracking this holding?',
      message: 'Its trades and price history are deleted.',
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
    final holding = _holding;
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(holding?.symbol ?? 'Holding'),
        actions: [
          if (holding != null)
            IconButton(
              key: const Key('holding-delete'),
              tooltip: 'Stop tracking',
              icon: const Icon(PhosphorIconsRegular.trash),
              onPressed: () => unawaited(_deleteHolding()),
            ),
        ],
      ),
      body: ContentWidth(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : holding == null
            ? ErrorState(
                message: _error ?? 'Could not load this holding.',
                onRetry: () => unawaited(_load()),
              )
            : ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                children: [
                  SurfaceCard(
                    margin: const EdgeInsets.only(bottom: 12),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          holding.name,
                          style: TextStyle(color: colors.inkSoft),
                        ),
                        const SizedBox(height: 4),
                        Text(
                          holding.marketValue == null
                              ? 'No price yet'
                              : formatMoney(
                                  holding.marketValue!,
                                  holding.currency,
                                ),
                          key: const Key('holding-value'),
                          style: Theme.of(context).textTheme.headlineSmall,
                        ),
                        const SizedBox(height: 6),
                        Text(
                          '${formatQuantity(holding.quantity)} shares at an average of '
                          '${formatMoney(holding.averageCost, holding.currency)}',
                          style: TextStyle(color: colors.inkSoft),
                        ),
                        if (holding.unrealizedProfit != null)
                          Text(
                            '${formatSigned(holding.unrealizedProfit!, holding.currency, plus: true)} unrealized',
                            style: TextStyle(
                              color: holding.unrealizedProfit! >= 0
                                  ? colors.success
                                  : colors.danger,
                            ),
                          ),
                        if (holding.realizedProfit != 0 ||
                            holding.dividends != 0)
                          Text(
                            'Realized ${formatSigned(holding.realizedProfit, holding.currency, plus: true)}'
                            ' · dividends ${formatMoney(holding.dividends, holding.currency)}',
                            style: TextStyle(color: colors.muted, fontSize: 13),
                          ),
                        if (holding.lastPrice != null)
                          Padding(
                            padding: const EdgeInsets.only(top: 4),
                            child: Text(
                              'Price ${formatMoney(holding.lastPrice!, holding.currency)}'
                              '${holding.priceSource == 'provider' ? ' (live quote)' : ' (entered by you)'}'
                              '${holding.priceIsStale ? ' · may be out of date' : ''}',
                              style: TextStyle(
                                color: holding.priceIsStale
                                    ? colors.warning
                                    : colors.muted,
                                fontSize: 13,
                              ),
                            ),
                          ),
                        if (_history.length > 1) ...[
                          const SizedBox(height: 12),
                          Semantics(
                            label:
                                'Price history from ${formatMoney(_history.first.price, holding.currency)} '
                                'to ${formatMoney(_history.last.price, holding.currency)}',
                            child: SizedBox(
                              height: 64,
                              width: double.infinity,
                              child: CustomPaint(
                                key: const Key('holding-history'),
                                painter: _SparklinePainter([
                                  for (final p in _history) p.price,
                                ], colors.accent),
                              ),
                            ),
                          ),
                        ],
                        const SizedBox(height: 8),
                        Wrap(
                          spacing: 8,
                          children: [
                            FilledButton.icon(
                              key: const Key('holding-add-trade'),
                              onPressed: () => unawaited(_addTrade()),
                              icon: const Icon(
                                PhosphorIconsRegular.plus,
                                size: 16,
                              ),
                              label: const Text('Add trade'),
                            ),
                            OutlinedButton(
                              key: const Key('holding-set-price'),
                              onPressed: () => unawaited(_setPrice()),
                              child: const Text('Set price'),
                            ),
                          ],
                        ),
                      ],
                    ),
                  ),
                  const SectionHeader('Trades'),
                  if (_trades.isEmpty)
                    Text(
                      'No trades yet.',
                      style: TextStyle(color: colors.inkSoft),
                    ),
                  for (final trade in _trades) _trade(context, holding, trade),
                ],
              ),
      ),
    );
  }

  Widget _trade(BuildContext context, HoldingData holding, TradeData trade) {
    final colors = JarvisColors.of(context);
    final split = trade.kind == 'split';
    final total = trade.quantity * trade.price;
    return SurfaceCard(
      key: Key('trade-${trade.id}'),
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(14, 8, 4, 8),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  split
                      ? 'Split ${formatQuantity(trade.quantity)}-for-1'
                      : '${tradeKindLabel(trade.kind)} ${formatQuantity(trade.quantity)} × '
                            '${formatMoney(trade.price, holding.currency)}',
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                Text(
                  [
                    dayLabel(trade.tradedOn, now: _now),
                    if (trade.fees > 0)
                      'fees ${formatMoney(trade.fees, holding.currency)}',
                  ].join(' · '),
                  style: TextStyle(color: colors.muted, fontSize: 13),
                ),
              ],
            ),
          ),
          if (!split)
            Text(
              formatMoney(total, holding.currency),
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
          IconButton(
            key: Key('trade-delete-${trade.id}'),
            tooltip: 'Delete trade',
            icon: const Icon(PhosphorIconsRegular.x, size: 18),
            onPressed: () => unawaited(_deleteTrade(trade)),
          ),
        ],
      ),
    );
  }
}

class _SparklinePainter extends CustomPainter {
  _SparklinePainter(this.values, this.color);

  final List<double> values;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    if (values.length < 2) return;
    final low = values.reduce((a, b) => a < b ? a : b);
    final high = values.reduce((a, b) => a > b ? a : b);
    final span = high - low == 0 ? 1 : high - low;
    final path = Path();
    for (var i = 0; i < values.length; i++) {
      final x = size.width * i / (values.length - 1);
      final y = size.height - (values[i] - low) / span * (size.height - 4) - 2;
      i == 0 ? path.moveTo(x, y) : path.lineTo(x, y);
    }
    canvas.drawPath(
      path,
      Paint()
        ..color = color
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2
        ..strokeJoin = StrokeJoin.round,
    );
  }

  @override
  bool shouldRepaint(_SparklinePainter old) =>
      old.values != values || old.color != color;
}
