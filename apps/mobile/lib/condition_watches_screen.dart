import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

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
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>('/api/v1/watches');
      if (mounted && revision == _requestRevision) {
        setState(
          () => _watches = jsonMaps(response.data),
        );
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load watches.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
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
    if (!mounted) {
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
    final title = asJsonString(watch['title']) ?? 'Condition watch';
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Stop watching?',
      message: '“$title” will no longer be checked.',
      cancelLabel: 'Keep watch',
      confirmLabel: 'Stop watching',
      destructive: true,
      icon: PhosphorIconsRegular.stopCircle,
    );
    if (!confirmed) return;
    if (!mounted) return;
    try {
      await widget.http.delete<void>('/api/v1/watches/${watch['id']}');
      if (mounted) await _load();
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
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'New watch',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createWatch,
          busy: _creating,
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _watches.isEmpty,
      onRetry: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.pulse,
        title: 'No watches yet',
        message:
            'No watches yet. Set a threshold and Jarvis will keep an eye on it.',
      ),
      child: RefreshIndicator(
            onRefresh: _load,
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              itemCount: _watches.length,
              itemBuilder: (context, index) =>
                  ContentWidth(child: _watchCard(_watches[index])),
            ),
          ),
    ),
  );

  Widget _watchCard(Map<String, dynamic> watch) {
    final status = asJsonString(watch['status']) ?? '';
    final active = status == 'active';
    final lastValue = watch['lastValue'];
    final style = statusStyle(status);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 6, 14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          IconBadge(
            icon: active
                ? PhosphorIconsRegular.pulse
                : PhosphorIconsRegular.checkCircle,
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        asJsonString(watch['title']) ?? 'Condition watch',
                        style: Theme.of(
                          context,
                        ).textTheme.titleSmall?.copyWith(fontSize: 15),
                      ),
                    ),
                    StatusPill(label: style.label, color: style.color),
                  ],
                ),
                const SizedBox(height: 10),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 10,
                    vertical: 6,
                  ),
                  decoration: BoxDecoration(
                    color: JarvisColors.canvas,
                    borderRadius: BorderRadius.circular(JarvisRadii.sm),
                    border: Border.all(color: JarvisColors.outline),
                  ),
                  child: Text(
                    _condition(watch),
                    style: const TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 13,
                      color: JarvisColors.ink,
                    ),
                  ),
                ),
                if (active) ...[
                  const SizedBox(height: 10),
                  Wrap(
                    spacing: 14,
                    runSpacing: 4,
                    children: [
                      _meta(
                        PhosphorIconsRegular.timer,
                        'Checks every ${watch['intervalMinutes']} min',
                      ),
                      if (lastValue != null)
                        _meta(
                          PhosphorIconsRegular.chartLine,
                          'latest $lastValue',
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
          if (active)
            IconButton(
              tooltip: 'Stop watching',
              onPressed: () => _cancel(watch),
              icon: const Icon(PhosphorIconsRegular.stopCircle, size: 22),
            ),
        ],
      ),
    );
  }

  Widget _meta(IconData icon, String text) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Icon(icon, size: 15, color: JarvisColors.muted),
      const SizedBox(width: 5),
      Text(text, style: Theme.of(context).textTheme.bodySmall),
    ],
  );
}
