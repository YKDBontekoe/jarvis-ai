import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../../json_maps.dart';
import '../whatsapp/whatsapp_models.dart';

/// Where a chat in the unified list lives.
enum ChatSource { jarvis, whatsapp }

enum ChatFilter {
  all('All'),
  unread('Unread'),
  jarvis('Jarvis'),
  whatsapp('WhatsApp');

  const ChatFilter(this.label);

  final String label;
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
    this.unread = 0,
    this.group = false,
    this.pinned = false,
    this.conversationId,
    this.channelId,
    this.whatsApp,
    this.account,
  });

  /// Unique across sources.
  final String key;
  final ChatSource source;
  final String title;
  final String? preview;
  final DateTime? time;
  final int unread;
  final bool group;
  final bool pinned;

  /// Set for Jarvis conversations.
  final String? conversationId;

  /// Set for WhatsApp chats: the linked account and the chat itself.
  final String? channelId;
  final WhatsAppChat? whatsApp;
  final String? account;

  bool get isJarvis => source == ChatSource.jarvis;
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
          ChatListItem(
            key: 'jarvis:$id',
            source: ChatSource.jarvis,
            title: jsonString(conversation, 'title') ?? 'New conversation',
            preview: jsonString(conversation, 'profileName'),
            time: jsonDate(conversation['updatedAt'], local: true),
            pinned: asJsonBool(conversation['pinned']),
            conversationId: id,
          ),
    ];
    notifyListeners();
  }

  void clear() {
    _revision++;
    _jarvis = const [];
    _whatsApp = const [];
    _whatsAppLinked = false;
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
                    : '${chat.previewFromMe == true ? 'You: ' : ''}${chat.preview}',
                time: chat.lastMessageAt,
                unread: chat.unreadCount,
                group: chat.isGroup,
                channelId: id,
                whatsApp: chat,
                account: asJsonString(account['account']),
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
    } on DioException {
      // Keep the last known chats while the API is unreachable.
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
              ChatFilter.all => true,
              ChatFilter.unread => item.unread > 0,
              ChatFilter.jarvis => item.isJarvis,
              ChatFilter.whatsapp => !item.isJarvis,
            } &&
            (needle.isEmpty ||
                item.title.toLowerCase().contains(needle) ||
                (item.preview?.toLowerCase().contains(needle) ?? false)))
          item,
    ];
  }

  /// The WhatsApp side only, as the tile and unread badge need it.
  List<ChatListItem> get whatsApp => _whatsApp;
}
