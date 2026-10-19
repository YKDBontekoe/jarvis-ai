import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/chat_entries.dart';
import '../chat/chat_widgets.dart';

/// A search hit in its conversation: the matched message with the messages around it, highlighted for a moment, and
/// a way to keep reading in either direction or to continue the chat itself. Read-only, so opening an old chat at a hit
/// never disturbs the composer or a reply that is streaming.
class MessageContextScreen extends StatefulWidget {
  const MessageContextScreen({
    required this.http,
    required this.conversationId,
    required this.messageId,
    required this.onOpenConversation,
    this.title,
    super.key,
  });

  final Dio http;
  final String conversationId;
  final String messageId;
  final Future<void> Function(String conversationId) onOpenConversation;
  final String? title;

  @override
  State<MessageContextScreen> createState() => _MessageContextScreenState();
}

class _MessageContextScreenState extends State<MessageContextScreen> {
  static const _pageSize = 40;

  final _scroll = ScrollController();
  final _anchorKey = GlobalKey();
  List<Map<String, dynamic>> _messages = const [];
  String? _olderCursor;
  String? _newerCursor;
  bool _hasOlder = false;
  bool _hasNewer = false;
  bool _loading = true;
  bool _missing = false;
  bool _loadingMore = false;
  bool _highlight = true;
  String? _error;
  Timer? _fade;

  String get _base => '/api/v1/conversations/${widget.conversationId}/messages';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _fade?.cancel();
    _scroll.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>(
        _base,
        queryParameters: {'around': widget.messageId, 'limit': _pageSize},
      );
      if (!mounted) return;
      final page = jsonObject(response.data);
      setState(() {
        _messages = _readable(page?['items']);
        _olderCursor = asJsonString(page?['nextCursor']);
        _hasOlder = asJsonBool(page?['hasMore']);
        _newerCursor = asJsonString(page?['newerCursor']);
        _hasNewer = asJsonBool(page?['hasNewer']);
        _loading = false;
        _highlight = true;
      });
      WidgetsBinding.instance.addPostFrameCallback((_) {
        final anchor = _anchorKey.currentContext;
        if (anchor != null && mounted) {
          unawaited(
            Scrollable.ensureVisible(
              anchor,
              alignment: .3,
              duration: JarvisMotion.of(context, JarvisMotion.base),
              curve: Curves.easeOutCubic,
            ),
          );
        }
      });
      _fade?.cancel();
      _fade = Timer(const Duration(milliseconds: 2800), () {
        if (mounted) setState(() => _highlight = false);
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _missing = error.response?.statusCode == 404;
        _error = describeApiError(error);
      });
    } catch (_) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Jarvis could not load this conversation.';
        });
      }
    }
  }

  /// Only what was said: tool traffic stays out of the reading view.
  static List<Map<String, dynamic>> _readable(dynamic items) => [
    for (final item in jsonMaps(items))
      if ((item['role'] == 'user' || item['role'] == 'assistant') &&
          item['content'] is String)
        item,
  ];

  Future<void> _loadMore({required bool older}) async {
    final cursor = older ? _olderCursor : _newerCursor;
    if (cursor == null || _loadingMore) return;
    setState(() => _loadingMore = true);
    try {
      final response = await widget.http.get<dynamic>(
        _base,
        queryParameters: {
          'limit': _pageSize,
          if (older) 'cursor': cursor else 'after': cursor,
        },
      );
      if (!mounted) return;
      final page = jsonObject(response.data);
      final more = _readable(page?['items']);
      final before = _scroll.hasClients ? _scroll.position.maxScrollExtent : 0.0;
      final have = {for (final item in _messages) item['id']};
      setState(() {
        final fresh = [
          for (final item in more)
            if (!have.contains(item['id'])) item,
        ];
        _messages = older ? [...fresh, ..._messages] : [..._messages, ...fresh];
        if (older) {
          _olderCursor = asJsonString(page?['nextCursor']);
          _hasOlder = asJsonBool(page?['hasMore']);
        } else {
          _newerCursor = asJsonString(page?['newerCursor']);
          _hasNewer = asJsonBool(page?['hasNewer']);
        }
      });
      if (older) {
        // Keep what the person was reading in place when earlier messages appear above it.
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!_scroll.hasClients) return;
          final added = _scroll.position.maxScrollExtent - before;
          _scroll.jumpTo(
            (_scroll.position.pixels + added).clamp(
              0.0,
              _scroll.position.maxScrollExtent,
            ),
          );
        });
      }
    } on DioException catch (error) {
      if (mounted) setState(() => _error = describeApiError(error));
    } finally {
      if (mounted) setState(() => _loadingMore = false);
    }
  }

  Future<void> _openChat() async {
    await widget.onOpenConversation(widget.conversationId);
    if (mounted && Navigator.of(context).canPop()) Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(
          widget.title ?? 'Conversation',
          overflow: TextOverflow.ellipsis,
        ),
        actions: [
          if (!_missing)
            TextButton.icon(
              key: const Key('message-context-open-chat'),
              onPressed: _loading ? null : () => unawaited(_openChat()),
              icon: const Icon(PhosphorIconsRegular.chatCircle, size: 18),
              label: const Text('Open chat'),
            ),
          const SizedBox(width: 8),
        ],
      ),
      body: _body(colors),
    );
  }

  Widget _body(JarvisColors colors) {
    if (_loading) return const SkeletonList(shape: SkeletonShape.detail);
    if (_missing) {
      return const EmptyState(
        icon: PhosphorIconsRegular.chatCircle,
        title: 'That message is gone',
        message: 'It was deleted, or its chat was. Search again to find where it moved.',
      );
    }
    if (_messages.isEmpty) {
      return ErrorState(
        message: _error ?? 'Jarvis could not load this conversation.',
        onRetry: _load,
      );
    }
    return SingleChildScrollView(
      controller: _scroll,
      padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
      child: ContentWidth(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (_hasOlder)
              _MoreButton(
                key: const Key('message-context-older'),
                label: 'Earlier messages',
                busy: _loadingMore,
                onTap: () => unawaited(_loadMore(older: true)),
              ),
            for (final message in _messages)
              _ContextMessage(
                key: message['id'] == widget.messageId ? _anchorKey : null,
                message: message,
                highlighted: _highlight && message['id'] == widget.messageId,
                isHit: message['id'] == widget.messageId,
              ),
            if (_hasNewer)
              _MoreButton(
                key: const Key('message-context-newer'),
                label: 'Later messages',
                busy: _loadingMore,
                onTap: () => unawaited(_loadMore(older: false)),
              )
            else
              Padding(
                padding: const EdgeInsets.only(top: 16),
                child: Center(
                  child: Text(
                    'End of the conversation',
                    style: TextStyle(fontSize: 13, color: colors.muted),
                  ),
                ),
              ),
            if (_error != null)
              Padding(
                padding: const EdgeInsets.only(top: 12),
                child: InlineNotice(message: _error!, tone: NoticeTone.danger),
              ),
          ],
        ),
      ),
    );
  }
}

class _ContextMessage extends StatelessWidget {
  const _ContextMessage({
    required this.message,
    required this.highlighted,
    required this.isHit,
    super.key,
  });

  final Map<String, dynamic> message;
  final bool highlighted;
  final bool isHit;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final role = asJsonString(message['role']) ?? 'assistant';
    return Semantics(
      container: true,
      label: isHit ? 'Search match' : null,
      child: AnimatedContainer(
        key: isHit ? const Key('message-context-hit') : null,
        duration: JarvisMotion.of(context, JarvisMotion.base),
        curve: Curves.easeOut,
        margin: const EdgeInsets.symmetric(vertical: 4),
        decoration: BoxDecoration(
          color: highlighted ? colors.accentSoft : Colors.transparent,
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          border: Border.all(
            color: highlighted ? colors.accent : Colors.transparent,
          ),
        ),
        child: MessageBubble(
          message: MessageEntry(
            role: role,
            content: asJsonString(message['content']) ?? '',
            id: asJsonString(message['id']),
          ),
        ),
      ),
    );
  }
}

class _MoreButton extends StatelessWidget {
  const _MoreButton({
    required this.label,
    required this.busy,
    required this.onTap,
    super.key,
  });

  final String label;
  final bool busy;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 8),
    child: Center(
      child: OutlinedButton(
        onPressed: busy ? null : onTap,
        child: Text(busy ? 'Loading…' : label),
      ),
    ),
  );
}
