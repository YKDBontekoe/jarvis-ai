import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../entities/entity_ref.dart';
import '../entities/entity_screen.dart';

String _iso(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-${date.month.toString().padLeft(2, '0')}-${date.day.toString().padLeft(2, '0')}';

String _day(DateTime date) {
  const names = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${names[date.weekday - 1]} ${date.day} ${months[date.month - 1]}';
}

/// What the week left behind, counted from your own records, and up to three optional next steps. Reading it creates
/// nothing; accepting a step changes exactly one thing, on the day you pick.
class FollowThroughCard extends StatefulWidget {
  const FollowThroughCard({required this.http, this.weekStart, super.key});

  final Dio http;

  /// The Monday of the week to show (`yyyy-MM-dd`); the current week when null.
  final String? weekStart;

  @override
  State<FollowThroughCard> createState() => _FollowThroughCardState();
}

class _FollowThroughCardState extends State<FollowThroughCard> {
  Map<String, dynamic>? _data;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(FollowThroughCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.weekStart != widget.weekStart) unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/reviews/weekly/follow-through',
        queryParameters: {
          if (widget.weekStart != null) 'weekStart': widget.weekStart,
        },
      );
      if (!mounted) return;
      setState(() {
        _data = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Could not load the follow-through.';
      });
    }
  }

  void _say(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _accept(Map<String, dynamic> suggestion) async {
    final kind = asJsonString(suggestion['kind']) ?? '';
    final choice = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _AcceptSheet(suggestion: suggestion),
    );
    if (choice == null || !mounted || _busy) return;
    setState(() => _busy = true);
    try {
      await widget.http.post<dynamic>(
        '/api/v1/reviews/weekly/follow-through/accept',
        data: {'kind': kind, 'targetId': suggestion['targetId'], ...choice},
      );
      if (mounted) {
        _say(
          kind == 'plan_commitment'
              ? 'Moved to ${_day(DateTime.parse('${choice['on']}'))}.'
              : 'Next step saved.',
        );
        await _load();
      }
    } on DioException catch (error) {
      if (mounted) {
        _say(firstProblemMessage(error.response?.data) ?? 'Could not do that.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _dismiss(Map<String, dynamic> suggestion) async {
    try {
      await widget.http.post<dynamic>(
        '/api/v1/reviews/weekly/follow-through/dismiss',
        data: {'kind': suggestion['kind'], 'targetId': suggestion['targetId']},
      );
      if (mounted) await _load();
    } on DioException {
      if (mounted) _say('Could not put that away.');
    }
  }

  Future<void> _open(String ref) async {
    final entity = EntityRef.tryParse(ref);
    if (entity != null) await openEntity(context, widget.http, entity);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final data = _data;
    if (_loading && data == null) return const SizedBox(height: 4);
    if (data == null) {
      return Text(_error ?? '', style: TextStyle(color: colors.muted));
    }
    final suggestions = jsonMaps(data['suggestions']);
    final sections = <(String, List<Map<String, dynamic>>)>[
      ('Done this week', jsonMaps(data['fulfilledItems'])),
      ('Overdue for a week or more', jsonMaps(data['staleItems'])),
      ('Decisions to review next week', jsonMaps(data['nextWeekDecisions'])),
      ('Projects without a next step', jsonMaps(data['projectsNeedingStep'])),
    ];
    final quiet =
        asJsonInt(data['fulfilled']) == 0 &&
        asJsonInt(data['dropped']) == 0 &&
        asJsonInt(data['overdue']) == 0 &&
        asJsonInt(data['decisionsDueNextWeek']) == 0 &&
        sections.every((section) => section.$2.isEmpty);
    return SurfaceCard(
      key: const Key('follow-through'),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Follow-through',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          if (quiet)
            Text(
              'A quiet week. Nothing is waiting on you from it.',
              key: const Key('follow-through-quiet'),
              style: TextStyle(color: colors.inkSoft),
            )
          else ...[
            Wrap(
              spacing: 8,
              runSpacing: 6,
              children: [
                _Count('${data['fulfilled']} fulfilled', colors.accent),
                if (asJsonInt(data['dropped']) > 0)
                  _Count('${data['dropped']} dropped', colors.muted),
                if (asJsonInt(data['overdue']) > 0)
                  _Count('${data['overdue']} overdue', colors.warning),
                if (asJsonInt(data['decisionsDueNextWeek']) > 0)
                  _Count(
                    '${data['decisionsDueNextWeek']} decisions next week',
                    colors.info,
                  ),
              ],
            ),
            for (final (title, items) in sections)
              if (items.isNotEmpty) ...[
                Padding(
                  padding: const EdgeInsets.only(top: 12, bottom: 2),
                  child: Text(
                    title,
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: FontWeight.w600,
                      color: colors.muted,
                    ),
                  ),
                ),
                for (final item in items)
                  InkWell(
                    onTap: () =>
                        unawaited(_open(asJsonString(item['ref']) ?? '')),
                    child: Padding(
                      padding: const EdgeInsets.symmetric(vertical: 6),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              asJsonString(item['title']) ?? '',
                              style: TextStyle(
                                fontSize: 14.5,
                                color: colors.ink,
                              ),
                            ),
                          ),
                          if (asJsonString(item['detail']) case final detail?)
                            Padding(
                              padding: const EdgeInsets.only(left: 8),
                              child: Text(
                                detail,
                                style: TextStyle(
                                  fontSize: 12.5,
                                  color: colors.muted,
                                ),
                              ),
                            ),
                        ],
                      ),
                    ),
                  ),
              ],
          ],
          if (suggestions.isNotEmpty) ...[
            const SizedBox(height: 14),
            Text(
              'Worth doing next week, if you like',
              style: TextStyle(
                fontSize: 13,
                fontWeight: FontWeight.w600,
                color: colors.muted,
              ),
            ),
            for (final suggestion in suggestions)
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Container(
                  key: Key('suggestion-${suggestion['targetId']}'),
                  padding: const EdgeInsets.fromLTRB(12, 10, 8, 4),
                  decoration: BoxDecoration(
                    color: colors.surfaceMuted,
                    borderRadius: BorderRadius.circular(JarvisRadii.md),
                  ),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        asJsonString(suggestion['title']) ?? '',
                        style: TextStyle(
                          fontSize: 14.5,
                          fontWeight: FontWeight.w600,
                          color: colors.ink,
                        ),
                      ),
                      Text(
                        asJsonString(suggestion['reason']) ?? '',
                        style: TextStyle(fontSize: 13, color: colors.inkSoft),
                      ),
                      Wrap(
                        children: [
                          TextButton(
                            key: Key(
                              'suggestion-accept-${suggestion['targetId']}',
                            ),
                            onPressed: _busy
                                ? null
                                : () => unawaited(_accept(suggestion)),
                            child: Text(
                              suggestion['kind'] == 'plan_commitment'
                                  ? 'Pick a day'
                                  : 'Choose it',
                            ),
                          ),
                          TextButton(
                            key: Key(
                              'suggestion-dismiss-${suggestion['targetId']}',
                            ),
                            onPressed: _busy
                                ? null
                                : () => unawaited(_dismiss(suggestion)),
                            child: const Text('Not now'),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              ),
          ],
        ],
      ),
    );
  }
}

class _Count extends StatelessWidget {
  const _Count(this.label, this.color);

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) => StatusPill(label: label, color: color);
}

class _AcceptSheet extends StatefulWidget {
  const _AcceptSheet({required this.suggestion});

  final Map<String, dynamic> suggestion;

  @override
  State<_AcceptSheet> createState() => _AcceptSheetState();
}

class _AcceptSheetState extends State<_AcceptSheet> {
  late DateTime _on =
      jsonDate(widget.suggestion['suggestedOn'], local: true) ??
      DateTime.now().add(const Duration(days: 1));
  final _note = TextEditingController();

  bool get _plan => widget.suggestion['kind'] == 'plan_commitment';

  @override
  void dispose() {
    _note.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SingleChildScrollView(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            asJsonString(widget.suggestion['title']) ?? '',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 4),
          Text(
            _plan
                ? 'This moves the promise to the day you pick and sets its morning reminder. Nothing else changes.'
                : 'This sets the project’s next step. Nothing else changes.',
          ),
          const SizedBox(height: 12),
          if (_plan)
            ListTile(
              key: const Key('follow-through-day'),
              contentPadding: EdgeInsets.zero,
              title: const Text('Move it to'),
              subtitle: Text(_day(_on)),
              trailing: const Icon(PhosphorIconsRegular.calendarBlank),
              onTap: () async {
                final picked = await showDatePicker(
                  context: context,
                  initialDate: _on.isBefore(DateTime.now())
                      ? DateTime.now()
                      : _on,
                  firstDate: DateTime.now().subtract(const Duration(days: 1)),
                  lastDate: DateTime.now().add(const Duration(days: 700)),
                );
                if (picked != null) setState(() => _on = picked);
              },
            )
          else
            TextField(
              key: const Key('follow-through-note'),
              controller: _note,
              autofocus: true,
              maxLength: 200,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(labelText: 'The next step'),
            ),
          const SizedBox(height: 8),
          FilledButton(
            key: const Key('follow-through-save'),
            onPressed: !_plan && _note.text.trim().isEmpty
                ? null
                : () => Navigator.pop<Map<String, Object?>>(
                    context,
                    _plan ? {'on': _iso(_on)} : {'note': _note.text.trim()},
                  ),
            child: Text(_plan ? 'Move it' : 'Save the step'),
          ),
        ],
      ),
    ),
  );
}
