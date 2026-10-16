import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../json_maps.dart';
import '../whatsapp/whatsapp_models.dart';

/// Where a chat in the unified list lives.
enum ChatSource { jarvis, whatsapp }

enum ChatFilter {
  forYou('For you'),
  all('All'),
  unread('Unread'),
  jarvis('Jarvis'),
  whatsapp('WhatsApp'),
  groups('Groups');

  const ChatFilter(this.label);

  final String label;
}

/// A heading in the chat list. The For you view groups by what needs you, the
/// others by when the chat last moved.
enum ChatSection {
  waiting('Waiting on you'),
  jarvis('With Jarvis'),
  quiet('Quiet'),
  pinned('Pinned'),
  today('Today'),
  yesterday('Yesterday'),
  thisWeek('This week'),
  earlier('Earlier');

  const ChatSection(this.label);

  final String label;
}

/// One section of the list: its rows, plus the rows folded into a single
/// summary line (reminder chats with Jarvis, busy groups) where it has any.
@immutable
class ChatGroup {
  const ChatGroup(
    this.section,
    this.items, {
    this.reminders = const [],
    this.busyGroups = const [],
  });

  final ChatSection section;
  final List<ChatListItem> items;
  final List<ChatListItem> reminders;
  final List<ChatListItem> busyGroups;
}

/// One row in Chats: a Jarvis conversation or a chat on linked WhatsApp.
@immutable
class ChatListItem {
  const ChatListItem({
    required this.key,
    required this.source,
    required this.title,
    this.preview,
    this.time,
    this.startedAt,
    this.unread = 0,
    this.group = false,
    this.pinned = false,
    this.conversationId,
    this.channelId,
    this.whatsApp,
    this.account,
    this.profile,
    this.fromMe = false,
    this.responding = false,
    this.origin,
    this.originId,
  });

  /// Unique across sources.
  final String key;
  final ChatSource source;
  final String title;
  final String? preview;
  final DateTime? time;
  final DateTime? startedAt;
  final int unread;
  final bool group;
  final bool pinned;

  /// Set for Jarvis conversations.
  final String? conversationId;

  /// Set for WhatsApp chats: the linked account and the chat itself.
  final String? channelId;
  final WhatsAppChat? whatsApp;
  final String? account;

  /// The assistant profile a Jarvis conversation runs under, when it has one.
  final String? profile;

  /// The newest message in the preview is yours.
  final bool fromMe;

  /// Jarvis is writing a reply in this conversation right now.
  final bool responding;

  /// What started a Jarvis conversation (`reminder`) and its id.
  final String? origin;
  final String? originId;

  bool get isJarvis => source == ChatSource.jarvis;

  bool get fromReminder => origin == 'reminder';

  /// A group chat on WhatsApp that asks something of you, per its catch-up.
  bool get asksYou => (whatsApp?.catchUp?.toReply.isNotEmpty ?? false);

  ChatListItem copyWith({bool? pinned, int? unread}) => ChatListItem(
    key: key,
    source: source,
    title: title,
    preview: preview,
    time: time,
    startedAt: startedAt,
    unread: unread ?? this.unread,
    group: group,
    pinned: pinned ?? this.pinned,
    conversationId: conversationId,
    channelId: channelId,
    whatsApp: whatsApp,
    account: account,
    profile: profile,
    fromMe: fromMe,
    responding: responding,
    origin: origin,
    originId: originId,
  );
}

/// Jarvis conversations and WhatsApp chats in one list, newest first with
/// pinned conversations on top. The Chats tab and the Chats tile read it.
class ChatList extends ChangeNotifier {
  List<ChatListItem> _jarvis = const [];
  List<ChatListItem> _whatsApp = const [];
  bool _whatsAppLinked = false;
  bool _loadingWhatsApp = false;
  int _revision = 0;

  bool get whatsAppLinked => _whatsAppLinked;
  bool get loadingWhatsApp => _loadingWhatsApp;

  /// Replaces the Jarvis side from the conversation list the app already loads.
  void setConversations(List<Map<String, dynamic>> conversations) {
    _jarvis = [
      for (final conversation in conversations)
        if (jsonString(conversation, 'id') case final id?)
          _jarvisItem(id, conversation),
    ];
    notifyListeners();
  }

  static ChatListItem _jarvisItem(String id, Map<String, dynamic> json) {
    final last = jsonString(json, 'lastMessage');
    final fromMe = jsonString(json, 'lastMessageRole') == 'user';
    return ChatListItem(
      key: 'jarvis:$id',
      source: ChatSource.jarvis,
      title: jsonString(json, 'title') ?? 'New conversation',
      preview: last == null ? null : '${fromMe ? 'You: ' : ''}$last',
      time: jsonDate(json['updatedAt'], local: true),
      startedAt: jsonDate(json['createdAt'], local: true),
      pinned: asJsonBool(json['pinned']),
      conversationId: id,
      profile: jsonString(json, 'profileName'),
      fromMe: fromMe,
      responding: asJsonBool(json['responding']),
      origin: jsonString(json, 'origin'),
      originId: jsonString(json, 'originId'),
    );
  }

  void clear() {
    _revision++;
    _jarvis = const [];
    _whatsApp = const [];
    _whatsAppLinked = false;
    _whatsAppError = false;
    notifyListeners();
  }

  /// Loads the chats of every linked WhatsApp account. A server without the
  /// WhatsApp bridge, or one that is unreachable, leaves the list as it was.
  Future<void> loadWhatsApp(Dio http) async {
    final revision = ++_revision;
    _loadingWhatsApp = true;
    notifyListeners();
    try {
      final response = await http.get<dynamic>('/api/v1/channels');
      final accounts = [
        for (final channel in jsonMaps(response.data))
          if (asJsonString(channel['kind']) == 'whatsapp_linked' &&
              asJsonString(channel['id']) != null)
            channel,
      ];
      final items = <ChatListItem>[];
      for (final account in accounts) {
        final id = account['id'] as String;
        try {
          final chats = await http.get<dynamic>('/api/v1/channels/$id/chats');
          final body = jsonObject(chats.data);
          for (final item in jsonMaps(body?['chats'])) {
            final chat = WhatsAppChat.fromJson(item);
            if (chat == null || chat.lastMessageAt == null) continue;
            items.add(
              ChatListItem(
                key: 'whatsapp:$id:${chat.chatId}',
                source: ChatSource.whatsapp,
                title: chat.name,
                preview: chat.preview == null
                    ? null
                    : '${chat.previewFromMe == true ? 'You: ' : ''}${whatsAppPreviewText(chat.preview!)}',
                time: chat.lastMessageAt,
                unread: chat.unreadCount,
                group: chat.isGroup,
                channelId: id,
                whatsApp: chat,
                account: asJsonString(account['account']),
                fromMe: chat.previewFromMe == true,
              ),
            );
          }
        } on DioException {
          // One unreachable account never hides the others.
        }
      }
      if (revision != _revision) return;
      _whatsApp = items;
      _whatsAppLinked = accounts.isNotEmpty;
      _whatsAppError = false;
    } on DioException {
      // Keep the last known chats while the API is unreachable.
      if (revision == _revision) _whatsAppError = _whatsAppLinked;
    } catch (_) {
      // A malformed payload leaves the list alone.
    } finally {
      if (revision == _revision) {
        _loadingWhatsApp = false;
        notifyListeners();
      }
    }
  }

  int get unreadCount =>
      _whatsApp.fold(0, (total, item) => total + item.unread);

  /// How many chats have something unread: what the Unread filter and the
  /// tab badge count, not the messages inside them.
  int get unreadChats => _whatsApp.where((item) => item.unread > 0).length;

  /// The last load of WhatsApp chats failed; the list shows what it had.
  bool get whatsAppError => _whatsAppError;
  bool _whatsAppError = false;

  /// Pins or unpins a Jarvis conversation at once and asks the server to keep
  /// it; the row goes back if the server says no.
  Future<bool> setPinned(Dio http, ChatListItem item, bool pinned) async {
    final id = item.conversationId;
    if (id == null) return false;
    _replace(item.key, (current) => current.copyWith(pinned: pinned));
    try {
      await http.patch<dynamic>(
        '/api/v1/conversations/$id',
        data: {'pinned': pinned},
      );
      return true;
    } on DioException {
      _replace(item.key, (current) => current.copyWith(pinned: !pinned));
      return false;
    }
  }

  /// Marks a WhatsApp chat read up to its newest message, optimistically.
  Future<bool> markRead(Dio http, ChatListItem item) async {
    final channel = item.channelId;
    final chat = item.whatsApp;
    if (channel == null || chat == null || item.unread == 0) return false;
    final before = item.unread;
    _replace(item.key, (current) => current.copyWith(unread: 0));
    try {
      await http.post<dynamic>(
        whatsAppChatPath(channel, action: 'read'),
        queryParameters: whatsAppChatQuery(chat.chatId),
        data: const <String, Object?>{},
      );
      return true;
    } on DioException {
      _replace(item.key, (current) => current.copyWith(unread: before));
      return false;
    }
  }

  void _replace(String key, ChatListItem Function(ChatListItem) change) {
    _jarvis = [
      for (final item in _jarvis) item.key == key ? change(item) : item,
    ];
    _whatsApp = [
      for (final item in _whatsApp) item.key == key ? change(item) : item,
    ];
    notifyListeners();
  }

  /// Everything, newest first, pinned Jarvis conversations on top.
  List<ChatListItem> get all {
    final items = [..._jarvis, ..._whatsApp];
    items.sort((a, b) {
      if (a.pinned != b.pinned) return a.pinned ? -1 : 1;
      final x = a.time ?? DateTime.fromMillisecondsSinceEpoch(0);
      final y = b.time ?? DateTime.fromMillisecondsSinceEpoch(0);
      return y.compareTo(x);
    });
    return items;
  }

  /// [all] narrowed by [filter] and a search [query] over titles and previews.
  List<ChatListItem> filtered(ChatFilter filter, [String query = '']) {
    final needle = query.trim().toLowerCase();
    return [
      for (final item in all)
        if (switch (filter) {
              ChatFilter.forYou || ChatFilter.all => true,
              ChatFilter.unread => item.unread > 0,
              ChatFilter.jarvis => item.isJarvis,
              ChatFilter.whatsapp => !item.isJarvis,
              ChatFilter.groups => item.group,
            } &&
            (needle.isEmpty ||
                item.title.toLowerCase().contains(needle) ||
                (item.preview?.toLowerCase().contains(needle) ?? false)))
          item,
    ];
  }

  /// The WhatsApp side only, as the tile and unread badge need it.
  List<ChatListItem> get whatsApp => _whatsApp;

  /// How many chats the For you view shows in each calm section before it
  /// points to the full list.
  static const forYouJarvisLimit = 4;
  static const forYouQuietLimit = 5;

  /// [filtered] split into sections. Searching shows one flat section.
  List<ChatGroup> grouped(
    ChatFilter filter, {
    String query = '',
    DateTime? now,
  }) {
    final items = filtered(filter, query);
    if (items.isEmpty) return const [];
    if (query.trim().isNotEmpty) return [ChatGroup(ChatSection.today, items)];
    if (filter == ChatFilter.forYou) return _forYou(items);
    return _byTime(items, now ?? DateTime.now());
  }

  static List<ChatGroup> _forYou(List<ChatListItem> items) {
    final waiting =
        [
          for (final item in items)
            if (!item.isJarvis &&
                item.unread > 0 &&
                (!item.group || item.asksYou))
              item,
        ]..sort((a, b) {
          // People before groups, then whoever has waited longest.
          if (a.group != b.group) return a.group ? 1 : -1;
          final x = a.time ?? DateTime.fromMillisecondsSinceEpoch(0);
          final y = b.time ?? DateTime.fromMillisecondsSinceEpoch(0);
          return x.compareTo(y);
        });
    final jarvis =
        [
          for (final item in items)
            if (item.isJarvis && !item.fromReminder) item,
        ]..sort((a, b) {
          if (a.responding != b.responding) return a.responding ? -1 : 1;
          return 0;
        });
    final reminders = [
      for (final item in items)
        if (item.isJarvis && item.fromReminder) item,
    ];
    final busy = [
      for (final item in items)
        if (!item.isJarvis && item.group && item.unread > 0 && !item.asksYou)
          item,
    ];
    final quiet = [
      for (final item in items)
        if (!item.isJarvis && item.unread == 0) item,
    ];
    return [
      if (waiting.isNotEmpty) ChatGroup(ChatSection.waiting, waiting),
      if (jarvis.isNotEmpty || reminders.isNotEmpty)
        ChatGroup(
          ChatSection.jarvis,
          jarvis.take(forYouJarvisLimit).toList(),
          reminders: reminders,
        ),
      if (quiet.isNotEmpty || busy.isNotEmpty)
        ChatGroup(
          ChatSection.quiet,
          quiet.take(forYouQuietLimit).toList(),
          busyGroups: busy,
        ),
    ];
  }

  static List<ChatGroup> _byTime(List<ChatListItem> items, DateTime now) {
    final today = DateTime(now.year, now.month, now.day);
    ChatSection sectionOf(ChatListItem item) {
      if (item.pinned) return ChatSection.pinned;
      final time = item.time;
      if (time == null) return ChatSection.earlier;
      final days = today
          .difference(DateTime(time.year, time.month, time.day))
          .inDays;
      if (days <= 0) return ChatSection.today;
      if (days == 1) return ChatSection.yesterday;
      if (days < 7) return ChatSection.thisWeek;
      return ChatSection.earlier;
    }

    final bySection = <ChatSection, List<ChatListItem>>{};
    for (final item in items) {
      bySection.putIfAbsent(sectionOf(item), () => []).add(item);
    }
    return [
      for (final section in const [
        ChatSection.pinned,
        ChatSection.today,
        ChatSection.yesterday,
        ChatSection.thisWeek,
        ChatSection.earlier,
      ])
        if (bySection[section] case final rows?) ChatGroup(section, rows),
    ];
  }
}
