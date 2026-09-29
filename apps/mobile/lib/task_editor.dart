part of 'tasks_screen.dart';

class _NewTask {
  const _NewTask({required this.title, required this.prompt, this.profileId});

  final String title;
  final String prompt;
  final String? profileId;
}

class _NewTaskDialog extends StatefulWidget {
  const _NewTaskDialog({required this.http});

  final Dio http;

  @override
  State<_NewTaskDialog> createState() => _NewTaskDialogState();
}

class _NewTaskDialogState extends State<_NewTaskDialog> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _prompt = TextEditingController();
  String? _profileId;
  List<Map<String, dynamic>> _profiles = const [];

  @override
  void initState() {
    super.initState();
    widget.http.get<dynamic>('/api/v1/profiles').then((response) {
      if (!mounted) return;
      final profiles = jsonMaps(response.data)
          .where((item) => jsonString(item, 'id') != null)
          .toList();
      String? selected;
      for (final profile in profiles) {
        if (asJsonBool(profile['isDefault'])) {
          selected = jsonString(profile, 'id');
          break;
        }
      }
      selected ??= profiles.isEmpty ? null : jsonString(profiles.first, 'id');
      setState(() {
        _profiles = profiles;
        _profileId = selected;
      });
    }).catchError((_) {});
  }

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
      _NewTask(
        title: _title.text.trim(),
        prompt: _prompt.text.trim(),
        profileId: _profileId,
      ),
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
          if (_profiles.length > 1) ...[
            const SizedBox(height: 12),
            DropdownButtonFormField<String>(
              initialValue: _profileId,
              decoration: const InputDecoration(labelText: 'Assistant profile'),
              items: [
                for (final profile in _profiles)
                  DropdownMenuItem(
                    value: jsonString(profile, 'id'),
                    child: Text(asJsonString(profile['name']) ?? 'Profile'),
                  ),
              ],
              onChanged: (value) => setState(() => _profileId = value),
            ),
          ],
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
