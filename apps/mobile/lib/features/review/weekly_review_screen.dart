import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../decisions/decision_format.dart';
import '../journal/journal_format.dart';
import 'mood_trend_chart.dart';
import 'weekly_review_format.dart';

/// Sunday look back: Jarvis's short story about the week, the numbers behind
/// it, a mood trend across weeks, earlier reviews, and when it arrives.
class WeeklyReviewScreen extends StatefulWidget {
  const WeeklyReviewScreen({required this.http, super.key});

  final Dio http;

  @override
  State<WeeklyReviewScreen> createState() => _WeeklyReviewScreenState();
}

class _WeeklyReviewScreenState extends State<WeeklyReviewScreen> {
  static const _path = '/api/v1/reviews/weekly';

  Map<String, dynamic>? _overview;
  bool _loading = true;
  bool _generating = false;
  bool _savingSettings = false;
  String? _error;
  String? _notice;
  int _selected = 0;
  int _requestRevision = 0;
  final _scroll = ScrollController();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _scroll.dispose();
    super.dispose();
  }

  List<Map<String, dynamic>> get _reviews => jsonMaps(_overview?['reviews']);

  Map<String, dynamic> get _settings =>
      jsonObject(_overview?['settings']) ?? const {};

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        _path,
        queryParameters: {'weeks': 8},
      );
      final data = jsonObject(response.data);
      if (data == null) throw const FormatException('Missing review data.');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _overview = data;
        _selected = 0;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your weekly review.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load your weekly review.';
      });
    }
  }

  Future<void> _generate() async {
    setState(() {
      _generating = true;
      _notice = null;
    });
    try {
      await widget.http.post<dynamic>('$_path/generate');
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not write this week\'s review.',
        );
      }
    } finally {
      if (mounted) setState(() => _generating = false);
    }
  }

  Future<void> _saveSettings({
    bool? enabled,
    TimeOfDay? time,
    String? zone,
  }) async {
    final current = _settings;
    var zoneId = zone ?? asJsonString(current['timeZoneId']) ?? 'UTC';
    final turningOn = enabled == true && !asJsonBool(current['enabled']);
    if (turningOn && zone == null && zoneId == 'UTC') {
      zoneId = await deviceTimeZoneLookup() ?? zoneId;
    }
    final localTime = time ?? _timeOf(current);
    setState(() {
      _savingSettings = true;
      _notice = null;
    });
    try {
      await widget.http.put<dynamic>(
        '$_path/settings',
        data: {
          'enabled': enabled ?? asJsonBool(current['enabled']),
          'localTime':
              '${localTime.hour.toString().padLeft(2, '0')}:'
              '${localTime.minute.toString().padLeft(2, '0')}:00',
          'timeZoneId': zoneId,
        },
      );
      await _load();
      if (mounted) setState(() => _notice = 'Saved.');
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _notice =
              firstProblemMessage(error.response?.data) ??
              'Could not save these settings.',
        );
      }
    } finally {
      if (mounted) setState(() => _savingSettings = false);
    }
  }

  Future<void> _chooseTime() async {
    final selected = await showTimePicker(
      context: context,
      initialTime: _timeOf(_settings),
    );
    if (selected != null && mounted) await _saveSettings(time: selected);
  }

  Future<void> _useDeviceZone() async {
    final zone = await deviceTimeZoneLookup();
    if (!mounted) return;
    if (zone == null) {
      setState(() => _notice = 'This device did not share its time zone.');
      return;
    }
    await _saveSettings(zone: zone);
  }

  void _select(int index) {
    setState(() => _selected = index);
    if (_scroll.hasClients) {
      unawaited(
        _scroll.animateTo(
          0,
          duration: const Duration(milliseconds: 280),
          curve: Curves.easeOutCubic,
        ),
      );
    }
  }

  static TimeOfDay _timeOf(Map<String, dynamic> settings) {
    final parts = (asJsonString(settings['localTime']) ?? '19:00:00').split(
      ':',
    );
    return TimeOfDay(
      hour: int.tryParse(parts.first) ?? 19,
      minute: parts.length > 1 ? int.tryParse(parts[1]) ?? 0 : 0,
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Weekly review'),
      actions: [
        if (_overview != null)
          HeaderAction(
            label: 'Write now',
            icon: PhosphorIconsRegular.sparkle,
            busy: _generating,
            collapsesWhenNarrow: true,
            onPressed: () => unawaited(_generate()),
          ),
      ],
    ),
    body: _body(),
  );

  Widget _body() {
    if (_loading && _overview == null) return const SkeletonList(shape: SkeletonShape.detail);
    if (_overview == null) {
      return ErrorState(
        message: _error ?? 'Could not load your weekly review.',
        onRetry: () => unawaited(_load()),
      );
    }
    final reviews = _reviews;
    final review = reviews.isEmpty
        ? null
        : reviews[_selected.clamp(0, reviews.length - 1)];
    final stats = jsonObject(review?['stats']);
    final trend = [
      for (final point in jsonMaps(_overview?['trend']))
        if (parseWeekStart(point['weekStart']) case final week?)
          (
            week: week,
            mood: (point['mood'] as num?)?.toDouble(),
            energy: (point['energy'] as num?)?.toDouble(),
          ),
    ];
    final bottom = 32 + MediaQuery.paddingOf(context).bottom;
    return OrbRefresh(
      onRefresh: _load,
      child: ListView(
        controller: _scroll,
        physics: const AlwaysScrollableScrollPhysics(),
        padding: EdgeInsets.fromLTRB(16, 8, 16, bottom),
        children: [
          if (_error != null)
            ContentWidth(
              child: InlineNotice(
                message: _error!,
                tone: NoticeTone.danger,
                margin: const EdgeInsets.only(bottom: 14),
              ),
            ),
          ContentWidth(
            child: FadeSlideIn(
              child: review == null
                  ? _FirstReviewCard(
                      settings: _settings,
                      nextDeliveryAt: asJsonString(
                        _overview?['nextDeliveryAt'],
                      ),
                      busy: _generating,
                      onGenerate: () => unawaited(_generate()),
                    )
                  : _StoryCard(review: review, latest: _selected == 0),
            ),
          ),
          if (stats != null) ...[
            const SizedBox(height: 14),
            ContentWidth(
              child: FadeSlideIn(index: 1, child: _StatGrid(stats: stats)),
            ),
            if (jsonMaps(stats['days']).isNotEmpty) ...[
              const SizedBox(height: 14),
              ContentWidth(
                child: FadeSlideIn(
                  index: 2,
                  child: _WeekStrip(
                    weekStart: parseWeekStart(review?['weekStart']),
                    days: jsonMaps(stats['days']),
                    tags: jsonStrings(stats['topTags']),
                  ),
                ),
              ),
            ],
          ],
          const SizedBox(height: 26),
          ContentWidth(
            child: FadeSlideIn(index: 3, child: _TrendCard(points: trend)),
          ),
          if (reviews.length > 1) ...[
            const SizedBox(height: 26),
            ContentWidth(
              child: _EarlierWeeks(
                reviews: reviews,
                selected: _selected,
                onSelect: _select,
              ),
            ),
          ],
          const SizedBox(height: 26),
          ContentWidth(
            child: _DeliverySettings(
              settings: _settings,
              saving: _savingSettings,
              notice: _notice,
              onEnabled: (value) => unawaited(_saveSettings(enabled: value)),
              onChooseTime: () => unawaited(_chooseTime()),
              onUseDeviceZone: () => unawaited(_useDeviceZone()),
            ),
          ),
        ],
      ),
    );
  }
}

class _StoryCard extends StatelessWidget {
  const _StoryCard({required this.review, required this.latest});

  final Map<String, dynamic> review;
  final bool latest;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final start = parseWeekStart(review['weekStart']);
    final narrated = asJsonBool(review['narrated']);
    final story = asJsonString(review['story']) ?? '';
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(22, 22, 22, 24),
      elevated: true,
      gradient: LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [colors.accentSoft, colors.surface],
        stops: const [0, .7],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.sparkle, size: 34),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  start == null ? 'This week' : formatWeekRange(start),
                  style: TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                    color: colors.inkSoft,
                  ),
                ),
              ),
              const SizedBox(width: 8),
              Flexible(
                child: narrated
                    ? StatusPill(label: 'By Jarvis', color: colors.accent)
                    : StatusPill(label: 'Summary', color: colors.muted),
              ),
            ],
          ),
          const SizedBox(height: 16),
          Text(
            latest ? 'Your week in review' : 'Looking back',
            style: JarvisType.displayOf(context).copyWith(fontSize: 30),
          ),
          const SizedBox(height: 10),
          SelectableText(
            story,
            style: TextStyle(fontSize: 16, height: 1.55, color: colors.ink),
          ),
        ],
      ),
    );
  }
}

class _FirstReviewCard extends StatelessWidget {
  const _FirstReviewCard({
    required this.settings,
    required this.nextDeliveryAt,
    required this.busy,
    required this.onGenerate,
  });

  final Map<String, dynamic> settings;
  final String? nextDeliveryAt;
  final bool busy;
  final VoidCallback onGenerate;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final next = DateTime.tryParse(nextDeliveryAt ?? '')?.toLocal();
    final enabled = asJsonBool(settings['enabled']);
    final when = next == null ? null : friendlyWhen(context, next);
    return SurfaceCard(
      padding: const EdgeInsets.all(24),
      gradient: LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [colors.accentSoft, colors.surface],
        stops: const [0, .7],
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const IconBadge(icon: PhosphorIconsRegular.chartLine, size: 44),
          const SizedBox(height: 18),
          Text(
            'Your week, in one look',
            style: JarvisType.displayOf(context).copyWith(fontSize: 30),
          ),
          const SizedBox(height: 10),
          Text(
            enabled && when != null
                ? 'Your first review arrives $when. Jarvis looks at your '
                      'journal, finished tasks, reminders, and what it learned '
                      'about you, and writes a short story about the week.'
                : 'Every Sunday evening Jarvis can look at your journal, '
                      'finished tasks, reminders, and what it learned about '
                      'you, and write a short story about the week.',
            style: TextStyle(fontSize: 15, height: 1.5, color: colors.inkSoft),
          ),
          const SizedBox(height: 18),
          FilledButton.icon(
            key: const Key('weekly-review-generate'),
            onPressed: busy ? null : onGenerate,
            icon: busy
                ? const SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(PhosphorIconsRegular.sparkle, size: 18),
            label: Text(busy ? 'Writing' : 'Write this week\'s review now'),
          ),
        ],
      ),
    );
  }
}

class _StatGrid extends StatelessWidget {
  const _StatGrid({required this.stats});

  final Map<String, dynamic> stats;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final days = jsonMaps(stats['days']).length;
    final tiles = [
      _Stat(
        icon: PhosphorIconsRegular.heart,
        color: colors.success,
        value: formatAverage(stats['mood']),
        unit: '/5',
        label: 'Mood',
        detail: changeLabel(stats['mood'], stats['previousMood']),
      ),
      _Stat(
        icon: PhosphorIconsRegular.lightning,
        color: colors.warning,
        value: formatAverage(stats['energy']),
        unit: '/5',
        label: 'Energy',
      ),
      _Stat(
        icon: PhosphorIconsRegular.pulse,
        color: colors.danger,
        value: formatAverage(stats['stress']),
        unit: '/5',
        label: 'Stress',
      ),
      _Stat(
        icon: PhosphorIconsRegular.star,
        color: colors.accent,
        value: formatAverage(stats['rating']),
        unit: '/10',
        label: 'Day rating',
      ),
      _Stat(
        icon: PhosphorIconsRegular.listChecks,
        color: colors.info,
        value: '${asJsonInt(stats['tasksCompleted'])}',
        label: 'Tasks finished',
      ),
      _Stat(
        icon: PhosphorIconsRegular.bell,
        color: colors.violet,
        value: '${asJsonInt(stats['remindersHandled'])}',
        label: 'Reminders handled',
      ),
      _Stat(
        icon: PhosphorIconsRegular.brain,
        color: colors.rose,
        value: '${asJsonInt(stats['newMemories'])}',
        label: 'New memories',
      ),
      if (asJsonInt(stats['decisionsResolved']) > 0)
        _Stat(
          icon: PhosphorIconsRegular.hourglassMedium,
          color: colors.success,
          value: '${asJsonInt(stats['decisionsResolved'])}',
          label: 'Decisions settled',
          detail: brierChangeLabel(
            stats['brierScore'],
            stats['previousBrierScore'],
          ),
        ),
      _Stat(
        icon: PhosphorIconsRegular.notebook,
        color: colors.sky,
        value: '$days',
        unit: '/7',
        label: 'Days journaled',
      ),
    ];
    return LayoutBuilder(
      builder: (context, constraints) {
        final columns = constraints.maxWidth >= 600 ? 4 : 2;
        const gap = 10.0;
        final width = (constraints.maxWidth - gap * (columns - 1)) / columns;
        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [
            for (final tile in tiles) SizedBox(width: width, child: tile),
          ],
        );
      },
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({
    required this.icon,
    required this.color,
    required this.value,
    required this.label,
    this.unit,
    this.detail,
  });

  final IconData icon;
  final Color color;
  final String value;
  final String label;
  final String? unit;
  final String? detail;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    return Semantics(
      label: '$label: $value${unit ?? ''}${detail == null ? '' : ', $detail'}',
      excludeSemantics: true,
      child: SurfaceCard(
        padding: const EdgeInsets.fromLTRB(14, 14, 14, 14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(icon, size: 16, color: color),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    label,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Text.rich(
              TextSpan(
                children: [
                  TextSpan(text: value, style: theme.textTheme.headlineSmall),
                  if (unit != null && value != '–')
                    TextSpan(
                      text: unit,
                      style: TextStyle(fontSize: 13, color: colors.muted),
                    ),
                ],
              ),
            ),
            if (detail != null) ...[
              const SizedBox(height: 2),
              Text(
                detail!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(fontSize: 12, color: colors.muted),
              ),
            ],
          ],
        ),
      ),
    );
  }
}

/// Seven columns, Monday to Sunday: the mood face for journaled days.
class _WeekStrip extends StatelessWidget {
  const _WeekStrip({
    required this.weekStart,
    required this.days,
    required this.tags,
  });

  final DateTime? weekStart;
  final List<Map<String, dynamic>> days;
  final List<String> tags;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final byDate = {
      for (final day in days) ?parseJournalDate(day['date']): day,
    };
    return SurfaceCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Day by day', style: Theme.of(context).textTheme.titleSmall),
          const SizedBox(height: 14),
          Row(
            children: [
              for (var index = 0; index < 7; index++)
                Expanded(
                  child: _DayDot(
                    initial: weekdayInitials[index],
                    day: weekStart == null
                        ? null
                        : byDate[weekStart!.add(Duration(days: index))],
                  ),
                ),
            ],
          ),
          if (tags.isNotEmpty) ...[
            const SizedBox(height: 16),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final tag in tags)
                  StatusPill(label: '#$tag', color: colors.inkSoft),
              ],
            ),
          ],
        ],
      ),
    );
  }
}

class _DayDot extends StatelessWidget {
  const _DayDot({required this.initial, required this.day});

  final String initial;
  final Map<String, dynamic>? day;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final mood = day?['mood'];
    final rating = day?['rating'];
    final tone = switch (mood) {
      final int value when value >= 4 => colors.success,
      final int value when value == 3 => colors.warning,
      final int _ => colors.danger,
      _ => colors.accent,
    };
    return Column(
      children: [
        Text(initial, style: TextStyle(fontSize: 12, color: colors.muted)),
        const SizedBox(height: 8),
        AnimatedContainer(
          duration: const Duration(milliseconds: 220),
          width: 36,
          height: 36,
          alignment: Alignment.center,
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            color: day == null ? null : tone.withValues(alpha: .14),
            border: Border.all(
              color: day == null ? colors.outline : tone.withValues(alpha: .5),
            ),
          ),
          child: day == null
              ? null
              : Text(
                  mood is int ? moodEmoji(mood) : '✓',
                  style: TextStyle(fontSize: mood is int ? 17 : 14),
                ),
        ),
        const SizedBox(height: 6),
        Text(
          rating is int ? '$rating' : ' ',
          style: TextStyle(
            fontSize: 11.5,
            fontWeight: FontWeight.w600,
            color: colors.inkSoft,
          ),
        ),
      ],
    );
  }
}

class _TrendCard extends StatelessWidget {
  const _TrendCard({required this.points});

  final List<TrendPoint> points;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final hasMood = points.any((point) => point.mood != null);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader('Mood over ${points.length} weeks'),
        SurfaceCard(
          padding: const EdgeInsets.fromLTRB(14, 18, 18, 14),
          child: hasMood
              ? Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    MoodTrendChart(points: points),
                    const SizedBox(height: 10),
                    Wrap(
                      spacing: 16,
                      children: [
                        _Legend(color: colors.accent, label: 'Mood'),
                        _Legend(color: colors.sky, label: 'Energy'),
                      ],
                    ),
                  ],
                )
              : Padding(
                  padding: const EdgeInsets.all(6),
                  child: Text(
                    'Add a mood to your journal entries and the trend shows '
                    'up here, week by week.',
                    style: TextStyle(height: 1.5, color: colors.inkSoft),
                  ),
                ),
        ),
      ],
    );
  }
}

class _Legend extends StatelessWidget {
  const _Legend({required this.color, required this.label});

  final Color color;
  final String label;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Container(
        width: 14,
        height: 3,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(2),
        ),
      ),
      const SizedBox(width: 6),
      Text(
        label,
        style: TextStyle(fontSize: 12, color: JarvisColors.of(context).inkSoft),
      ),
    ],
  );
}

class _EarlierWeeks extends StatelessWidget {
  const _EarlierWeeks({
    required this.reviews,
    required this.selected,
    required this.onSelect,
  });

  final List<Map<String, dynamic>> reviews;
  final int selected;
  final ValueChanged<int> onSelect;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader('All reviews'),
        GroupedSection(
          children: [
            for (final (index, review) in reviews.indexed)
              ListTile(
                selected: index == selected,
                selectedTileColor: colors.accentSoft.withValues(alpha: .5),
                onTap: () => onSelect(index),
                title: Text(switch (parseWeekStart(review['weekStart'])) {
                  final start? => formatWeekRange(start),
                  null => 'Week',
                }, style: const TextStyle(fontWeight: FontWeight.w600)),
                subtitle: Text(
                  asJsonString(review['story']) ?? '',
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                ),
                trailing: switch (jsonObject(review['stats'])?['mood']) {
                  final num mood => Text(
                    '${moodEmoji(mood.round())} ${mood.toStringAsFixed(1)}',
                    style: TextStyle(color: colors.inkSoft),
                  ),
                  _ => null,
                },
              ),
          ],
        ),
      ],
    );
  }
}

class _DeliverySettings extends StatelessWidget {
  const _DeliverySettings({
    required this.settings,
    required this.saving,
    required this.notice,
    required this.onEnabled,
    required this.onChooseTime,
    required this.onUseDeviceZone,
  });

  final Map<String, dynamic> settings;
  final bool saving;
  final String? notice;
  final ValueChanged<bool> onEnabled;
  final VoidCallback onChooseTime;
  final VoidCallback onUseDeviceZone;

  @override
  Widget build(BuildContext context) {
    final enabled = asJsonBool(settings['enabled']);
    final time = _WeeklyReviewScreenState._timeOf(settings);
    final zone = asJsonString(settings['timeZoneId']) ?? 'UTC';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        const SectionHeader('Delivery'),
        GroupedSection(
          children: [
            SwitchListTile.adaptive(
              key: const Key('weekly-review-enabled'),
              title: const Text('Send every Sunday'),
              subtitle: const Text('A notification with your week in review'),
              value: enabled,
              onChanged: saving ? null : onEnabled,
            ),
            ListTile(
              enabled: !saving,
              title: const Text('Time'),
              subtitle: Text('Sunday at ${time.format(context)}'),
              trailing: const Icon(PhosphorIconsRegular.clock),
              onTap: onChooseTime,
            ),
            ListTile(
              enabled: !saving,
              title: const Text('Time zone'),
              subtitle: Text('$zone · tap to use this device\'s zone'),
              trailing: const Icon(PhosphorIconsRegular.globeSimple),
              onTap: onUseDeviceZone,
            ),
          ],
        ),
        if (notice != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(6, 10, 6, 0),
            child: Text(
              notice!,
              style: TextStyle(
                fontSize: 13,
                color: JarvisColors.of(context).inkSoft,
              ),
            ),
          ),
      ],
    );
  }
}
