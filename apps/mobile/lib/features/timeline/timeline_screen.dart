import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../expenses/expense_models.dart' show dayLabel;
import 'timeline_models.dart';

/// Everything that happened in the owner's life, newest first: journal,
/// spending, habits, people, finished tasks, reminders, what Jarvis learned,
/// and chats. Above it sit patterns Jarvis found and "on this day" memories.
class TimelineScreen extends StatefulWidget {
  const TimelineScreen({required this.http, this.now, super.key});

  final Dio http;

  /// Fixed clock for tests.
  final DateTime? now;

  @override
  State<TimelineScreen> createState() => _TimelineScreenState();
}

class _TimelineScreenState extends State<TimelineScreen> {
  static const _ranges = [
    (label: '7 days', days: 7),
    (label: '30 days', days: 30),
    (label: '90 days', days: 90),
    (label: 'Year', days: 365),
  ];

  final _search = TextEditingController();
  int _rangeDays = 30;
  final Set<String> _kinds = {};
  TimelineData? _data;
  List<TimelineInsightData> _insights = const [];
  List<TimelineYearData> _years = const [];
  bool _loading = true;
  String? _error;
  int _revision = 0;

  DateTime get _now => widget.now ?? DateTime.now();

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  static String _iso(DateTime day) =>
      '${day.year.toString().padLeft(4, '0')}-'
      '${day.month.toString().padLeft(2, '0')}-'
      '${day.day.toString().padLeft(2, '0')}';

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() => _loading = true);
    final today = DateTime(_now.year, _now.month, _now.day);
    final query = <String, dynamic>{
      'from': _iso(today.subtract(Duration(days: _rangeDays - 1))),
      'to': _iso(today),
      if (_kinds.isNotEmpty) 'kinds': (_kinds.toList()..sort()).join(','),
      if (_search.text.trim().isNotEmpty) 'q': _search.text.trim(),
    };
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/timeline',
        queryParameters: query,
      );
      if (!mounted || revision != _revision) return;
      final data = TimelineData.fromJson(response.data);
      setState(() {
        _data = data;
        _loading = false;
        _error = data == null ? 'Could not load your timeline.' : null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your timeline.';
      });
    }
    unawaited(_loadExtras(revision));
  }

  // Patterns and anniversaries are nice to have; the timeline works without them.
  Future<void> _loadExtras(int revision) async {
    try {
      final insights = await widget.http.get<dynamic>(
        '/api/v1/timeline/insights',
      );
      final years = await widget.http.get<dynamic>(
        '/api/v1/timeline/on-this-day',
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _insights = TimelineInsightData.listFromJson(insights.data);
        _years = TimelineYearData.listFromJson(years.data);
      });
    } on DioException {
      // Ignored: the extras stay hidden.
    }
  }

  void _toggleKind(String kind) {
    setState(() {
      if (!_kinds.add(kind)) _kinds.remove(kind);
    });
    unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    final data = _data;
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Timeline')),
      body: ContentWidth(
        child: ListScreenBody(
          loading: _loading,
          error: _error,
          isEmpty: data == null || data.days.isEmpty,
          onRetry: () => unawaited(_load()),
          empty: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
            children: [
              _controls(colors),
              const SizedBox(height: 40),
              const EmptyState(
                icon: PhosphorIconsRegular.clockCounterClockwise,
                title: 'Nothing on your timeline yet',
                message:
                    'Journal entries, expenses, habits, and finished tasks '
                    'show up here as you use Jarvis.',
              ),
            ],
          ),
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
            children: [
              _controls(colors),
              if (_insights.isNotEmpty) _insightsCard(colors),
              if (_years.isNotEmpty) _onThisDayCard(colors),
              if (data != null && data.failedKinds.isNotEmpty)
                InlineNotice(
                  message:
                      'Could not read ${data.failedKinds.map(timelineKindLabel).join(', ')}.',
                  tone: NoticeTone.warning,
                  margin: const EdgeInsets.only(bottom: 8),
                ),
              if (data != null)
                for (final day in data.days) ..._day(colors, day),
              if (data != null && data.truncated)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 12),
                  child: Text(
                    'Showing the newest ${data.days.fold<int>(0, (sum, d) => sum + d.events.length)} '
                    'of ${data.total}. Narrow the dates or kinds to see more.',
                    textAlign: TextAlign.center,
                    style: TextStyle(color: colors.muted, fontSize: 13),
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _controls(JarvisColors colors) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      TextField(
        key: const Key('timeline-search'),
        controller: _search,
        textInputAction: TextInputAction.search,
        onSubmitted: (_) => unawaited(_load()),
        decoration: InputDecoration(
          hintText: 'Search your timeline',
          prefixIcon: const Icon(PhosphorIconsRegular.magnifyingGlass),
          suffixIcon: _search.text.isEmpty
              ? null
              : IconButton(
                  tooltip: 'Clear',
                  icon: const Icon(PhosphorIconsRegular.x),
                  onPressed: () {
                    _search.clear();
                    unawaited(_load());
                  },
                ),
        ),
      ),
      const SizedBox(height: 10),
      Wrap(
        spacing: 8,
        runSpacing: 4,
        children: [
          for (final range in _ranges)
            ChoiceChip(
              key: Key('timeline-range-${range.days}'),
              label: Text(range.label),
              selected: _rangeDays == range.days,
              onSelected: (_) {
                setState(() => _rangeDays = range.days);
                unawaited(_load());
              },
            ),
        ],
      ),
      const SizedBox(height: 6),
      Wrap(
        spacing: 8,
        runSpacing: 4,
        children: [
          for (final kind in timelineKinds)
            FilterChip(
              key: Key('timeline-kind-$kind'),
              avatar: Icon(
                timelineKindIcon(kind),
                size: 16,
                color: timelineKindColor(colors, kind),
              ),
              label: Text(timelineKindLabel(kind)),
              selected: _kinds.contains(kind),
              onSelected: (_) => _toggleKind(kind),
            ),
        ],
      ),
      const SizedBox(height: 12),
    ],
  );

  Widget _insightsCard(JarvisColors colors) => SurfaceCard(
    margin: const EdgeInsets.only(bottom: 12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SectionHeader('Patterns Jarvis noticed'),
        for (final insight in _insights.take(3))
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  insight.headline,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                if (insight.detail.isNotEmpty)
                  Text(
                    insight.detail,
                    style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
                  ),
              ],
            ),
          ),
        Text(
          'Patterns, not proof of cause.',
          style: TextStyle(color: colors.muted, fontSize: 12),
        ),
      ],
    ),
  );

  Widget _onThisDayCard(JarvisColors colors) => SurfaceCard(
    margin: const EdgeInsets.only(bottom: 12),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const SectionHeader('On this day'),
        for (final year in _years)
          if (year.events.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(bottom: 8),
              child: Text.rich(
                TextSpan(
                  children: [
                    TextSpan(
                      text: '${year.year}  ',
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                    TextSpan(
                      text: year.events.take(3).map((e) => e.title).join(' · '),
                    ),
                  ],
                ),
              ),
            ),
      ],
    ),
  );

  List<Widget> _day(JarvisColors colors, TimelineDayData day) => [
    Padding(
      padding: const EdgeInsets.fromLTRB(4, 14, 0, 6),
      child: Text(
        dayLabel(day.date, now: _now),
        style: TextStyle(
          fontWeight: FontWeight.w600,
          color: colors.inkSoft,
          fontSize: 13,
        ),
      ),
    ),
    for (final event in day.events)
      Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: SurfaceCard(
          key: Key('timeline-event-${event.id}'),
          padding: const EdgeInsets.all(12),
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              IconBadge(
                icon: timelineKindIcon(event.kind),
                color: timelineKindColor(colors, event.kind),
                size: 34,
              ),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      event.title,
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                    if (event.detail != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 2),
                        child: Text(
                          event.detail!,
                          style: TextStyle(
                            color: colors.inkSoft,
                            fontSize: 13.5,
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
  ];
}
