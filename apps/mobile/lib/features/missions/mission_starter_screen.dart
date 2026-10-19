import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Practical starting points for a mission. Picking one asks only what changes
/// the plan, then the goal goes back to the caller. Nothing is planned here.
class MissionStarterScreen extends StatefulWidget {
  const MissionStarterScreen({required this.http, super.key});

  final Dio http;

  @override
  State<MissionStarterScreen> createState() => _MissionStarterScreenState();
}

class _MissionStarterScreenState extends State<MissionStarterScreen> {
  List<Map<String, dynamic>> _templates = const [];
  bool _loading = true;
  String? _error;

  static const _icons = {
    'decision': PhosphorIconsRegular.scan,
    'brief': PhosphorIconsRegular.fileText,
    'compare': PhosphorIconsRegular.table,
    'meeting': PhosphorIconsRegular.calendarCheck,
  };

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/missions/templates',
      );
      if (!mounted) return;
      setState(() {
        _templates = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load the starters.';
      });
    }
  }

  Future<void> _pick(Map<String, dynamic> template) async {
    final composed = await Navigator.of(context).push<Map<String, dynamic>>(
      MaterialPageRoute(
        builder: (_) => _StarterForm(http: widget.http, template: template),
      ),
    );
    if (composed != null && mounted) Navigator.of(context).pop(composed);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('New mission')),
    body: _loading
        ? const SkeletonList()
        : _error != null
        ? ErrorState(message: _error!, onRetry: _load)
        : ListView(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            children: [
              ContentWidth(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const GroupLabel('Start from'),
                    for (var i = 0; i < _templates.length; i++)
                      FeatureRow(
                        key: Key('starter-${_templates[i]['id']}'),
                        index: i,
                        icon:
                            _icons[_templates[i]['id']] ??
                            PhosphorIconsRegular.flowArrow,
                        title: asJsonString(_templates[i]['title']) ?? '',
                        subtitle: asJsonString(_templates[i]['purpose']),
                        trailing: const Icon(Icons.chevron_right, size: 20),
                        onTap: () => unawaited(_pick(_templates[i])),
                      ),
                    const GroupLabel('Or'),
                    FeatureRow(
                      key: const Key('starter-own'),
                      index: _templates.length,
                      icon: PhosphorIconsRegular.pencilSimple,
                      title: 'Describe your own',
                      subtitle: 'Write the job in your own words.',
                      trailing: const Icon(Icons.chevron_right, size: 20),
                      onTap: () =>
                          Navigator.of(context)
                              .pop(<String, dynamic>{'own': true}),
                    ),
                  ],
                ),
              ),
            ],
          ),
  );
}

class _StarterForm extends StatefulWidget {
  const _StarterForm({required this.http, required this.template});

  final Dio http;
  final Map<String, dynamic> template;

  @override
  State<_StarterForm> createState() => _StarterFormState();
}

class _StarterFormState extends State<_StarterForm> {
  final Map<String, TextEditingController> _fields = {};
  bool _busy = false;
  String? _problem;

  List<Map<String, dynamic>> get _questions =>
      jsonMaps(widget.template['questions']);

  @override
  void dispose() {
    for (final controller in _fields.values) {
      controller.dispose();
    }
    super.dispose();
  }

  Future<void> _compose() async {
    setState(() {
      _busy = true;
      _problem = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/missions/templates/${widget.template['id']}/compose',
        data: {
          'answers': {
            for (final entry in _fields.entries)
              entry.key: entry.value.text.trim(),
          },
        },
      );
      if (mounted) Navigator.of(context).pop(jsonObject(response.data));
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _problem =
              firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(asJsonString(widget.template['title']) ?? 'Mission'),
    ),
    body: ListView(
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
      children: [
        ContentWidth(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              FadeSlideIn(
                child: HeroCard(
                  label: 'You will get',
                  title: asJsonString(widget.template['deliverable']) ?? '',
                  subtitle: 'You review the plan before anything starts. Nothing is sent or changed outside Jarvis.',
                ),
              ),
              for (final question in _questions) ...[
                Padding(
                  padding: const EdgeInsets.fromLTRB(2, 18, 0, 6),
                  child: Text(
                    '${asJsonString(question['label']) ?? ''}${question['required'] == true ? '' : ' (optional)'}',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                ),
                TextField(
                  key: Key('starter-field-${question['key']}'),
                  controller: _fields.putIfAbsent(
                    asJsonString(question['key']) ?? '',
                    TextEditingController.new,
                  ),
                  maxLines: null,
                  decoration: InputDecoration(
                    hintText: asJsonString(question['hint']),
                  ),
                ),
              ],
              if (_problem != null)
                InlineNotice(
                  key: const Key('starter-problem'),
                  tone: NoticeTone.warning,
                  message: _problem!,
                  margin: const EdgeInsets.only(top: 14),
                ),
              const SizedBox(height: 18),
              Align(
                alignment: Alignment.centerLeft,
                child: PillAction(
                  key: const Key('starter-compose'),
                  primary: true,
                  icon: PhosphorIconsRegular.sparkle,
                  label: 'Plan it',
                  onPressed: _busy ? null : () => unawaited(_compose()),
                ),
              ),
            ],
          ),
        ),
      ],
    ),
  );
}
