part of 'condition_watches_screen.dart';

class _NewWatch {
  const _NewWatch({
    required this.title,
    required this.url,
    required this.jsonPath,
    required this.comparison,
    required this.threshold,
    required this.intervalMinutes,
  });

  final String title;
  final String url;
  final String jsonPath;
  final String comparison;
  final double threshold;
  final int intervalMinutes;
}

class _NewWatchDialog extends StatefulWidget {
  const _NewWatchDialog();

  @override
  State<_NewWatchDialog> createState() => _NewWatchDialogState();
}

class _NewWatchDialogState extends State<_NewWatchDialog> {
  final _formKey = GlobalKey<FormState>();
  final _title = TextEditingController();
  final _url = TextEditingController();
  final _jsonPath = TextEditingController();
  final _threshold = TextEditingController();
  final _interval = TextEditingController(text: '15');
  String _comparison = 'below';

  @override
  void dispose() {
    _title.dispose();
    _url.dispose();
    _jsonPath.dispose();
    _threshold.dispose();
    _interval.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final parsedThreshold = double.tryParse(_threshold.text);
    final parsedInterval = int.tryParse(_interval.text);
    if (parsedThreshold == null ||
        !parsedThreshold.isFinite ||
        parsedInterval == null ||
        parsedInterval < 5 ||
        parsedInterval > 1440) {
      return;
    }
    Navigator.pop(
      context,
      _NewWatch(
        title: _title.text.trim(),
        url: _url.text.trim(),
        jsonPath: _jsonPath.text.trim(),
        comparison: _comparison,
        threshold: parsedThreshold,
        intervalMinutes: parsedInterval,
      ),
    );
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Create a condition watch'),
    content: SizedBox(
      width: 520,
      child: SingleChildScrollView(
        child: Form(
          key: _formKey,
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextFormField(
                controller: _title,
                autofocus: true,
                maxLength: 200,
                decoration: const InputDecoration(
                  labelText: 'What are we watching?',
                ),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Enter a short name.'
                    : null,
              ),
              const SizedBox(height: 8),
              TextFormField(
                controller: _url,
                maxLength: 2048,
                keyboardType: TextInputType.url,
                decoration: const InputDecoration(
                  labelText: 'Public JSON HTTPS URL',
                  hintText: 'https://example.com/api/price',
                ),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Enter a public HTTPS URL.'
                    : null,
              ),
              const SizedBox(height: 8),
              TextFormField(
                controller: _jsonPath,
                decoration: const InputDecoration(
                  labelText: 'Numeric JSON property path',
                  hintText: 'data.price',
                ),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Enter a property path.'
                    : null,
              ),
              const SizedBox(height: 8),
              DropdownButtonFormField<String>(
                initialValue: _comparison,
                decoration: const InputDecoration(
                  labelText: 'Alert when value is',
                ),
                items: const [
                  DropdownMenuItem(
                    value: 'below',
                    child: Text('at or below threshold'),
                  ),
                  DropdownMenuItem(
                    value: 'above',
                    child: Text('at or above threshold'),
                  ),
                ],
                onChanged: (value) =>
                    setState(() => _comparison = value ?? 'below'),
              ),
              const SizedBox(height: 8),
              TextFormField(
                controller: _threshold,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                  signed: true,
                ),
                decoration: const InputDecoration(labelText: 'Threshold'),
                validator: (value) {
                  final number = double.tryParse(value ?? '');
                  return number == null || !number.isFinite
                      ? 'Enter a finite number.'
                      : null;
                },
              ),
              const SizedBox(height: 8),
              TextFormField(
                controller: _interval,
                keyboardType: TextInputType.number,
                decoration: const InputDecoration(
                  labelText: 'Check interval (minutes, 5–1440)',
                ),
                validator: (value) {
                  final minutes = int.tryParse(value ?? '');
                  return minutes == null || minutes < 5 || minutes > 1440
                      ? 'Choose between 5 and 1,440 minutes.'
                      : null;
                },
              ),
              const SizedBox(height: 12),
              Text(
                'Jarvis checks the public endpoint on a timer and stops after the threshold is reached. URLs requiring sign-in or containing credentials are not supported.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(onPressed: _save, child: const Text('Start watching')),
    ],
  );
}
