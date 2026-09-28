part of 'condition_watches_screen.dart';

class _NewWatch {
  const _NewWatch({
    required this.title,
    required this.kind,
    required this.url,
    required this.jsonPath,
    required this.comparison,
    required this.threshold,
    required this.intervalMinutes,
    this.credentialProvider,
    this.latitude,
    this.longitude,
    this.radiusMeters,
    this.minutesBefore,
  });

  final String title;
  final String kind;
  final String url;
  final String jsonPath;
  final String comparison;
  final double threshold;
  final int intervalMinutes;
  final String? credentialProvider;
  final double? latitude;
  final double? longitude;
  final double? radiusMeters;
  final int? minutesBefore;
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
  final _provider = TextEditingController();
  final _latitude = TextEditingController();
  final _longitude = TextEditingController();
  final _radius = TextEditingController(text: '200');
  final _minutesBefore = TextEditingController(text: '30');
  String _kind = 'public_json';
  String _comparison = 'below';

  bool get _isJson =>
      _kind == 'public_json' || _kind == 'authenticated_json';

  @override
  void dispose() {
    _title.dispose();
    _url.dispose();
    _jsonPath.dispose();
    _threshold.dispose();
    _interval.dispose();
    _provider.dispose();
    _latitude.dispose();
    _longitude.dispose();
    _radius.dispose();
    _minutesBefore.dispose();
    super.dispose();
  }

  void _save() {
    if (!(_formKey.currentState?.validate() ?? false)) return;
    final parsedInterval = int.tryParse(_interval.text);
    if (parsedInterval == null ||
        parsedInterval < 5 ||
        parsedInterval > 1440) {
      return;
    }
    var comparison = _comparison;
    var threshold = double.tryParse(_threshold.text);
    String url = '';
    String jsonPath = '';
    String? provider;
    double? latitude;
    double? longitude;
    double? radius;
    int? minutesBefore;
    if (_isJson) {
      final parsed = parsePublicHttpsUrl(_url.text);
      if (parsed == null || threshold == null || !threshold.isFinite) return;
      url = parsed.toString();
      jsonPath = _jsonPath.text.trim();
      if (_kind == 'authenticated_json') {
        provider = _provider.text.trim();
        if (provider.isEmpty) return;
      }
    } else if (_kind == 'device_battery') {
      if (threshold == null || threshold < 0 || threshold > 100) return;
    } else if (_kind == 'device_location') {
      latitude = double.tryParse(_latitude.text);
      longitude = double.tryParse(_longitude.text);
      radius = double.tryParse(_radius.text);
      if (latitude == null ||
          longitude == null ||
          radius == null ||
          radius < 25 ||
          radius > 50000) {
        return;
      }
      threshold = radius;
    } else if (_kind == 'calendar') {
      minutesBefore = int.tryParse(_minutesBefore.text);
      if (minutesBefore == null ||
          minutesBefore < 5 ||
          minutesBefore > 1440) {
        return;
      }
      threshold = minutesBefore.toDouble();
      comparison = 'below';
    }
    if (threshold == null || !threshold.isFinite) return;
    Navigator.pop(
      context,
      _NewWatch(
        title: _title.text.trim(),
        kind: _kind,
        url: url,
        jsonPath: jsonPath,
        comparison: comparison,
        threshold: threshold,
        intervalMinutes: parsedInterval,
        credentialProvider: provider,
        latitude: latitude,
        longitude: longitude,
        radiusMeters: radius,
        minutesBefore: minutesBefore,
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
              DropdownButtonFormField<String>(
                initialValue: _kind,
                decoration: const InputDecoration(labelText: 'Watch type'),
                items: const [
                  DropdownMenuItem(
                    value: 'public_json',
                    child: Text('Public JSON HTTPS'),
                  ),
                  DropdownMenuItem(
                    value: 'authenticated_json',
                    child: Text('Authenticated JSON (stored token)'),
                  ),
                  DropdownMenuItem(
                    value: 'device_battery',
                    child: Text('This device’s battery'),
                  ),
                  DropdownMenuItem(
                    value: 'device_location',
                    child: Text('This device’s location'),
                  ),
                  DropdownMenuItem(
                    value: 'calendar',
                    child: Text('Next calendar event'),
                  ),
                ],
                onChanged: (value) =>
                    setState(() => _kind = value ?? 'public_json'),
              ),
              if (_isJson) ...[
                const SizedBox(height: 8),
                TextFormField(
                  controller: _url,
                  maxLength: 2048,
                  keyboardType: TextInputType.url,
                  decoration: const InputDecoration(
                    labelText: 'JSON HTTPS URL',
                    hintText: 'https://example.com/api/price',
                  ),
                  validator: (value) => parsePublicHttpsUrl(value) == null
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
              ],
              if (_kind == 'authenticated_json') ...[
                const SizedBox(height: 8),
                TextFormField(
                  controller: _provider,
                  decoration: const InputDecoration(
                    labelText: 'Credential provider slug',
                    hintText: 'home-assistant',
                  ),
                  validator: (value) => value == null || value.trim().isEmpty
                      ? 'Enter the stored credential provider.'
                      : null,
                ),
              ],
              if (_kind == 'device_location') ...[
                const SizedBox(height: 8),
                TextFormField(
                  controller: _latitude,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                    signed: true,
                  ),
                  decoration: const InputDecoration(labelText: 'Latitude'),
                  validator: (value) {
                    final number = double.tryParse(value ?? '');
                    return number == null || number < -90 || number > 90
                        ? 'Enter a latitude between -90 and 90.'
                        : null;
                  },
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _longitude,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                    signed: true,
                  ),
                  decoration: const InputDecoration(labelText: 'Longitude'),
                  validator: (value) {
                    final number = double.tryParse(value ?? '');
                    return number == null || number < -180 || number > 180
                        ? 'Enter a longitude between -180 and 180.'
                        : null;
                  },
                ),
                const SizedBox(height: 8),
                TextFormField(
                  controller: _radius,
                  keyboardType: const TextInputType.numberWithOptions(
                    decimal: true,
                  ),
                  decoration: const InputDecoration(
                    labelText: 'Radius (meters, 25–50,000)',
                  ),
                  validator: (value) {
                    final number = double.tryParse(value ?? '');
                    return number == null || number < 25 || number > 50000
                        ? 'Choose a radius between 25 and 50,000 meters.'
                        : null;
                  },
                ),
              ],
              if (_kind == 'calendar') ...[
                const SizedBox(height: 8),
                TextFormField(
                  controller: _minutesBefore,
                  keyboardType: TextInputType.number,
                  decoration: const InputDecoration(
                    labelText: 'Minutes before the next event',
                  ),
                  validator: (value) {
                    final minutes = int.tryParse(value ?? '');
                    return minutes == null || minutes < 5 || minutes > 1440
                        ? 'Choose between 5 and 1,440 minutes.'
                        : null;
                  },
                ),
              ],
              if (_kind != 'calendar' && _kind != 'device_location') ...[
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
                  decoration: InputDecoration(
                    labelText: _kind == 'device_battery'
                        ? 'Battery percent (0–100)'
                        : 'Threshold',
                  ),
                  validator: (value) {
                    final number = double.tryParse(value ?? '');
                    if (number == null || !number.isFinite) {
                      return 'Enter a finite number.';
                    }
                    if (_kind == 'device_battery' &&
                        (number < 0 || number > 100)) {
                      return 'Battery percent must be between 0 and 100.';
                    }
                    return null;
                  },
                ),
              ],
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
                switch (_kind) {
                  'authenticated_json' =>
                    'Jarvis polls the HTTPS JSON URL with the stored provider token as Authorization. Never put secrets in the URL.',
                  'device_battery' =>
                    'Jarvis uses the latest battery snapshot from this app. Open Jarvis on the phone so the reading stays fresh.',
                  'device_location' =>
                    'Jarvis measures distance from this phone’s last location to the point you set. Alert when the distance is at or below the radius.',
                  'calendar' =>
                    'Jarvis reads the connected ICS calendar pack and alerts when the next event is within the chosen number of minutes.',
                  _ =>
                    'Jarvis checks the public endpoint on a timer and stops after the threshold is reached. URLs requiring sign-in or containing credentials are not supported.',
                },
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
