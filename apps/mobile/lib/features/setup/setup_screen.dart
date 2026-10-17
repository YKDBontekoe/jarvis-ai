import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';

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
      if (mounted)
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
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
      if (mounted)
        _toast(
          firstProblemMessage(error.response?.data) ??
              'Could not build the report.',
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final view = _view ?? const <String, dynamic>{};
    final goals = jsonMaps(view['goals']);
    final capabilities = jsonMaps(view['capabilities']);
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
                      Text(
                        asJsonString(view['summary']) ?? '',
                        key: const Key('setup-summary'),
                        style: const TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                      const SizedBox(height: 10),
                      for (final goal in goals) _goal(goal, colors),
                      const SectionHeader('What is connected'),
                      for (final capability in capabilities)
                        _capability(capability, colors),
                      const SizedBox(height: 8),
                      Align(
                        alignment: Alignment.centerLeft,
                        child: TextButton(
                          key: const Key('setup-support'),
                          onPressed: _supportBundle,
                          child: const Text('Review a support report'),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _goal(Map<String, dynamic> goal, JarvisColors colors) {
    final id = asJsonString(goal['id']) ?? '';
    final done = goal['done'] == true;
    final declined = goal['declined'] == true;
    final blocker = asJsonString(goal['blocker']);
    final optional = const {'calendar', 'source', 'brief'}.contains(id);
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: SurfaceCard(
        key: Key('setup-goal-$id'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                if (done)
                  const Padding(
                    padding: EdgeInsets.only(right: 6, top: 2),
                    child: Icon(
                      Icons.check_circle_outline,
                      size: 18,
                      key: Key('setup-done-mark'),
                    ),
                  ),
                Expanded(
                  child: Text(
                    '${asJsonString(goal['title']) ?? ''}${declined ? ' · skipped' : ''}',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                ),
              ],
            ),
            Text(
              asJsonString(goal['why']) ?? '',
              style: TextStyle(color: colors.inkSoft),
            ),
            if (blocker != null && !done)
              Text(
                blocker,
                key: Key('setup-blocker-$id'),
                style: TextStyle(color: colors.muted, fontSize: 13),
              ),
            if (!done)
              Wrap(
                spacing: 8,
                children: [
                  if (!declined)
                    TextButton(
                      key: Key('setup-act-$id'),
                      onPressed: () => unawaited(_act(goal)),
                      child: Text(asJsonString(goal['action']) ?? 'Open'),
                    ),
                  if (optional && !declined)
                    TextButton(
                      key: Key('setup-skip-$id'),
                      onPressed: () => unawaited(_choose(id, 'decline')),
                      child: const Text('Not for me'),
                    ),
                  if (declined)
                    TextButton(
                      key: Key('setup-reopen-$id'),
                      onPressed: () => unawaited(_choose(id, 'reopen')),
                      child: const Text('Bring back'),
                    ),
                ],
              ),
          ],
        ),
      ),
    );
  }

  Widget _capability(Map<String, dynamic> capability, JarvisColors colors) {
    final status = asJsonString(capability['status']) ?? '';
    final destination = asJsonString(capability['destination']);
    final label = switch (status) {
      'ready' => 'Ready',
      'not_connected' => 'Not connected',
      'needs_reauth' => 'Sign in again',
      'unavailable' => 'Unavailable',
      _ => 'Not checked',
    };
    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: SurfaceCard(
        key: Key('setup-capability-${capability['id']}'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Wrap(
              spacing: 8,
              crossAxisAlignment: WrapCrossAlignment.center,
              children: [
                Text(
                  asJsonString(capability['title']) ?? '',
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                Text(
                  label,
                  style: TextStyle(
                    color: status == 'ready' ? colors.inkSoft : colors.muted,
                    fontSize: 13,
                  ),
                ),
              ],
            ),
            Text(asJsonString(capability['detail']) ?? ''),
            if (destination != null)
              TextButton(
                key: Key('setup-fix-${capability['id']}'),
                onPressed: () {
                  final page = widget.pageFor(destination);
                  if (page != null) {
                    unawaited(
                      Navigator.of(context)
                          .push<void>(MaterialPageRoute(builder: (_) => page)),
                    );
                  }
                },
                child: const Text('Open'),
              ),
          ],
        ),
      ),
    );
  }
}
