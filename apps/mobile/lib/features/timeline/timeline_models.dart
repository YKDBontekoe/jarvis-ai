import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

/// Kinds of moments the server merges into the timeline, in filter order.
const timelineKinds = [
  'journal',
  'expense',
  'habit',
  'contact',
  'birthday',
  'task',
  'reminder',
  'memory',
  'conversation',
  'decision',
];

String timelineKindLabel(String kind) => switch (kind) {
  'journal' => 'Journal',
  'expense' => 'Spending',
  'habit' => 'Habits',
  'contact' => 'People',
  'birthday' => 'Birthdays',
  'task' => 'Tasks',
  'reminder' => 'Reminders',
  'memory' => 'Learned',
  'conversation' => 'Chats',
  'decision' => 'Decisions',
  _ => 'Other',
};

IconData timelineKindIcon(String kind) => switch (kind) {
  'journal' => PhosphorIconsRegular.pencilSimple,
  'expense' => PhosphorIconsRegular.wallet,
  'habit' => PhosphorIconsRegular.target,
  'contact' => PhosphorIconsRegular.users,
  'birthday' => PhosphorIconsRegular.cake,
  'task' => PhosphorIconsRegular.listChecks,
  'reminder' => PhosphorIconsRegular.bell,
  'memory' => PhosphorIconsRegular.brain,
  'conversation' => PhosphorIconsRegular.chatCircle,
  'decision' => PhosphorIconsRegular.hourglassMedium,
  _ => PhosphorIconsRegular.circle,
};

Color timelineKindColor(JarvisColors colors, String kind) => switch (kind) {
  'journal' => colors.violet,
  'expense' => colors.warning,
  'habit' => colors.success,
  'contact' || 'birthday' => colors.rose,
  'task' || 'decision' => colors.info,
  'reminder' => colors.sky,
  'memory' => colors.accent,
  _ => colors.inkSoft,
};

DateTime? _date(dynamic value) {
  final raw = asJsonString(value);
  if (raw == null) return null;
  final parsed = DateTime.tryParse(raw);
  return parsed == null
      ? null
      : DateTime(parsed.year, parsed.month, parsed.day);
}

class TimelineEventData {
  const TimelineEventData({
    required this.id,
    required this.kind,
    required this.title,
    required this.at,
    required this.date,
    this.detail,
  });

  final String id;
  final String kind;
  final String title;
  final String? detail;
  final DateTime at;
  final DateTime date;

  static TimelineEventData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    final id = jsonString(map, 'id');
    final kind = jsonString(map, 'kind');
    final title = jsonString(map, 'title');
    final date = _date(map['date']);
    if (id == null || kind == null || title == null || date == null) {
      return null;
    }
    return TimelineEventData(
      id: id,
      kind: kind,
      title: title,
      detail: jsonString(map, 'detail'),
      at: jsonDate(map['at'], local: true) ?? date,
      date: date,
    );
  }
}

class TimelineDayData {
  const TimelineDayData({required this.date, required this.events});

  final DateTime date;
  final List<TimelineEventData> events;
}

class TimelineData {
  const TimelineData({
    required this.days,
    required this.total,
    required this.truncated,
    required this.failedKinds,
  });

  final List<TimelineDayData> days;
  final int total;
  final bool truncated;
  final List<String> failedKinds;

  static TimelineData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    final days = <TimelineDayData>[];
    for (final day in jsonMaps(map['days'])) {
      final date = _date(day['date']);
      if (date == null) continue;
      final events = [
        for (final event in jsonMaps(day['events']))
          ?TimelineEventData.fromJson(event),
      ];
      if (events.isNotEmpty) {
        days.add(TimelineDayData(date: date, events: events));
      }
    }
    return TimelineData(
      days: days,
      total: asJsonInt(map['total']),
      truncated: asJsonBool(map['truncated']),
      failedKinds: jsonStrings(map['failedKinds']),
    );
  }
}

class TimelineInsightData {
  const TimelineInsightData({required this.headline, required this.detail});

  final String headline;
  final String detail;

  static List<TimelineInsightData> listFromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return const [];
    return [
      for (final item in jsonMaps(map['insights']))
        if (jsonString(item, 'headline') case final headline?)
          TimelineInsightData(
            headline: headline,
            detail: jsonString(item, 'detail') ?? '',
          ),
    ];
  }
}

class TimelineYearData {
  const TimelineYearData({required this.year, required this.events});

  final int year;
  final List<TimelineEventData> events;

  static List<TimelineYearData> listFromJson(dynamic json) => [
    for (final item in jsonMaps(json))
      if (item['year'] is int)
        TimelineYearData(
          year: item['year'] as int,
          events: [
            for (final event in jsonMaps(item['events']))
              ?TimelineEventData.fromJson(event),
          ],
        ),
  ];
}
