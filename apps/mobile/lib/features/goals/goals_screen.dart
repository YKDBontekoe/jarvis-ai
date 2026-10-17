import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'goal_screen.dart';

const goalFeelingLabels = {
  'on_track': 'On track',
  'slipping': 'Slipping',
  'stuck': 'Stuck',
};

/// Goals: an outcome you chose, the habits, project and promises that serve
/// it, and a gentle weekly loop. Nothing here nags; a missing check-in is
/// only "due".
class GoalsScreen extends StatefulWidget {
  const GoalsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<GoalsScreen> createState() => _GoalsScreenState();
}

class _GoalsScreenState extends State<GoalsScreen> {
  List<Map<String, dynamic>> _goals = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/goals');
      if (!mounted) return;
      setState(() {
        _goals = [
          for (final item in jsonMaps(response.data))
            if (jsonString(item, 'id') != null) item,
        ];
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your goals.';
      });
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => GoalScreen(http: widget.http, goalId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create() async {
    var projects = <Map<String, dynamic>>[];
    var habits = <Map<String, dynamic>>[];
    try {
      projects = jsonMaps(
        (await widget.http.get<dynamic>('/api/v1/projects')).data,
      );
    } on DioException {
      // Links are optional.
    }
    try {
      final data = jsonObject(
        (await widget.http.get<dynamic>('/api/v1/habits')).data,
      );
      habits = jsonMaps(data?['habits']);
    } on DioException {
      // Links are optional.
    }
    if (!mounted) return;
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _NewGoalSheet(projects: projects, habits: habits),
    );
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/goals',
        data: draft,
      );
      final id = jsonString(
        jsonObject(jsonObject(response.data)?['goal']) ?? const {},
        'id',
      );
      if (id != null && mounted) await _open(id);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Could not add the goal.',
            ),
          ),
        );
    }
  }

  Widget _goalRow(Map<String, dynamic> goal, int index) {
    final due = goal['checkInDue'] == true;
    final review = jsonObject(goal['review']);
    final open = asJsonInt(goal['openPlanItems']);
    return FeatureRow(
      key: Key('goal-${goal['id']}'),
      index: index,
      icon: PhosphorIconsRegular.target,
      caption: goal['status'] != 'active' ? '${goal['status']}' : null,
      title: asJsonString(goal['title']) ?? 'Goal',
      subtitle: [
        if (goal['feeling'] != null) goalFeelingLabels[goal['feeling']] ?? '',
        if (open > 0) '$open steps this week',
        if (review != null) asJsonString(review['reason']) ?? '',
      ].where((x) => x.isNotEmpty).join('\n'),
      trailing: due
          ? StatusPill(
              label: 'Check-in due',
              color: JarvisColors.of(context).warning,
            )
          : null,
      onTap: () => unawaited(_open(goal['id'] as String)),
    );
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Goals'),
        actions: [
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: _create,
          ),
        ],
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
                children: [
                  ContentWidth(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (_goals.isEmpty)
                          Padding(
                            padding: const EdgeInsets.only(top: 24),
                            child: EmptyState(
                              icon: PhosphorIconsRegular.target,
                              title: 'Make progress without the pressure',
                              message:
                                  'Pick one thing you want, link what already '
                                  'serves it, and check in when it suits you. '
                                  'Jarvis only proposes small steps.',
                              action: PillAction(
                                key: const Key('goal-start'),
                                primary: true,
                                icon: PhosphorIconsRegular.plus,
                                label: 'Add a goal',
                                onPressed: _create,
                              ),
                            ),
                          )
                        else
                          for (var i = 0; i < _goals.length; i++)
                            _goalRow(_goals[i], i),
                      ],
                    ),
                  ),
                ],
              ),
            ),
    );
  }
}

class _NewGoalSheet extends StatefulWidget {
  const _NewGoalSheet({required this.projects, required this.habits});

  final List<Map<String, dynamic>> projects;
  final List<Map<String, dynamic>> habits;

  @override
  State<_NewGoalSheet> createState() => _NewGoalSheetState();
}

class _NewGoalSheetState extends State<_NewGoalSheet> {
  final _title = TextEditingController();
  final _why = TextEditingController();
  final _minutes = TextEditingController(text: '90');
  String? _project;
  final _habits = <String>{};

  @override
  void dispose() {
    _title.dispose();
    _why.dispose();
    _minutes.dispose();
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
          Text('New goal', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 12),
          TextField(
            key: const Key('goal-title-field'),
            controller: _title,
            maxLength: 200,
            onChanged: (_) => setState(() {}),
            decoration: const InputDecoration(
              labelText: 'What do you want?',
              hintText: 'Run a 10k',
            ),
          ),
          TextField(
            key: const Key('goal-why-field'),
            controller: _why,
            maxLength: 300,
            decoration: const InputDecoration(labelText: 'Why does it matter?'),
          ),
          TextField(
            key: const Key('goal-minutes-field'),
            controller: _minutes,
            keyboardType: TextInputType.number,
            decoration: const InputDecoration(
              labelText: 'Minutes a week you can spare',
            ),
          ),
          if (widget.projects.isNotEmpty) ...[
            const SizedBox(height: 12),
            DropdownButtonFormField<String?>(
              key: const Key('goal-project-field'),
              initialValue: _project,
              decoration: const InputDecoration(labelText: 'Project'),
              items: [
                const DropdownMenuItem(value: null, child: Text('No project')),
                for (final project in widget.projects)
                  if (jsonString(project, 'id') != null)
                    DropdownMenuItem(
                      value: jsonString(project, 'id'),
                      child: Text(asJsonString(project['name']) ?? 'Project'),
                    ),
              ],
              onChanged: (value) => setState(() => _project = value),
            ),
          ],
          if (widget.habits.isNotEmpty) ...[
            const SizedBox(height: 12),
            Text(
              'Habits that serve it',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final habit in widget.habits)
                  if (jsonString(habit, 'id') != null &&
                      habit['archived'] != true)
                    FilterChip(
                      key: Key('goal-habit-${habit['id']}'),
                      label: Text(asJsonString(habit['name']) ?? 'Habit'),
                      selected: _habits.contains(habit['id']),
                      onSelected: (on) => setState(() {
                        on
                            ? _habits.add(habit['id'] as String)
                            : _habits.remove(habit['id']);
                      }),
                    ),
              ],
            ),
          ],
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('goal-create'),
            onPressed: _title.text.trim().isEmpty
                ? null
                : () => Navigator.pop<Map<String, Object?>>(context, {
                    'title': _title.text.trim(),
                    'motivation': _why.text.trim().isEmpty
                        ? null
                        : _why.text.trim(),
                    'weeklyMinutes': int.tryParse(_minutes.text.trim()),
                    'projectId': _project,
                    'habitIds': _habits.toList(),
                  }),
            child: const Text('Add goal'),
          ),
        ],
      ),
    ),
  );
}
