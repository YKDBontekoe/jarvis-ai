import 'package:flutter/material.dart';

/// Asks for one number, such as a bank balance or a share price. The dialog owns its text controller so it
/// stays valid while the dialog animates out.
Future<double?> showNumberDialog(
  BuildContext context, {
  required String title,
  required String label,
  required String confirmLabel,
  required Key fieldKey,
  required Key confirmKey,
  String? message,
  String initial = '',
  bool allowNegative = false,
}) => showDialog<double>(
  context: context,
  builder: (_) => _NumberDialog(
    title: title,
    label: label,
    confirmLabel: confirmLabel,
    fieldKey: fieldKey,
    confirmKey: confirmKey,
    message: message,
    initial: initial,
    allowNegative: allowNegative,
  ),
);

class _NumberDialog extends StatefulWidget {
  const _NumberDialog({
    required this.title,
    required this.label,
    required this.confirmLabel,
    required this.fieldKey,
    required this.confirmKey,
    required this.message,
    required this.initial,
    required this.allowNegative,
  });

  final String title;
  final String label;
  final String confirmLabel;
  final Key fieldKey;
  final Key confirmKey;
  final String? message;
  final String initial;
  final bool allowNegative;

  @override
  State<_NumberDialog> createState() => _NumberDialogState();
}

class _NumberDialogState extends State<_NumberDialog> {
  late final _controller = TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _confirm() {
    final value = double.tryParse(_controller.text.trim().replaceAll(',', '.'));
    if (value == null || (!widget.allowNegative && value <= 0)) return;
    Navigator.pop(context, value);
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (widget.message != null) Text(widget.message!),
        TextField(
          key: widget.fieldKey,
          controller: _controller,
          autofocus: true,
          keyboardType: TextInputType.numberWithOptions(
            decimal: true,
            signed: widget.allowNegative,
          ),
          onSubmitted: (_) => _confirm(),
          decoration: InputDecoration(labelText: widget.label),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: widget.confirmKey,
        onPressed: _confirm,
        child: Text(widget.confirmLabel),
      ),
    ],
  );
}
