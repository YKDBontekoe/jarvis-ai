import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Heartbeat, dreaming, and continuous-learning controls with a timeline of what Jarvis learned.
class LearningScreen extends StatefulWidget {
  const LearningScreen({required this.http, super.key});

  final Dio http;

  @override
  State<LearningScreen> createState() => _LearningScreenState();
}

class _LearningScreenState extends State<LearningScreen> {
  Map<String, dynamic> _settings = const {};
  Map<String, dynamic> _state = const {};
  Map<String, dynamic> _dreaming = const {};
  List<Map<String, dynamic>> _activity = const [];
  bool _loading = true;
  bool _saving = false;
  bool _running = false;
  bool _dreamingNow = false;
  String? _error;
  String? _result;
  int _requestRevision = 0;

  static const _intervals = [15, 30, 60, 120, 240, 720, 1440];
  static const _dreamHours = [0, 3, 5, 22, 23];

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/learning/status',
      );
      final data = jsonObject(response.data) ?? const {};
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _settings = jsonObject(data['settings']) ?? const {};
        _state = jsonObject(data['state']) ?? const {};
        _dreaming = jsonObject(data['dreaming']) ?? const {};
        _activity = jsonMaps(data['activity']);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load learning settings.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load learning settings.';
      });
    }
  }

  Future<void> _update(String key, Object value) async {
    final next = {..._settings, key: value};
    setState(() {
      _settings = next;
      _saving = true;
      _error = null;
    });
    try {
      final response = await widget.http.put<Map<String, dynamic>>(
        '/api/v1/settings/learning',
        data: next,
      );
      if (mounted) {
        setState(() => _settings = jsonObject(response.data) ?? next);
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not save that setting.',
      );
      await _load();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _runNow() async {
    setState(() {
      _running = true;
      _result = null;
      _error = null;
    });
    try {
      final response = await widget.http.post<Map<String, dynamic>>(
        '/api/v1/learning/run',
      );
      if (!mounted) return;
      setState(() => _result = asJsonString(response.data?['summary']));
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'The heartbeat could not run.',
        );
      }
    } finally {
      if (mounted) setState(() => _running = false);
    }
  }

  Future<void> _dreamNow() async {
    setState(() {
      _dreamingNow = true;
      _result = null;
      _error = null;
    });
    try {
      final response = await widget.http.post<Map<String, dynamic>>(
        '/api/v1/learning/dream',
      );
      if (!mounted) return;
      setState(() => _result = asJsonString(response.data?['summary']));
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Dreaming could not run.',
        );
      }
    } finally {
      if (mounted) setState(() => _dreamingNow = false);
    }
  }

  bool _flag(String key, [bool fallback = false]) =>
      asJsonBool(_settings[key], fallback);

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Learning & heartbeat')),
    body: _loading
        ? const LoadingState()
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _heartbeatCard(),
                      const SizedBox(height: 16),
                      _dreamingCard(),
                      if (_error != null)
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      if (_result != null)
                        InlineNotice(
                          message: _result!,
                          tone: NoticeTone.success,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      const SizedBox(height: 20),
                      const SectionHeader('What Jarvis may learn'),
                      GroupedSection(
                        children: [
                          _switch(
                            'learnPersona',
                            'Learn my preferences',
                            'Tone, format, language, and working style from chats and 👍/👎.',
                            PhosphorIconsRegular.userCircle,
                            fallback: true,
                          ),
                          _switch(
                            'autoCreateSkills',
                            'Write skills automatically',
                            'Save repeatable workflows as skills when Jarvis thinks it needs them.',
                            PhosphorIconsRegular.magicWand,
                            fallback: true,
                          ),
                          _switch(
                            'autoActivateSkills',
                            'Activate new skills right away',
                            'Off: learned skills wait for your review in Skills.',
                            PhosphorIconsRegular.sealCheck,
                            fallback: true,
                            enabled: _flag('autoCreateSkills', true),
                          ),
                          _switch(
                            'proactiveCheckIns',
                            'Proactive check-ins',
                            'Heads-ups about upcoming reminders, waiting approvals, and failed tasks.',
                            PhosphorIconsRegular.bellRinging,
                            fallback: true,
                          ),
                        ],
                      ),
                      const SizedBox(height: 20),
                      const SectionHeader('Quiet hours'),
                      _quietHours(),
                      const SizedBox(height: 20),
                      const SectionHeader('Dream diary'),
                      _diary(),
                      const SizedBox(height: 20),
                      const SectionHeader('Recent learning'),
                      _timeline(),
                    ],
                  ),
                ),
              ],
            ),
          ),
  );

  Widget _heartbeatCard() {
    final enabled = _flag('heartbeatEnabled');
    final minutes = asJsonInt(_settings['heartbeatMinutes'], 60);
    return SurfaceCard(
      gradient: LinearGradient(
        colors: enabled
            ? const [JarvisColors.accentSoft, JarvisColors.surface]
            : const [JarvisColors.surface, JarvisColors.surface],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBadge(
                icon: PhosphorIconsRegular.pulse,
                color: enabled ? JarvisColors.accent : JarvisColors.muted,
                size: 44,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Heartbeat',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      enabled
                          ? 'Reflects and checks in every ${_interval(minutes)}.'
                          : 'Off — Jarvis only learns during conversations.',
                      style: const TextStyle(color: JarvisColors.inkSoft),
                    ),
                  ],
                ),
              ),
              Switch(
                key: const Key('heartbeat-switch'),
                value: enabled,
                onChanged: _saving
                    ? null
                    : (value) => unawaited(_update('heartbeatEnabled', value)),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final option in _intervals)
                ChoiceChip(
                  label: Text(_interval(option)),
                  selected: minutes == option,
                  onSelected: _saving || !enabled
                      ? null
                      : (_) => unawaited(_update('heartbeatMinutes', option)),
                ),
            ],
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: Text(
                  asJsonString(_state['lastSummary']) ??
                      'No heartbeat has run yet.',
                  style: const TextStyle(
                    fontSize: 13,
                    color: JarvisColors.inkSoft,
                  ),
                ),
              ),
              const SizedBox(width: 12),
              OutlinedButton.icon(
                key: const Key('run-heartbeat'),
                onPressed: _running ? null : () => unawaited(_runNow()),
                icon: _running
                    ? const SizedBox.square(
                        dimension: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(PhosphorIconsRegular.play, size: 16),
                label: const Text('Reflect now'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _dreamingCard() {
    final enabled = _flag('dreamingEnabled', true);
    final hour = asJsonInt(_settings['dreamingHour'], 3);
    return SurfaceCard(
      gradient: LinearGradient(
        colors: enabled
            ? const [JarvisColors.accentSoft, JarvisColors.surface]
            : const [JarvisColors.surface, JarvisColors.surface],
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBadge(
                icon: PhosphorIconsRegular.sparkle,
                color: enabled ? JarvisColors.accent : JarvisColors.muted,
                size: 44,
              ),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      'Dreaming',
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      enabled
                          ? 'Consolidates memories, facts, and tone around ${_clockHour(hour)}.'
                          : 'Off — Jarvis will not improve stored memories overnight.',
                      style: const TextStyle(color: JarvisColors.inkSoft),
                    ),
                  ],
                ),
              ),
              Switch(
                key: const Key('dreaming-switch'),
                value: enabled,
                onChanged: _saving
                    ? null
                    : (value) => unawaited(_update('dreamingEnabled', value)),
              ),
            ],
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final option in _dreamHours)
                ChoiceChip(
                  label: Text(_clockHour(option)),
                  selected: hour == option,
                  onSelected: _saving || !enabled
                      ? null
                      : (_) => unawaited(_update('dreamingHour', option)),
                ),
            ],
          ),
          const SizedBox(height: 14),
          Row(
            children: [
              Expanded(
                child: Text(
                  asJsonString(_dreaming['lastSummary']) ??
                      'No dream has run yet.',
                  style: const TextStyle(
                    fontSize: 13,
                    color: JarvisColors.inkSoft,
                  ),
                ),
              ),
              const SizedBox(width: 12),
              OutlinedButton.icon(
                key: const Key('run-dreaming'),
                onPressed: _dreamingNow ? null : () => unawaited(_dreamNow()),
                icon: _dreamingNow
                    ? const SizedBox.square(
                        dimension: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(PhosphorIconsRegular.sparkle, size: 16),
                label: const Text('Dream now'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _diary() {
    final entries = jsonMaps(_dreaming['diary']);
    if (entries.isEmpty) {
      return const SurfaceCard(
        child: Text(
          'After a dream, Jarvis writes a short diary of what it staged, reflected on, and promoted. The diary is for you to review — it is never stored as a memory.',
          style: TextStyle(color: JarvisColors.inkSoft),
        ),
      );
    }
    return GroupedSection(
      dividerIndent: 60,
      children: [
        for (final item in entries.reversed)
          ListTile(
            leading: IconBadge(
              icon: switch (asJsonString(item['phase'])) {
                'light' => PhosphorIconsRegular.sunHorizon,
                'deep' => PhosphorIconsRegular.brain,
                _ => PhosphorIconsRegular.sparkle,
              },
              size: 32,
            ),
            title: Text(asJsonString(item['title']) ?? 'Dream'),
            subtitle: Text(
              [
                asJsonString(item['body']) ?? '',
                _when(asJsonString(item['at'])),
              ].where((part) => part.isNotEmpty).join('\n'),
            ),
            isThreeLine: true,
          ),
      ],
    );
  }

  Widget _switch(
    String key,
    String title,
    String subtitle,
    IconData icon, {
    bool fallback = false,
    bool enabled = true,
  }) => SwitchListTile(
    key: Key('learning-$key'),
    secondary: IconBadge(icon: icon, size: 34),
    title: Text(title),
    subtitle: Text(subtitle),
    value: _flag(key, fallback),
    onChanged: _saving || !enabled
        ? null
        : (value) => unawaited(_update(key, value)),
  );

  Widget _quietHours() {
    final start = asJsonInt(_settings['quietHoursStart'], 22);
    final end = asJsonInt(_settings['quietHoursEnd'], 7);
    Widget picker(String label, int value, String key) => Expanded(
      child: DropdownButtonFormField<int>(
        initialValue: value,
        decoration: InputDecoration(labelText: label),
        items: [
          for (var hour = 0; hour < 24; hour++)
            DropdownMenuItem(
              value: hour,
              child: Text('${hour.toString().padLeft(2, '0')}:00'),
            ),
        ],
        onChanged: _saving
            ? null
            : (hour) {
                if (hour != null) unawaited(_update(key, hour));
              },
      ),
    );
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              picker('From', start, 'quietHoursStart'),
              const SizedBox(width: 12),
              picker('Until', end, 'quietHoursEnd'),
            ],
          ),
          const SizedBox(height: 8),
          const Text(
            'Jarvis keeps learning quietly but sends no check-ins during these hours (your briefing time zone).',
            style: TextStyle(fontSize: 12.5, color: JarvisColors.muted),
          ),
        ],
      ),
    );
  }

  Widget _timeline() {
    if (_activity.isEmpty) {
      return const SurfaceCard(
        child: Text(
          'Nothing learned yet. Chat with Jarvis, rate replies, or tap “Reflect now”.',
          style: TextStyle(color: JarvisColors.inkSoft),
        ),
      );
    }
    return GroupedSection(
      dividerIndent: 60,
      children: [
        for (final item in _activity)
          ListTile(
            leading: IconBadge(icon: _activityIcon(item), size: 32),
            title: Text(_activityTitle(item)),
            subtitle: Text(_when(asJsonString(item['timestamp']))),
          ),
      ],
    );
  }

  IconData _activityIcon(Map<String, dynamic> item) =>
      switch (asJsonString(item['action'])) {
        'skill.learned' || 'skill.improved' => PhosphorIconsRegular.magicWand,
        'learning.reflected' => PhosphorIconsRegular.brain,
        'learning.dreamed' => PhosphorIconsRegular.sparkle,
        _ => PhosphorIconsRegular.sparkle,
      };

  String _activityTitle(Map<String, dynamic> item) {
    final action = asJsonString(item['action']) ?? '';
    final metadata = item['metadataJson'];
    final details = metadata is String ? metadata : '';
    String? field(String name) =>
        RegExp('"$name":\\s*"?([^",}]+)').firstMatch(details)?.group(1);
    return switch (action) {
      'skill.learned' => 'Learned the skill ${field('name') ?? ''}',
      'skill.improved' => 'Improved the skill ${field('name') ?? ''}',
      'skill.created' => 'You added the skill ${field('name') ?? ''}',
      'learning.reflected' =>
        'Reflected on ${field('messagesReviewed') ?? 'recent'} messages',
      'learning.dreamed' => 'Dreamed and improved stored memory',
      _ => action.replaceAll('.', ' '),
    };
  }

  String _clockHour(int hour) => '${hour.toString().padLeft(2, '0')}:00';

  String _interval(int minutes) => minutes < 60
      ? '$minutes min'
      : minutes % 60 == 0 && minutes < 1440
      ? '${minutes ~/ 60} h'
      : minutes == 1440
      ? 'day'
      : '$minutes min';

  String _when(String? iso) {
    final time = DateTime.tryParse(iso ?? '')?.toLocal();
    if (time == null) return '';
    final minutes = DateTime.now().difference(time).inMinutes;
    if (minutes < 1) return 'Just now';
    if (minutes < 60) return '$minutes min ago';
    if (minutes < 1440) return '${minutes ~/ 60} h ago';
    return '${minutes ~/ 1440} days ago';
  }
}
