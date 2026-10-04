import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../whatsapp/read_along_screen.dart';
import '../whatsapp/whatsapp_models.dart';
import 'chat_list.dart';

/// Every conversation in one place: your chats with Jarvis and your WhatsApp.
class ChatsScreen extends StatefulWidget {
  const ChatsScreen({
    required this.chats,
    required this.onOpen,
    required this.onNewChat,
    required this.onSearch,
    required this.onRefresh,
    this.onManage,
    this.onConnectWhatsApp,
    this.selectedKey,
    this.http,
    super.key,
  });

  final ChatList chats;
  final ValueChanged<ChatListItem> onOpen;
  final VoidCallback? onNewChat;
  final VoidCallback onSearch;
  final Future<void> Function() onRefresh;

  /// Opens the full conversation history, to rename, pin, export or delete.
  final VoidCallback? onManage;

  /// Offered on the WhatsApp filter while no account is linked.
  final VoidCallback? onConnectWhatsApp;

  /// The row to mark as open (wide layout).
  final String? selectedKey;

  /// Loads WhatsApp profile pictures. Without it, rows keep their initials.
  final Dio? http;

  @override
  State<ChatsScreen> createState() => _ChatsScreenState();
}

class _ChatsScreenState extends State<ChatsScreen> {
  final _query = TextEditingController();
  var _filter = ChatFilter.all;

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 640),
        child: ListenableBuilder(
          listenable: widget.chats,
          builder: (context, _) {
            final items = widget.chats.filtered(_filter, _query.text);
            final unread = widget.chats.unreadCount;
            return Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Padding(
                  padding: const EdgeInsets.fromLTRB(20, 12, 8, 0),
                  child: Row(
                    children: [
                      Expanded(
                        child: Text(
                          'Chats',
                          style: JarvisType.displayOf(
                            context,
                          ).copyWith(fontSize: 28),
                        ),
                      ),
                      IconButton(
                        key: const Key('chats-search'),
                        tooltip: 'Search everything',
                        onPressed: widget.onSearch,
                        icon: Icon(
                          PhosphorIconsRegular.magnifyingGlass,
                          size: 20,
                          color: colors.inkSoft,
                        ),
                      ),
                      if (widget.onManage != null)
                        IconButton(
                          key: const Key('chats-manage'),
                          tooltip: 'All conversations',
                          onPressed: widget.onManage,
                          icon: Icon(
                            PhosphorIconsRegular.dotsThree,
                            size: 22,
                            color: colors.inkSoft,
                          ),
                        ),
                      IconButton(
                        key: const Key('chats-new'),
                        tooltip: 'New chat',
                        onPressed: widget.onNewChat,
                        icon: Icon(
                          PhosphorIconsRegular.notePencil,
                          size: 20,
                          color: colors.inkSoft,
                        ),
                      ),
                    ],
                  ),
                ),
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 10, 16, 0),
                  child: TextField(
                    key: const Key('chats-filter-field'),
                    controller: _query,
                    onChanged: (_) => setState(() {}),
                    decoration: InputDecoration(
                      hintText: 'Search chats',
                      prefixIcon: Icon(
                        PhosphorIconsRegular.magnifyingGlass,
                        size: 18,
                        color: colors.muted,
                      ),
                      filled: true,
                      fillColor: colors.surfaceMuted,
                      contentPadding: const EdgeInsets.symmetric(vertical: 12),
                      border: _noBorder,
                      enabledBorder: _noBorder,
                      focusedBorder: _noBorder,
                    ),
                  ),
                ),
                _FilterTabs(
                  selected: _filter,
                  unread: unread,
                  onSelected: (filter) => setState(() => _filter = filter),
                ),
                Expanded(
                  child: RefreshIndicator(
                    onRefresh: widget.onRefresh,
                    child: items.isEmpty
                        ? _Empty(
                            filter: _filter,
                            searching: _query.text.trim().isNotEmpty,
                            linked: widget.chats.whatsAppLinked,
                            onConnect: widget.onConnectWhatsApp,
                          )
                        : ListView.builder(
                            key: const Key('chats-list'),
                            physics: const AlwaysScrollableScrollPhysics(),
                            padding: const EdgeInsets.fromLTRB(16, 4, 16, 24),
                            itemCount: items.length,
                            itemBuilder: (context, index) => _ChatRow(
                              item: items[index],
                              selected: items[index].key == widget.selectedKey,
                              http: widget.http,
                              onTap: () => widget.onOpen(items[index]),
                            ),
                          ),
                  ),
                ),
              ],
            );
          },
        ),
      ),
    );
  }
}

const _noBorder = OutlineInputBorder(
  borderRadius: BorderRadius.all(Radius.circular(12)),
  borderSide: BorderSide.none,
);

class _FilterTabs extends StatelessWidget {
  const _FilterTabs({
    required this.selected,
    required this.unread,
    required this.onSelected,
  });

  final ChatFilter selected;
  final int unread;
  final ValueChanged<ChatFilter> onSelected;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
      decoration: BoxDecoration(
        border: Border(bottom: BorderSide(color: colors.outline)),
      ),
      child: SingleChildScrollView(
        scrollDirection: Axis.horizontal,
        child: Row(
          children: [
            for (final filter in ChatFilter.values)
              Semantics(
                button: true,
                selected: filter == selected,
                child: InkWell(
                  key: Key('chats-tab-${filter.name}'),
                  onTap: () => onSelected(filter),
                  child: Container(
                    padding: const EdgeInsets.fromLTRB(0, 10, 0, 10),
                    margin: const EdgeInsets.only(right: 20),
                    decoration: BoxDecoration(
                      border: Border(
                        bottom: BorderSide(
                          width: 2,
                          color: filter == selected
                              ? colors.ink
                              : Colors.transparent,
                        ),
                      ),
                    ),
                    child: Text(
                      filter == ChatFilter.unread && unread > 0
                          ? '${filter.label} $unread'
                          : filter.label,
                      style: TextStyle(
                        fontSize: 14,
                        fontWeight: FontWeight.w500,
                        color: filter == selected ? colors.ink : colors.muted,
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

class _ChatRow extends StatelessWidget {
  const _ChatRow({
    required this.item,
    required this.selected,
    required this.onTap,
    this.http,
  });

  final ChatListItem item;
  final bool selected;
  final VoidCallback onTap;
  final Dio? http;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final unread = item.unread > 0;
    final preview = item.isJarvis
        ? (item.preview ?? 'Jarvis')
        : 'WhatsApp · ${item.preview ?? 'No messages yet'}';
    return Semantics(
      button: true,
      selected: selected,
      label: [
        item.title,
        if (unread) '${item.unread} unread',
        if (!item.isJarvis) 'WhatsApp',
      ].join(', '),
      onTap: onTap,
      excludeSemantics: true,
      child: InkWell(
        key: Key('chat-${item.key}'),
        borderRadius: BorderRadius.circular(14),
        onTap: onTap,
        child: Container(
          padding: const EdgeInsets.symmetric(vertical: 10, horizontal: 4),
          decoration: BoxDecoration(
            color: selected ? colors.surfaceMuted : null,
            borderRadius: BorderRadius.circular(14),
          ),
          child: Row(
            children: [
              _Avatar(item: item, http: http),
              const SizedBox(width: 12),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        if (item.pinned) ...[
                          Icon(
                            PhosphorIconsRegular.pushPin,
                            size: 12,
                            color: colors.muted,
                          ),
                          const SizedBox(width: 4),
                        ],
                        Expanded(
                          child: Text(
                            item.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 15,
                              fontWeight: FontWeight.w600,
                              letterSpacing: -.1,
                              color: colors.ink,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          whatsAppListTime(item.time),
                          style: TextStyle(
                            fontSize: 12,
                            fontWeight: unread
                                ? FontWeight.w600
                                : FontWeight.w400,
                            color: unread ? colors.accent : colors.muted,
                          ),
                        ),
                      ],
                    ),
                    const SizedBox(height: 2),
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            preview,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 13.5,
                              color: colors.inkSoft,
                            ),
                          ),
                        ),
                        if (unread) ...[
                          const SizedBox(width: 8),
                          Container(
                            key: const Key('chat-unread-dot'),
                            width: 8,
                            height: 8,
                            decoration: BoxDecoration(
                              color: colors.accent,
                              shape: BoxShape.circle,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Avatar extends StatelessWidget {
  const _Avatar({required this.item, this.http});

  final ChatListItem item;
  final Dio? http;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    if (item.isJarvis) return const JarvisOrb(size: 44, glow: false);
    final chat = item.whatsApp;
    final channelId = item.channelId;
    // The open chat already loads this picture. The list uses the same
    // request and the same session cache, so a face seen in a chat shows here.
    if (chat != null && channelId != null) {
      return ChatAvatar(
        key: ValueKey('chat-avatar-${item.key}'),
        chat: chat,
        http: http,
        channelId: channelId,
        size: 44,
      );
    }
    return Container(
      width: 44,
      height: 44,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        shape: BoxShape.circle,
      ),
      child: item.group
          ? Icon(PhosphorIconsRegular.users, size: 20, color: colors.inkSoft)
          : Text(
              item.whatsApp?.initials ?? '#',
              style: TextStyle(
                fontSize: 15,
                fontWeight: FontWeight.w600,
                color: colors.inkSoft,
              ),
            ),
    );
  }
}

class _Empty extends StatelessWidget {
  const _Empty({
    required this.filter,
    required this.searching,
    required this.linked,
    required this.onConnect,
  });

  final ChatFilter filter;
  final bool searching;
  final bool linked;
  final VoidCallback? onConnect;

  @override
  Widget build(BuildContext context) {
    final (icon, title, message) = switch ((searching, filter)) {
      (true, _) => (
        PhosphorIconsRegular.magnifyingGlass,
        'No chats match',
        'Try another word.',
      ),
      (_, ChatFilter.unread) => (
        PhosphorIconsRegular.checkCircle,
        'You are all caught up',
        'Unread WhatsApp chats show up here.',
      ),
      (_, ChatFilter.whatsapp) when !linked => (
        PhosphorIconsRegular.whatsappLogo,
        'Bring your WhatsApp in',
        'Link your account and its chats appear here next to Jarvis.',
      ),
      (_, ChatFilter.whatsapp) => (
        PhosphorIconsRegular.whatsappLogo,
        'No WhatsApp chats yet',
        'Messages show up here once they arrive.',
      ),
      _ => (
        PhosphorIconsRegular.chatCircle,
        'No conversations yet',
        'Start one with the pencil above.',
      ),
    };
    return ListView(
      physics: const AlwaysScrollableScrollPhysics(),
      children: [
        SizedBox(
          height: 360,
          child: EmptyState(
            icon: icon,
            title: title,
            message: message,
            action:
                filter == ChatFilter.whatsapp &&
                    !linked &&
                    !searching &&
                    onConnect != null
                ? FilledButton(
                    key: const Key('chats-connect-whatsapp'),
                    onPressed: onConnect,
                    child: const Text('Connect WhatsApp'),
                  )
                : null,
          ),
        ),
      ],
    );
  }
}
