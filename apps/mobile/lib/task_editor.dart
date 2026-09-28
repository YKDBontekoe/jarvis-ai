part of 'tasks_screen.dart';

class _NewTask {
  const _NewTask({required this.title, required this.prompt});

  final String title;
  final String prompt;
}

class _NewTaskDialog extends StatefulWidget {
  const _NewTaskDialog();

  @override
  State<_NewTaskDialog> createState() => _NewTaskDialogState();
}

class _NewTaskDialogState extends State<_NewTaskDialog> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _prompt = TextEditingController();

  @override
  void dispose() {
    _title.dispose();
    _prompt.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    Navigator.pop(
      context,
      _NewTask(title: _title.text.trim(), prompt: _prompt.text.trim()),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Give Jarvis a task'),
    content: Form(
      key: _formKey,
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextFormField(
            controller: _title,
            autofocus: true,
            maxLength: 200,
            decoration: const InputDecoration(labelText: 'Task name'),
            validator: (value) => value == null || value.trim().isEmpty
                ? 'Enter a task name.'
                : null,
          ),
          const SizedBox(height: 12),
          TextFormField(
            controller: _prompt,
            minLines: 2,
            maxLines: 5,
            maxLength: 32000,
            decoration: const InputDecoration(
              labelText: 'What should Jarvis do?',
              alignLabelWithHint: true,
            ),
            validator: (value) => value == null || value.trim().isEmpty
                ? 'Describe the task.'
                : null,
          ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _save, child: const Text('Start task')),
    ],
  );
}
