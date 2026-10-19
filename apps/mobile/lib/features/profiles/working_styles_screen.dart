import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Working styles: ready-made profiles with an honest scope, and a suggestion
/// for the job at hand that says what would change before anything does. A
/// suggestion never switches a profile; one that would widen access says so.
class WorkingStylesScreen extends StatefulWidget {
  const WorkingStylesScreen({
    required this.http,
    this.currentProfileId,
    super.key,
  });

  final Dio http;
  final String? currentProfileId;

  @override
  State<WorkingStylesScreen> createState() => _WorkingStylesScreenState();
}

class _WorkingStylesScreenState extends State<WorkingStylesScreen> {
  final _task = TextEditingController();
  List<Map<String, dynamic>> _presets = const [];
  Map<String, dynamic>? _suggestion;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _task.dispose();
    super.dispose();
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/profiles/presets',
      );
      if (!mounted) return;
      setState(() {
        _presets = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load working styles.';
      });
    }
  }

  Future<void> _suggest() async {
    if (_task.text.trim().isEmpty) return;
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/profiles/suggest',
        data: {
          'text': _task.text.trim(),
          if (widget.currentProfileId != null)
            'currentProfileId': widget.currentProfileId,
        },
      );
      if (mounted) setState(() => _suggestion = jsonObject(response.data));
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _create(String presetId) async {
    setState(() => _busy = true);
    try {
      await widget.http.post<dynamic>(
        '/api/v1/profiles/presets/$presetId/create',
      );
      await _load();
      if (mounted) _toast('Created. It is not switched on anywhere.');
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  String _scopeLine(Map<String, dynamic> scope) => [
    asJsonString(scope['memory']),
    asJsonString(scope['files']),
    asJsonString(scope['skills']),
  ].whereType<String>().join(' · ');

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final suggestion = _suggestion;
    final kind = asJsonString(suggestion?['kind']);
    final details = [
      ...jsonStrings(jsonObject(suggestion?['scopeChange'])?['details']),
    ];
    final broadens = jsonStrings(suggestion?['broadens']);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Working styles')),
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
                      const HeroCard(
                        key: Key('styles-hero'),
                        label: 'WORKING STYLES',
                        title: 'Pick the style that fits the job',
                        subtitle: 'A style sets tone, memory and what Jarvis may use. Nothing switches until you do it yourself.',
                      ),
                      const SizedBox(height: 16),
                      TextField(
                        key: const Key('styles-task'),
                        controller: _task,
                        maxLength: 500,
                        decoration: const InputDecoration(
                          labelText: 'What are you about to do?',
                          hintText: 'Rewrite an email, plan my week, study for an exam',
                        ),
                      ),
                      PillAction(
                        key: const Key('styles-suggest'),
                        primary: true,
                        icon: PhosphorIconsRegular.sparkle,
                        label: 'What fits?',
                        onPressed: _busy ? null : () => unawaited(_suggest()),
                      ),
                      if (suggestion != null) ...[
                        const SizedBox(height: 12),
                        FeatureRow(
                          key: const Key('styles-suggestion'),
                          index: 0,
                          icon: kind == 'none'
                              ? PhosphorIconsRegular.circle
                              : PhosphorIconsRegular.userCircle,
                          caption: switch (kind) {
                            'existing' => 'SUGGESTED · YOURS',
                            'preset' => 'SUGGESTED · NOT CREATED YET',
                            _ => 'NO CHANGE',
                          },
                          title:
                              asJsonString(suggestion['name']) ??
                              asJsonString(suggestion['note']) ??
                              '',
                          subtitle: kind == 'none'
                              ? null
                              : [
                                  asJsonString(suggestion['reason']),
                                  ...details,
                                  asJsonString(suggestion['note']),
                                ].whereType<String>().join('\n'),
                          extra: broadens.isEmpty
                              ? null
                              : Padding(
                                  padding: const EdgeInsets.only(top: 8),
                                  child: Text(
                                    key: const Key('styles-broadens'),
                                    'Wider access: ${broadens.join(' ')}',
                                    style: TextStyle(color: colors.warning),
                                  ),
                                ),
                          actions: [
                            if (kind == 'preset')
                              PillAction(
                                key: const Key('styles-create-suggested'),
                                compact: true,
                                primary: true,
                                label: 'Create it',
                                onPressed: _busy
                                    ? null
                                    : () => unawaited(
                                        _create(
                                          asJsonString(
                                                suggestion['presetId'],
                                              ) ??
                                              '',
                                        ),
                                      ),
                              ),
                          ],
                        ),
                      ],
                      const GroupLabel('Styles'),
                      for (final (i, view) in _presets.indexed)
                        _preset(view, i),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _preset(Map<String, dynamic> view, int index) {
    final preset = jsonObject(view['preset']) ?? const {};
    final id = asJsonString(preset['id']) ?? '';
    final exists = asJsonString(view['existingProfileId']) != null;
    final scope = jsonObject(view['scope']) ?? const {};
    final limits = jsonStrings(scope['limits']);
    return FeatureRow(
      key: Key('style-$id'),
      index: index,
      icon: PhosphorIconsRegular.userCircle,
      caption: exists ? 'CREATED' : 'NOT CREATED',
      title: asJsonString(preset['name']) ?? '',
      subtitle: [
        asJsonString(preset['description']),
        _scopeLine(scope),
        if (limits.isNotEmpty) limits.first,
        if (asJsonBool(preset['needsSources']))
          'Choose its documents or skills after creating it.',
      ].whereType<String>().join('\n'),
      actions: [
        PillAction(
          key: Key('style-create-$id'),
          compact: true,
          primary: !exists,
          label: exists ? 'Created' : 'Create',
          onPressed: exists || _busy ? null : () => unawaited(_create(id)),
        ),
      ],
    );
  }
}
