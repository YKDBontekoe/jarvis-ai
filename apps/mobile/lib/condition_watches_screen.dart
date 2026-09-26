import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

class ConditionWatchesScreen extends StatefulWidget {
  const ConditionWatchesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ConditionWatchesScreen> createState() => _ConditionWatchesScreenState();
}

class _ConditionWatchesScreenState extends State<ConditionWatchesScreen> {
  List<Map<String, dynamic>> _watches = [];
  bool _loading = true;
  bool _creating = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>('/api/v1/watches');
      if (mounted) {
        setState(
          () => _watches = (response.data ?? []).cast<Map<String, dynamic>>(),
        );
      }
    } on DioException {
      if (mounted) setState(() => _error = 'Jarvis could not load watches.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _createWatch() async {
    final title = TextEditingController();
    final url = TextEditingController();
    final jsonPath = TextEditingController();
    final threshold = TextEditingController();
    final interval = TextEditingController(text: '15');
    final formKey = GlobalKey<FormState>();
    var comparison = 'below';
    final create = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('Create a condition watch'),
          content: SizedBox(
            width: 520,
            child: SingleChildScrollView(
              child: Form(
                key: formKey,
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    TextFormField(
                      controller: title,
                      autofocus: true,
                      maxLength: 200,
                      decoration: const InputDecoration(
                        labelText: 'What are we watching?',
                      ),
                      validator: (value) =>
                          value == null || value.trim().isEmpty
                          ? 'Enter a short name.'
                          : null,
                    ),
                    const SizedBox(height: 8),
                    TextFormField(
                      controller: url,
                      maxLength: 2048,
                      keyboardType: TextInputType.url,
                      decoration: const InputDecoration(
                        labelText: 'Public JSON HTTPS URL',
                        hintText: 'https://example.com/api/price',
                      ),
                      validator: (value) =>
                          value == null || value.trim().isEmpty
                          ? 'Enter a public HTTPS URL.'
                          : null,
                    ),
                    const SizedBox(height: 8),
                    TextFormField(
                      controller: jsonPath,
                      decoration: const InputDecoration(
                        labelText: 'Numeric JSON property path',
                        hintText: 'data.price',
                      ),
                      validator: (value) =>
                          value == null || value.trim().isEmpty
                          ? 'Enter a property path.'
                          : null,
                    ),
                    const SizedBox(height: 8),
                    DropdownButtonFormField<String>(
                      initialValue: comparison,
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
                          setDialogState(() => comparison = value ?? 'below'),
                    ),
                    const SizedBox(height: 8),
                    TextFormField(
                      controller: threshold,
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
                      controller: interval,
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
              onPressed: () => Navigator.pop(dialogContext, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                if (formKey.currentState?.validate() ?? false) {
                  Navigator.pop(dialogContext, true);
                }
              },
              child: const Text('Start watching'),
            ),
          ],
        ),
      ),
    );
    if (create != true) {
      for (final controller in [title, url, jsonPath, threshold, interval]) {
        controller.dispose();
      }
      return;
    }

    setState(() => _creating = true);
    try {
      await widget.http.post<void>(
        '/api/v1/watches',
        data: {
          'title': title.text.trim(),
          'url': url.text.trim(),
          'jsonPath': jsonPath.text.trim(),
          'comparison': comparison,
          'threshold': double.parse(threshold.text),
          'intervalMinutes': int.parse(interval.text),
        },
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The durable watch service is unavailable. Try again shortly.'
            : 'Jarvis could not start that watch. Check that the URL is public HTTPS and returns JSON.';
        _showError(message);
      }
    } finally {
      for (final controller in [title, url, jsonPath, threshold, interval]) {
        controller.dispose();
      }
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _cancel(Map<String, dynamic> watch) async {
    try {
      await widget.http.delete<void>('/api/v1/watches/${watch['id']}');
      await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not stop that watch.');
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  String _condition(Map<String, dynamic> watch) {
    final comparison = watch['comparison'] == 'below' ? '≤' : '≥';
    return '${watch['jsonPath']} $comparison ${watch['threshold']}';
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Condition watches'),
      actions: [
        IconButton(
          tooltip: 'Refresh watches',
          onPressed: _loading ? null : _load,
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: _creating ? null : _createWatch,
      icon: _creating
          ? const SizedBox(
              width: 18,
              height: 18,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Icon(Icons.add_alert_outlined),
      label: const Text('New watch'),
    ),
    body: _loading && _watches.isEmpty
        ? const Center(child: CircularProgressIndicator())
        : _error != null && _watches.isEmpty
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!),
                const SizedBox(height: 12),
                OutlinedButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : _watches.isEmpty
        ? const Center(
            child: Text(
              'No watches yet. Set a threshold and Jarvis will keep an eye on it.',
            ),
          )
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(12, 12, 12, 96),
              itemCount: _watches.length,
              itemBuilder: (context, index) {
                final watch = _watches[index];
                final active = watch['status'] == 'active';
                final lastValue = watch['lastValue'];
                return Card(
                  child: ListTile(
                    leading: Icon(
                      active
                          ? Icons.visibility_outlined
                          : Icons.check_circle_outline,
                    ),
                    title: Text(watch['title'] as String? ?? 'Condition watch'),
                    subtitle: Padding(
                      padding: const EdgeInsets.only(top: 6),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(_condition(watch)),
                          const SizedBox(height: 3),
                          Text(
                            active
                                ? 'Checks every ${watch['intervalMinutes']} min${lastValue == null ? '' : ' · latest $lastValue'}'
                                : (watch['status'] as String? ?? '').replaceAll(
                                    '_',
                                    ' ',
                                  ),
                          ),
                        ],
                      ),
                    ),
                    trailing: active
                        ? IconButton(
                            tooltip: 'Stop watching',
                            onPressed: () => _cancel(watch),
                            icon: const Icon(Icons.stop_circle_outlined),
                          )
                        : null,
                  ),
                );
              },
            ),
          ),
  );
}
