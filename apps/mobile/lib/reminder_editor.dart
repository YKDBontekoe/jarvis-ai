part of 'reminders_screen.dart';

class _NewReminder {
  const _NewReminder({
    required this.title,
    required this.date,
    required this.time,
    required this.recurrence,
    required this.weekdays,
  });

  final String title;
  final DateTime date;
  final TimeOfDay time;
  final String recurrence;
  final int weekdays;
}

class _NewReminderDialog extends StatefulWidget {
  const _NewReminderDialog({required this.timeZone});

  /// Resolves to the zone the reminder will be saved in, shown under the time.
  final Future<String> timeZone;

  @override
  State<_NewReminderDialog> createState() => _NewReminderDialogState();
}

class _NewReminderDialogState extends State<_NewReminderDialog> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  DateTime _date = DateTime.now().add(const Duration(days: 1));
  TimeOfDay _time = const TimeOfDay(hour: 9, minute: 0);
  String _recurrence = 'once';
  int _weekdays = 0;

  @override
  void dispose() {
    _title.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    if (_recurrence == 'weekly' && _weekdays == 0) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Choose at least one weekday.')),
      );
      return;
    }
    Navigator.pop(
      context,
      _NewReminder(
        title: _title.text.trim(),
        date: _date,
        time: _time,
        recurrence: _recurrence,
        weekdays: _weekdays,
      ),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Create a reminder'),
    content: Form(
      key: _formKey,
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            TextFormField(
              controller: _title,
              autofocus: true,
              maxLength: 300,
              decoration: const InputDecoration(labelText: 'Remind me about'),
              validator: (value) => value == null || value.trim().isEmpty
                  ? 'Enter a reminder.'
                  : null,
            ),
            const SizedBox(height: 10),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final option in const [
                  ('once', 'Once'),
                  ('daily', 'Daily'),
                  ('weekdays', 'Weekdays'),
                  ('weekly', 'Weekly'),
                ])
                  ChoiceChip(
                    key: Key('recurrence-${option.$1}'),
                    label: Text(option.$2),
                    selected: _recurrence == option.$1,
                    onSelected: (_) => setState(() => _recurrence = option.$1),
                  ),
              ],
            ),
            if (_recurrence == 'weekly') ...[
              const SizedBox(height: 10),
              Wrap(
                spacing: 6,
                runSpacing: 6,
                children: [
                  for (final day in weekdayNames)
                    FilterChip(
                      key: Key('weekday-${day.$1}'),
                      label: Text(day.$2),
                      selected: _weekdays & day.$1 != 0,
                      onSelected: (selected) => setState(() {
                        _weekdays = selected
                            ? _weekdays | day.$1
                            : _weekdays & ~day.$1;
                      }),
                    ),
                ],
              ),
            ],
            const SizedBox(height: 10),
            ListTile(
              tileColor: JarvisColors.of(context).surfaceMuted,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(JarvisRadii.md),
              ),
              leading: const Icon(PhosphorIconsRegular.calendarBlank),
              trailing: const Icon(PhosphorIconsRegular.caretDown),
              title: Text(
                MaterialLocalizations.of(context).formatFullDate(_date),
              ),
              onTap: () async {
                final value = await showDatePicker(
                  context: context,
                  initialDate: _date,
                  firstDate: DateTime.now(),
                  lastDate: DateTime.now().add(const Duration(days: 730)),
                );
                if (value != null && mounted) setState(() => _date = value);
              },
            ),
            const SizedBox(height: 8),
            ListTile(
              tileColor: JarvisColors.of(context).surfaceMuted,
              shape: RoundedRectangleBorder(
                borderRadius: BorderRadius.circular(JarvisRadii.md),
              ),
              leading: const Icon(PhosphorIconsRegular.clock),
              trailing: const Icon(PhosphorIconsRegular.caretDown),
              title: Text(_time.format(context)),
              onTap: () async {
                final value = await showTimePicker(
                  context: context,
                  initialTime: _time,
                );
                if (value != null && mounted) setState(() => _time = value);
              },
            ),
            FutureBuilder<String>(
              future: widget.timeZone,
              builder: (context, snapshot) => Padding(
                padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
                child: Text(
                  snapshot.hasData
                      ? 'Time zone: ${snapshot.data!.replaceAll('_', ' ')}'
                      : ' ',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: JarvisColors.of(context).muted,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _save, child: const Text('Save')),
    ],
  );
}
