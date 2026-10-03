import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'mission_models.dart';

/// One mission as a live board: stages left to right in time, steps within a
/// stage side by side. The owner can pause, cancel, edit a waiting step, skip
/// or retry one, and read what each agent produced.
class MissionDetailScreen extends StatefulWidget {
  const MissionDetailScreen({
    required this.http,
    required this.missionId,
    this.poll = true,
    super.key,
  });

  final Dio http;
  final String missionId;
  final bool poll;

  @override
  State<MissionDetailScreen> createState() => _MissionDetailScreenState();
}

class _MissionDetailScreenState extends State<MissionDetailScreen> {
  MissionDetailData? _mission;
  bool _loading = true;
  String? _error;
  Timer? _timer;

  String get _base => '/api/v1/missions/${widget.missionId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
    if (widget.poll) {
      _timer = Timer.periodic(const Duration(seconds: 5), (_) {
        if (_mission?.status == 'running') unawaited(_load());
      });
    }
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _load() => _apply(() => widget.http.get<dynamic>(_base));

  Future<void> _apply(Future<Response<dynamic>> Function() request) async {
    try {
      final response = await request();
      if (!mounted) return;
      final mission = MissionDetailData.fromJson(response.data);
      setState(() {
        _mission = mission ?? _mission;
        _loading = false;
        _error = _mission == null ? 'Could not load this mission.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      final message =
          firstProblemMessage(error.response?.data) ??
          (error.response?.data is Map
              ? asJsonString((error.response!.data as Map)['detail'])
              : null) ??
          'That did not work.';
      setState(() => _loading = false);
      if (_mission == null) {
        setState(() => _error = message);
      } else {
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(message)));
      }
    }
  }

  Future<void> _post(String path) =>
      _apply(() => widget.http.post<dynamic>('$_base/$path'));

  Future<void> _edit(MissionStepData step) async {
    final text = await showDialog<String>(
      context: context,
      builder: (_) => _InstructionDialog(step: step),
    );
    if (text == null || text.isEmpty) return;
    await _apply(
      () => widget.http.put<dynamic>(
        '/api/v1/missions/steps/${step.id}',
        data: {'instruction': text},
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final mission = _mission;
    return Scaffold(
      appBar: AppBar(
        title: Text(mission?.title ?? 'Mission'),
        actions: mission == null ? null : _actions(mission),
      ),
      body: ContentWidth(
        child: ListScreenBody(
          loading: _loading,
          error: _error,
          isEmpty: mission == null,
          onRetry: () => unawaited(_load()),
          empty: const SizedBox.shrink(),
          child: mission == null
              ? const SizedBox.shrink()
              : ListView(
                  padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
                  children: _body(mission),
                ),
        ),
      ),
    );
  }

  List<Widget> _actions(MissionDetailData mission) => [
    if (mission.status == 'ready')
      HeaderAction(
        key: const Key('mission-start'),
        label: 'Start',
        icon: PhosphorIconsRegular.play,
        onPressed: () => unawaited(_post('start')),
      ),
    if (mission.status == 'running')
      HeaderAction(
        key: const Key('mission-pause'),
        label: 'Pause',
        icon: PhosphorIconsRegular.pauseCircle,
        onPressed: () => unawaited(_post('pause')),
      ),
    if (mission.status == 'paused')
      HeaderAction(
        key: const Key('mission-resume'),
        label: 'Resume',
        icon: PhosphorIconsRegular.play,
        onPressed: () => unawaited(_post('resume')),
      ),
    if (const {'ready', 'running', 'paused'}.contains(mission.status)) ...[
      const SizedBox(width: 8),
      HeaderAction(
        key: const Key('mission-cancel'),
        label: 'Cancel',
        icon: PhosphorIconsRegular.stopCircle,
        collapsesWhenNarrow: true,
        onPressed: () async {
          final ok = await showJarvisConfirm(
            context,
            title: 'Cancel this mission?',
            message: 'Steps that are running are stopped.',
            confirmLabel: 'Cancel mission',
            destructive: true,
          );
          if (ok) await _post('cancel');
        },
      ),
    ],
  ];

  List<Widget> _body(MissionDetailData mission) {
    final colors = JarvisColors.of(context);
    return [
      SurfaceCard(
        margin: const EdgeInsets.only(bottom: 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
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
            const SizedBox(height: 8),
            Text(mission.goal),
            if (mission.status == 'ready')
              Padding(
                padding: const EdgeInsets.only(top: 8),
                child: Text(
                  'Review the plan below, change a step if you like, then '
                  'start. Steps that ask for approval still ask you.',
                  style: TextStyle(color: colors.inkSoft, fontSize: 13),
                ),
              ),
            if (mission.failureReason != null)
              InlineNotice(
                message: mission.failureReason!,
                tone: NoticeTone.danger,
                margin: const EdgeInsets.only(top: 8),
              ),
          ],
        ),
      ),
      if (mission.summary != null)
        SurfaceCard(
          key: const Key('mission-summary'),
          margin: const EdgeInsets.only(bottom: 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SectionHeader('Result'),
              SelectableText(mission.summary!),
            ],
          ),
        ),
      for (final (index, stage) in mission.stages.indexed) ...[
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 8, 0, 6),
          child: Text(
            stage.length > 1
                ? 'Stage ${index + 1} · in parallel'
                : 'Stage ${index + 1}',
            style: TextStyle(
              color: colors.muted,
              fontSize: 12.5,
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        for (final step in stage) _step(mission, step),
      ],
      if (mission.notes.isNotEmpty) ...[
        const SizedBox(height: 12),
        const SectionHeader('Shared notes'),
        for (final (key, value) in mission.notes)
          Padding(
            padding: const EdgeInsets.only(bottom: 4),
            child: Text.rich(
              TextSpan(
                children: [
                  TextSpan(
                    text: '$key: ',
                    style: const TextStyle(fontWeight: FontWeight.w600),
                  ),
                  TextSpan(text: value),
                ],
              ),
            ),
          ),
      ],
    ];
  }

  Widget _step(MissionDetailData mission, MissionStepData step) {
    final colors = JarvisColors.of(context);
    final statusColor = stepStatusColor(colors, step.status);
    final canEdit = step.status == 'pending';
    final canSkip = const {'pending', 'failed', 'cancelled'}.contains(step.status);
    final canRetry = const {'failed', 'cancelled'}.contains(step.status);
    return SurfaceCard(
      key: Key('step-${step.key}'),
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(icon: roleIcon(step.role), size: 32),
              const SizedBox(width: 10),
              Expanded(
                child: Text(
                  step.title,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              Icon(stepStatusIcon(step.status), size: 18, color: statusColor),
              const SizedBox(width: 6),
              Text(
                stepStatusLabel(step.status, step.taskStatus),
                style: TextStyle(color: statusColor, fontSize: 12.5),
              ),
            ],
          ),
          if (step.dependsOn.isNotEmpty)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: Text(
                'After ${step.dependsOn.join(', ')}',
                style: TextStyle(color: colors.muted, fontSize: 12),
              ),
            ),
          Padding(
            padding: const EdgeInsets.only(top: 6),
            child: Text(
              step.instruction,
              style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
            ),
          ),
          if (step.result != null)
            Theme(
              data: Theme.of(context).copyWith(dividerColor: Colors.transparent),
              child: ExpansionTile(
                key: Key('step-result-${step.key}'),
                tilePadding: EdgeInsets.zero,
                title: const Text('Result', style: TextStyle(fontSize: 13)),
                children: [
                  Align(
                    alignment: Alignment.centerLeft,
                    child: SelectableText(step.result!),
                  ),
                ],
              ),
            ),
          if (step.error != null)
            Text(
              step.error!,
              style: TextStyle(color: colors.warning, fontSize: 12.5),
            ),
          if (canEdit || canSkip || canRetry)
            Wrap(
              spacing: 4,
              children: [
                if (canEdit)
                  TextButton(
                    key: Key('step-edit-${step.key}'),
                    onPressed: () => unawaited(_edit(step)),
                    child: const Text('Edit'),
                  ),
                if (canRetry)
                  TextButton(
                    key: Key('step-retry-${step.key}'),
                    onPressed: () => unawaited(
                      _apply(
                        () => widget.http.post<dynamic>(
                          '/api/v1/missions/steps/${step.id}/retry',
                        ),
                      ),
                    ),
                    child: const Text('Try again'),
                  ),
                if (canSkip)
                  TextButton(
                    key: Key('step-skip-${step.key}'),
                    onPressed: () => unawaited(
                      _apply(
                        () => widget.http.post<dynamic>(
                          '/api/v1/missions/steps/${step.id}/skip',
                        ),
                      ),
                    ),
                    child: const Text('Skip'),
                  ),
              ],
            ),
        ],
      ),
    );
  }
}

class _InstructionDialog extends StatefulWidget {
  const _InstructionDialog({required this.step});

  final MissionStepData step;

  @override
  State<_InstructionDialog> createState() => _InstructionDialogState();
}

class _InstructionDialogState extends State<_InstructionDialog> {
  late final _controller = TextEditingController(text: widget.step.instruction);

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.step.title),
    content: TextField(
      key: const Key('step-instruction'),
      controller: _controller,
      maxLines: 6,
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('step-save'),
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: const Text('Save'),
      ),
    ],
  );
}
