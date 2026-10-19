import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Getting started in plain words: what is done, what is next, what you chose
/// to skip, and which connection is not ready, with the page that fixes it.
/// Nothing here sends or creates anything.
class SetupScreen extends StatefulWidget {
  const SetupScreen({
    required this.http,
    required this.pageFor,
    this.onAskInChat,
    super.key,
  });

  final Dio http;
  final Widget? Function(String destination) pageFor;
  final ValueChanged<String>? onAskInChat;

  @override
  State<SetupScreen> createState() => _SetupScreenState();
}

class _SetupScreenState extends State<SetupScreen> {
  Map<String, dynamic>? _view;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/setup');
      if (!mounted) return;
      setState(() {
        _view = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your setup.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _choose(String goal, String verb) async {
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/setup/goals/$goal/$verb',
      );
      if (mounted) setState(() => _view = jsonObject(response.data) ?? _view);
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    }
  }

  Future<void> _act(Map<String, dynamic> goal) async {
    final destination = asJsonString(goal['destination']);
    if (destination == null) {
      widget.onAskInChat?.call('Hi Jarvis! What can you help me with?');
      return;
    }
    final page = widget.pageFor(destination);
    if (page == null) {
      _toast('That page is not available here.');
      return;
    }
    await Navigator.of(context)
        .push<void>(MaterialPageRoute(builder: (_) => page));
    if (mounted) unawaited(_load());
  }

  Future<void> _supportBundle() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/setup/support-bundle',
      );
      if (!mounted) return;
      final text = const JsonEncoder.withIndent('  ').convert(response.data);
      await showJarvisDialog<void>(
        context: context,
        builder: (dialog) => AlertDialog(
          title: const Text('What a support report holds'),
          content: SingleChildScrollView(
            child: SelectableText(text, key: const Key('setup-bundle-text')),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialog),
              child: const Text('Close'),
            ),
            FilledButton(
              key: const Key('setup-bundle-copy'),
              onPressed: () async {
                await Clipboard.setData(ClipboardData(text: text));
                if (dialog.mounted) Navigator.pop(dialog);
              },
              child: const Text('Copy'),
            ),
          ],
        ),
      );
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ??
              'Could not build the report.',
        );
      }
    }
  }

  static const _goalIcons = {
    'talk': PhosphorIconsRegular.chatCircle,
    'reminder': PhosphorIconsRegular.alarm,
    'calendar': PhosphorIconsRegular.calendarBlank,
    'source': PhosphorIconsRegular.bookmarkSimple,
    'brief': PhosphorIconsRegular.sunHorizon,
  };

  @override
  Widget build(BuildContext context) {
    final view = _view ?? const <String, dynamic>{};
    final goals = jsonMaps(view['goals']);
    final capabilities = jsonMaps(view['capabilities']);
    final done = asJsonInt(view['done']);
    final total = asJsonInt(view['total']);
    final next = goals.where((g) => g['id'] == view['nextGoalId']).firstOrNull;
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Getting started')),
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
                      FadeSlideIn(
                        child: KeyedSubtree(
                          key: const Key('setup-summary'),
                          child: HeroCard(
                            label: '$done of $total done',
                            title: next == null
                                ? 'You are set up'
                                : asJsonString(next['title']) ?? 'Next step',
                            subtitle: next == null
                                ? 'Anything you skipped can be turned on later.'
                                : asJsonString(next['why']),
                            segments: total,
                            doneSegments: done,
                          ),
                        ),
                      ),
                      const GroupLabel('Your first steps'),
                      for (var i = 0; i < goals.length; i++) _goal(goals[i], i),
                      const GroupLabel('What is connected'),
                      for (var i = 0; i < capabilities.length; i++)
                        _capability(capabilities[i], i),
                      const SizedBox(height: 8),
                      Align(
                        alignment: Alignment.centerLeft,
                        child: PillAction(
                          key: const Key('setup-support'),
                          icon: PhosphorIconsRegular.shieldCheck,
                          label: 'Review a support report',
                          onPressed: _supportBundle,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _goal(Map<String, dynamic> goal, int index) {
    final id = asJsonString(goal['id']) ?? '';
    final done = goal['done'] == true;
    final declined = goal['declined'] == true;
    final blocker = asJsonString(goal['blocker']);
    final optional = const {'calendar', 'source', 'brief'}.contains(id);
    final colors = JarvisColors.of(context);
    return FeatureRow(
      key: Key('setup-goal-$id'),
      index: index,
      icon: done
          ? PhosphorIconsRegular.checkCircle
          : _goalIcons[id] ?? PhosphorIconsRegular.circle,
      badgeColor: done ? colors.accent : colors.ink,
      quiet: declined,
      caption: declined
          ? 'Skipped'
          : done
          ? 'Done'
          : null,
      title: asJsonString(goal['title']) ?? '',
      subtitle: blocker != null && !done
          ? '${asJsonString(goal['why']) ?? ''}\n$blocker'
          : asJsonString(goal['why']),
      actions: done
          ? null
          : [
              if (!declined)
                PillAction(
                  key: Key('setup-act-$id'),
                  primary: asJsonString(goal['id']) == _nextId,
                  label: asJsonString(goal['action']) ?? 'Open',
                  onPressed: () => unawaited(_act(goal)),
                ),
              if (optional && !declined)
                PillAction(
                  key: Key('setup-skip-$id'),
                  label: 'Not for me',
                  onPressed: () => unawaited(_choose(id, 'decline')),
                ),
              if (declined)
                PillAction(
                  key: Key('setup-reopen-$id'),
                  label: 'Bring back',
                  onPressed: () => unawaited(_choose(id, 'reopen')),
                ),
            ],
      trailing: done
          ? const Icon(Icons.check, size: 18, key: Key('setup-done-mark'))
          : null,
    );
  }

  String? get _nextId => asJsonString(_view?['nextGoalId']);

  Widget _capability(Map<String, dynamic> capability, int index) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(capability['status']) ?? '';
    final destination = asJsonString(capability['destination']);
    final ready = status == 'ready';
    final label = switch (status) {
      'ready' => 'Ready',
      'not_connected' => 'Not connected',
      'needs_reauth' => 'Sign in again',
      'unavailable' => 'Unavailable',
      _ => 'Not checked',
    };
    return FeatureRow(
      key: Key('setup-capability-${capability['id']}'),
      index: index,
      icon: switch (capability['id']) {
        'model' => PhosphorIconsRegular.brain,
        'calendar' => PhosphorIconsRegular.calendarBlank,
        'brief' => PhosphorIconsRegular.sunHorizon,
        _ => PhosphorIconsRegular.bell,
      },
      title: asJsonString(capability['title']) ?? '',
      subtitle: asJsonString(capability['detail']),
      trailing: StatusPill(
        label: label,
        color: ready
            ? colors.accent
            : status == 'needs_reauth'
            ? colors.warning
            : colors.muted,
      ),
      onTap: destination == null
          ? null
          : () {
              final page = widget.pageFor(destination);
              if (page != null) {
                unawaited(
                  Navigator.of(context)
                      .push<void>(MaterialPageRoute(builder: (_) => page)),
                );
              }
            },
    );
  }
}
