import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'json_maps.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

class AutomationsScreen extends StatefulWidget {
  const AutomationsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<AutomationsScreen> createState() => _AutomationsScreenState();
}

class _AutomationsScreenState extends State<AutomationsScreen> {
  List<Map<String, dynamic>> _rules = [];
  bool _loading = true;
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
      final response = await widget.http.get<dynamic>('/api/v1/automations');
      setState(() => _rules = jsonMaps(response.data));
    } on DioException {
      setState(() => _error = 'Jarvis could not load automations.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _createSimple() async {
    final created = await showDialog<_SimpleAutomation>(
      context: context,
      builder: (_) => const _SimpleAutomationDialog(),
    );
    if (created == null) return;
    try {
      await widget.http.post<void>(
        '/api/v1/automations',
        data: {
          'name': created.name,
          'definition': {
            'schemaVersion': 1,
            'trigger': {
              'kind': 'schedule',
              'localTime': created.localTime,
              'timeZoneId': created.timeZoneId,
            },
            'actions': [
              {
                'kind': 'notification',
                'title': created.notificationTitle,
                'body': created.notificationBody,
              },
            ],
            'limits': {'cooldownMinutes': 5, 'maxActionsPerRun': 3},
          },
        },
      );
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(_problem(error) ?? 'Could not save automation.')),
      );
    }
  }

  Future<void> _enable(String id) async {
    await widget.http.post<void>('/api/v1/automations/$id/enable');
    await _load();
  }

  Future<void> _disable(String id) async {
    await widget.http.post<void>('/api/v1/automations/$id/disable');
    await _load();
  }

  Future<void> _testRun(String id) async {
    await widget.http.post<void>('/api/v1/automations/$id/test-run');
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(content: Text('Test run started. Check run history when it finishes.')),
    );
  }

  Future<void> _showRuns(String id, String name) async {
    final response = await widget.http.get<dynamic>('/api/v1/automations/$id/runs');
    final runs = jsonMaps(response.data);
    if (!mounted) return;
    await showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (_) => _RunHistorySheet(title: name, runs: runs),
    );
  }

  String? _problem(DioException error) {
    final data = error.response?.data;
    if (data is Map && data['errors'] is Map) {
      final errors = data['errors'] as Map;
      return errors.values.first?.toString();
    }
    return null;
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('Automations')),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: _loading ? null : _createSimple,
        icon: const Icon(PhosphorIconsRegular.plus),
        label: const Text('New'),
      ),
      body: JarvisScrollBody(
        child: _loading
            ? const Center(child: CircularProgressIndicator())
            : _error != null
            ? Center(child: Text(_error!, style: Theme.of(context).textTheme.bodyLarge))
            : _rules.isEmpty
            ? const Center(child: Text('No automations yet. Create one to chain triggers and actions.'))
            : ListView.separated(
                itemCount: _rules.length,
                separatorBuilder: (_, __) => const SizedBox(height: 8),
                itemBuilder: (context, index) {
                  final rule = _rules[index];
                  final id = asJsonString(rule['id']) ?? '';
                  final name = asJsonString(rule['name']) ?? 'Automation';
                  final status = asJsonString(rule['status']) ?? 'draft';
                  final definition = rule['definition'];
                  final trigger = definition is Map ? definition['kind'] ?? definition['trigger'] : null;
                  final triggerKind = trigger is Map
                      ? asJsonString(trigger['kind'])
                      : asJsonString(definition?['trigger']?['kind']);
                  return Card(
                    child: ListTile(
                      title: Text(name),
                      subtitle: Text('${status.toUpperCase()} · trigger ${triggerKind ?? 'unknown'}'),
                      trailing: PopupMenuButton<String>(
                        onSelected: (value) async {
                          switch (value) {
                            case 'enable':
                              await _enable(id);
                            case 'disable':
                              await _disable(id);
                            case 'test':
                              await _testRun(id);
                            case 'runs':
                              await _showRuns(id, name);
                          }
                        },
                        itemBuilder: (_) => [
                          if (status != 'enabled')
                            const PopupMenuItem(value: 'enable', child: Text('Enable')),
                          if (status == 'enabled')
                            const PopupMenuItem(value: 'disable', child: Text('Disable')),
                          const PopupMenuItem(value: 'test', child: Text('Test run')),
                          const PopupMenuItem(value: 'runs', child: Text('Run history')),
                        ],
                      ),
                    ),
                  );
                },
              ),
      ),
    );
  }
}

class _RunHistorySheet extends StatelessWidget {
  const _RunHistorySheet({required this.title, required this.runs});

  final String title;
  final List<Map<String, dynamic>> runs;

  @override
  Widget build(BuildContext context) {
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Runs · $title', style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 12),
            Expanded(
              child: runs.isEmpty
                  ? const Center(child: Text('No runs yet.'))
                  : ListView.builder(
                      itemCount: runs.length,
                      itemBuilder: (_, index) {
                        final run = runs[index];
                        final status = asJsonString(run['status']) ?? '';
                        final reason = asJsonString(run['triggerReason']) ?? '';
                        final started = asJsonString(run['startedAt']) ?? '';
                        return ListTile(
                          dense: true,
                          title: Text('$status · $reason'),
                          subtitle: Text(started),
                        );
                      },
                    ),
            ),
          ],
        ),
      ),
    );
  }
}

class _SimpleAutomation {
  const _SimpleAutomation({
    required this.name,
    required this.localTime,
    required this.timeZoneId,
    required this.notificationTitle,
    required this.notificationBody,
  });

  final String name;
  final String localTime;
  final String timeZoneId;
  final String notificationTitle;
  final String notificationBody;
}

class _SimpleAutomationDialog extends StatefulWidget {
  const _SimpleAutomationDialog();

  @override
  State<_SimpleAutomationDialog> createState() => _SimpleAutomationDialogState();
}

class _SimpleAutomationDialogState extends State<_SimpleAutomationDialog> {
  final _name = TextEditingController();
  final _title = TextEditingController(text: 'Automation');
  final _body = TextEditingController();
  final _time = TextEditingController(text: '09:00:00');
  final _zone = TextEditingController(text: 'UTC');

  @override
  void dispose() {
    _name.dispose();
    _title.dispose();
    _body.dispose();
    _time.dispose();
    _zone.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    return AlertDialog(
      title: const Text('Scheduled notification'),
      content: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            TextField(controller: _name, decoration: const InputDecoration(labelText: 'Name')),
            TextField(controller: _time, decoration: const InputDecoration(labelText: 'Local time (HH:mm:ss)')),
            TextField(controller: _zone, decoration: const InputDecoration(labelText: 'IANA time zone')),
            TextField(controller: _title, decoration: const InputDecoration(labelText: 'Notification title')),
            TextField(controller: _body, decoration: const InputDecoration(labelText: 'Notification body')),
          ],
        ),
      ),
      actions: [
        TextButton(onPressed: () => Navigator.pop(context), child: const Text('Cancel')),
        FilledButton(
          onPressed: () {
            if (_name.text.trim().isEmpty || _body.text.trim().isEmpty) return;
            Navigator.pop(
              context,
              _SimpleAutomation(
                name: _name.text.trim(),
                localTime: _time.text.trim(),
                timeZoneId: _zone.text.trim(),
                notificationTitle: _title.text.trim(),
                notificationBody: _body.text.trim(),
              ),
            );
          },
          child: const Text('Save draft'),
        ),
      ],
    );
  }
}
