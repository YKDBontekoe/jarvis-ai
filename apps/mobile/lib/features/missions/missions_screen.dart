import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'mission_detail_screen.dart';
import 'mission_models.dart';

/// Big jobs split among a crew of agents. Describe one, review the plan, start it.
class MissionsScreen extends StatefulWidget {
  const MissionsScreen({required this.http, this.poll = true, super.key});

  final Dio http;

  /// Tests turn polling off.
  final bool poll;

  @override
  State<MissionsScreen> createState() => _MissionsScreenState();
}

class _MissionsScreenState extends State<MissionsScreen> {
  List<MissionSummaryData> _missions = const [];
  bool _loading = true;
  bool _planning = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/missions');
      if (!mounted) return;
      setState(() {
        _missions = [
          for (final item in jsonMaps(response.data)) ?MissionSummaryData.fromJson(item),
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
            'Could not load your missions.';
      });
    }
  }

  Future<void> _create() async {
    final goal = await showJarvisDialog<String>(
      context: context,
      builder: (_) => const _GoalDialog(),
    );
    if (goal == null || !mounted) return;
    setState(() => _planning = true);
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/missions',
        data: {'goal': goal},
        options: Options(receiveTimeout: const Duration(minutes: 2)),
      );
      final id = jsonId(jsonObject(response.data));
      await _load();
      if (mounted && id != null) await _open(id);
    } on DioException catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Jarvis could not plan that.',
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _planning = false);
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) =>
            MissionDetailScreen(http: widget.http, missionId: id, poll: widget.poll),
      ),
    );
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const Text('Missions'),
        actions: [
          HeaderAction(
            key: const Key('mission-new'),
            label: 'New mission',
            icon: PhosphorIconsRegular.plus,
            onPressed: _planning ? null : () => unawaited(_create()),
          ),
        ],
      ),
      body: ContentWidth(
        child: Stack(
          children: [
            ListScreenBody(
              loading: _loading,
              error: _error,
              isEmpty: _missions.isEmpty,
              onRetry: () => unawaited(_load()),
              empty: const EmptyState(
                icon: PhosphorIconsRegular.flowArrow,
                title: 'No missions yet',
                message:
                    'Describe a big job, like planning a trip. Jarvis splits '
                    'it among a crew of agents and shows you the plan first.',
              ),
              child: ListView(
                padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                children: [
                  for (final mission in _missions)
                    SurfaceCard(
                      key: Key('mission-${mission.id}'),
                      margin: const EdgeInsets.only(bottom: 10),
                      onTap: () => unawaited(_open(mission.id)),
                      child: Row(
                        children: [
                          const IconBadge(icon: PhosphorIconsRegular.flowArrow),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Text(
                              mission.title,
                              style: const TextStyle(fontWeight: FontWeight.w600),
                            ),
                          ),
                          StatusPill(
                            label: missionStatusLabel(mission.status),
                            color: switch (mission.status) {
                              'completed' => colors.success,
                              'failed' => colors.danger,
                              'running' => colors.info,
                              _ => colors.muted,
                            },
                          ),
                        ],
                      ),
                    ),
                ],
              ),
            ),
            if (_planning)
              const Positioned.fill(
                child: ColoredBox(
                  color: Color(0x66000000),
                  child: Center(
                    child: Card(
                      child: Padding(
                        padding: EdgeInsets.all(24),
                        child: Text('Planning your mission…'),
                      ),
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

class _GoalDialog extends StatefulWidget {
  const _GoalDialog();

  @override
  State<_GoalDialog> createState() => _GoalDialogState();
}

class _GoalDialogState extends State<_GoalDialog> {
  final _goal = TextEditingController();

  @override
  void dispose() {
    _goal.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('What should the crew do?'),
    content: TextField(
      key: const Key('mission-goal'),
      controller: _goal,
      autofocus: true,
      maxLines: 5,
      decoration: const InputDecoration(
        hintText:
            'For example: plan a three day trip to Lisbon for two in '
            'November, budget €1,500.',
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('mission-plan'),
        onPressed: () {
          final text = _goal.text.trim();
          if (text.length >= 10) Navigator.pop(context, text);
        },
        child: const Text('Plan it'),
      ),
    ],
  );
}
