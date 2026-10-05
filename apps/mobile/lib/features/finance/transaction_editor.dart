import 'package:flutter/material.dart';

import '../../theme.dart';
import '../expenses/expense_models.dart'
    show dateKey, expenseCategories, expenseCategoryLabel;
import 'wealth_models.dart';

/// What the owner decided in the editor: the request body to save, or to delete the transaction.
class TransactionEdit {
  const TransactionEdit.save(this.body) : delete = false;
  const TransactionEdit.remove() : body = null, delete = true;

  final Map<String, Object?>? body;
  final bool delete;
}

Future<TransactionEdit?> showTransactionEditor(
  BuildContext context, {
  required List<AccountData> accounts,
  required DateTime now,
  TransactionData? existing,
  String? initialAccountId,
}) => showModalBottomSheet<TransactionEdit>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _TransactionForm(
    accounts: accounts,
    now: now,
    existing: existing,
    initialAccountId: initialAccountId,
  ),
);

class _TransactionForm extends StatefulWidget {
  const _TransactionForm({
    required this.accounts,
    required this.now,
    this.existing,
    this.initialAccountId,
  });

  final List<AccountData> accounts;
  final DateTime now;
  final TransactionData? existing;
  final String? initialAccountId;

  @override
  State<_TransactionForm> createState() => _TransactionFormState();
}

class _TransactionFormState extends State<_TransactionForm> {
  late String _kind = widget.existing?.kind ?? 'expense';
  late final _amount = TextEditingController(
    text: widget.existing == null ? '' : widget.existing!.amount.toString(),
  );
  late final _merchant = TextEditingController(text: widget.existing?.merchant);
  late final _note = TextEditingController(text: widget.existing?.note);
  late String? _category = widget.existing?.category;
  late String? _accountId =
      widget.existing?.accountId ?? widget.initialAccountId;
  late String? _toAccountId = widget.existing?.transferAccountId;
  late DateTime _date = widget.existing?.spentOn ?? widget.now;
  String? _error;

  @override
  void dispose() {
    _amount.dispose();
    _merchant.dispose();
    _note.dispose();
    super.dispose();
  }

  List<String> get _categories =>
      _kind == 'income' ? incomeCategories : expenseCategories;

  String _categoryLabel(String category) => _kind == 'income'
      ? incomeCategoryLabel(category)
      : expenseCategoryLabel(category);

  void _save() {
    final amount = double.tryParse(_amount.text.trim().replaceAll(',', '.'));
    if (amount == null || amount <= 0) {
      setState(() => _error = 'Enter an amount above zero.');
      return;
    }
    if (_kind == 'transfer' &&
        (_accountId == null ||
            _toAccountId == null ||
            _accountId == _toAccountId)) {
      setState(() => _error = 'Pick two different accounts.');
      return;
    }
    final category = _categories.contains(_category) ? _category : null;
    Navigator.pop(
      context,
      TransactionEdit.save({
        'kind': _kind,
        'amount': amount,
        'merchant': _merchant.text.trim(),
        'note': _note.text.trim(),
        'spentOn': dateKey(_date),
        'accountId': _accountId,
        if (_kind == 'transfer') 'transferAccountId': _toAccountId,
        if (_kind != 'transfer' && category != null) 'category': category,
      }),
    );
  }

  Future<void> _pickDate() async {
    final picked = await showDatePicker(
      context: context,
      initialDate: _date,
      firstDate: DateTime(widget.now.year - 5),
      lastDate: widget.now.add(const Duration(days: 1)),
    );
    if (picked != null) setState(() => _date = picked);
  }

  Widget _accountField(
    Key key,
    String label,
    String? value,
    ValueChanged<String?> onChanged, {
    bool optional = true,
  }) => DropdownButtonFormField<String?>(
    key: key,
    initialValue: widget.accounts.any((a) => a.id == value) ? value : null,
    decoration: InputDecoration(labelText: label),
    items: [
      if (optional)
        const DropdownMenuItem<String?>(value: null, child: Text('No account')),
      for (final account in widget.accounts)
        DropdownMenuItem<String?>(value: account.id, child: Text(account.name)),
    ],
    onChanged: onChanged,
  );

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
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
            SegmentedButton<String>(
              key: const Key('transaction-kind'),
              segments: const [
                ButtonSegment(value: 'expense', label: Text('Spent')),
                ButtonSegment(value: 'income', label: Text('Received')),
                ButtonSegment(value: 'transfer', label: Text('Transfer')),
              ],
              selected: {_kind},
              onSelectionChanged: (value) => setState(() {
                _kind = value.first;
                if (!_categories.contains(_category)) _category = null;
              }),
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('transaction-amount'),
              controller: _amount,
              autofocus: widget.existing == null,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              decoration: const InputDecoration(labelText: 'Amount'),
            ),
            if (_kind != 'transfer')
              TextField(
                key: const Key('transaction-merchant'),
                controller: _merchant,
                decoration: InputDecoration(
                  labelText: _kind == 'income' ? 'From' : 'Shop or payee',
                ),
              ),
            if (_kind != 'transfer')
              DropdownButtonFormField<String?>(
                key: ValueKey('transaction-category-$_kind'),
                initialValue: _categories.contains(_category)
                    ? _category
                    : null,
                decoration: const InputDecoration(labelText: 'Category'),
                items: [
                  const DropdownMenuItem<String?>(
                    value: null,
                    child: Text('Pick for me'),
                  ),
                  for (final category in _categories)
                    DropdownMenuItem<String?>(
                      value: category,
                      child: Text(_categoryLabel(category)),
                    ),
                ],
                onChanged: (value) => setState(() => _category = value),
              ),
            _accountField(
              const Key('transaction-account'),
              _kind == 'transfer' ? 'From account' : 'Account',
              _accountId,
              (value) => setState(() => _accountId = value),
              optional: _kind != 'transfer',
            ),
            if (_kind == 'transfer')
              _accountField(
                const Key('transaction-to-account'),
                'To account',
                _toAccountId,
                (value) => setState(() => _toAccountId = value),
                optional: false,
              ),
            TextField(
              key: const Key('transaction-note'),
              controller: _note,
              decoration: const InputDecoration(labelText: 'Note'),
            ),
            const SizedBox(height: 4),
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('transaction-date'),
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
            Row(
              children: [
                if (widget.existing != null)
                  TextButton(
                    key: const Key('transaction-delete'),
                    onPressed: () =>
                        Navigator.pop(context, const TransactionEdit.remove()),
                    style: TextButton.styleFrom(foregroundColor: colors.danger),
                    child: const Text('Delete'),
                  ),
                const Spacer(),
                FilledButton(
                  key: const Key('transaction-save'),
                  onPressed: _save,
                  child: const Text('Save'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}
