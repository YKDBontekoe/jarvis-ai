import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../schedule_format.dart' show deviceTimeZoneLookup;
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';

const _feelings = {
  'on_track': 'On track',
  'slipping': 'Slipping',
  'stuck': 'Stuck',
};

/// One goal: where it stands in three labelled kinds of evidence (measured,
/// in your words, a rule-based suggestion), this week's small plan, the
/// things linked to it and one bounded experiment.
class GoalScreen extends StatefulWidget {
  const GoalScreen({required this.http, required this.goalId, super.key});

  final Dio http;
  final String goalId;

  @override
  State<GoalScreen> createState() => _GoalScreenState();
}

class _GoalScreenState extends State<GoalScreen> {
  Map<String, dynamic>? _view;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  String get _path => '/api/v1/goals/${widget.goalId}';
  Map<String, dynamic> get _goal => jsonObject(_view?['goal']) ?? const {};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
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
            'Could not load this goal.';
      });
    }
  }

  /// Runs a change that answers with the new goal view.
  Future<bool> _send(
    Future<Response<dynamic>> Function() call, {
    String? done,
  }) async {
    setState(() => _busy = true);
    try {
      final response = await call();
      if (!mounted) return true;
      setState(() => _view = jsonObject(response.data) ?? _view);
      if (done != null) _toast(done);
      return true;
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
      return false;
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _patch(Map<String, Object?> data) =>
      _send(() => widget.http.patch<dynamic>(_path, data: data));

  Future<void> _plan() => _send(() => widget.http.post<dynamic>('$_path/plan'));

  Future<void> _act(String itemId, String action, {int? minutes}) async {
    final zone = action == 'schedule' ? await deviceTimeZoneLookup() : null;
    await _send(
      () => widget.http.post<dynamic>(
        '$_path/plan/items/$itemId',
        queryParameters: zone == null ? null : {'timeZone': zone},
        data: {'action': action, 'minutes': ?minutes},
      ),
      done: action == 'schedule'
          ? 'Added to today. Plan your day to place it.'
          : null,
    );
  }

  Future<void> _reduce(Map<String, dynamic> item) async {
    final current = asJsonInt(item['minutes']);
    final minutes = await _askNumber(
      title: 'A smaller step',
      label: 'Minutes (less than $current)',
      initial: (current ~/ 2).clamp(5, current),
    );
    if (minutes != null) {
      await _act(item['id'] as String, 'reduce', minutes: minutes);
    }
  }

  Future<int?> _askNumber({
    required String title,
    required String label,
    required int initial,
  }) async {
    final text = await showJarvisDialog<String>(
      context: context,
      builder: (_) => _PromptDialog(
        title: title,
        label: label,
        initial: '$initial',
        numeric: true,
        fieldKey: const Key('goal-number-field'),
        okKey: const Key('goal-number-ok'),
        okLabel: 'Save',
      ),
    );
    return text == null ? null : int.tryParse(text);
  }

  Future<void> _checkIn() async {
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _CheckInSheet(),
    );
    if (draft == null) return;
    await _send(
      () => widget.http.post<dynamic>('$_path/check-ins', data: draft),
      done: 'Check-in saved.',
    );
  }

  Future<void> _startExperiment() async {
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => const _ExperimentSheet(),
    );
    if (draft == null) return;
    await _send(
      () => widget.http.post<dynamic>('$_path/experiments', data: draft),
    );
  }

  Future<void> _note(String experimentId) async {
    final text = await showJarvisDialog<String>(
      context: context,
      builder: (_) => const _PromptDialog(
        title: 'What did you notice?',
        label: 'Observation',
        fieldKey: Key('experiment-note-field'),
        okKey: Key('experiment-note-ok'),
        okLabel: 'Add',
      ),
    );
    if (text == null || text.isEmpty) return;
    await _send(
      () => widget.http.post<dynamic>(
        '$_path/experiments/$experimentId/notes',
        data: {'text': text},
      ),
    );
  }

  Future<void> _finish(String experimentId, bool keep) => _send(
    () => widget.http.post<dynamic>(
      '$_path/experiments/$experimentId/finish',
      data: {'keep': keep},
    ),
  );

  Future<void> _menu(String value) async {
    switch (value) {
      case 'pause':
        await _patch({'status': 'paused'});
      case 'resume':
        await _patch({'status': 'active'});
      case 'achieved':
        await _patch({'status': 'achieved'});
      case 'archive':
        await _patch({'status': 'archived'});
        if (mounted) Navigator.of(context).maybePop();
      case 'season':
        await _send(
          () => widget.http.post<dynamic>('$_path/reset-season'),
          done: 'New season started. Your history is kept.',
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(_goal['status']) ?? 'active';
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(asJsonString(_goal['title']) ?? 'Goal'),
        actions: [
          if (_view != null)
            PopupMenuButton<String>(
              key: const Key('goal-menu'),
              onSelected: (value) => unawaited(_menu(value)),
              itemBuilder: (_) => [
                if (status == 'active')
                  const PopupMenuItem(value: 'pause', child: Text('Pause')),
                if (status != 'active')
                  const PopupMenuItem(value: 'resume', child: Text('Resume')),
                if (status != 'achieved')
                  const PopupMenuItem(
                    value: 'achieved',
                    child: Text('Mark achieved'),
                  ),
                const PopupMenuItem(
                  value: 'season',
                  child: Text('Start a new season'),
                ),
                const PopupMenuItem(value: 'archive', child: Text('Archive')),
              ],
            ),
        ],
      ),
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
                      if (asJsonString(_goal['motivation']) case final why?)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 12),
                          child: Text(
                            why,
                            style: TextStyle(color: colors.inkSoft),
                          ),
                        ),
                      if (status != 'active')
                        InlineNotice(
                          key: const Key('goal-status-notice'),
                          tone: NoticeTone.info,
                          message: status == 'paused'
                              ? 'Paused. Nothing is planned or suggested until you resume.'
                              : 'This goal is $status.',
                        ),
                      if (_view?['checkInDue'] == true && status == 'active')
                        _checkInPrompt(colors),
                      ..._progress(colors),
                      ..._planSection(colors, status),
                      ..._linked(colors),
                      ..._experiments(colors, status),
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _checkInPrompt(JarvisColors colors) => Padding(
    padding: const EdgeInsets.only(bottom: 12),
    child: SurfaceCard(
      key: const Key('goal-check-in-due'),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Whenever you like: how is it going?',
            style: TextStyle(color: colors.ink),
          ),
          const SizedBox(height: 8),
          FilledButton(
            key: const Key('goal-check-in'),
            onPressed: _busy ? null : _checkIn,
            child: const Text('Check in'),
          ),
        ],
      ),
    ),
  );

  List<Widget> _progress(JarvisColors colors) {
    final progress = jsonObject(_view?['progress']) ?? const {};
    final reflections = jsonMaps(progress['reflections']);
    final review = jsonObject(progress['interpretation']);
    return [
      const SectionHeader('Where it stands'),
      SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text('Measured', style: _label(colors)),
            for (final line in jsonStrings(progress['measured']))
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(line, key: const Key('goal-measured')),
              ),
            const SizedBox(height: 12),
            Text('In your words', style: _label(colors)),
            if (reflections.isEmpty)
              Padding(
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  'No check-ins yet. They are optional.',
                  style: TextStyle(color: colors.muted),
                ),
              ),
            for (final item in reflections)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text(
                  '${_feelings[item['feeling']] ?? ''}: ${asJsonString(item['progress']) ?? ''}'
                  '${asJsonString(item['friction']) == null ? '' : ' · In the way: ${item['friction']}'}'
                  '${asJsonString(item['adjustment']) == null ? '' : ' · Next: ${item['adjustment']}'}',
                ),
              ),
            if (review != null) ...[
              const SizedBox(height: 12),
              Text('Suggestion (from your check-ins)', style: _label(colors)),
              Padding(
                key: const Key('goal-review'),
                padding: const EdgeInsets.only(top: 4),
                child: Text(
                  '${(asJsonString(review['verdict']) ?? '').toUpperCase()}: ${asJsonString(review['reason']) ?? ''}',
                ),
              ),
              Align(
                alignment: Alignment.centerRight,
                child: TextButton(
                  key: const Key('goal-review-dismiss'),
                  onPressed: _busy
                      ? null
                      : () => unawaited(
                          _send(
                            () => widget.http.post<dynamic>(
                              '$_path/review/dismiss',
                            ),
                          ),
                        ),
                  child: const Text('Not now'),
                ),
              ),
            ],
          ],
        ),
      ),
      if (_view?['checkInDue'] != true &&
          asJsonString(_goal['status']) == 'active')
        Align(
          alignment: Alignment.centerRight,
          child: TextButton(
            key: const Key('goal-check-in-early'),
            onPressed: _busy ? null : _checkIn,
            child: const Text('Add a check-in'),
          ),
        ),
      const SizedBox(height: 16),
    ];
  }

  TextStyle _label(JarvisColors colors) => TextStyle(
    color: colors.muted,
    fontSize: 12.5,
    fontWeight: FontWeight.w600,
  );

  List<Widget> _planSection(JarvisColors colors, String status) {
    final plan = jsonObject(_goal['plan']);
    final items = jsonMaps(plan?['items']);
    return [
      SectionHeader(
        'This week',
        trailing: status == 'active'
            ? TextButton(
                key: const Key('goal-plan'),
                onPressed: _busy ? null : _plan,
                child: Text(items.isEmpty ? 'Plan my week' : 'Refresh'),
              )
            : null,
      ),
      if (items.isEmpty)
        Padding(
          padding: const EdgeInsets.only(bottom: 16),
          child: Text(
            'Jarvis proposes a few small steps that fit the ${asJsonInt(_goal['weeklyMinutes'])} minutes you set aside.',
            style: TextStyle(color: colors.inkSoft),
          ),
        ),
      for (final item in items)
        if (item['status'] != 'declined')
          Padding(
            padding: const EdgeInsets.only(bottom: 8),
            child: SurfaceCard(
              key: Key('goal-step-${item['id']}'),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    asJsonString(item['title']) ?? '',
                    style: TextStyle(
                      fontWeight: FontWeight.w600,
                      decoration: item['status'] == 'done'
                          ? TextDecoration.lineThrough
                          : null,
                    ),
                  ),
                  Text(
                    '${asJsonInt(item['minutes'])} min · ${item['status']}',
                    style: TextStyle(color: colors.inkSoft, fontSize: 13),
                  ),
                  if (item['status'] != 'done')
                    Wrap(
                      spacing: 4,
                      children: [
                        if (item['status'] == 'proposed')
                          TextButton(
                            key: Key('goal-accept-${item['id']}'),
                            onPressed: _busy
                                ? null
                                : () => unawaited(
                                    _act(item['id'] as String, 'accept'),
                                  ),
                            child: const Text('Accept'),
                          ),
                        if (item['status'] == 'accepted')
                          TextButton(
                            key: Key('goal-schedule-${item['id']}'),
                            onPressed: _busy
                                ? null
                                : () => unawaited(
                                    _act(item['id'] as String, 'schedule'),
                                  ),
                            child: const Text('Add to today'),
                          ),
                        if (asJsonInt(item['minutes']) > 5)
                          TextButton(
                            key: Key('goal-smaller-${item['id']}'),
                            onPressed: _busy
                                ? null
                                : () => unawaited(_reduce(item)),
                            child: const Text('Smaller'),
                          ),
                        TextButton(
                          key: Key('goal-decline-${item['id']}'),
                          onPressed: _busy
                              ? null
                              : () => unawaited(
                                  _act(item['id'] as String, 'decline'),
                                ),
                          child: const Text('Not this week'),
                        ),
                        TextButton(
                          key: Key('goal-done-${item['id']}'),
                          onPressed: _busy
                              ? null
                              : () => unawaited(
                                  _act(item['id'] as String, 'done'),
                                ),
                          child: const Text('Done'),
                        ),
                      ],
                    ),
                ],
              ),
            ),
          ),
      if (asJsonInt(plan?['omittedForTime']) > 0)
        Padding(
          padding: const EdgeInsets.only(bottom: 8),
          child: Text(
            '${asJsonInt(plan?['omittedForTime'])} more did not fit your weekly time.',
            style: TextStyle(color: colors.muted, fontSize: 13),
          ),
        ),
      const SizedBox(height: 8),
    ];
  }

  List<Widget> _linked(JarvisColors colors) {
    final project = jsonObject(_view?['project']);
    final habits = jsonMaps(_view?['habits']);
    final promises = jsonMaps(_view?['commitments']);
    if (project == null && habits.isEmpty && promises.isEmpty) return const [];
    return [
      const SectionHeader('Linked'),
      SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            if (project != null)
              Text(
                'Project: ${asJsonString(project['name']) ?? ''}'
                '${asJsonString(project['nextAction']) == null ? '' : ' · next: ${project['nextAction']}'}',
              ),
            for (final habit in habits)
              Text(
                'Habit: ${asJsonString(habit['name']) ?? ''} '
                '(${asJsonInt(habit['thisWeekCount'])}/${asJsonInt(habit['targetPerWeek'])} this week)',
              ),
            for (final promise in promises)
              Text('Promise: ${asJsonString(promise['description']) ?? ''}'),
          ].expand((w) => [w, const SizedBox(height: 4)]).toList(),
        ),
      ),
      const SizedBox(height: 16),
    ];
  }

  List<Widget> _experiments(JarvisColors colors, String status) {
    final experiments = jsonMaps(_goal['experiments']);
    final running = experiments
        .where((e) => e['status'] == 'running')
        .firstOrNull;
    final finished = experiments
        .where((e) => e['status'] != 'running')
        .toList();
    return [
      SectionHeader(
        'Experiment',
        trailing: running == null && status == 'active'
            ? TextButton(
                key: const Key('goal-experiment-start'),
                onPressed: _busy ? null : _startExperiment,
                child: const Text('Try one'),
              )
            : null,
      ),
      if (running == null && finished.isEmpty)
        Text(
          'Change one thing for a few weeks, note what you notice, then decide.',
          style: TextStyle(color: colors.inkSoft),
        ),
      if (running != null)
        SurfaceCard(
          key: const Key('goal-experiment'),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                asJsonString(running['change']) ?? '',
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
              Text(
                'Hoping for: ${asJsonString(running['expectedBenefit']) ?? ''} · review ${asJsonString(running['reviewOn']) ?? ''}',
                style: TextStyle(color: colors.inkSoft, fontSize: 13),
              ),
              for (final note in jsonMaps(running['notes']))
                Padding(
                  padding: const EdgeInsets.only(top: 4),
                  child: Text(
                    '${asJsonString(note['on']) ?? ''}: ${asJsonString(note['text']) ?? ''}',
                  ),
                ),
              Wrap(
                spacing: 4,
                children: [
                  TextButton(
                    key: const Key('experiment-note'),
                    onPressed: _busy
                        ? null
                        : () => unawaited(_note(running['id'] as String)),
                    child: const Text('Add a note'),
                  ),
                  TextButton(
                    key: const Key('experiment-keep'),
                    onPressed: _busy
                        ? null
                        : () =>
                              unawaited(_finish(running['id'] as String, true)),
                    child: const Text('Keep it'),
                  ),
                  TextButton(
                    key: const Key('experiment-stop'),
                    onPressed: _busy
                        ? null
                        : () => unawaited(
                            _finish(running['id'] as String, false),
                          ),
                    child: const Text('Stop'),
                  ),
                ],
              ),
            ],
          ),
        ),
      for (final past in finished)
        Padding(
          padding: const EdgeInsets.only(top: 6),
          child: Text(
            '${asJsonString(past['change']) ?? ''} · ${past['status']}',
            style: TextStyle(color: colors.muted, fontSize: 13),
          ),
        ),
    ];
  }
}

class _CheckInSheet extends StatefulWidget {
  const _CheckInSheet();

  @override
  State<_CheckInSheet> createState() => _CheckInSheetState();
}

class _CheckInSheetState extends State<_CheckInSheet> {
  final _progress = TextEditingController();
  final _friction = TextEditingController();
  final _adjustment = TextEditingController();
  String _feeling = 'on_track';

  @override
  void dispose() {
    _progress.dispose();
    _friction.dispose();
    _adjustment.dispose();
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
          Text('Check in', style: Theme.of(context).textTheme.titleMedium),
          TextField(
            key: const Key('checkin-progress'),
            controller: _progress,
            maxLength: 300,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'What moved, even a little?',
            ),
          ),
          TextField(
            key: const Key('checkin-friction'),
            controller: _friction,
            maxLength: 300,
            decoration: const InputDecoration(
              labelText: 'What got in the way? (optional)',
            ),
          ),
          TextField(
            key: const Key('checkin-adjustment'),
            controller: _adjustment,
            maxLength: 300,
            decoration: const InputDecoration(
              labelText: 'What would you change? (optional)',
            ),
          ),
          Wrap(
            spacing: 8,
            children: [
              for (final entry in _feelings.entries)
                ChoiceChip(
                  key: Key('checkin-feeling-${entry.key}'),
                  label: Text(entry.value),
                  selected: _feeling == entry.key,
                  onSelected: (_) => setState(() => _feeling = entry.key),
                ),
            ],
          ),
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('checkin-save'),
            onPressed: _progress.text.trim().isEmpty
                ? null
                : () => Navigator.pop<Map<String, Object?>>(context, {
                    'progress': _progress.text.trim(),
                    'friction': _friction.text.trim().isEmpty
                        ? null
                        : _friction.text.trim(),
                    'adjustment': _adjustment.text.trim().isEmpty
                        ? null
                        : _adjustment.text.trim(),
                    'feeling': _feeling,
                  }),
            child: const Text('Save'),
          ),
        ],
      ),
    ),
  );
}

class _ExperimentSheet extends StatefulWidget {
  const _ExperimentSheet();

  @override
  State<_ExperimentSheet> createState() => _ExperimentSheetState();
}

class _ExperimentSheetState extends State<_ExperimentSheet> {
  final _change = TextEditingController();
  final _benefit = TextEditingController();
  int _days = 14;

  @override
  void dispose() {
    _change.dispose();
    _benefit.dispose();
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
            'One small experiment',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          TextField(
            key: const Key('experiment-change'),
            controller: _change,
            maxLength: 200,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'What will you change?',
            ),
          ),
          TextField(
            key: const Key('experiment-benefit'),
            controller: _benefit,
            maxLength: 200,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'What do you hope it does?',
            ),
          ),
          Wrap(
            spacing: 8,
            children: [
              for (final days in const [7, 14, 21, 30])
                ChoiceChip(
                  key: Key('experiment-days-$days'),
                  label: Text('$days days'),
                  selected: _days == days,
                  onSelected: (_) => setState(() => _days = days),
                ),
            ],
          ),
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('experiment-save'),
            onPressed:
                _change.text.trim().isEmpty || _benefit.text.trim().isEmpty
                ? null
                : () => Navigator.pop<Map<String, Object?>>(context, {
                    'change': _change.text.trim(),
                    'expectedBenefit': _benefit.text.trim(),
                    'durationDays': _days,
                  }),
            child: const Text('Start'),
          ),
        ],
      ),
    ),
  );
}

/// A one-field dialog that owns its controller, so it is not disposed while
/// the route is still animating out.
class _PromptDialog extends StatefulWidget {
  const _PromptDialog({
    required this.title,
    required this.label,
    required this.fieldKey,
    required this.okKey,
    required this.okLabel,
    this.initial = '',
    this.numeric = false,
  });

  final String title;
  final String label;
  final String initial;
  final bool numeric;
  final Key fieldKey;
  final Key okKey;
  final String okLabel;

  @override
  State<_PromptDialog> createState() => _PromptDialogState();
}

class _PromptDialogState extends State<_PromptDialog> {
  late final _controller = TextEditingController(text: widget.initial);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: TextField(
      key: widget.fieldKey,
      controller: _controller,
      keyboardType: widget.numeric ? TextInputType.number : null,
      maxLength: widget.numeric ? null : 300,
      decoration: InputDecoration(labelText: widget.label),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: widget.okKey,
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: Text(widget.okLabel),
      ),
    ],
  );
}
