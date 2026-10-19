import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// One routine: each action with the device it would use and whether it can
/// run, can be taken out before running, and a simulated run with an honest
/// outcome per action. Security controls are listed but never run.
class RoutineScreen extends StatefulWidget {
  const RoutineScreen({required this.http, required this.routineId, super.key});

  final Dio http;
  final String routineId;

  @override
  State<RoutineScreen> createState() => _RoutineScreenState();
}

class _RoutineScreenState extends State<RoutineScreen> {
  Map<String, dynamic>? _plan;
  Map<String, dynamic>? _run;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  String get _path => '/api/v1/home-routines/${widget.routineId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('$_path/plan');
      if (!mounted) return;
      setState(() {
        _plan = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this routine.';
      });
    }
  }

  Future<void> _call(
    Future<Response<dynamic>> Function() request,
    void Function(Map<String, dynamic>? data) apply,
  ) async {
    setState(() => _busy = true);
    try {
      final response = await request();
      if (mounted) setState(() => apply(jsonObject(response.data)));
    } on DioException catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)
          ?..hideCurrentSnackBar()
          ..showSnackBar(
            SnackBar(
              content: Text(
                firstProblemMessage(error.response?.data) ??
                    'That did not work.',
              ),
            ),
          );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _toggle(String stepId, bool remove) {
    final removed = {
      for (final step in jsonMaps(_plan?['steps']))
        if (asJsonString(step['status']) == 'removed')
          asJsonString(jsonObject(step['step'])?['id']) ?? '',
    }..remove('');
    if (remove) {
      removed.add(stepId);
    } else {
      removed.remove(stepId);
    }
    return _call(
      () => widget.http.put<dynamic>(
        '$_path/choices',
        data: {'removed': removed.toList()},
      ),
      (data) => _plan = data ?? _plan,
    );
  }

  IconData _outcomeIcon(String outcome) => switch (outcome) {
    'simulated' => PhosphorIconsRegular.checkCircle,
    'read' => PhosphorIconsRegular.eye,
    'reversed' => PhosphorIconsRegular.arrowBendUpLeft,
    'not_run' => PhosphorIconsRegular.shieldCheck,
    _ => PhosphorIconsRegular.prohibit,
  };

  String _outcomeLabel(String outcome) => switch (outcome) {
    'simulated' => 'Simulated',
    'read' => 'Read',
    'reversed' => 'Undone',
    'not_run' => 'Not run',
    'skipped' => 'Taken out',
    _ => 'Unavailable',
  };

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final template = jsonObject(_plan?['template']) ?? const {};
    final steps = jsonMaps(_plan?['steps']);
    final runSteps = jsonMaps(_run?['steps']);
    final canUndo = runSteps.any(
      (s) =>
          asJsonString(s['outcome']) == 'simulated' &&
          asJsonBool(s['reversible']),
    );
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(asJsonString(template['title']) ?? 'Routine'),
      ),
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
                      HeroCard(
                        key: const Key('routine-summary'),
                        label: 'SIMULATION',
                        title: asJsonString(_plan?['summary']) ?? '',
                        subtitle: asJsonString(template['description']),
                        footer: PillAction(
                          key: const Key('routine-run'),
                          onDark: true,
                          icon: PhosphorIconsRegular.play,
                          label: 'Simulate',
                          onPressed: _busy
                              ? null
                              : () => unawaited(
                                  _call(
                                    () =>
                                        widget.http.post<dynamic>('$_path/run'),
                                    (data) => _run = data,
                                  ),
                                ),
                        ),
                      ),
                      const SizedBox(height: 4),
                      const GroupLabel('Actions'),
                      for (final (i, entry) in steps.indexed)
                        _step(entry, i, colors),
                      if (_run != null) ...[
                        const GroupLabel('Result'),
                        Padding(
                          padding: const EdgeInsets.fromLTRB(4, 0, 4, 8),
                          child: Text(
                            asJsonString(_run?['summary']) ?? '',
                            key: const Key('routine-result'),
                            style: TextStyle(color: colors.inkSoft),
                          ),
                        ),
                        for (final (i, step) in runSteps.indexed)
                          FeatureRow(
                            key: Key('run-${step['stepId']}'),
                            index: i,
                            icon: _outcomeIcon(
                              asJsonString(step['outcome']) ?? '',
                            ),
                            caption: _outcomeLabel(
                              asJsonString(step['outcome']) ?? '',
                            ).toUpperCase(),
                            title: asJsonString(step['label']) ?? '',
                            subtitle: asJsonString(step['detail']),
                          ),
                        if (canUndo)
                          Padding(
                            padding: const EdgeInsets.only(top: 8),
                            child: PillAction(
                              key: const Key('routine-undo'),
                              icon: PhosphorIconsRegular.arrowBendUpLeft,
                              label: 'Undo what can be undone',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(
                                      _call(
                                        () => widget.http.post<dynamic>(
                                          '/api/v1/home-routines/runs/${_run?['id']}/reverse',
                                        ),
                                        (data) => _run = data ?? _run,
                                      ),
                                    ),
                            ),
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _step(Map<String, dynamic> entry, int index, JarvisColors colors) {
    final step = jsonObject(entry['step']) ?? const {};
    final id = asJsonString(step['id']) ?? '';
    final status = asJsonString(entry['status']) ?? '';
    final device = jsonObject(entry['device']);
    final removed = status == 'removed';
    return FeatureRow(
      key: Key('step-$id'),
      index: index,
      icon: asJsonBool(step['readOnly'])
          ? PhosphorIconsRegular.eye
          : asJsonBool(step['sensitive'])
          ? PhosphorIconsRegular.key
          : PhosphorIconsRegular.lightbulb,
      quiet: removed,
      strike: removed,
      caption: asJsonString(device?['name']),
      title: asJsonString(step['label']) ?? '',
      subtitle: asJsonString(entry['reason']),
      trailing: StatusPill(
        label: switch (status) {
          'ready' => 'Ready',
          'unavailable' => 'Unavailable',
          'needs_approval' => 'Needs approval',
          _ => 'Taken out',
        },
        color: status == 'ready' ? colors.accent : colors.warning,
      ),
      actions: [
        PillAction(
          key: Key('step-toggle-$id'),
          compact: true,
          label: removed ? 'Put back' : 'Take out',
          onPressed: _busy ? null : () => unawaited(_toggle(id, !removed)),
        ),
      ],
    );
  }
}
