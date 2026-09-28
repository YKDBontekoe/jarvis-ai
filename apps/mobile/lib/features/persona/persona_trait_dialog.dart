part of 'persona_screen.dart';

class _TraitDialog extends StatefulWidget {
  const _TraitDialog({this.category, this.statement});

  final String? category;
  final String? statement;

  @override
  State<_TraitDialog> createState() => _TraitDialogState();
}

class _TraitDialogState extends State<_TraitDialog> {
  late String _category = widget.category ?? 'format';
  late final _statement = TextEditingController(text: widget.statement ?? '');

  @override
  void dispose() {
    _statement.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.statement == null ? 'Teach Jarvis' : 'Edit rule'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (widget.statement == null)
          DropdownButtonFormField<String>(
            initialValue: _category,
            decoration: const InputDecoration(labelText: 'About'),
            items: [
              for (final entry in personaCategories.entries)
                DropdownMenuItem(value: entry.key, child: Text(entry.value.$1)),
            ],
            onChanged: (value) => setState(() => _category = value ?? 'other'),
          ),
        const SizedBox(height: 12),
        TextField(
          key: const Key('trait-statement'),
          controller: _statement,
          autofocus: true,
          maxLines: 3,
          maxLength: 280,
          decoration: const InputDecoration(
            hintText: 'e.g. Keep answers under five sentences.',
          ),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('save-trait'),
        onPressed: () =>
            Navigator.pop(context, (_category, _statement.text.trim())),
        child: const Text('Save'),
      ),
    ],
  );
}
