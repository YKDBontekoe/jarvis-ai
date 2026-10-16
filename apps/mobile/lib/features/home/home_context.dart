import 'package:flutter/foundation.dart';

import '../../json_maps.dart';
import '../chats/chat_list.dart';

/// `/home/context`: what Home leads with, read from the owner's own data.
@immutable
class HomeContext {
  const HomeContext({
    this.dayLine,
    this.hero,
    this.people = const [],
    this.noticed = const [],
    this.tomorrow = const [],
    this.recap = const HomeRecap(),
  });

  final String? dayLine;
  final HomeHero? hero;
  final List<HomePerson> people;
  final List<HomeInsight> noticed;
  final List<HomeAgendaItem> tomorrow;
  final HomeRecap recap;

  static HomeContext? fromJson(Object? data) {
    final json = jsonObject(data);
    if (json == null) return null;
    return HomeContext(
      dayLine: asJsonString(json['dayLine']),
      hero: HomeHero.fromJson(json['hero']),
      people: [
        for (final item in jsonMaps(json['people'])) ?HomePerson.fromJson(item),
      ],
      noticed: [
        for (final item in jsonMaps(json['noticed']))
          ?HomeInsight.fromJson(item),
      ],
      tomorrow: [
        for (final item in jsonMaps(json['tomorrow']))
          if (asJsonString(item['title']) case final title?)
            HomeAgendaItem(
              title,
              jsonDate(item['at'], local: true),
              asJsonString(item['kind']) ?? 'reminder',
            ),
      ],
      recap: HomeRecap.fromJson(json['dayRecap']),
    );
  }

  /// The person linked to a chat, if any.
  HomePerson? personFor(ChatListItem item) => personForChat(people, item);
}

/// The person in [people] linked to [item]'s WhatsApp chat, if any.
HomePerson? personForChat(List<HomePerson> people, ChatListItem item) {
  if (item.isJarvis || item.channelId == null || item.whatsApp == null) {
    return null;
  }
  final chat = sameChatId(item.whatsApp!.chatId);
  for (final person in people) {
    if (person.connectionId == item.channelId &&
        sameChatId(person.chatId) == chat) {
      return person;
    }
  }
  return null;
}

/// Chat ids differ in form between the bridge and People links ("+3161…",
/// "3161…@s.whatsapp.net"). Digits identify a phone chat; anything else is
/// compared whole.
String sameChatId(String chatId) {
  final at = chatId.indexOf('@');
  final local = at < 0 ? chatId : chatId.substring(0, at);
  final digits = local.replaceAll(RegExp(r'\D'), '');
  final domain = at < 0 ? '' : chatId.substring(at);
  if (digits.isNotEmpty && (domain.isEmpty || domain == '@s.whatsapp.net')) {
    return digits;
  }
  return chatId.toLowerCase();
}

/// The one card at the top of Home.
@immutable
class HomeHero {
  const HomeHero({
    required this.id,
    required this.kind,
    required this.title,
    required this.reason,
    this.detail,
    this.startsAt,
    this.facts = const [],
    this.reminderId,
  });

  final String id;

  /// `soon`, `brief`, `battery` or `wind_down`.
  final String kind;
  final String title;
  final String? detail;
  final DateTime? startsAt;
  final String reason;
  final List<String> facts;
  final String? reminderId;

  static HomeHero? fromJson(Object? data) {
    final json = jsonObject(data);
    final id = asJsonString(json?['id']);
    final kind = asJsonString(json?['kind']);
    final title = asJsonString(json?['title']);
    if (json == null || id == null || kind == null || title == null) {
      return null;
    }
    return HomeHero(
      id: id,
      kind: kind,
      title: title,
      detail: asJsonString(json['detail']),
      startsAt: jsonDate(json['startsAt'], local: true),
      reason: asJsonString(json['reason']) ?? '',
      facts: jsonStrings(json['facts']),
      reminderId: asJsonString(json['reminderId']),
    );
  }
}

/// Someone linked to a WhatsApp chat, with how the two of you keep in touch.
@immutable
class HomePerson {
  const HomePerson({
    required this.personId,
    required this.name,
    required this.connectionId,
    required this.chatId,
    this.relationship,
    this.usualReplyMinutes,
    this.waitingHours,
    this.quiet = false,
    this.baselinePerWeek,
    this.lastMessageAt,
    this.birthday,
  });

  final String personId;
  final String name;
  final String? relationship;
  final String connectionId;
  final String chatId;
  final double? usualReplyMinutes;
  final double? waitingHours;
  final bool quiet;

  /// How many messages a week the two of you usually exchange.
  final double? baselinePerWeek;
  final DateTime? lastMessageAt;

  /// "dd-MM".
  final String? birthday;

  static HomePerson? fromJson(Map<String, dynamic> json) {
    final id = asJsonString(json['personId']);
    final name = asJsonString(json['name']);
    final connection = asJsonString(json['connectionId']);
    final chat = asJsonString(json['chatId']);
    if (id == null || name == null || connection == null || chat == null) {
      return null;
    }
    return HomePerson(
      personId: id,
      name: name,
      connectionId: connection,
      chatId: chat,
      relationship: asJsonString(json['relationship']),
      usualReplyMinutes: _number(json['usualReplyMinutes']),
      waitingHours: _number(json['waitingHours']),
      quiet: asJsonBool(json['quiet']),
      baselinePerWeek: _number(json['baselinePerWeek']),
      lastMessageAt: jsonDate(json['lastMessageAt'], local: true),
      birthday: asJsonString(json['birthday']),
    );
  }

  /// "Quiet lately · you usually talk weekly", when the radar saw it drift.
  String? get drift {
    if (!quiet) return null;
    final rate = baselinePerWeek;
    final habit = rate == null
        ? null
        : rate >= 5
        ? 'daily'
        : rate >= .8
        ? 'weekly'
        : null;
    return habit == null
        ? 'Quiet lately'
        : 'Quiet lately · you usually talk $habit';
  }

  /// How far past the usual reply time this chat is; 0 without a habit.
  double get overdue {
    final usual = usualReplyMinutes;
    final waited = waitingHours;
    if (usual == null || usual <= 0 || waited == null) return 0;
    return waited * 60 / usual;
  }

  /// "you usually reply in 20 min", or null when the radar has no habit yet.
  String? get usualReply {
    final minutes = usualReplyMinutes;
    if (minutes == null || minutes <= 0) return null;
    if (minutes < 60) return 'You usually reply in ${minutes.round()} min';
    final hours = minutes / 60;
    if (hours < 24) return 'You usually reply in ${hours.round()} h';
    return null;
  }
}

/// Something Jarvis noticed, with the evidence for it.
@immutable
class HomeInsight {
  const HomeInsight({
    required this.id,
    required this.kind,
    required this.title,
    required this.evidence,
    required this.actionLabel,
    this.confidence,
    this.sourceId,
    this.personId,
  });

  final String id;

  /// `routine`, `drift` or `birthday`.
  final String kind;
  final String title;
  final String evidence;
  final String actionLabel;
  final double? confidence;
  final String? sourceId;
  final String? personId;

  static HomeInsight? fromJson(Map<String, dynamic> json) {
    final id = asJsonString(json['id']);
    final kind = asJsonString(json['kind']);
    final title = asJsonString(json['title']);
    if (id == null || kind == null || title == null) return null;
    return HomeInsight(
      id: id,
      kind: kind,
      title: title,
      evidence: asJsonString(json['evidence']) ?? '',
      actionLabel: asJsonString(json['actionLabel']) ?? 'Open',
      confidence: _number(json['confidence']),
      sourceId: asJsonString(json['sourceId']),
      personId: asJsonString(json['personId']),
    );
  }
}

@immutable
class HomeAgendaItem {
  const HomeAgendaItem(this.title, this.at, this.kind);

  final String title;
  final DateTime? at;
  final String kind;
}

@immutable
class HomeRecap {
  const HomeRecap({
    this.remindersDone = 0,
    this.tasksDone = 0,
    this.done = const [],
  });

  final int remindersDone;
  final int tasksDone;
  final List<String> done;

  static HomeRecap fromJson(Object? data) {
    final json = jsonObject(data);
    if (json == null) return const HomeRecap();
    return HomeRecap(
      remindersDone: _number(json['remindersDone'])?.round() ?? 0,
      tasksDone: _number(json['tasksDone'])?.round() ?? 0,
      done: jsonStrings(json['done']),
    );
  }
}

double? _number(Object? value) => value is num ? value.toDouble() : null;
