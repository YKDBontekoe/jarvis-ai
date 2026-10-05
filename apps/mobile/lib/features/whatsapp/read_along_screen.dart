import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'catch_up_card.dart';
import 'whatsapp_chat_screen.dart';
import 'whatsapp_models.dart';

/// Pick which chats on the owner's own WhatsApp Jarvis may read along with.
/// Everything starts off, groups included; chats that are on open a chat view
/// with reply drafts, "Ask Jarvis" and automatic reminders.
class ReadAlongScreen extends StatefulWidget {
  const ReadAlongScreen({
    required this.http,
    required this.channelId,
    this.account,
    this.title = 'Read along',
    this.accountPickerBuilder,
    this.selecting = true,
    this.onManageAccount,
    this.onConnect,
    super.key,
  });

  final Dio http;
  final String channelId;
  final String? account;
  final String title;
  final Widget Function(BuildContext context, String account, String state)?
  accountPickerBuilder;
  final bool selecting;
  final VoidCallback? onManageAccount;
  final VoidCallback? onConnect;

  @override
  State<ReadAlongScreen> createState() => _ReadAlongScreenState();
}

class _ReadAlongScreenState extends State<ReadAlongScreen> {
  final _search = TextEditingController();
  List<WhatsAppChat> _chats = const [];
  bool _live = true;
  bool _loading = true;
  String? _error;
  String? _refreshError;
  String? _account;
  String _state = 'open';
  final Set<String> _saving = {};
  int _requestRevision = 0;
  Timer? _refresh;
  bool _fetching = false;
  String _filter = 'All';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
    // The phone keeps adding chats while history syncs right after linking.
    _refresh = Timer.periodic(const Duration(seconds: 20), (_) {
      if (mounted &&
          _saving.isEmpty &&
          ModalRoute.of(context)?.isCurrent != false) {
        unawaited(_load(quiet: true));
      }
    });
  }

  @override
  void dispose() {
    _refresh?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({bool quiet = false}) async {
    if (_fetching) return;
    _fetching = true;
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/channels/${widget.channelId}/chats',
      );
      if (!mounted || revision != _requestRevision) return;
      final body = jsonObject(response.data);
      if (body == null || body['chats'] is! List) {
        throw const FormatException('Invalid chat list');
      }
      setState(() {
        _chats = [
          for (final item in jsonMaps(body['chats']))
            ?WhatsAppChat.fromJson(item),
        ];
        _live = asJsonBool(body['live'], true);
        _state =
            asJsonString(body['state']) ?? (_live ? 'open' : 'unreachable');
        _account = asJsonString(body['account']) ?? widget.account;
        _loading = false;
        _error = null;
        _refreshError = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      if (quiet) {
        setState(() {
          _live = false;
          _state = 'unreachable';
          _refreshError =
              'Could not refresh chats. Showing the last saved list; retrying automatically.';
        });
        return;
      }
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your WhatsApp chats.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      if (quiet) {
        setState(() {
          _live = false;
          _state = 'unreachable';
          _refreshError = 'Could not refresh chats. Retrying automatically.';
        });
        return;
      }
      setState(() {
        _loading = false;
        _error = 'Could not load your WhatsApp chats.';
      });
    } finally {
      _fetching = false;
    }
  }

  Future<void> _setReadAlong(WhatsAppChat chat, bool on) async {
    if (_saving.contains(chat.chatId)) return;
    final index = _chats.indexWhere((x) => x.chatId == chat.chatId);
    if (index < 0) return;
    ++_requestRevision;
    setState(() {
      _saving.add(chat.chatId);
      _chats = [..._chats]..[index] = chat.copyWith(readAlong: on);
    });
    try {
      await widget.http.put<dynamic>(
        whatsAppChatPath(widget.channelId),
        queryParameters: whatsAppChatQuery(chat.chatId),
        data: {
          'name': chat.name,
          'readAlong': on,
          'autoReminders': chat.autoReminders,
        },
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            on
                ? 'Jarvis now reads along with ${chat.name}.'
                : 'Jarvis stopped reading ${chat.name}.',
          ),
        ),
      );
    } on DioException catch (error) {
      if (!mounted) return;
      _revert(chat);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ??
                'Could not change ${chat.name}.',
          ),
        ),
      );
    } catch (_) {
      if (mounted) _revert(chat);
    } finally {
      if (mounted) setState(() => _saving.remove(chat.chatId));
    }
  }

  void _revert(WhatsAppChat chat) {
    final index = _chats.indexWhere((x) => x.chatId == chat.chatId);
    if (index < 0) return;
    setState(() => _chats = [..._chats]..[index] = chat);
  }

  Future<void> _open(WhatsAppChat chat) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => WhatsAppChatScreen(
          http: widget.http,
          channelId: widget.channelId,
          chat: chat,
          account: _account ?? widget.account,
        ),
      ),
    );
    if (mounted) unawaited(_load(quiet: true));
  }

  Future<void> _chooseChats() async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => ReadAlongScreen(
          http: widget.http,
          channelId: widget.channelId,
          account: _account ?? widget.account,
          title: 'Choose chats',
        ),
      ),
    );
    if (mounted) unawaited(_load(quiet: true));
  }

  void _about() => unawaited(
    showModalBottomSheet<void>(
      context: context,
      showDragHandle: true,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(24, 8, 24, 28),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'Your chats, your choice',
                style: Theme.of(context).textTheme.titleLarge,
              ),
              const SizedBox(height: 12),
              const Text(
                'Jarvis only reads chats you turn on. In an enabled chat, use More → Load WhatsApp history to request older messages available on your phone. You can stop reading or clear saved messages at any time.',
              ),
              const SizedBox(height: 12),
              const Text(
                'Replies are sent from your account only when you tap Send or approve them. Unread counts belong to Jarvis and do not change WhatsApp read receipts.',
              ),
            ],
          ),
        ),
      ),
    ),
  );

  @override
  Widget build(BuildContext context) {
    final query = _search.text.trim().toLowerCase();
    bool matches(WhatsAppChat chat) =>
        query.isEmpty ||
        chat.name.toLowerCase().contains(query) ||
        chat.chatId.contains(query);
    final reading = _chats
        .where((chat) => chat.readAlong && matches(chat))
        .toList();
    final visible = reading
        .where(
          (chat) => switch (_filter) {
            'Unread' => chat.unreadCount > 0,
            'Groups' => chat.isGroup,
            _ => true,
          },
        )
        .toList();
    final people = _chats
        .where((chat) => !chat.readAlong && !chat.isGroup && matches(chat))
        .toList();
    final groups = _chats
        .where((chat) => !chat.readAlong && chat.isGroup && matches(chat))
        .toList();
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final account = _account ?? widget.account ?? 'WhatsApp';

    return Scaffold(
      appBar: AppBar(
        toolbarHeight: widget.selecting ? null : 68,
        title: widget.selecting
            ? Text(widget.title)
            : Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(widget.title),
                  const SizedBox(height: 3),
                  widget.accountPickerBuilder?.call(context, account, _state) ??
                      Text(
                        '$account · ${whatsAppConnectionLabel(_state)}',
                        key: const Key('whatsapp-account-status'),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: theme.textTheme.bodySmall,
                      ),
                ],
              ),
        actions: [
          if (!widget.selecting)
            HeaderAction(
              key: const Key('whatsapp-choose-chats'),
              label: 'Choose chats',
              icon: PhosphorIconsRegular.sliders,
              collapsesWhenNarrow: true,
              onPressed: () => unawaited(_chooseChats()),
            ),
          PopupMenuButton<String>(
            tooltip: 'WhatsApp options',
            icon: const Icon(PhosphorIconsRegular.dotsThree),
            onSelected: (value) {
              if (value == 'about') _about();
              if (value == 'account') widget.onManageAccount?.call();
              if (value == 'connect') widget.onConnect?.call();
            },
            itemBuilder: (_) => [
              if (widget.onManageAccount != null)
                const PopupMenuItem(
                  value: 'account',
                  child: Text('Account settings'),
                ),
              if (widget.onConnect != null)
                const PopupMenuItem(
                  value: 'connect',
                  child: Text('Connect another account'),
                ),
              const PopupMenuItem(
                value: 'about',
                child: Text('About read along'),
              ),
            ],
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: false,
        onRetry: () => unawaited(_load()),
        empty: const SizedBox.shrink(),
        child: RefreshIndicator(
          onRefresh: _load,
          child: ListView(
            key: const Key('read-along-list'),
            physics: const AlwaysScrollableScrollPhysics(),
            padding: EdgeInsets.fromLTRB(
              20,
              12,
              20,
              32 + MediaQuery.paddingOf(context).bottom,
            ),
            children: [
              if (widget.selecting)
                ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.only(bottom: 20),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          'A little help with your chats.',
                          style: JarvisType.displayOf(
                            context,
                          ).copyWith(fontSize: 30),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Choose the chats Jarvis may read. You stay in control of every reply.',
                          style: theme.textTheme.bodyMedium?.copyWith(
                            color: colors.inkSoft,
                          ),
                        ),
                        const SizedBox(height: 10),
                        Text(
                          '$account · ${whatsAppConnectionLabel(_state)}',
                          key: const Key('whatsapp-account-status'),
                          style: theme.textTheme.bodySmall,
                        ),
                      ],
                    ),
                  ),
                ),
              ContentWidth(
                child: TextField(
                  key: const Key('read-along-search'),
                  controller: _search,
                  onChanged: (_) => setState(() {}),
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Search chats',
                    filled: true,
                    fillColor: colors.surfaceMuted,
                    contentPadding: const EdgeInsets.symmetric(
                      horizontal: 16,
                      vertical: 12,
                    ),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(14),
                      borderSide: BorderSide.none,
                    ),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(14),
                      borderSide: BorderSide.none,
                    ),
                    prefixIcon: const Icon(
                      PhosphorIconsRegular.magnifyingGlass,
                      size: 18,
                    ),
                    suffixIcon: _search.text.isEmpty
                        ? null
                        : IconButton(
                            tooltip: 'Clear search',
                            onPressed: () => setState(_search.clear),
                            icon: const Icon(PhosphorIconsRegular.x, size: 16),
                          ),
                  ),
                ),
              ),
              if (!widget.selecting)
                ContentWidth(
                  child: CatchUpDigest(
                    margin: const EdgeInsets.only(top: 12),
                    entries: [
                      for (final chat in reading)
                        if (chat.catchUp != null)
                          CatchUpDigestEntry(
                            id: chat.chatId,
                            title: chat.name,
                            catchUp: chat.catchUp!,
                            onOpen: () => unawaited(_open(chat)),
                            leading: ChatAvatar(
                              chat: chat,
                              http: widget.http,
                              channelId: widget.channelId,
                              size: 36,
                            ),
                          ),
                    ],
                  ),
                ),
              if (!widget.selecting)
                ContentWidth(
                  child: Padding(
                    padding: const EdgeInsets.symmetric(vertical: 16),
                    child: Align(
                      alignment: Alignment.centerLeft,
                      child: Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          for (final filter in ['All', 'Unread', 'Groups'])
                            ChoiceChip(
                              key: Key('whatsapp-filter-$filter'),
                              label: Text(filter),
                              selected: _filter == filter,
                              onSelected: (_) =>
                                  setState(() => _filter = filter),
                            ),
                        ],
                      ),
                    ),
                  ),
                ),
              if (!_live || _state != 'open' || _refreshError != null)
                ContentWidth(
                  child: InlineNotice(
                    message: _refreshError ?? whatsAppConnectionMessage(_state),
                    margin: const EdgeInsets.only(top: 12, bottom: 16),
                  ),
                ),
              if (widget.selecting) ...[
                const SizedBox(height: 22),
                if (_chats.isEmpty)
                  ContentWidth(
                    child: EmptyState(
                      icon: PhosphorIconsRegular.chatsCircle,
                      title: _state == 'open'
                          ? 'No chats yet'
                          : 'No saved chats',
                      message: _state == 'open'
                          ? 'WhatsApp is syncing your chat list. New conversations also appear as messages arrive.'
                          : 'Reconnect this account to see its chats.',
                    ),
                  ),
                if (_chats.isNotEmpty &&
                    reading.isEmpty &&
                    people.isEmpty &&
                    groups.isEmpty)
                  const ContentWidth(
                    child: EmptyState(
                      icon: PhosphorIconsRegular.magnifyingGlass,
                      title: 'No matching chats',
                      message: 'Try another name or number.',
                    ),
                  ),
                if (reading.isNotEmpty)
                  _Section(
                    title: 'Reading along',
                    chats: reading,
                    saving: _saving,
                    onToggle: _setReadAlong,
                    onOpen: _open,
                    http: widget.http,
                    channelId: widget.channelId,
                  ),
                if (people.isNotEmpty)
                  _Section(
                    title: 'People',
                    chats: people,
                    saving: _saving,
                    onToggle: _setReadAlong,
                    http: widget.http,
                    channelId: widget.channelId,
                  ),
                if (groups.isNotEmpty)
                  _Section(
                    title: 'Groups',
                    chats: groups,
                    saving: _saving,
                    onToggle: _setReadAlong,
                    http: widget.http,
                    channelId: widget.channelId,
                  ),
              ] else ...[
                if (visible.isEmpty)
                  ContentWidth(
                    child: EmptyState(
                      icon: _filter == 'Unread'
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.chatsCircle,
                      title: query.isNotEmpty
                          ? 'No matching chats'
                          : _filter == 'Unread'
                          ? 'All caught up'
                          : _filter == 'Groups'
                          ? 'No groups selected'
                          : 'Bring a few chats along',
                      message: query.isNotEmpty
                          ? 'Try another name or number.'
                          : _filter == 'Unread'
                          ? 'No unread messages in your selected chats.'
                          : 'Choose conversations for reply drafts, questions and reminders.',
                      action: query.isNotEmpty || _filter == 'Unread'
                          ? null
                          : FilledButton.icon(
                              onPressed: () => unawaited(_chooseChats()),
                              icon: const Icon(
                                PhosphorIconsRegular.plus,
                                size: 18,
                              ),
                              label: const Text('Choose chats'),
                            ),
                    ),
                  ),
                for (final chat in visible)
                  ContentWidth(
                    child: SurfaceCard(
                      key: Key('read-along-chat-${chat.chatId}'),
                      margin: const EdgeInsets.only(bottom: 8),
                      padding: const EdgeInsets.fromLTRB(16, 16, 16, 16),
                      onTap: () => unawaited(_open(chat)),
                      child: _ConversationRow(
                        chat: chat,
                        http: widget.http,
                        channelId: widget.channelId,
                      ),
                    ),
                  ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _ConversationRow extends StatelessWidget {
  const _ConversationRow({
    required this.chat,
    required this.http,
    required this.channelId,
  });
  final WhatsAppChat chat;
  final Dio http;
  final String channelId;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final unread = chat.unreadCount > 0;
    final largeText =
        MediaQuery.sizeOf(context).width < 480 &&
        MediaQuery.textScalerOf(context).scale(14) > 18;
    final preview = chat.preview == null
        ? 'Waiting for new messages'
        : '${chat.previewFromMe == true ? 'You: ' : ''}${whatsAppPreviewText(chat.preview!)}';
    return Row(
      crossAxisAlignment: CrossAxisAlignment.center,
      children: [
        ChatAvatar(chat: chat, http: http, channelId: channelId, size: 46),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                chat.name,
                maxLines: largeText ? 2 : 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.titleMedium?.copyWith(
                  fontWeight: unread ? FontWeight.w600 : FontWeight.w500,
                ),
              ),
              const SizedBox(height: 4),
              Text(
                preview,
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: unread ? colors.inkSoft : colors.muted,
                  height: 1.45,
                ),
              ),
              if (largeText && chat.lastMessageAt != null) ...[
                const SizedBox(height: 6),
                Text(
                  whatsAppListTime(chat.lastMessageAt),
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: colors.muted,
                  ),
                ),
              ],
            ],
          ),
        ),
        const SizedBox(width: 14),
        Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            if (!largeText && chat.lastMessageAt != null)
              Text(
                whatsAppListTime(chat.lastMessageAt),
                style: theme.textTheme.labelSmall?.copyWith(
                  color: unread ? colors.inkSoft : colors.muted,
                ),
              ),
            if (unread) ...[
              const SizedBox(height: 8),
              Tooltip(
                message: 'Unread in Jarvis',
                child: Semantics(
                  label: '${chat.unreadCount} unread messages in Jarvis',
                  child: Container(
                    key: Key('whatsapp-unread-${chat.chatId}'),
                    constraints: const BoxConstraints(
                      minWidth: 22,
                      minHeight: 22,
                    ),
                    alignment: Alignment.center,
                    padding: const EdgeInsets.symmetric(horizontal: 6),
                    decoration: BoxDecoration(
                      color: colors.ink,
                      borderRadius: BorderRadius.circular(20),
                    ),
                    child: Text(
                      '${chat.unreadCount}',
                      style: theme.textTheme.labelSmall?.copyWith(
                        color: colors.onInk,
                      ),
                    ),
                  ),
                ),
              ),
            ],
          ],
        ),
      ],
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({
    required this.title,
    required this.chats,
    required this.saving,
    required this.onToggle,
    required this.http,
    required this.channelId,
    this.onOpen,
  });

  final String title;
  final List<WhatsAppChat> chats;
  final Set<String> saving;
  final Future<void> Function(WhatsAppChat chat, bool on) onToggle;
  final Future<void> Function(WhatsAppChat chat)? onOpen;
  final Dio http;
  final String channelId;

  @override
  Widget build(BuildContext context) => ContentWidth(
    child: Padding(
      padding: const EdgeInsets.only(bottom: 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SectionHeader(title),
          GroupedSection(
            dividerIndent: 70,
            children: [
              for (final chat in chats)
                _ChatRow(
                  chat: chat,
                  http: http,
                  channelId: channelId,
                  saving: saving.contains(chat.chatId),
                  onToggle: (on) => unawaited(onToggle(chat, on)),
                  onOpen: onOpen == null
                      ? null
                      : () => unawaited(onOpen!(chat)),
                ),
            ],
          ),
        ],
      ),
    ),
  );
}

class _ChatRow extends StatelessWidget {
  const _ChatRow({
    required this.chat,
    required this.http,
    required this.channelId,
    required this.saving,
    required this.onToggle,
    this.onOpen,
  });

  final WhatsAppChat chat;
  final Dio http;
  final String channelId;
  final bool saving;
  final ValueChanged<bool> onToggle;
  final VoidCallback? onOpen;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    final subtitle = [
      if (chat.isGroup) 'Group',
      ?chat.phone,
      if (chat.readAlong && chat.autoReminders && chat.preview == null)
        'Auto reminders',
    ].join(' · ');
    return InkWell(
      key: Key('read-along-chat-${chat.chatId}'),
      onTap: saving ? null : onOpen ?? () => onToggle(!chat.readAlong),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
        child: Row(
          children: [
            ChatAvatar(chat: chat, http: http, channelId: channelId),
            const SizedBox(width: 14),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    chat.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.titleSmall,
                  ),
                  if (subtitle.isNotEmpty)
                    Text(
                      subtitle,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colors.muted,
                      ),
                    ),
                  if (chat.preview != null && chat.readAlong)
                    Text(
                      '${chat.previewFromMe == true ? 'You: ' : ''}${whatsAppPreviewText(chat.preview!)}',
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodySmall?.copyWith(
                        color: colors.inkSoft,
                      ),
                    ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.only(left: 8),
              child: Column(
                mainAxisSize: MainAxisSize.min,
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  if (chat.lastMessageAt != null ||
                      (chat.readAlong && chat.unreadCount > 0))
                    Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (chat.lastMessageAt != null)
                          Text(
                            whatsAppListTime(chat.lastMessageAt),
                            style: theme.textTheme.labelSmall?.copyWith(
                              color: colors.muted,
                            ),
                          ),
                        if (chat.readAlong && chat.unreadCount > 0)
                          Padding(
                            padding: const EdgeInsets.only(left: 6),
                            child: Tooltip(
                              message: 'Unread in Jarvis',
                              child: Semantics(
                                label:
                                    '${chat.unreadCount} unread messages in Jarvis',
                                child: Container(
                                  key: Key('whatsapp-unread-${chat.chatId}'),
                                  padding: const EdgeInsets.symmetric(
                                    horizontal: 6,
                                    vertical: 2,
                                  ),
                                  decoration: BoxDecoration(
                                    color: colors.accentSoft,
                                    borderRadius: BorderRadius.circular(12),
                                  ),
                                  child: Text(
                                    '${chat.unreadCount}',
                                    style: theme.textTheme.labelSmall,
                                  ),
                                ),
                              ),
                            ),
                          ),
                      ],
                    ),
                  Semantics(
                    label: chat.readAlong
                        ? 'Stop reading ${chat.name}'
                        : 'Read along with ${chat.name}',
                    child: Switch.adaptive(
                      key: Key('read-along-switch-${chat.chatId}'),
                      value: chat.readAlong,
                      onChanged: saving ? null : onToggle,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// Round picture, or initials when WhatsApp has no picture. Groups without a
/// picture get a people glyph.
class ChatAvatar extends StatefulWidget {
  const ChatAvatar({
    this.chat,
    this.http,
    this.channelId,
    this.subject,
    this.initials,
    this.group = false,
    this.size = 42,
    super.key,
  });

  final WhatsAppChat? chat;
  final Dio? http;
  final String? channelId;

  /// Chat id or participant id whose picture to load. Defaults to [chat].
  final String? subject;
  final String? initials;
  final bool group;
  final double size;

  @override
  State<ChatAvatar> createState() => _ChatAvatarState();
}

class _ChatAvatarState extends State<ChatAvatar> {
  Uint8List? _image;
  String? _requested;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void didUpdateWidget(ChatAvatar oldWidget) {
    super.didUpdateWidget(oldWidget);
    final subjectChanged = _subject != _requested;
    final sourceChanged =
        widget.channelId != oldWidget.channelId ||
        widget.http != oldWidget.http;
    if (!subjectChanged && !sourceChanged) return;
    // Drop a picture from the previous account before asking for the new one.
    if (sourceChanged) _image = null;
    _load();
  }

  String? get _subject => widget.subject ?? widget.chat?.chatId;

  void _load() {
    final http = widget.http;
    final channelId = widget.channelId;
    final subject = _subject;
    // A reused row must not keep the previous person's face while the next
    // picture is still loading, or when that chat has no picture.
    if (subject != _requested) _image = null;
    _requested = subject;
    if (http == null ||
        channelId == null ||
        subject == null ||
        subject.isEmpty) {
      return;
    }
    final known = WhatsAppPictures.cached(channelId, subject);
    if (known != null) _image = known;
    WhatsAppPictures.load(http, channelId, subject).then((bytes) {
      if (!mounted || bytes == null || _requested != subject) return;
      if (identical(_image, bytes)) return;
      setState(() => _image = bytes);
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final color = colors.inkSoft;
    final group = widget.chat?.isGroup ?? widget.group;
    final letters = widget.initials ?? widget.chat?.initials ?? '#';
    final image = _image;
    return Container(
      width: widget.size,
      height: widget.size,
      alignment: Alignment.center,
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: colors.surfaceRaised,
      ),
      child: image != null
          ? Image.memory(
              image,
              key: ValueKey('whatsapp-picture-$_subject'),
              width: widget.size,
              height: widget.size,
              fit: BoxFit.cover,
              gaplessPlayback: true,
              errorBuilder: (_, _, _) => _fallback(group, letters, color),
            )
          : _fallback(group, letters, color),
    );
  }

  Widget _fallback(bool group, String letters, Color color) => group
      ? Icon(PhosphorIconsRegular.users, size: widget.size * .45, color: color)
      : Text(
          letters,
          style: TextStyle(
            color: color,
            fontWeight: FontWeight.w700,
            fontSize: widget.size * .36,
          ),
        );
}

/// Session cache so a group does not download the same face for every message.
class WhatsAppPictures {
  static final _memory = <String, Uint8List?>{};
  static final _loading = <String, Future<Uint8List?>>{};

  static void clear() {
    _memory.clear();
    _loading.clear();
  }

  /// Picture already fetched this session, or null when it is unknown or absent.
  static Uint8List? cached(String channelId, String subject) =>
      _memory['$channelId\n$subject'];

  static Future<Uint8List?> load(Dio http, String channelId, String subject) {
    final key = '$channelId\n$subject';
    if (_memory.containsKey(key)) return Future.value(_memory[key]);
    return _loading[key] ??= _fetch(http, channelId, subject).then(
      (bytes) {
        _memory[key] = bytes;
        _loading.remove(key);
        return bytes;
      },
      onError: (_) {
        _loading.remove(key);
        return null;
      },
    );
  }

  static Future<Uint8List?> _fetch(
    Dio http,
    String channelId,
    String subject,
  ) async {
    try {
      final response = await http.get<List<int>>(
        whatsAppChatPath(channelId, action: 'picture'),
        queryParameters: {'subject': whatsAppChatToken(subject)},
        options: Options(
          responseType: ResponseType.bytes,
          receiveTimeout: const Duration(seconds: 15),
        ),
      );
      final data = response.data;
      if (data == null || data.isEmpty) return null;
      return Uint8List.fromList(data);
    } on DioException catch (error) {
      if (error.response?.statusCode == 404) return null;
      rethrow;
    }
  }
}
