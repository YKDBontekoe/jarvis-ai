part of 'memory_screen.dart';

class _MemoryDraft {
  const _MemoryDraft({
    required this.kind,
    required this.content,
    required this.pinned,
  });

  final String kind;
  final String content;
  final bool pinned;
}

class _MemoryEditorDialog extends StatefulWidget {
  const _MemoryEditorDialog({
    required this.title,
    required this.saveLabel,
    this.kind,
    this.content,
    this.pinned = false,
    this.kindFallback = 'fact',
  });

  final String title;
  final String saveLabel;
  final String? kind;
  final String? content;
  final bool pinned;
  final String kindFallback;

  @override
  State<_MemoryEditorDialog> createState() => _MemoryEditorDialogState();
}

class _MemoryEditorDialogState extends State<_MemoryEditorDialog> {
  final _formKey = GlobalKey<FormState>();
  late final _content = TextEditingController(text: widget.content ?? '');
  late String _kind = _memoryKinds.contains(widget.kind)
      ? widget.kind!
      : widget.kindFallback;
  late bool _pinned = widget.pinned;

  @override
  void dispose() {
    _content.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    Navigator.pop(
      context,
      _MemoryDraft(kind: _kind, content: _content.text.trim(), pinned: _pinned),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: Form(
      key: _formKey,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          DropdownButtonFormField<String>(
            initialValue: _kind,
            decoration: const InputDecoration(labelText: 'Type'),
            items: [
              for (final value in _memoryKinds)
                DropdownMenuItem(value: value, child: Text(value)),
            ],
            onChanged: (value) =>
                setState(() => _kind = value ?? widget.kindFallback),
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _content,
            autofocus: true,
            minLines: 2,
            maxLines: 5,
            maxLength: 8000,
            decoration: const InputDecoration(
              labelText: 'What should Jarvis remember?',
              alignLabelWithHint: true,
            ),
            validator: (value) => value == null || value.trim().isEmpty
                ? 'Enter something to remember.'
                : null,
          ),
          SwitchListTile(
            contentPadding: EdgeInsets.zero,
            title: const Text('Pin this memory'),
            value: _pinned,
            onChanged: (value) => setState(() => _pinned = value),
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _save, child: Text(widget.saveLabel)),
    ],
  );
}
