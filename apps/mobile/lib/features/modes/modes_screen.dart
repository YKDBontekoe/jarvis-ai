import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'ambient_screen.dart';
import 'modes_models.dart';

/// The mode Jarvis is in (focus, meeting, sleep, …): switch it by hand, let
/// Jarvis follow the clock and calendar, and decide what each mode silences.
class ModesScreen extends StatefulWidget {
  const ModesScreen({required this.http, this.now, super.key});

  final Dio http;
  final DateTime? now;

  @override
  State<ModesScreen> createState() => _ModesScreenState();
}

class _ModesScreenState extends State<ModesScreen> {
  ModeStateData? _state;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    await _call(() => widget.http.get<dynamic>('/api/v1/modes'));
  }

  Future<void> _call(Future<Response<dynamic>> Function() request) async {
    try {
      final response = await request();
      if (!mounted) return;
      final state = ModeStateData.fromJson(response.data);
      setState(() {
        _state = state ?? _state;
        _loading = false;
        _error = _state == null ? 'Could not load modes.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() => _loading = false);
      final message =
          firstProblemMessage(error.response?.data) ?? 'That did not work.';
      if (_state == null) {
        setState(() => _error = message);
      } else {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(message)));
      }
    }
  }

  Future<void> _switch(String mode, {int? minutes}) => _call(
    () => widget.http.put<dynamic>(
      '/api/v1/modes/active',
      data: {'mode': mode, 'minutes': ?minutes},
    ),
  );

  Future<void> _pickPolicy(ModeInfo mode) async {
    final level = await showDialog<String>(
      context: context,
      builder: (context) => SimpleDialog(
        title: Text('${mode.label}: phone notifications'),
        children: [
          for (final value in const ['all', 'important', 'none'])
            SimpleDialogOption(
              key: Key('policy-$value'),
              onPressed: () => Navigator.pop(context, value),
              child: Row(
                children: [
                  Icon(
                    value == mode.notifications
                        ? PhosphorIconsRegular.checkCircle
                        : PhosphorIconsRegular.circle,
                    size: 18,
                  ),
                  const SizedBox(width: 10),
                  Text(notificationLevelLabel(value)),
                ],
              ),
            ),
          if (mode.customised)
            SimpleDialogOption(
              key: const Key('policy-reset'),
              onPressed: () => Navigator.pop(context, 'reset'),
              child: const Text('Back to the default'),
            ),
        ],
      ),
    );
    if (level == null) return;
    if (level == 'reset') {
      await _call(
        () => widget.http.delete<dynamic>('/api/v1/modes/${mode.id}/policy'),
      );
    } else {
      await _call(
        () => widget.http.put<dynamic>(
          '/api/v1/modes/${mode.id}/policy',
          data: {'notifications': level},
        ),
      );
    }
  }

  Future<void> _pickSleep(ModeStateData state) async {
    TimeOfDay parse(String text) {
      final parts = text.split(':');
      return TimeOfDay(
        hour: int.tryParse(parts.first) ?? 23,
        minute: int.tryParse(parts.last) ?? 0,
      );
    }

    String format(TimeOfDay time) =>
        '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}';

    final start = await showTimePicker(
      context: context,
      initialTime: parse(state.sleepStart),
      helpText: 'Sleep mode starts',
    );
    if (start == null || !mounted) return;
    final end = await showTimePicker(
      context: context,
      initialTime: parse(state.sleepEnd),
      helpText: 'Sleep mode ends',
    );
    if (end == null) return;
    await _call(
      () => widget.http.put<dynamic>(
        '/api/v1/modes/settings',
        data: {'sleepStart': format(start), 'sleepEnd': format(end)},
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final state = _state;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Modes'),
        actions: [
          HeaderAction(
            key: const Key('modes-ambient'),
            label: 'Ambient display',
            icon: PhosphorIconsRegular.monitor,
            collapsesWhenNarrow: true,
            onPressed: () => unawaited(
              Navigator.of(context).push<void>(
                MaterialPageRoute(
                  builder: (_) =>
                      AmbientScreen(http: widget.http, now: widget.now),
                ),
              ),
            ),
          ),
        ],
      ),
      body: ContentWidth(
        child: ListScreenBody(
          loading: _loading,
          error: _error,
          isEmpty: state == null,
          onRetry: () => unawaited(_load()),
          empty: const SizedBox.shrink(),
          child: state == null
              ? const SizedBox.shrink()
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  children: [_current(state), ..._list(state), _auto(state)],
                ),
        ),
      ),
    );
  }

  Widget _current(ModeStateData state) {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 16),
      child: Row(
        children: [
          IconBadge(icon: modeIcon(state.mode), size: 44, color: colors.accent),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  state.label,
                  key: const Key('mode-current'),
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                Text(state.reason, style: TextStyle(color: colors.inkSoft)),
                Text(
                  notificationLevelLabel(state.notifications),
                  style: TextStyle(color: colors.muted, fontSize: 12.5),
                ),
              ],
            ),
          ),
          if (state.manual)
            TextButton(
              key: const Key('mode-auto'),
              onPressed: () => unawaited(_switch('auto')),
              child: const Text('Back to auto'),
            ),
        ],
      ),
    );
  }

  List<Widget> _list(ModeStateData state) => [
    for (final mode in state.modes)
      SurfaceCard(
        key: Key('mode-${mode.id}'),
        margin: const EdgeInsets.only(bottom: 10),
        padding: const EdgeInsets.fromLTRB(16, 12, 8, 12),
        child: Row(
          children: [
            IconBadge(icon: modeIcon(mode.id), size: 36),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    mode.label,
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  Text(
                    mode.description,
                    style: TextStyle(
                      color: JarvisColors.of(context).inkSoft,
                      fontSize: 13,
                    ),
                  ),
                ],
              ),
            ),
            IconButton(
              key: Key('mode-policy-${mode.id}'),
              tooltip: 'Notifications in this mode',
              icon: const Icon(PhosphorIconsRegular.bellSlash, size: 20),
              onPressed: () => unawaited(_pickPolicy(mode)),
            ),
            FilledButton.tonal(
              key: Key('mode-switch-${mode.id}'),
              onPressed: state.mode == mode.id && state.manual
                  ? null
                  : () => unawaited(
                      _switch(mode.id, minutes: mode.id == 'normal' ? null : 120),
                    ),
              child: Text(state.mode == mode.id ? 'On' : 'Use for 2 h'),
            ),
          ],
        ),
      ),
  ];

  Widget _auto(ModeStateData state) => SurfaceCard(
    margin: const EdgeInsets.only(top: 6),
    child: Column(
      children: [
        SwitchListTile(
          key: const Key('modes-auto-switch'),
          contentPadding: EdgeInsets.zero,
          title: const Text('Let Jarvis choose'),
          subtitle: const Text(
            'Sleep at night, Meeting during calendar events, Weekend on '
            'Saturday and Sunday.',
          ),
          value: state.auto,
          onChanged: (value) => unawaited(
            _call(
              () => widget.http.put<dynamic>(
                '/api/v1/modes/settings',
                data: {'auto': value},
              ),
            ),
          ),
        ),
        ListTile(
          key: const Key('modes-sleep-hours'),
          contentPadding: EdgeInsets.zero,
          title: const Text('Sleep hours'),
          subtitle: Text('${state.sleepStart} to ${state.sleepEnd}'),
          trailing: const Icon(PhosphorIconsRegular.caretRight, size: 18),
          onTap: () => unawaited(_pickSleep(state)),
        ),
      ],
    ),
  );
}
