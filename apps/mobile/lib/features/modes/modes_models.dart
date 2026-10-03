import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../ui/phosphor_icons.dart';

IconData modeIcon(String mode) => switch (mode) {
  'focus' => PhosphorIconsRegular.target,
  'commuting' => PhosphorIconsRegular.car,
  'meeting' => PhosphorIconsRegular.users,
  'sleep' => PhosphorIconsRegular.moon,
  'travel' => PhosphorIconsRegular.airplaneTilt,
  'weekend' => PhosphorIconsRegular.sunHorizon,
  _ => PhosphorIconsRegular.house,
};

String notificationLevelLabel(String level) => switch (level) {
  'none' => 'No pushes',
  'important' => 'Only reminders & approvals',
  _ => 'All pushes',
};

class ModeInfo {
  const ModeInfo({
    required this.id,
    required this.label,
    required this.description,
    required this.notifications,
    required this.customised,
    this.tone,
  });

  final String id;
  final String label;
  final String description;
  final String notifications;
  final String? tone;
  final bool customised;

  static ModeInfo? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final label = map == null ? null : jsonString(map, 'label');
    if (map == null || id == null || label == null) return null;
    return ModeInfo(
      id: id,
      label: label,
      description: jsonString(map, 'description') ?? '',
      notifications: jsonString(map, 'notifications') ?? 'all',
      tone: jsonString(map, 'tone'),
      customised: asJsonBool(map['customised']),
    );
  }
}

class ModeStateData {
  const ModeStateData({
    required this.mode,
    required this.label,
    required this.source,
    required this.reason,
    required this.notifications,
    required this.auto,
    required this.sleepStart,
    required this.sleepEnd,
    required this.modes,
    this.until,
  });

  final String mode;
  final String label;
  final String source;
  final String reason;
  final String notifications;
  final bool auto;
  final String sleepStart;
  final String sleepEnd;
  final DateTime? until;
  final List<ModeInfo> modes;

  bool get manual => source == 'manual';

  static ModeStateData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final mode = map == null ? null : jsonString(map, 'mode');
    final label = map == null ? null : jsonString(map, 'label');
    if (map == null || mode == null || label == null) return null;
    return ModeStateData(
      mode: mode,
      label: label,
      source: jsonString(map, 'source') ?? 'default',
      reason: jsonString(map, 'reason') ?? '',
      notifications: jsonString(map, 'notifications') ?? 'all',
      auto: asJsonBool(map['auto'], true),
      sleepStart: jsonString(map, 'sleepStart') ?? '23:00',
      sleepEnd: jsonString(map, 'sleepEnd') ?? '07:00',
      until: jsonDate(map['until'], local: true),
      modes: [
        for (final item in jsonMaps(map['modes'])) ?ModeInfo.fromJson(item),
      ],
    );
  }
}
