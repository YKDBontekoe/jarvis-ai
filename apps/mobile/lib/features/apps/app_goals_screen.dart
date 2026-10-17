import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../integrations/integrations_screen.dart';

/// Start from what you want done: Jarvis says which app does it, what it
/// could read, prepare and change, and the real next step. Connecting uses the
/// existing setup screens, so no secret passes through here.
class AppGoalsScreen extends StatefulWidget {
  const AppGoalsScreen({required this.http, this.onAskInChat, super.key});

  final Dio http;
  final ValueChanged<String>? onAskInChat;

  @override
  State<AppGoalsScreen> createState() => _AppGoalsScreenState();
}

class _AppGoalsScreenState extends State<AppGoalsScreen> {
  final _query = TextEditingController();
  List<Map<String, dynamic>> _goals = const [];
  String? _suggestion;
  bool _loading = true;
  String? _error;
  final _busy = <String>{};
  final _events = <String, List<Map<String, dynamic>>>{};

  @override
  void initState() {
    super.initState();
    unawaited(_search());
  }

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  Future<void> _search() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/apps/goals',
        queryParameters: {
          if (_query.text.trim().isNotEmpty) 'q': _query.text.trim(),
        },
      );
      final data = jsonObject(response.data) ?? const {};
      if (!mounted) return;
      setState(() {
        _goals = jsonMaps(data['goals']);
        _suggestion = asJsonBool(data['unmatched'])
            ? asJsonString(data['suggestion'])
            : null;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not look that up.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _connect() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => IntegrationsScreen(
          http: widget.http,
          onAskInChat: widget.onAskInChat,
        ),
      ),
    );
    if (mounted) unawaited(_search());
  }

  Future<void> _try(Map<String, dynamic> goal) async {
    final id = asJsonString(goal['id']);
    if (id == null) return;
    setState(() => _busy.add(id));
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/apps/goals/$id/try',
      );
      final result = jsonObject(response.data) ?? const {};
      if (!mounted) return;
      if (asJsonString(result['problem']) case final problem?) {
        _events.remove(id);
        _toast(problem);
      } else if (result['kind'] == 'calendar_today') {
        setState(() => _events[id] = jsonMaps(result['events']));
      } else if (asJsonString(result['prompt']) case final prompt?) {
        final ask = widget.onAskInChat;
        if (ask != null) {
          Navigator.of(context).pop();
          ask(prompt);
        } else {
          _toast(prompt);
        }
      }
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ??
              'Connect it first, then try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy.remove(id));
    }
  }

  Future<void> _pin(Map<String, dynamic> goal) async {
    final id = asJsonString(goal['id']);
    if (id == null) return;
    final pinned = goal['pinned'] == true;
    try {
      pinned
          ? await widget.http.delete<dynamic>('/api/v1/apps/goals/$id/pin')
          : await widget.http.put<dynamic>('/api/v1/apps/goals/$id/pin');
      if (mounted) unawaited(_search());
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ??
              'Could not change the pin.',
        );
      }
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Find an app')),
      body: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                TextField(
                  key: const Key('apps-query'),
                  controller: _query,
                  textInputAction: TextInputAction.search,
                  onSubmitted: (_) => unawaited(_search()),
                  decoration: InputDecoration(
                    hintText: 'What do you want help with?',
                    suffixIcon: IconButton(
                      key: const Key('apps-search'),
                      tooltip: 'Search',
                      onPressed: () => unawaited(_search()),
                      icon: const Icon(PhosphorIconsRegular.magnifyingGlass),
                    ),
                  ),
                ),
                const SizedBox(height: 12),
                if (_loading)
                  const Padding(
                    padding: EdgeInsets.all(24),
                    child: Center(child: CircularProgressIndicator()),
                  )
                else if (_error != null)
                  InlineNotice(message: _error!)
                else if (_suggestion != null)
                  SurfaceCard(
                    key: const Key('apps-none'),
                    child: Text(_suggestion!),
                  )
                else
                  for (var i = 0; i < _goals.length; i++)
                    _card(_goals[i], i, colors),
              ],
            ),
          ),
        ],
      ),
    );
  }

  static const _icons = {
    'calendar': PhosphorIconsRegular.calendarBlank,
    'mail': PhosphorIconsRegular.paperPlaneTilt,
    'contacts': PhosphorIconsRegular.addressBook,
  };

  Widget _card(Map<String, dynamic> goal, int index, JarvisColors colors) {
    final id = asJsonString(goal['id']) ?? '';
    final ready = goal['status'] == 'ready';
    final capabilities = jsonObject(goal['capabilities']) ?? const {};
    final tryAction = jsonObject(goal['try']);
    final events = _events[id];
    String line(String label, List<String> items) =>
        items.isEmpty ? '' : '$label: ${items.join(', ')}';
    final facts = [
      line('Reads', jsonStrings(capabilities['reads'])),
      line('Prepares', jsonStrings(capabilities['prepares'])),
      line('Changes', jsonStrings(capabilities['changes'])),
      asJsonString(capabilities['needs']) ?? '',
    ].where((text) => text.isNotEmpty);
    return FeatureRow(
      key: Key('app-goal-$id'),
      index: index,
      icon: _icons[id] ?? PhosphorIconsRegular.plugsConnected,
      title: asJsonString(goal['title']) ?? '',
      caption: ready ? 'Connected' : 'Not connected',
      subtitle: asJsonString(goal['purpose']),
      extra: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          for (final fact in facts)
            Text(
              fact,
              style: TextStyle(
                fontFamily: 'Geist',
                color: colors.muted,
                fontSize: 13,
                height: 1.35,
              ),
            ),
          const SizedBox(height: 6),
          Text(
            asJsonString(goal['nextStep']) ?? '',
            key: Key('app-goal-next-$id'),
            style: TextStyle(
              fontFamily: 'Geist',
              color: ready ? colors.accent : colors.ink,
              fontWeight: FontWeight.w600,
            ),
          ),
          if (events != null)
            Padding(
              key: Key('app-goal-events-$id'),
              padding: const EdgeInsets.only(top: 8),
              child: events.isEmpty
                  ? Text(
                      'Nothing on today.',
                      style: TextStyle(color: colors.inkSoft),
                    )
                  : Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        for (final event in events)
                          Text('· ${asJsonString(event['title']) ?? ''}'),
                      ],
                    ),
            ),
        ],
      ),
      actions: [
        if (!ready)
          PillAction(
            key: Key('app-goal-connect-$id'),
            primary: true,
            icon: PhosphorIconsRegular.plugsConnected,
            label: 'Connect',
            onPressed: () => unawaited(_connect()),
          ),
        if (ready && tryAction != null)
          PillAction(
            key: Key('app-goal-try-$id'),
            primary: true,
            label: asJsonString(tryAction['label']) ?? 'Try it',
            onPressed: _busy.contains(id) ? null : () => unawaited(_try(goal)),
          ),
        if (ready)
          PillAction(
            key: Key('app-goal-pin-$id'),
            icon: PhosphorIconsRegular.pushPin,
            label: goal['pinned'] == true ? 'Unpin' : 'Pin this',
            onPressed: () => unawaited(_pin(goal)),
          ),
      ],
    );
  }
}
