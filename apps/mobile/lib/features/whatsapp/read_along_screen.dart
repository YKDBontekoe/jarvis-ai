import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'whatsapp_chat_screen.dart';
import 'whatsapp_models.dart';

/// Pick which chats on the owner's own WhatsApp Jarvis may read along with.
/// Everything starts off, groups included; chats that are on open a chat view
/// with reply drafts, "Ask Jarvis" and automatic reminders.
class ReadAlongScreen extends StatefulWidget {
  const ReadAlongScreen({
    required this.http,
    required this.channelId,
    super.key,
  });

  final Dio http;
  final String channelId;

  @override
  State<ReadAlongScreen> createState() => _ReadAlongScreenState();
}

class _ReadAlongScreenState extends State<ReadAlongScreen> {
  final _search = TextEditingController();
  List<WhatsAppChat> _chats = const [];
  bool _live = true;
  bool _loading = true;
  String? _error;
  final Set<String> _saving = {};
  int _requestRevision = 0;
  Timer? _refresh;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
    // The phone keeps adding chats while history syncs right after linking.
    _refresh = Timer.periodic(const Duration(seconds: 20), (_) {
      if (mounted && _saving.isEmpty) unawaited(_load(quiet: true));
    });
  }

  @override
  void dispose() {
    _refresh?.cancel();
    _search.dispose();
    super.dispose();
  }

  Future<void> _load({bool quiet = false}) async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/channels/${widget.channelId}/chats',
      );
      if (!mounted || revision != _requestRevision) return;
      final body = jsonObject(response.data);
      setState(() {
        _chats = [
          for (final item in jsonMaps(body?['chats']))
            ?WhatsAppChat.fromJson(item),
        ];
        _live = asJsonBool(body?['live'], true);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision || quiet) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your WhatsApp chats.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision || quiet) return;
      setState(() {
        _loading = false;
        _error = 'Could not load your WhatsApp chats.';
      });
    }
  }

  Future<void> _setReadAlong(WhatsAppChat chat, bool on) async {
    if (_saving.contains(chat.chatId)) return;
    final index = _chats.indexWhere((x) => x.chatId == chat.chatId);
    if (index < 0) return;
    setState(() {
      _saving.add(chat.chatId);
      _chats = [..._chats]..[index] = chat.copyWith(readAlong: on);
    });
    try {
      await widget.http.put<dynamic>(
        whatsAppChatPath(widget.channelId, chat.chatId),
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
        ),
      ),
    );
    if (mounted) unawaited(_load(quiet: true));
  }

  @override
  Widget build(BuildContext context) {
    final query = _search.text.trim().toLowerCase();
    bool matches(WhatsAppChat chat) =>
        query.isEmpty ||
        chat.name.toLowerCase().contains(query) ||
        chat.chatId.contains(query);
    final on = _chats.where((x) => x.readAlong && matches(x)).toList();
    final people = _chats
        .where((x) => !x.readAlong && !x.isGroup && matches(x))
        .toList();
    final groups = _chats
        .where((x) => !x.readAlong && x.isGroup && matches(x))
        .toList();
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);

    return Scaffold(
      appBar: AppBar(title: const Text('Read along')),
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
            padding: EdgeInsets.fromLTRB(
              16,
              4,
              16,
              32 + MediaQuery.paddingOf(context).bottom,
            ),
            children: [
              ContentWidth(
                child: SurfaceCard(
                  padding: const EdgeInsets.all(16),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      IconBadge(
                        icon: PhosphorIconsRegular.whatsappLogo,
                        color: const Color(0xff16a34a),
                        size: 40,
                      ),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              'Your WhatsApp, with Jarvis',
                              style: theme.textTheme.titleSmall,
                            ),
                            const SizedBox(height: 4),
                            Text(
                              'Turn on the chats Jarvis may read. It drafts '
                              'replies, answers questions about them and sets '
                              'reminders for plans you make. Nothing is sent '
                              'unless you tap Send or approve it.',
                              style: theme.textTheme.bodySmall?.copyWith(
                                color: colors.inkSoft,
                                height: 1.4,
                              ),
                            ),
                          ],
                        ),
                      ),
                    ],
                  ),
                ),
              ),
              const SizedBox(height: 14),
              ContentWidth(
                child: TextField(
                  key: const Key('read-along-search'),
                  controller: _search,
                  onChanged: (_) => setState(() {}),
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Search chats',
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
              if (!_live)
                const ContentWidth(
                  child: InlineNotice(
                    message:
                        'Your phone did not answer, so only chats you already '
                        'set up are shown. Keep WhatsApp linked and pull to '
                        'refresh.',
                    margin: EdgeInsets.only(top: 12),
                  ),
                ),
              const SizedBox(height: 18),
              if (_chats.isEmpty)
                const ContentWidth(
                  child: EmptyState(
                    icon: PhosphorIconsRegular.chatsCircle,
                    title: 'No chats yet',
                    message:
                        'Right after linking, WhatsApp sends your chat list in '
                        'the background. New chats also appear as soon as a '
                        'message comes in.',
                  ),
                )
              else if (on.isEmpty && people.isEmpty && groups.isEmpty)
                const ContentWidth(
                  child: Padding(
                    padding: EdgeInsets.symmetric(vertical: 24),
                    child: Text('No chats match your search.'),
                  ),
                ),
              if (on.isNotEmpty)
                _Section(
                  title: 'Reading along',
                  chats: on,
                  saving: _saving,
                  onToggle: _setReadAlong,
                  onOpen: _open,
                ),
              if (people.isNotEmpty)
                _Section(
                  title: 'People',
                  chats: people,
                  saving: _saving,
                  onToggle: _setReadAlong,
                ),
              if (groups.isNotEmpty)
                _Section(
                  title: 'Groups',
                  chats: groups,
                  saving: _saving,
                  onToggle: _setReadAlong,
                ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Section extends StatelessWidget {
  const _Section({
    required this.title,
    required this.chats,
    required this.saving,
    required this.onToggle,
    this.onOpen,
  });

  final String title;
  final List<WhatsAppChat> chats;
  final Set<String> saving;
  final Future<void> Function(WhatsAppChat chat, bool on) onToggle;
  final Future<void> Function(WhatsAppChat chat)? onOpen;

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
              for (final chat in chats.take(150))
                _ChatRow(
                  chat: chat,
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
    required this.saving,
    required this.onToggle,
    this.onOpen,
  });

  final WhatsAppChat chat;
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
      if (chat.readAlong && chat.autoReminders) 'Auto reminders',
    ].join(' · ');
    return InkWell(
      key: Key('read-along-chat-${chat.chatId}'),
      onTap: onOpen ?? () => onToggle(!chat.readAlong),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 10, 8, 10),
        child: Row(
          children: [
            ChatAvatar(chat: chat),
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
                ],
              ),
            ),
            if (chat.lastMessageAt != null)
              Padding(
                padding: const EdgeInsets.only(left: 8),
                child: Text(
                  whatsAppListTime(chat.lastMessageAt),
                  style: theme.textTheme.labelSmall?.copyWith(
                    color: colors.muted,
                  ),
                ),
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
    );
  }
}

/// Round initials avatar; groups get a people glyph.
class ChatAvatar extends StatelessWidget {
  const ChatAvatar({required this.chat, this.size = 42, super.key});

  final WhatsAppChat chat;
  final double size;

  static const _palette = [
    Color(0xff16a34a),
    Color(0xff0ea5e9),
    Color(0xff8b5cf6),
    Color(0xfff97316),
    Color(0xffe11d48),
    Color(0xff0d9488),
  ];

  @override
  Widget build(BuildContext context) {
    final color = _palette[chat.chatId.hashCode.abs() % _palette.length];
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: color.withValues(alpha: .14),
      ),
      child: chat.isGroup
          ? Icon(PhosphorIconsRegular.users, size: size * .45, color: color)
          : Text(
              chat.initials,
              style: TextStyle(
                color: color,
                fontWeight: FontWeight.w700,
                fontSize: size * .36,
              ),
            ),
    );
  }
}
