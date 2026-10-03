import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// One step of a simulated run, as the server describes it.
class SimulatedStepData {
  const SimulatedStepData({
    required this.label,
    required this.willRun,
    required this.needsApproval,
    this.reason,
    this.title,
    this.body,
  });

  final String label;
  final bool willRun;
  final bool needsApproval;
  final String? reason;
  final String? title;
  final String? body;

  static SimulatedStepData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final label = map == null ? null : jsonString(map, 'label');
    if (map == null || label == null) return null;
    return SimulatedStepData(
      label: label,
      willRun: asJsonBool(map['willRun']),
      needsApproval: asJsonBool(map['needsApproval']),
      reason: jsonString(map, 'skippedReason'),
      title: jsonString(map, 'title'),
      body: jsonString(map, 'body'),
    );
  }
}

class SimulationData {
  const SimulationData({
    required this.trigger,
    required this.steps,
    this.note,
  });

  final String trigger;
  final String? note;
  final List<SimulatedStepData> steps;

  static SimulationData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final trigger = map == null ? null : jsonString(map, 'trigger');
    if (map == null || trigger == null) return null;
    return SimulationData(
      trigger: trigger,
      note: jsonString(map, 'triggerNote'),
      steps: [
        for (final step in jsonMaps(map['steps']))
          ?SimulatedStepData.fromJson(step),
      ],
    );
  }
}

/// Templates, webhooks, and a "what would happen" preview for automations.
class AutomationStudioScreen extends StatefulWidget {
  const AutomationStudioScreen({required this.http, super.key});

  final Dio http;

  @override
  State<AutomationStudioScreen> createState() => _AutomationStudioScreenState();
}

class _AutomationStudioScreenState extends State<AutomationStudioScreen> {
  List<Map<String, dynamic>> _templates = const [];
  List<Map<String, dynamic>> _webhooks = const [];
  List<Map<String, dynamic>> _rules = const [];
  bool _loading = true;
  String? _error;

  String? _ruleId;
  final _title = TextEditingController();
  final _detail = TextEditingController();
  SimulationData? _simulation;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _title.dispose();
    _detail.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final templates = await widget.http.get<dynamic>(
        '/api/v1/automations/templates',
      );
      final webhooks = await widget.http.get<dynamic>(
        '/api/v1/automations/webhooks',
      );
      final rules = await widget.http.get<dynamic>('/api/v1/automations');
      if (!mounted) return;
      setState(() {
        _templates = jsonMaps(templates.data);
        _webhooks = jsonMaps(webhooks.data);
        _rules = jsonMaps(rules.data);
        _ruleId ??= _rules.isEmpty ? null : jsonId(_rules.first);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load the studio.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _useTemplate(String id) async {
    try {
      await widget.http.post<dynamic>(
        '/api/v1/automations/templates/$id/create',
      );
      _toast('Draft created. Review it in Automations and switch it on.');
      await _load();
    } on DioException catch (error) {
      _toast(firstProblemMessage(error.response?.data) ?? 'That did not work.');
    }
  }

  Future<void> _createWebhook() async {
    final name = await showDialog<String>(
      context: context,
      builder: (_) => const _NameDialog(),
    );
    if (name == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/automations/webhooks',
        data: {'name': name},
      );
      final url = asJsonString(jsonObject(response.data)?['url']);
      await _load();
      if (!mounted || url == null) return;
      await showDialog<void>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Your webhook URL'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const Text(
                'Copy it now: it is shown only once. Send a POST with JSON '
                '(title, description) or plain text to start event automations.',
              ),
              const SizedBox(height: 12),
              SelectableText(url, key: const Key('webhook-url')),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => unawaited(Clipboard.setData(ClipboardData(text: url))),
              child: const Text('Copy'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context),
              child: const Text('Done'),
            ),
          ],
        ),
      );
    } on DioException catch (error) {
      _toast(firstProblemMessage(error.response?.data) ?? 'That did not work.');
    }
  }

  Future<void> _deleteWebhook(String id) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete this webhook?',
      message: 'Anything still calling it will get an error.',
      confirmLabel: 'Delete',
      destructive: true,
    );
    if (!confirmed) return;
    try {
      await widget.http.delete<dynamic>('/api/v1/automations/webhooks/$id');
      await _load();
    } on DioException {
      _toast('Could not delete the webhook.');
    }
  }

  Future<void> _simulate() async {
    final id = _ruleId;
    if (id == null) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/automations/$id/simulate',
        data: {'title': _title.text.trim(), 'detail': _detail.text.trim()},
      );
      if (!mounted) return;
      setState(() => _simulation = SimulationData.fromJson(response.data));
    } on DioException catch (error) {
      _toast(firstProblemMessage(error.response?.data) ?? 'That did not work.');
    }
  }

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 3,
    child: Scaffold(
      appBar: AppBar(
        title: const Text('Automation studio'),
        bottom: const TabBar(
          tabs: [
            Tab(key: Key('studio-tab-templates'), text: 'Templates'),
            Tab(key: Key('studio-tab-webhooks'), text: 'Webhooks'),
            Tab(key: Key('studio-tab-try'), text: 'Try it'),
          ],
        ),
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: () => unawaited(_load()))
          : ContentWidth(
              child: TabBarView(
                children: [_templatesTab(), _webhooksTab(), _tryTab()],
              ),
            ),
    ),
  );

  Widget _templatesTab() {
    final colors = JarvisColors.of(context);
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
      children: [
        for (final template in _templates)
          SurfaceCard(
            key: Key('template-${jsonId(template)}'),
            margin: const EdgeInsets.only(bottom: 10),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  asJsonString(template['title']) ?? '',
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 2),
                Text(
                  asJsonString(template['description']) ?? '',
                  style: TextStyle(color: colors.inkSoft),
                ),
                const SizedBox(height: 6),
                Text(
                  'Starts: ${asJsonString(template['trigger']) ?? ''}'
                  '${asJsonBool(template['needsApproval']) ? ' · asks for approval' : ''}',
                  style: TextStyle(color: colors.muted, fontSize: 12.5),
                ),
                Align(
                  alignment: Alignment.centerRight,
                  child: TextButton(
                    key: Key('template-use-${jsonId(template)}'),
                    onPressed: () =>
                        unawaited(_useTemplate(jsonId(template) ?? '')),
                    child: const Text('Use this'),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }

  Widget _webhooksTab() => ListView(
    padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
    children: [
      Align(
        alignment: Alignment.centerLeft,
        child: FilledButton.icon(
          key: const Key('webhook-create'),
          onPressed: () => unawaited(_createWebhook()),
          icon: const Icon(PhosphorIconsRegular.plus, size: 18),
          label: const Text('New webhook'),
        ),
      ),
      const SizedBox(height: 12),
      if (_webhooks.isEmpty)
        Text(
          'A webhook lets another service (CI, a form, a smart button) start '
          'an automation whose trigger is "a webhook is called".',
          style: TextStyle(color: JarvisColors.of(context).inkSoft),
        ),
      for (final hook in _webhooks)
        SurfaceCard(
          margin: const EdgeInsets.only(bottom: 10),
          child: Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.webhooksLogo),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      asJsonString(hook['name']) ?? 'Webhook',
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                    Text(
                      '…${asJsonString(hook['tokenHint']) ?? ''} · '
                      'called ${asJsonInt(hook['useCount'])} times',
                      style: TextStyle(
                        color: JarvisColors.of(context).muted,
                        fontSize: 12.5,
                      ),
                    ),
                  ],
                ),
              ),
              IconButton(
                key: Key('webhook-delete-${jsonId(hook)}'),
                tooltip: 'Delete',
                icon: const Icon(PhosphorIconsRegular.trash, size: 18),
                onPressed: () =>
                    unawaited(_deleteWebhook(jsonId(hook) ?? '')),
              ),
            ],
          ),
        ),
    ],
  );

  Widget _tryTab() {
    final colors = JarvisColors.of(context);
    if (_rules.isEmpty) {
      return const EmptyState(
        icon: PhosphorIconsRegular.lightning,
        title: 'No automations to try',
        message: 'Create one from a template, then preview it here.',
      );
    }
    final simulation = _simulation;
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 96),
      children: [
        DropdownButtonFormField<String>(
          key: const Key('studio-rule'),
          initialValue: _ruleId,
          decoration: const InputDecoration(labelText: 'Automation'),
          items: [
            for (final rule in _rules)
              DropdownMenuItem(
                value: jsonId(rule),
                child: Text(asJsonString(rule['name']) ?? 'Automation'),
              ),
          ],
          onChanged: (value) => setState(() {
            _ruleId = value;
            _simulation = null;
          }),
        ),
        TextField(
          key: const Key('studio-title'),
          controller: _title,
          decoration: const InputDecoration(
            labelText: 'Sample event title (for example a sender)',
          ),
        ),
        TextField(
          key: const Key('studio-detail'),
          controller: _detail,
          decoration: const InputDecoration(
            labelText: 'Sample event text (for example a message)',
          ),
        ),
        const SizedBox(height: 12),
        Align(
          alignment: Alignment.centerLeft,
          child: FilledButton.icon(
            key: const Key('studio-preview'),
            onPressed: () => unawaited(_simulate()),
            icon: const Icon(PhosphorIconsRegular.play, size: 18),
            label: const Text('What would happen?'),
          ),
        ),
        if (simulation != null) ...[
          const SizedBox(height: 16),
          Text('Starts: ${simulation.trigger}'),
          if (simulation.note != null)
            InlineNotice(
              message: simulation.note!,
              tone: NoticeTone.warning,
              margin: const EdgeInsets.only(top: 8),
            ),
          const SizedBox(height: 8),
          for (final step in simulation.steps)
            SurfaceCard(
              key: const Key('studio-step'),
              margin: const EdgeInsets.only(bottom: 8),
              padding: const EdgeInsets.all(12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(
                        step.willRun
                            ? PhosphorIconsRegular.checkCircle
                            : PhosphorIconsRegular.minusCircle,
                        size: 18,
                        color: step.willRun ? colors.success : colors.muted,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          step.label,
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                      ),
                      if (step.willRun && step.needsApproval)
                        const StatusPill(
                          label: 'Asks first',
                          color: Colors.orange,
                        ),
                    ],
                  ),
                  if (step.reason != null)
                    Text(
                      step.reason!,
                      style: TextStyle(color: colors.muted, fontSize: 13),
                    ),
                  if (step.willRun && step.title != null)
                    Text(step.title!, style: TextStyle(color: colors.inkSoft)),
                  if (step.willRun && step.body != null)
                    Text(
                      step.body!,
                      style: TextStyle(color: colors.inkSoft, fontSize: 13),
                    ),
                ],
              ),
            ),
        ],
      ],
    );
  }
}

class _NameDialog extends StatefulWidget {
  const _NameDialog();

  @override
  State<_NameDialog> createState() => _NameDialogState();
}

class _NameDialogState extends State<_NameDialog> {
  final _name = TextEditingController();

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Name this webhook'),
    content: TextField(
      key: const Key('webhook-name'),
      controller: _name,
      autofocus: true,
      decoration: const InputDecoration(labelText: 'For example: CI server'),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('webhook-name-save'),
        onPressed: () {
          final name = _name.text.trim();
          if (name.isNotEmpty) Navigator.pop(context, name);
        },
        child: const Text('Create'),
      ),
    ],
  );
}
