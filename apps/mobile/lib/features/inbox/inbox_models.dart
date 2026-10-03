import '../../json_maps.dart';

const inboxStates = ['needs_reply', 'waiting', 'fyi', 'snoozed', 'done'];

String inboxStateLabel(String state) => switch (state) {
  'needs_reply' => 'Needs reply',
  'waiting' => 'Waiting',
  'fyi' => 'For info',
  'snoozed' => 'Snoozed',
  'done' => 'Done',
  _ => state,
};

String priorityLabel(int priority) => switch (priority) {
  >= 3 => 'Urgent',
  2 => 'High',
  1 => 'Normal',
  _ => 'Low',
};

class InboxThreadData {
  const InboxThreadData({
    required this.id,
    required this.source,
    required this.title,
    required this.state,
    required this.priority,
    this.counterparty,
    this.summary,
    this.suggestedReply,
    this.preview,
    this.lastFromMe = false,
    this.lastMessageAt,
  });

  final String id;
  final String source;
  final String title;
  final String? counterparty;
  final String state;
  final int priority;
  final String? summary;
  final String? suggestedReply;
  final String? preview;
  final bool lastFromMe;
  final DateTime? lastMessageAt;

  static InboxThreadData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    final id = jsonString(map, 'id');
    final title = jsonString(map, 'title');
    final state = jsonString(map, 'state');
    if (id == null || title == null || state == null) return null;
    return InboxThreadData(
      id: id,
      source: jsonString(map, 'source') ?? 'manual',
      title: title,
      counterparty: jsonString(map, 'counterparty'),
      state: state,
      priority: asJsonInt(map['priority'], 1),
      summary: jsonString(map, 'summary'),
      suggestedReply: jsonString(map, 'suggestedReply'),
      preview: jsonString(map, 'lastMessagePreview'),
      lastFromMe: asJsonBool(map['lastFromMe']),
      lastMessageAt: jsonDate(map['lastMessageAt'], local: true),
    );
  }
}

class CommitmentData {
  const CommitmentData({
    required this.id,
    required this.direction,
    required this.counterparty,
    required this.description,
    required this.status,
    required this.suggested,
    this.dueOn,
  });

  final String id;
  final String direction;
  final String counterparty;
  final String description;
  final String status;
  final bool suggested;
  final DateTime? dueOn;

  bool get iOwe => direction == 'i_owe';

  bool isOverdue(DateTime today) =>
      status == 'open' &&
      !suggested &&
      dueOn != null &&
      dueOn!.isBefore(DateTime(today.year, today.month, today.day));

  String get headline => iOwe
      ? 'You owe $counterparty'
      : '$counterparty owes you';

  static CommitmentData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    final id = jsonString(map, 'id');
    final description = jsonString(map, 'description');
    if (id == null || description == null) return null;
    final due = jsonString(map, 'dueOn');
    final parsed = due == null ? null : DateTime.tryParse(due);
    return CommitmentData(
      id: id,
      direction: jsonString(map, 'direction') ?? 'i_owe',
      counterparty: jsonString(map, 'counterparty') ?? 'Someone',
      description: description,
      status: jsonString(map, 'status') ?? 'open',
      suggested: asJsonBool(map['suggested']),
      dueOn: parsed == null
          ? null
          : DateTime(parsed.year, parsed.month, parsed.day),
    );
  }
}

class InboxData {
  const InboxData({required this.threads, required this.counts});

  final List<InboxThreadData> threads;
  final Map<String, int> counts;

  static InboxData? fromJson(dynamic json) {
    final map = jsonObject(json);
    if (map == null) return null;
    final counts = <String, int>{};
    jsonObject(map['counts'])?.forEach((key, value) {
      counts[key] = asJsonInt(value);
    });
    return InboxData(
      threads: [
        for (final item in jsonMaps(map['threads']))
          ?InboxThreadData.fromJson(item),
      ],
      counts: counts,
    );
  }
}
