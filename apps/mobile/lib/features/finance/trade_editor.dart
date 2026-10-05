import 'package:flutter/material.dart';

import '../../theme.dart';
import '../expenses/expense_models.dart' show dateKey;
import 'wealth_models.dart';

/// Asks for a trade on a holding that already exists; returns the request body or null.
Future<Map<String, Object?>?> showTradeEditor(
  BuildContext context, {
  required DateTime now,
  String symbol = '',
  bool askSymbol = false,
}) => showModalBottomSheet<Map<String, Object?>>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _TradeForm(now: now, symbol: symbol, askSymbol: askSymbol),
);

class _TradeForm extends StatefulWidget {
  const _TradeForm({
    required this.now,
    required this.symbol,
    required this.askSymbol,
  });

  final DateTime now;
  final String symbol;
  final bool askSymbol;

  @override
  State<_TradeForm> createState() => _TradeFormState();
}

class _TradeFormState extends State<_TradeForm> {
  late final _symbol = TextEditingController(text: widget.symbol);
  final _name = TextEditingController();
  final _currency = TextEditingController(text: 'EUR');
  final _quantity = TextEditingController();
  final _price = TextEditingController();
  final _fees = TextEditingController();
  String _kind = 'buy';
  String _assetType = 'stock';
  late DateTime _date = widget.now;
  String? _error;

  @override
  void dispose() {
    _symbol.dispose();
    _name.dispose();
    _currency.dispose();
    _quantity.dispose();
    _price.dispose();
    _fees.dispose();
    super.dispose();
  }

  double? _parse(TextEditingController controller) =>
      double.tryParse(controller.text.trim().replaceAll(',', '.'));

  void _save() {
    final quantity = _parse(_quantity);
    final price = _kind == 'split' ? 0.0 : _parse(_price);
    if (widget.askSymbol && _symbol.text.trim().isEmpty) {
      setState(() => _error = 'Enter a ticker such as VWRL.AS or AAPL.');
      return;
    }
    if (quantity == null || quantity <= 0) {
      setState(() => _error = 'Enter a quantity above zero.');
      return;
    }
    if (price == null || price < 0) {
      setState(() => _error = 'Enter a price.');
      return;
    }
    Navigator.pop(context, {
      if (widget.askSymbol) ...{
        'symbol': _symbol.text.trim().toUpperCase(),
        'name': _name.text.trim(),
        'assetType': _assetType,
        'currency': _currency.text.trim().toUpperCase(),
      },
      'kind': _kind,
      'quantity': quantity,
      'price': price,
      'fees': _parse(_fees) ?? 0,
      'tradedOn': dateKey(_date),
    });
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(widget.now.year - 30),
      lastDate: widget.now.add(const Duration(days: 1)),
    );
    if (picked != null) setState(() => _date = picked);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final split = _kind == 'split';
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (widget.askSymbol) ...[
              TextField(
                key: const Key('trade-symbol'),
                controller: _symbol,
                autofocus: true,
                textCapitalization: TextCapitalization.characters,
                decoration: const InputDecoration(labelText: 'Ticker'),
              ),
              TextField(
                key: const Key('trade-name'),
                controller: _name,
                decoration: const InputDecoration(labelText: 'Name (optional)'),
              ),
              Row(
                children: [
                  Expanded(
                    child: DropdownButtonFormField<String>(
                      key: const Key('trade-asset-type'),
                      initialValue: _assetType,
                      decoration: const InputDecoration(labelText: 'Type'),
                      items: [
                        for (final type in assetTypes)
                          DropdownMenuItem(
                            value: type,
                            child: Text(assetTypeLabel(type)),
                          ),
                      ],
                      onChanged: (value) =>
                          setState(() => _assetType = value ?? _assetType),
                    ),
                  ),
                  const SizedBox(width: 12),
                  SizedBox(
                    width: 90,
                    child: TextField(
                      key: const Key('trade-currency'),
                      controller: _currency,
                      maxLength: 3,
                      textCapitalization: TextCapitalization.characters,
                      decoration: const InputDecoration(
                        labelText: 'Currency',
                        counterText: '',
                      ),
                    ),
                  ),
                ],
              ),
              const SizedBox(height: 8),
            ],
            SegmentedButton<String>(
              key: const Key('trade-kind'),
              showSelectedIcon: false,
              segments: [
                for (final kind in tradeKinds)
                  ButtonSegment(value: kind, label: Text(tradeKindLabel(kind))),
              ],
              selected: {_kind},
              onSelectionChanged: (value) =>
                  setState(() => _kind = value.first),
            ),
            TextField(
              key: const Key('trade-quantity'),
              controller: _quantity,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              decoration: InputDecoration(
                labelText: split ? 'Split ratio (2 for 2-for-1)' : 'Shares',
              ),
            ),
            if (!split)
              TextField(
                key: const Key('trade-price'),
                controller: _price,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                decoration: InputDecoration(
                  labelText: _kind == 'dividend'
                      ? 'Dividend per share'
                      : 'Price per share',
                ),
              ),
            if (!split)
              TextField(
                key: const Key('trade-fees'),
                controller: _fees,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                decoration: const InputDecoration(labelText: 'Fees (optional)'),
              ),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('trade-date'),
                onPressed: _pickDate,
                icon: const Icon(Icons.calendar_today_outlined, size: 16),
                label: Text(dateKey(_date)),
              ),
            ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Text(_error!, style: TextStyle(color: colors.danger)),
              ),
            FilledButton(
              key: const Key('trade-save'),
              onPressed: _save,
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
  }
}
