import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

String missionStatusLabel(String status) => switch (status) {
  'ready' => 'Ready to start',
  'running' => 'Working',
  'paused' => 'Paused',
  'completed' => 'Done',
  'failed' => 'Needs attention',
  'cancelled' => 'Cancelled',
  _ => status,
};

String stepStatusLabel(String status, String? taskStatus) {
  if (status == 'running' && taskStatus == 'needs_approval') {
    return 'Waiting for your approval';
  }
  return switch (status) {
    'pending' => 'Waiting',
    'running' => 'Working',
    'completed' => 'Done',
    'failed' => 'Failed',
    'skipped' => 'Skipped',
    'cancelled' => 'Stopped',
    _ => status,
  };
}

IconData stepStatusIcon(String status) => switch (status) {
  'running' => PhosphorIconsRegular.play,
  'completed' => PhosphorIconsRegular.checkCircle,
  'failed' => PhosphorIconsRegular.warningCircle,
  'skipped' => PhosphorIconsRegular.minusCircle,
  'cancelled' => PhosphorIconsRegular.stopCircle,
  _ => PhosphorIconsRegular.hourglassMedium,
};

Color stepStatusColor(JarvisColors colors, String status) => switch (status) {
  'running' => colors.info,
  'completed' => colors.success,
  'failed' => colors.danger,
  'cancelled' => colors.warning,
  _ => colors.muted,
};

IconData roleIcon(String role) => switch (role) {
  'researcher' => PhosphorIconsRegular.magnifyingGlass,
  'planner' => PhosphorIconsRegular.listChecks,
  'browser' => PhosphorIconsRegular.globeSimple,
  'coder' => PhosphorIconsRegular.code,
  'finance' => PhosphorIconsRegular.wallet,
  'writer' => PhosphorIconsRegular.notePencil,
  _ => PhosphorIconsRegular.robot,
};

class MissionSummaryData {
  const MissionSummaryData({
    required this.id,
    required this.title,
    required this.status,
  });

  final String id;
  final String title;
  final String status;

  bool get active => const {'ready', 'running', 'paused'}.contains(status);

  static MissionSummaryData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final title = map == null ? null : jsonString(map, 'title');
    if (map == null || id == null || title == null) return null;
    return MissionSummaryData(
      id: id,
      title: title,
      status: jsonString(map, 'status') ?? 'ready',
    );
  }
}

class MissionStepData {
  const MissionStepData({
    required this.id,
    required this.key,
    required this.stage,
    required this.role,
    required this.title,
    required this.instruction,
    required this.dependsOn,
    required this.status,
    this.taskStatus,
    this.result,
    this.error,
  });

  final String id;
  final String key;
  final int stage;
  final String role;
  final String title;
  final String instruction;
  final List<String> dependsOn;
  final String status;
  final String? taskStatus;
  final String? result;
  final String? error;

  static MissionStepData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final title = map == null ? null : jsonString(map, 'title');
    if (map == null || id == null || title == null) return null;
    return MissionStepData(
      id: id,
      key: jsonString(map, 'key') ?? '',
      stage: asJsonInt(map['stage']),
      role: jsonString(map, 'role') ?? 'generalist',
      title: title,
      instruction: jsonString(map, 'instruction') ?? '',
      dependsOn: jsonStrings(map['dependsOn']),
      status: jsonString(map, 'status') ?? 'pending',
      taskStatus: jsonString(map, 'taskStatus'),
      result: jsonString(map, 'result'),
      error: jsonString(map, 'error'),
    );
  }
}

class MissionDetailData {
  const MissionDetailData({
    required this.id,
    required this.title,
    required this.goal,
    required this.status,
    required this.steps,
    required this.notes,
    this.summary,
    this.failureReason,
  });

  final String id;
  final String title;
  final String goal;
  final String status;
  final String? summary;
  final String? failureReason;
  final List<MissionStepData> steps;
  final List<(String, String)> notes;

  /// Steps grouped by stage: steps in the same stage run side by side.
  List<List<MissionStepData>> get stages {
    final byStage = <int, List<MissionStepData>>{};
    for (final step in steps) {
      byStage.putIfAbsent(step.stage, () => []).add(step);
    }
    return [for (final key in byStage.keys.toList()..sort()) byStage[key]!];
  }

  static MissionDetailData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final title = map == null ? null : jsonString(map, 'title');
    if (map == null || id == null || title == null) return null;
    return MissionDetailData(
      id: id,
      title: title,
      goal: jsonString(map, 'goal') ?? '',
      status: jsonString(map, 'status') ?? 'ready',
      summary: jsonString(map, 'summary'),
      failureReason: jsonString(map, 'failureReason'),
      steps: [
        for (final step in jsonMaps(map['steps'])) ?MissionStepData.fromJson(step),
      ],
      notes: [
        for (final note in jsonMaps(map['notes']))
          if (jsonString(note, 'key') case final key?)
            (key, jsonString(note, 'value') ?? ''),
      ],
    );
  }
}
