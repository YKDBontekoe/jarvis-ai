import 'package:flutter/widgets.dart';

import '../../json_maps.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/tool_catalog.dart' show humanizeToolName;
import '../chats/chat_list.dart';

/// A ready-made question for the Ask bar, picked for this moment.
@immutable
class HomeSuggestion {
  const HomeSuggestion(this.label, this.prompt, this.icon);

  /// Short text on the chip.
  final String label;

  /// What is sent to Jarvis when the chip is tapped.
  final String prompt;
  final IconData icon;
}

/// Three or four suggestions that fit the time of day, the next plan and who
/// is waiting. Pure, so it is easy to test with a fixed clock.
List<HomeSuggestion> homeSuggestions({
  required DateTime now,
  Map<String, dynamic>? briefing,
  ChatList? chats,
  int max = 4,
}) {
  final result = <HomeSuggestion>[];
  final hour = now.hour;
  final waiting = [
    for (final item in chats?.whatsApp ?? const <ChatListItem>[])
      if (item.unread > 0 && !item.group) item,
  ];
  if (waiting.isNotEmpty) {
    final person = waiting.first.title;
    result.add(
      HomeSuggestion(
        'Reply to $person',
        'Draft a short reply to $person on WhatsApp, in the language of our chat.',
        PhosphorIconsRegular.pencilSimple,
      ),
    );
  }
  final next = _nextReminder(briefing, now);
  if (next != null && next.$2.difference(now) <= const Duration(hours: 3)) {
    result.add(
      HomeSuggestion(
        'Prepare for ${_short(next.$1)}',
        'Help me prepare for "${next.$1}". What should I bring or know?',
        PhosphorIconsRegular.listChecks,
      ),
    );
  }
  if (hour >= 5 && hour < 11) {
    result.add(
      const HomeSuggestion(
        'Plan my day',
        'Plan my day around what is on my calendar and reminders.',
        PhosphorIconsRegular.sunHorizon,
      ),
    );
  } else if (hour >= 11 && hour < 18) {
    result.add(
      const HomeSuggestion(
        "What's left today?",
        'What is still left for me today?',
        PhosphorIconsRegular.listChecks,
      ),
    );
  } else {
    result.add(
      const HomeSuggestion(
        'Plan tomorrow',
        'What does tomorrow look like, and what should I prepare tonight?',
        PhosphorIconsRegular.moon,
      ),
    );
  }
  final groupsWithNews = (chats?.whatsApp ?? const <ChatListItem>[])
      .where((item) => item.group && item.unread > 0)
      .length;
  if (groupsWithNews > 0) {
    result.add(
      const HomeSuggestion(
        'What did the groups say?',
        'Summarize what I missed in my WhatsApp groups, and tell me if anyone asked me something.',
        PhosphorIconsRegular.users,
      ),
    );
  }
  if (result.length < 3) {
    result.add(
      const HomeSuggestion(
        'Remind me later',
        'Remind me to stretch in 20 minutes',
        PhosphorIconsRegular.alarm,
      ),
    );
  }
  return result.take(max).toList();
}

(String, DateTime)? _nextReminder(
  Map<String, dynamic>? briefing,
  DateTime now,
) {
  (String, DateTime)? best;
  for (final reminder in jsonMaps(briefing?['reminders'])) {
    final title = asJsonString(reminder['title']);
    final due = jsonDate(reminder['dueAt'], local: true);
    if (title == null || due == null || due.isBefore(now)) continue;
    if (best == null || due.isBefore(best.$2)) best = (title, due);
  }
  return best;
}

String _short(String text) =>
    text.length <= 22 ? text : '${text.substring(0, 21).trimRight()}…';

enum NeedsYouKind { approval, reminder, person, task, catchUp }

/// One thing on Home that is waiting on the person, with what tapping it does.
@immutable
class NeedsYouItem {
  const NeedsYouItem({
    required this.kind,
    required this.id,
    required this.title,
    required this.detail,
    this.overdue = false,
    this.chat,
    this.chats = const [],
  });

  final NeedsYouKind kind;

  /// The approval, reminder or task id; the chat key for a person.
  final String id;
  final String title;
  final String detail;
  final bool overdue;
  final ChatListItem? chat;

  /// For [NeedsYouKind.catchUp]: every chat with a catch-up.
  final List<ChatListItem> chats;
}

/// What needs the person now, most urgent first: approvals Jarvis is waiting
/// on, reminders that are due, people waiting for a reply, tasks that stopped
/// to ask, and catch-ups.
List<NeedsYouItem> needsYouItems({
  required DateTime now,
  Map<String, dynamic>? briefing,
  ChatList? chats,
  int max = 5,
}) {
  final items = <NeedsYouItem>[];
  for (final approval in jsonMaps(briefing?['approvals'])) {
    final id = asJsonString(approval['id']);
    if (id == null) continue;
    final name = humanizeToolName(asJsonString(approval['toolName']) ?? 'tool');
    final created = jsonDate(approval['createdAt'], local: true);
    items.add(
      NeedsYouItem(
        kind: NeedsYouKind.approval,
        id: id,
        title: name.isEmpty
            ? 'Approval'
            : name[0].toUpperCase() + name.substring(1),
        detail: created == null
            ? 'Jarvis is waiting for your OK'
            : 'Waiting for your OK · ${_ago(created, now)}',
      ),
    );
  }
  for (final reminder in jsonMaps(briefing?['reminders'])) {
    final id = asJsonString(reminder['id']);
    final title = asJsonString(reminder['title']);
    final due = jsonDate(reminder['dueAt'], local: true);
    final status = asJsonString(reminder['status']);
    if (id == null || title == null || due == null) continue;
    if (status == 'done' || status == 'cancelled' || status == 'completed') {
      continue;
    }
    // Due now or overdue; later ones belong to Today and Next up.
    if (due.isAfter(now.add(const Duration(minutes: 5)))) continue;
    final late = now.difference(due);
    items.add(
      NeedsYouItem(
        kind: NeedsYouKind.reminder,
        id: id,
        title: title,
        detail: late.inMinutes < 2 ? 'Due now' : 'Overdue · ${_span(late)}',
        overdue: late.inMinutes >= 2,
      ),
    );
  }
  final waiting =
      [
        for (final item in chats?.whatsApp ?? const <ChatListItem>[])
          if (item.unread > 0 && (!item.group || item.asksYou)) item,
      ]..sort((a, b) {
        if (a.group != b.group) return a.group ? 1 : -1;
        final x = a.time ?? now;
        final y = b.time ?? now;
        return x.compareTo(y);
      });
  for (final chat in waiting) {
    final ask = chat.whatsApp?.catchUp?.toReply.firstOrNull;
    items.add(
      NeedsYouItem(
        kind: NeedsYouKind.person,
        id: chat.key,
        title: chat.title,
        detail: [
          if (chat.group && ask != null)
            '${ask.who} asked: ${ask.about}'
          else if (chat.preview != null)
            chat.preview!,
          if (chat.time != null) _ago(chat.time!, now),
        ].join(' · '),
        chat: chat,
      ),
    );
  }
  for (final task in jsonMaps(briefing?['tasks'])) {
    final id = asJsonString(task['id']);
    final title = asJsonString(task['title']);
    if (id == null || title == null) continue;
    if (asJsonString(task['status']) != 'needs_approval') continue;
    items.add(
      NeedsYouItem(
        kind: NeedsYouKind.task,
        id: id,
        title: title,
        detail: 'A task stopped to ask you something',
      ),
    );
  }
  final catchUps = [
    for (final item in chats?.whatsApp ?? const <ChatListItem>[])
      if (item.whatsApp?.catchUp != null && !waiting.contains(item)) item,
  ];
  if (catchUps.isNotEmpty) {
    items.add(
      NeedsYouItem(
        kind: NeedsYouKind.catchUp,
        id: 'catch-up',
        title: catchUps.length == 1
            ? 'Catch up on ${catchUps.single.title}'
            : 'Catch up on ${catchUps.length} chats',
        detail: catchUps.map((item) => item.title).take(3).join(', '),
        chats: catchUps,
      ),
    );
  }
  return items.take(max).toList();
}

String _ago(DateTime time, DateTime now) {
  final gap = now.difference(time);
  if (gap.inMinutes < 1) return 'just now';
  return '${_span(gap)} ago';
}

String _span(Duration gap) {
  if (gap.inMinutes < 60) return '${gap.inMinutes} min';
  if (gap.inHours < 24) return '${gap.inHours} h';
  return '${gap.inDays} d';
}
