part of 'learning_screen.dart';

const _intervals = [15, 30, 60, 120, 240, 720, 1440];
const _dreamHours = [0, 3, 5, 22, 23];

mixin _LearningCards on _LearningController {
  Widget _heartbeatCard() {
    final enabled = _flag('heartbeatEnabled', true);
    final minutes = asJsonInt(_settings['heartbeatMinutes'], 60);
    return SurfaceCard(
      gradient: LinearGradient(
        colors: enabled
            ? [
                JarvisColors.of(context).accentSoft,
                JarvisColors.of(context).surface,
              ]
            : [
                JarvisColors.of(context).surface,
                JarvisColors.of(context).surface,
              ],
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
                color: enabled
                    ? JarvisColors.of(context).accent
                    : JarvisColors.of(context).muted,
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
                      style: TextStyle(color: JarvisColors.of(context).inkSoft),
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
                  style: TextStyle(
                    fontSize: 13,
                    color: JarvisColors.of(context).inkSoft,
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
            ? [
                JarvisColors.of(context).accentSoft,
                JarvisColors.of(context).surface,
              ]
            : [
                JarvisColors.of(context).surface,
                JarvisColors.of(context).surface,
              ],
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
                color: enabled
                    ? JarvisColors.of(context).accent
                    : JarvisColors.of(context).muted,
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
                      style: TextStyle(color: JarvisColors.of(context).inkSoft),
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
                  style: TextStyle(
                    fontSize: 13,
                    color: JarvisColors.of(context).inkSoft,
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

  Widget _portrait() {
    final summary = asJsonString(_dreaming['userSummary']);
    final updated = _when(asJsonString(_dreaming['userSummaryUpdatedAt']));
    if (summary == null || summary.trim().isEmpty) {
      return SurfaceCard(
        child: Text(
          'After a dream, Jarvis writes a short portrait from your memories and adds it to every chat. The next dream revises it. The portrait is background — not a memory of its own.',
          style: TextStyle(color: JarvisColors.of(context).inkSoft),
        ),
      );
    }
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(summary),
          if (updated.isNotEmpty) ...[
            const SizedBox(height: 8),
            Text(
              'Updated $updated. Included in chat as background.',
              style: TextStyle(
                fontSize: 12.5,
                color: JarvisColors.of(context).muted,
              ),
            ),
          ],
        ],
      ),
    );
  }

  Widget _diary() {
    final entries = jsonMaps(_dreaming['diary']);
    if (entries.isEmpty) {
      return SurfaceCard(
        child: Text(
          'After a dream, Jarvis writes a short diary of what it staged, reflected on, and promoted. The diary is for you to review — it is never stored as a memory.',
          style: TextStyle(color: JarvisColors.of(context).inkSoft),
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
          Text(
            'Jarvis keeps learning quietly but sends no check-ins during these hours (your briefing time zone).',
            style: TextStyle(
              fontSize: 12.5,
              color: JarvisColors.of(context).muted,
            ),
          ),
        ],
      ),
    );
  }

  Widget _timeline() {
    if (_activity.isEmpty) {
      return SurfaceCard(
        child: Text(
          'Nothing learned yet. Chat with Jarvis, rate replies, or tap “Reflect now”.',
          style: TextStyle(color: JarvisColors.of(context).inkSoft),
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
    final time = jsonDate(iso, local: true);
    if (time == null) return '';
    final minutes = DateTime.now().difference(time).inMinutes;
    if (minutes < 1) return 'Just now';
    if (minutes < 60) return '$minutes min ago';
    if (minutes < 1440) return '${minutes ~/ 60} h ago';
    return '${minutes ~/ 1440} days ago';
  }
}
