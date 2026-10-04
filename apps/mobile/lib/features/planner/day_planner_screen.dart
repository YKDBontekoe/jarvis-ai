import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/mcp_setup.dart';
import 'day_planner_format.dart';

/// Today: calendar events, reminders and planned focus blocks on one timeline,
/// with to-dos that "Plan my day" fits into the free time. The plan lives in
/// Jarvis; the calendar only changes through chat, where Jarvis asks first.
class DayPlannerScreen extends StatefulWidget {
  const DayPlannerScreen({required this.http, this.onAskInChat, super.key});

  final Dio http;

  /// Sends a prompt to chat (connect a calendar, copy blocks to it). Null
  /// hides those actions.
  final ValueChanged<String>? onAskInChat;

  @override
  State<DayPlannerScreen> createState() => _DayPlannerScreenState();
}

class _DayPlannerScreenState extends State<DayPlannerScreen> {
  static const _base = '/api/v1/planner/today';
  static const _durations = [15, 30, 45, 60, 90, 120];

  final _titleController = TextEditingController();
  final _titleFocus = FocusNode();
  Map<String, dynamic>? _today;
  String? _timeZone;
  bool _loading = true;
  bool _planning = false;
  bool _adding = false;
  String? _error;
  int _minutes = 30;
  int _revision = 0;
  final Set<String> _busyItems = {};

  @override
  void initState() {
    super.initState();
    unawaited(_start());
  }

  @override
  void dispose() {
    _titleController.dispose();
    _titleFocus.dispose();
    super.dispose();
  }

  Future<void> _start() async {
    _timeZone = await deviceTimeZoneLookup();
    await _load();
  }

  Map<String, dynamic> get _zone =>
      _timeZone == null ? const {} : {'timeZone': _timeZone};

  Future<void> _load() async {
    final revision = ++_revision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        _base,
        queryParameters: _zone,
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _today = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      _failLoad(revision, firstProblemMessage(error.response?.data));
    } catch (_) {
      _failLoad(revision, null);
    }
  }

  void _failLoad(int revision, String? message) {
    if (!mounted || revision != _revision) return;
    setState(() {
      _loading = false;
      _error = message ?? 'Could not load your day.';
    });
  }

  void _toast(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _problem(Object error, String fallback) => error is DioException
      ? firstProblemMessage(error.response?.data) ?? fallback
      : fallback;

  Future<void> _plan() async {
    if (_planning) return;
    setState(() => _planning = true);
    try {
      final response = await widget.http.post<dynamic>(
        '$_base/plan',
        queryParameters: _zone,
      );
      final result = jsonObject(response.data) ?? const {};
      if (!mounted) return;
      final today = jsonObject(result['today']);
      setState(() {
        if (today != null) _today = today;
        _error = null;
      });
      _toast(
        planResultMessage(
          asJsonInt(result['scheduled']),
          jsonMaps(
            result['didNotFit'],
          ).map((item) => asJsonString(item['title']) ?? 'A to-do').toList(),
        ),
      );
    } catch (error) {
      _toast(_problem(error, 'Could not plan your day.'));
    } finally {
      if (mounted) setState(() => _planning = false);
    }
  }

  Future<void> _clearPlan() async {
    try {
      final response = await widget.http.delete<dynamic>(
        '$_base/plan',
        queryParameters: _zone,
      );
      if (!mounted) return;
      setState(() => _today = jsonObject(response.data) ?? _today);
      _toast('Focus blocks cleared.');
    } catch (error) {
      _toast(_problem(error, 'Could not clear the plan.'));
    }
  }

  Future<void> _add() async {
    final title = _titleController.text.trim();
    if (title.isEmpty || _adding) return;
    setState(() => _adding = true);
    try {
      await widget.http.post<dynamic>(
        '$_base/items',
        queryParameters: _zone,
        data: {'title': title, 'minutes': _minutes},
      );
      _titleController.clear();
      await _load();
      _titleFocus.requestFocus();
    } catch (error) {
      _toast(_problem(error, 'Could not add that to-do.'));
    } finally {
      if (mounted) setState(() => _adding = false);
    }
  }

  Future<void> _toggle(Map<String, dynamic> item) async {
    final id = asJsonString(item['id']);
    if (id == null || _busyItems.contains(id)) return;
    setState(() => _busyItems.add(id));
    try {
      await widget.http.patch<dynamic>(
        '$_base/items/$id',
        queryParameters: _zone,
        data: {'done': !asJsonBool(item['done'])},
      );
      await _load();
    } catch (error) {
      _toast(_problem(error, 'Could not update that to-do.'));
    } finally {
      if (mounted) setState(() => _busyItems.remove(id));
    }
  }

  Future<void> _remove(Map<String, dynamic> item) async {
    final id = asJsonString(item['id']);
    if (id == null || _busyItems.contains(id)) return;
    setState(() => _busyItems.add(id));
    try {
      await widget.http.delete<dynamic>(
        '$_base/items/$id',
        queryParameters: _zone,
      );
      await _load();
    } catch (error) {
      _toast(_problem(error, 'Could not remove that to-do.'));
    } finally {
      if (mounted) setState(() => _busyItems.remove(id));
    }
  }

  Future<void> _editHours() async {
    final today = _today;
    if (today == null) return;
    final start = minutesOfDay(today['dayStart']) ?? 8 * 60;
    final end = minutesOfDay(today['dayEnd']) ?? 18 * 60;
    final picked = await showModalBottomSheet<RangeValues>(
      context: context,
      showDragHandle: true,
      builder: (_) => _HoursSheet(start: start, end: end),
    );
    if (picked == null || !mounted) return;
    try {
      final response = await widget.http.put<dynamic>(
        '$_base/hours',
        queryParameters: _zone,
        data: {
          'dayStart': timeOfDayValue(picked.start.round()),
          'dayEnd': timeOfDayValue(picked.end.round()),
        },
      );
      if (!mounted) return;
      setState(() => _today = jsonObject(response.data) ?? _today);
    } catch (error) {
      _toast(_problem(error, 'Could not save your hours.'));
    }
  }

  void _ask(String prompt) {
    final ask = widget.onAskInChat;
    if (ask == null) return;
    Navigator.of(context).pop();
    ask(prompt);
  }

  @override
  Widget build(BuildContext context) {
    final today = _today;
    final hasOpen = jsonMaps(
      today?['items'],
    ).any((item) => !asJsonBool(item['done']));
    return Scaffold(
      appBar: AppBar(
        title: const Text('Today'),
        actions: [
          IconButton(
            tooltip: 'Day hours',
            onPressed: today == null ? null : () => unawaited(_editHours()),
            icon: const Icon(PhosphorIconsRegular.sliders, size: 20),
          ),
          HeaderAction(
            key: const Key('planner-plan'),
            label: 'Plan my day',
            icon: PhosphorIconsRegular.magicWand,
            busy: _planning,
            onPressed: today == null || !hasOpen
                ? null
                : () => unawaited(_plan()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: today == null,
        onRetry: () => unawaited(_load()),
        onRefresh: _load,
        empty: const SizedBox.shrink(),
        child: today == null ? const SizedBox.shrink() : _content(today),
      ),
    );
  }

  Widget _content(Map<String, dynamic> today) {
    final colors = JarvisColors.of(context);
    final entries = jsonMaps(today['entries']);
    final items = jsonMaps(today['items']);
    final date = DateTime.tryParse(asJsonString(today['date']) ?? '');
    final freeMinutes = asJsonInt(today['freeMinutes']);
    final events = entries.where((entry) => entry['kind'] == 'event').length;
    final focus = entries.where((entry) => entry['kind'] == 'focus').toList();
    final connected = asJsonBool(today['calendarConnected']);
    final unavailable = asJsonBool(today['calendarUnavailable']);
    final summary = [
      if (connected) events == 1 ? '1 event' : '$events events',
      if (focus.isNotEmpty)
        focus.length == 1 ? '1 focus block' : '${focus.length} focus blocks',
      '${durationLabel(freeMinutes)} free',
    ].join('  ·  ');

    return ListView(
      padding: const EdgeInsets.fromLTRB(20, 8, 20, 40),
      children: [
        ContentWidth(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              FadeSlideIn(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      date == null ? 'Today' : longDayLabel(date),
                      key: const Key('planner-date'),
                      style: JarvisType.displayOf(
                        context,
                      ).copyWith(fontSize: 34),
                    ),
                    const SizedBox(height: 6),
                    Text(
                      summary,
                      key: const Key('planner-summary'),
                      style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
                    ),
                    const SizedBox(height: 16),
                    _DayStrip(today: today),
                  ],
                ),
              ),
              if (!connected && widget.onAskInChat != null) ...[
                const SizedBox(height: 16),
                InlineNotice(
                  key: const Key('planner-connect'),
                  tone: NoticeTone.info,
                  message:
                      'Connect a calendar so your events show up and Jarvis plans around them.',
                  actions: [
                    TextButton(
                      onPressed: () => _ask(mcpCalendarPrompt),
                      child: const Text('Connect'),
                    ),
                  ],
                ),
              ] else if (unavailable) ...[
                const SizedBox(height: 16),
                const InlineNotice(
                  key: Key('planner-calendar-down'),
                  message:
                      'Your calendar could not be read just now, so events are missing.',
                ),
              ],
              const SizedBox(height: 28),
              SectionHeader(
                'Timeline',
                padding: const EdgeInsets.only(left: 4, bottom: 8),
                trailing: focus.isEmpty
                    ? null
                    : TextButton(
                        key: const Key('planner-clear'),
                        onPressed: () => unawaited(_clearPlan()),
                        child: const Text('Clear blocks'),
                      ),
              ),
              _Timeline(entries: entries, onToggle: _toggleEntry),
              if (focus.isNotEmpty && widget.onAskInChat != null) ...[
                const SizedBox(height: 8),
                Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton.icon(
                    key: const Key('planner-to-calendar'),
                    onPressed: () => _ask(addPlanToCalendarPrompt),
                    icon: const Icon(
                      PhosphorIconsRegular.calendarBlank,
                      size: 16,
                    ),
                    label: const Text('Add focus blocks to my calendar'),
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.only(left: 12),
                  child: Text(
                    'Jarvis asks for your approval before it changes your calendar.',
                    style: TextStyle(color: colors.muted, fontSize: 12.5),
                  ),
                ),
              ],
              const SizedBox(height: 28),
              SectionHeader(
                'To-dos',
                padding: const EdgeInsets.only(left: 4, bottom: 8),
              ),
              _composer(),
              const SizedBox(height: 12),
              if (items.isEmpty)
                Padding(
                  padding: const EdgeInsets.fromLTRB(4, 4, 4, 0),
                  child: Text(
                    'Add what you want to get done today. '
                    'Plan my day fits it around your calendar.',
                    style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
                  ),
                )
              else
                GroupedSection(
                  dividerIndent: 52,
                  children: [
                    for (final item in items)
                      _TodoRow(
                        key: ValueKey('todo-${item['id']}'),
                        item: item,
                        busy: _busyItems.contains(asJsonString(item['id'])),
                        onToggle: () => unawaited(_toggle(item)),
                        onRemove: () => unawaited(_remove(item)),
                      ),
                  ],
                ),
            ],
          ),
        ),
      ],
    );
  }

  void _toggleEntry(Map<String, dynamic> entry) {
    final id = asJsonString(entry['id']);
    final item = jsonMaps(
      _today?['items'],
    ).where((item) => item['id'] == id).firstOrNull;
    if (item != null) unawaited(_toggle(item));
  }

  Widget _composer() {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(16, 10, 12, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Expanded(
                child: TextField(
                  key: const Key('planner-new-title'),
                  controller: _titleController,
                  focusNode: _titleFocus,
                  maxLength: 200,
                  textInputAction: TextInputAction.done,
                  onSubmitted: (_) => unawaited(_add()),
                  decoration: const InputDecoration(
                    hintText: 'Add a to-do',
                    border: InputBorder.none,
                    enabledBorder: InputBorder.none,
                    focusedBorder: InputBorder.none,
                    filled: false,
                    counterText: '',
                    isDense: true,
                  ),
                ),
              ),
              IconButton.filled(
                key: const Key('planner-add'),
                tooltip: 'Add to-do',
                onPressed: _adding ? null : () => unawaited(_add()),
                icon: _adding
                    ? const SizedBox.square(
                        dimension: 16,
                        child: CircularProgressIndicator(strokeWidth: 1.8),
                      )
                    : const Icon(PhosphorIconsRegular.plus, size: 18),
              ),
            ],
          ),
          const SizedBox(height: 4),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: [
                Icon(PhosphorIconsRegular.timer, size: 16, color: colors.muted),
                const SizedBox(width: 8),
                for (final minutes in _durations)
                  Padding(
                    padding: const EdgeInsets.only(right: 6),
                    child: ChoiceChip(
                      key: Key('planner-minutes-$minutes'),
                      label: Text(durationLabel(minutes)),
                      selected: _minutes == minutes,
                      showCheckmark: false,
                      visualDensity: VisualDensity.compact,
                      onSelected: (_) => setState(() => _minutes = minutes),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}

/// A slim bar for the day hours: calendar time, focus blocks and now.
class _DayStrip extends StatelessWidget {
  const _DayStrip({required this.today});

  final Map<String, dynamic> today;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final date = DateTime.tryParse(asJsonString(today['date']) ?? '');
    final startMinutes = minutesOfDay(today['dayStart']) ?? 8 * 60;
    final endMinutes = minutesOfDay(today['dayEnd']) ?? 18 * 60;
    if (date == null || endMinutes <= startMinutes) {
      return const SizedBox.shrink();
    }
    final dayStart = DateTime(
      date.year,
      date.month,
      date.day,
    ).add(Duration(minutes: startMinutes));
    final span = (endMinutes - startMinutes).toDouble();
    double position(DateTime time) =>
        (time.toLocal().difference(dayStart).inMinutes / span).clamp(0, 1);

    final segments = <(double, double, Color)>[];
    for (final entry in jsonMaps(today['entries'])) {
      final kind = entry['kind'];
      if (kind == 'reminder') continue;
      final start = jsonDate(entry['startAt'], local: true);
      final end = jsonDate(entry['endAt'], local: true);
      if (start == null || end == null) continue;
      if (end.difference(start).inHours >= 20) continue;
      final from = position(start);
      final to = position(end);
      if (to <= from) continue;
      segments.add((
        from,
        to,
        kind == 'focus' ? colors.accent : colors.inkSoft.withValues(alpha: .45),
      ));
    }
    final now = DateTime.now();
    final nowAt = now.isAfter(dayStart) ? position(now) : null;

    return Semantics(
      label: 'Day overview',
      excludeSemantics: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            height: 14,
            child: LayoutBuilder(
              builder: (context, constraints) {
                final width = constraints.maxWidth;
                return Stack(
                  clipBehavior: Clip.none,
                  children: [
                    Positioned.fill(
                      top: 3,
                      bottom: 3,
                      child: DecoratedBox(
                        decoration: BoxDecoration(
                          color: colors.surfaceMuted,
                          borderRadius: BorderRadius.circular(4),
                          border: Border.all(color: colors.outline),
                        ),
                      ),
                    ),
                    for (final (from, to, color) in segments)
                      Positioned(
                        left: from * width,
                        width: ((to - from) * width).clamp(3, width),
                        top: 3,
                        bottom: 3,
                        child: DecoratedBox(
                          decoration: BoxDecoration(
                            color: color,
                            borderRadius: BorderRadius.circular(4),
                          ),
                        ),
                      ),
                    if (nowAt != null && nowAt < 1)
                      Positioned(
                        left: nowAt * width - 1,
                        width: 2,
                        top: 0,
                        bottom: 0,
                        child: DecoratedBox(
                          decoration: BoxDecoration(
                            color: colors.danger,
                            borderRadius: BorderRadius.circular(1),
                          ),
                        ),
                      ),
                  ],
                );
              },
            ),
          ),
          const SizedBox(height: 6),
          Row(
            children: [
              Text(
                _hm(startMinutes),
                style: TextStyle(color: colors.muted, fontSize: 11.5),
              ),
              const Spacer(),
              _Legend(
                color: colors.inkSoft.withValues(alpha: .45),
                label: 'Calendar',
              ),
              const SizedBox(width: 12),
              _Legend(color: colors.accent, label: 'Focus'),
              const Spacer(),
              Text(
                _hm(endMinutes),
                style: TextStyle(color: colors.muted, fontSize: 11.5),
              ),
            ],
          ),
        ],
      ),
    );
  }

  static String _hm(int minutes) =>
      '${(minutes ~/ 60).toString().padLeft(2, '0')}:'
      '${(minutes % 60).toString().padLeft(2, '0')}';
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
        width: 8,
        height: 8,
        decoration: BoxDecoration(
          color: color,
          borderRadius: BorderRadius.circular(2),
        ),
      ),
      const SizedBox(width: 5),
      Text(
        label,
        style: TextStyle(color: JarvisColors.of(context).muted, fontSize: 11.5),
      ),
    ],
  );
}

/// Time-ordered rows with a rail; a "Now" marker splits past and future.
class _Timeline extends StatelessWidget {
  const _Timeline({required this.entries, required this.onToggle});

  final List<Map<String, dynamic>> entries;
  final ValueChanged<Map<String, dynamic>> onToggle;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    if (entries.isEmpty) {
      return SurfaceCard(
        child: Row(
          children: [
            const IconBadge(icon: PhosphorIconsRegular.sunHorizon, size: 40),
            const SizedBox(width: 14),
            Expanded(
              child: Text(
                'Nothing scheduled yet. Your events, reminders and focus blocks show up here.',
                style: TextStyle(color: colors.inkSoft, height: 1.4),
              ),
            ),
          ],
        ),
      );
    }
    final now = DateTime.now();
    final allDay = <Map<String, dynamic>>[];
    final timed = <Map<String, dynamic>>[];
    for (final entry in entries) {
      final start = jsonDate(entry['startAt'], local: true);
      final end = jsonDate(entry['endAt'], local: true);
      if (start != null && end != null && end.difference(start).inHours >= 20) {
        allDay.add(entry);
      } else {
        timed.add(entry);
      }
    }
    final rows = <Widget>[];
    var nowShown = false;
    for (final entry in timed) {
      final start = jsonDate(entry['startAt'], local: true);
      if (!nowShown && start != null && start.isAfter(now)) {
        if (rows.isNotEmpty) rows.add(_NowMarker(time: now));
        nowShown = true;
      }
      rows.add(
        _TimelineRow(
          entry: entry,
          past: _isPast(entry, now),
          onToggle: entry['kind'] == 'focus' ? () => onToggle(entry) : null,
        ),
      );
    }
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (allDay.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final entry in allDay)
                  StatusPill(
                    label:
                        'All day · ${asJsonString(entry['title']) ?? 'Event'}',
                    color: colors.sky,
                  ),
              ],
            ),
          ),
        ...rows,
      ],
    );
  }

  static bool _isPast(Map<String, dynamic> entry, DateTime now) {
    final end =
        jsonDate(entry['endAt'], local: true) ??
        jsonDate(entry['startAt'], local: true);
    return end != null && end.isBefore(now);
  }
}

class _NowMarker extends StatelessWidget {
  const _NowMarker({required this.time});

  final DateTime time;

  @override
  Widget build(BuildContext context) {
    final danger = JarvisColors.of(context).danger;
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 4),
      child: Row(
        children: [
          SizedBox(
            width: 52,
            child: Text(
              clockLabel(time),
              style: TextStyle(
                color: danger,
                fontSize: 12,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          Container(
            width: 8,
            height: 8,
            decoration: BoxDecoration(color: danger, shape: BoxShape.circle),
          ),
          Expanded(child: Container(height: 1.5, color: danger)),
        ],
      ),
    );
  }
}

class _TimelineRow extends StatelessWidget {
  const _TimelineRow({required this.entry, required this.past, this.onToggle});

  final Map<String, dynamic> entry;
  final bool past;
  final VoidCallback? onToggle;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final kind = asJsonString(entry['kind']) ?? 'event';
    final start = jsonDate(entry['startAt'], local: true);
    final end = jsonDate(entry['endAt'], local: true);
    final done = asJsonBool(entry['done']);
    final title = asJsonString(entry['title']) ?? 'Untitled';
    final (icon, tint, label) = switch (kind) {
      'focus' => (
        PhosphorIconsRegular.lightning,
        colors.accent,
        end == null || start == null
            ? 'Focus'
            : 'Focus · ${durationLabel(end.difference(start).inMinutes)}',
      ),
      'reminder' => (PhosphorIconsRegular.bell, colors.violet, 'Reminder'),
      _ => (PhosphorIconsRegular.calendarBlank, colors.inkSoft, 'Calendar'),
    };
    final dim = past || (kind != 'event' && done);
    return Opacity(
      opacity: dim ? .55 : 1,
      child: Padding(
        padding: const EdgeInsets.only(bottom: 8),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 52,
              child: Padding(
                padding: const EdgeInsets.only(top: 14),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      start == null ? '' : clockLabel(start),
                      style: TextStyle(
                        color: colors.ink,
                        fontSize: 13,
                        fontWeight: FontWeight.w600,
                        fontFeatures: const [FontFeature.tabularFigures()],
                      ),
                    ),
                    if (end != null)
                      Text(
                        clockLabel(end),
                        style: TextStyle(
                          color: colors.muted,
                          fontSize: 12,
                          fontFeatures: const [FontFeature.tabularFigures()],
                        ),
                      ),
                  ],
                ),
              ),
            ),
            Expanded(
              child: SurfaceCard(
                padding: const EdgeInsets.fromLTRB(0, 12, 12, 12),
                onTap: onToggle,
                child: IntrinsicHeight(
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Container(
                        width: 3,
                        margin: const EdgeInsets.only(right: 12),
                        decoration: BoxDecoration(
                          color: tint,
                          borderRadius: const BorderRadius.horizontal(
                            right: Radius.circular(3),
                          ),
                        ),
                      ),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          mainAxisAlignment: MainAxisAlignment.center,
                          children: [
                            Text(
                              title,
                              maxLines: 2,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.titleSmall
                                  ?.copyWith(
                                    decoration: kind == 'focus' && done
                                        ? TextDecoration.lineThrough
                                        : null,
                                  ),
                            ),
                            const SizedBox(height: 3),
                            Row(
                              children: [
                                Icon(icon, size: 13, color: tint),
                                const SizedBox(width: 5),
                                Flexible(
                                  child: Text(
                                    label,
                                    style: TextStyle(
                                      color: colors.inkSoft,
                                      fontSize: 12.5,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                      if (onToggle != null)
                        Center(
                          child: Icon(
                            done
                                ? PhosphorIconsRegular.checkCircle
                                : PhosphorIconsRegular.circle,
                            size: 22,
                            color: done ? colors.success : colors.muted,
                            semanticLabel: done ? 'Done' : 'Not done',
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _TodoRow extends StatelessWidget {
  const _TodoRow({
    required this.item,
    required this.busy,
    required this.onToggle,
    required this.onRemove,
    super.key,
  });

  final Map<String, dynamic> item;
  final bool busy;
  final VoidCallback onToggle;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final done = asJsonBool(item['done']);
    final minutes = asJsonInt(item['minutes']);
    final start = jsonDate(item['startAt'], local: true);
    final title = asJsonString(item['title']) ?? 'Untitled';
    final when = done
        ? 'Done'
        : start == null
        ? 'Not planned yet'
        : 'Planned ${clockLabel(start)}';
    return InkWell(
      onTap: busy ? null : onToggle,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(14, 10, 4, 10),
        child: Row(
          children: [
            SizedBox.square(
              dimension: 24,
              child: busy
                  ? const Padding(
                      padding: EdgeInsets.all(3),
                      child: CircularProgressIndicator(strokeWidth: 1.8),
                    )
                  : Icon(
                      done
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.circle,
                      size: 24,
                      color: done ? colors.success : colors.muted,
                      semanticLabel: done ? 'Done' : 'Not done',
                    ),
            ),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    title,
                    style: TextStyle(
                      color: done ? colors.muted : colors.ink,
                      fontSize: 15,
                      decoration: done ? TextDecoration.lineThrough : null,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    '${durationLabel(minutes)}  ·  $when',
                    style: TextStyle(color: colors.inkSoft, fontSize: 12.5),
                  ),
                ],
              ),
            ),
            IconButton(
              tooltip: 'Remove $title',
              onPressed: busy ? null : onRemove,
              icon: Icon(PhosphorIconsRegular.x, size: 16, color: colors.muted),
            ),
          ],
        ),
      ),
    );
  }
}

/// Picks the hours Jarvis may plan in, in half-hour steps.
class _HoursSheet extends StatefulWidget {
  const _HoursSheet({required this.start, required this.end});

  final int start;
  final int end;

  @override
  State<_HoursSheet> createState() => _HoursSheetState();
}

class _HoursSheetState extends State<_HoursSheet> {
  late RangeValues _values = RangeValues(
    widget.start.toDouble(),
    widget.end.toDouble().clamp(widget.start + 60, 24 * 60 - 30),
  );

  String _label(double minutes) {
    final value = minutes.round();
    return '${(value ~/ 60).toString().padLeft(2, '0')}:'
        '${(value % 60).toString().padLeft(2, '0')}';
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(24, 0, 24, 20),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text('Day hours', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 4),
          Text(
            'Plan my day only uses time between ${_label(_values.start)} and ${_label(_values.end)}.',
            style: TextStyle(color: JarvisColors.of(context).inkSoft),
          ),
          const SizedBox(height: 12),
          RangeSlider(
            values: _values,
            min: 0,
            max: 24 * 60 - 30,
            divisions: 47,
            labels: RangeLabels(_label(_values.start), _label(_values.end)),
            onChanged: (values) {
              if (values.end - values.start < 60) return;
              setState(() => _values = values);
            },
          ),
          const SizedBox(height: 8),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(_values),
            child: const Text('Save'),
          ),
        ],
      ),
    ),
  );
}
