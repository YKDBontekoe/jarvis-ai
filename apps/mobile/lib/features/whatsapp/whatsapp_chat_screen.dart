import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'catch_up_card.dart';
import 'read_along_screen.dart';
import 'whatsapp_media.dart';
import 'whatsapp_models.dart';
import 'whatsapp_visuals.dart';

/// One read-along chat: the conversation as Jarvis sees it, a reply drafted in
/// the owner's voice, "Ask Jarvis" about the chat, and sending by tapping Send.
class WhatsAppChatScreen extends StatefulWidget {
  const WhatsAppChatScreen({
    required this.http,
    required this.channelId,
    required this.chat,
    this.account,
    this.pollInterval = const Duration(seconds: 3),
    super.key,
  });

  final Dio http;
  final String channelId;
  final WhatsAppChat chat;
  final String? account;
  final Duration pollInterval;

  @override
  State<WhatsAppChatScreen> createState() => _WhatsAppChatScreenState();
}

class _WhatsAppChatScreenState extends State<WhatsAppChatScreen> {
  final _composer = TextEditingController();
  late WhatsAppChat _chat = widget.chat;
  List<WhatsAppMessage> _messages = const [];
  bool _loading = true;
  bool _sending = false;
  bool _drafting = false;
  String? _error;
  Timer? _poll;
  int _requestRevision = 0;
  final _scroll = ScrollController();
  final _composerFocus = FocusNode();

  /// The message the next send replies to, quoted above the composer.
  WhatsAppMessage? _replyTo;
  bool _fetching = false;
  bool _loadingOlder = false;
  bool _hasOlder = false;
  bool _checkingStatus = false;
  String? _connectionState;
  String? _account;
  DateTime? _lastStatusCheck;
  String? _refreshError;
  String? _markedMessage;
  bool _requestingHistory = false;

  /// Kept from when the chat opened: reading the chat moves the unread mark, so
  /// the next list refresh would otherwise drop the card while you read it.
  WhatsAppCatchUp? _catchUp;

  String _path([String? action]) =>
      whatsAppChatPath(widget.channelId, action: action);

  Map<String, dynamic> _query([Map<String, dynamic>? extra]) =>
      whatsAppChatQuery(_chat.chatId, extra);

  @override
  void initState() {
    super.initState();
    _catchUp = widget.chat.catchUp;
    unawaited(_load());
    unawaited(_loadStatus());
    _poll = Timer.periodic(widget.pollInterval, (_) {
      if (mounted && ModalRoute.of(context)?.isCurrent != false) {
        unawaited(_load(quiet: true));
        if (_lastStatusCheck == null ||
            DateTime.now().difference(_lastStatusCheck!) >
                const Duration(seconds: 15)) {
          unawaited(_loadStatus());
        }
      }
    });
    _composer.addListener(() => setState(() {}));
  }

  @override
  void didUpdateWidget(WhatsAppChatScreen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.channelId == widget.channelId &&
        oldWidget.chat.chatId == widget.chat.chatId) {
      return;
    }
    // The wide layout reuses this screen when another chat is opened. Keep the
    // new group's id, or the list keeps showing the previous conversation.
    _chat = widget.chat;
    _catchUp = widget.chat.catchUp;
    _messages = const [];
    _loading = true;
    _error = null;
    _refreshError = null;
    _markedMessage = null;
    _hasOlder = false;
    _requestRevision++;
    _fetching = false;
    unawaited(_load());
  }

  @override
  void dispose() {
    _poll?.cancel();
    _composer.dispose();
    _scroll.dispose();
    _composerFocus.dispose();
    super.dispose();
  }

  Future<void> _load({bool quiet = false}) async {
    if (_fetching || _loadingOlder) return;
    _fetching = true;
    final revision = ++_requestRevision;
    try {
      final messages = await _fetchPage();
      if (!mounted || revision != _requestRevision) return;
      // Catch up across a long disconnect rather than leaving a gap between saved and recent pages.
      final known = _messages.map((m) => m.id).toSet();
      var page = messages.toList();
      while (known.isNotEmpty &&
          page.length == 60 &&
          !page.any((m) => known.contains(m.id))) {
        page = await _fetchPage(beforeId: page.last.id);
        if (!mounted || revision != _requestRevision) return;
        final seen = messages.map((m) => m.id).toSet();
        if (page.isNotEmpty && page.every((m) => seen.contains(m.id))) {
          throw const FormatException('Message cursor did not advance');
        }
        messages.addAll(page);
      }
      final firstLoad = _loading || _messages.isEmpty;
      final anchor = _scroll.hasClients && _scroll.offset > 80;
      final extent = _scroll.hasClients
          ? _scroll.position.maxScrollExtent
          : 0.0;
      setState(() {
        _messages = _merge(messages);
        if (firstLoad || messages.length == 60) {
          _hasOlder = messages.length == 60;
        }
        _loading = false;
        _error = null;
        _refreshError = null;
      });
      if (anchor) {
        WidgetsBinding.instance.addPostFrameCallback((_) {
          if (!mounted || !_scroll.hasClients) return;
          final delta = _scroll.position.maxScrollExtent - extent;
          if (delta > 0) {
            _scroll.jumpTo(
              (_scroll.offset + delta).clamp(
                0.0,
                _scroll.position.maxScrollExtent,
              ),
            );
          }
        });
      }
      unawaited(_markRead());
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      if (quiet) {
        setState(
          () => _refreshError =
              'Could not refresh messages. Showing saved messages; retrying automatically.',
        );
        return;
      }
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this chat.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      if (quiet) {
        setState(
          () => _refreshError =
              'Could not refresh messages. Retrying automatically.',
        );
        return;
      }
      setState(() {
        _loading = false;
        _error = 'Could not load this chat.';
      });
    } finally {
      if (revision == _requestRevision) _fetching = false;
    }
  }

  List<WhatsAppMessage> _merge(List<WhatsAppMessage> incoming) =>
      {
        ...{for (final message in _messages) message.id: message},
        ...{for (final message in incoming) message.id: message},
      }.values.toList()..sort((a, b) {
        final time = a.sentAt.compareTo(b.sentAt);
        return time == 0 ? a.id.compareTo(b.id) : time;
      });

  Future<List<WhatsAppMessage>> _fetchPage({String? beforeId}) async {
    final response = await widget.http.get<dynamic>(
      _path('messages'),
      queryParameters: _query(beforeId == null ? null : {'beforeId': beforeId}),
    );
    if (response.data is! List) {
      throw const FormatException('Invalid message list');
    }
    return [
      for (final item in jsonMaps(response.data))
        ?WhatsAppMessage.fromJson(item),
    ]..sort((a, b) {
      final time = b.sentAt.compareTo(a.sentAt);
      return time == 0 ? b.id.compareTo(a.id) : time;
    });
  }

  Future<void> _loadOlder() async {
    if (_loadingOlder || _fetching || _messages.isEmpty) return;
    setState(() => _loadingOlder = true);
    final revision = _requestRevision;
    try {
      final older = await _fetchPage(beforeId: _messages.first.id);
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _messages = _merge(older);
        _hasOlder = older.length == 60;
      });
      if (older.isEmpty && _chat.readAlong) unawaited(_requestHistory());
    } catch (_) {
      if (mounted) _snack('Could not load older messages. Try again.');
    } finally {
      if (mounted) setState(() => _loadingOlder = false);
    }
  }

  Future<void> _requestHistory() async {
    if (_requestingHistory || !_chat.readAlong) return;
    setState(() => _requestingHistory = true);
    final targetChat = _chat.chatId;
    final targetChannel = widget.channelId;
    try {
      await widget.http.post<dynamic>(
        _path('history'),
        queryParameters: _query(
          _messages.isEmpty ? null : {'beforeId': _messages.first.id},
        ),
      );
      if (!mounted ||
          _chat.chatId != targetChat ||
          widget.channelId != targetChannel) {
        return;
      }
      _snack(
        'History requested. Keep WhatsApp connected on your phone; messages appear as it sends them.',
      );
      unawaited(_load(quiet: true));
    } on DioException catch (error) {
      if (mounted &&
          _chat.chatId == targetChat &&
          widget.channelId == targetChannel) {
        _snack(
          firstProblemMessage(error.response?.data) ??
              'Could not request WhatsApp history. Try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _requestingHistory = false);
    }
  }

  Future<void> _startReading() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Read along with ${_chat.name}?',
      message:
          'Jarvis will save new messages from this chat. You can also request available history from your phone.',
      confirmLabel: 'Start reading',
    );
    if (confirmed == true && mounted) await _save(readAlong: true);
  }

  Future<void> _markRead() async {
    if (_messages.isEmpty || ModalRoute.of(context)?.isCurrent == false) return;
    final newestReceived = _messages.reduce(
      (a, b) =>
          (a.receivedAt ?? a.sentAt).isAfter(b.receivedAt ?? b.sentAt) ? a : b,
    );
    if (_markedMessage == newestReceived.id) return;
    try {
      await widget.http.post<dynamic>(
        _path('read'),
        queryParameters: _query(),
        data: {'messageId': newestReceived.id},
      );
      _markedMessage = newestReceived.id;
    } catch (_) {
      // Keep the badge unread and retry on the next successful message refresh.
    }
  }

  Future<void> _loadStatus() async {
    if (_checkingStatus) return;
    _checkingStatus = true;
    _lastStatusCheck = DateTime.now();
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/channels/${widget.channelId}/chats/status',
      );
      if (!mounted) return;
      final body = jsonObject(response.data);
      setState(() {
        _connectionState = asJsonString(body?['state']);
        _account = asJsonString(body?['account']) ?? widget.account;
      });
    } on DioException catch (error) {
      if (mounted && error.response?.statusCode != 404) {
        setState(() => _connectionState = 'unreachable');
      }
    } catch (_) {
      if (mounted) setState(() => _connectionState = 'unreachable');
    } finally {
      _checkingStatus = false;
    }
  }

  Future<void> _send() async {
    final text = _composer.text.trim();
    if (text.isEmpty || _sending) return;
    setState(() => _sending = true);
    try {
      await widget.http.post<dynamic>(
        _path('send'),
        queryParameters: _query(),
        data: {'text': text, 'replyTo': ?_replyTo?.id},
      );
      if (!mounted) return;
      _composer.clear();
      setState(() => _replyTo = null);
      unawaited(_load(quiet: true));
    } on DioException catch (error) {
      if (!mounted) return;
      _snack(
        firstProblemMessage(error.response?.data) ??
            asJsonString(jsonObject(error.response?.data)?['message']) ??
            'The message was not sent.',
      );
    } catch (_) {
      if (mounted) _snack('The message was not sent.');
    } finally {
      if (mounted) setState(() => _sending = false);
    }
  }

  void _startReply(WhatsAppMessage message) {
    unawaited(HapticFeedback.lightImpact());
    setState(() => _replyTo = message);
    _composerFocus.requestFocus();
  }

  /// Holding a bubble: react, reply or copy.
  Future<void> _messageActions(WhatsAppMessage message, Rect anchor) async {
    final action = await showMessageActions(
      context,
      anchor: anchor,
      mine: message.fromMe,
      canReply: _canSend,
    );
    if (!mounted || action == null) return;
    switch (action) {
      case ReactWith(:final emoji):
        await _react(message, emoji, anchor);
      case ReplyTo():
        _startReply(message);
      case CopyText():
        await Clipboard.setData(ClipboardData(text: message.text));
        if (mounted) _snack('Message copied');
    }
  }

  Future<void> _react(
    WhatsAppMessage message,
    String emoji,
    Rect anchor,
  ) async {
    // The emoji lifts off at once; a failure is reported after.
    floatEmoji(context, anchor, emoji);
    unawaited(HapticFeedback.lightImpact());
    try {
      await widget.http.post<dynamic>(
        _path('react'),
        queryParameters: _query(),
        data: {'messageId': message.id, 'emoji': emoji},
      );
    } on DioException catch (error) {
      if (!mounted) return;
      _snack(
        firstProblemMessage(error.response?.data) ??
            asJsonString(jsonObject(error.response?.data)?['message']) ??
            'The reaction was not sent.',
      );
    } catch (_) {
      if (mounted) _snack('The reaction was not sent.');
    }
  }

  bool get _canSend =>
      _chat.readAlong &&
      (_connectionState == null || _connectionState == 'open');

  Future<void> _draft({String? preset}) async {
    if (_drafting) return;
    final instruction =
        preset ??
        await showModalBottomSheet<String>(
          context: context,
          isScrollControlled: true,
          showDragHandle: true,
          builder: (_) => _DraftSheet(name: _chat.name),
        );
    if (instruction == null || !mounted) return;
    setState(() => _drafting = true);
    try {
      final response = await widget.http.post<dynamic>(
        _path('suggest'),
        queryParameters: _query(),
        data: {'instruction': instruction},
        options: Options(receiveTimeout: const Duration(seconds: 75)),
      );
      final text = asJsonString(jsonObject(response.data)?['text']);
      if (!mounted || text == null) return;
      _composer.value = TextEditingValue(
        text: text,
        selection: TextSelection.collapsed(offset: text.length),
      );
    } on DioException catch (error) {
      if (mounted) {
        _snack(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not write a reply right now.',
        );
      }
    } catch (_) {
      if (mounted) _snack('Jarvis could not write a reply right now.');
    } finally {
      if (mounted) setState(() => _drafting = false);
    }
  }

  Future<void> _ask() async {
    final reply = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) =>
          _AskSheet(http: widget.http, path: _path('ask'), chat: _chat),
    );
    if (reply == null || !mounted) return;
    _composer.value = TextEditingValue(
      text: reply,
      selection: TextSelection.collapsed(offset: reply.length),
    );
  }

  Future<void> _save({bool? readAlong, bool? autoReminders}) async {
    final previous = _chat;
    setState(
      () => _chat = _chat.copyWith(
        readAlong: readAlong,
        autoReminders: autoReminders,
      ),
    );
    try {
      await widget.http.put<dynamic>(
        _path(),
        queryParameters: _query(),
        data: {
          'name': _chat.name,
          'readAlong': _chat.readAlong,
          'autoReminders': _chat.autoReminders,
        },
      );
      if (!mounted) return;
      if (readAlong == false) {
        Navigator.of(context).pop();
      } else if (readAlong == true) {
        unawaited(_load());
      } else if (autoReminders != null) {
        _snack(
          autoReminders
              ? 'Jarvis sets reminders for plans in this chat.'
              : 'No more automatic reminders from this chat.',
        );
      }
    } catch (_) {
      if (!mounted) return;
      setState(() => _chat = previous);
      _snack('Could not save that change.');
    }
  }

  Future<void> _clearHistory() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Clear what Jarvis saved?',
      message:
          'Jarvis forgets the messages it saved from ${_chat.name}. Your '
          'WhatsApp itself is not changed.',
      confirmLabel: 'Clear',
      destructive: true,
    );
    if (confirmed != true || !mounted) return;
    try {
      ++_requestRevision;
      await widget.http.delete<void>(
        _path('messages'),
        queryParameters: _query(),
      );
      ++_requestRevision;
      if (mounted) {
        setState(() {
          _messages = const [];
          _hasOlder = false;
          _markedMessage = null;
        });
      }
    } catch (_) {
      if (mounted) _snack('Could not clear this chat.');
    }
  }

  Future<void> _stopReading() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Stop reading ${_chat.name}?',
      message:
          'Jarvis stops receiving new messages from this chat. Messages it '
          'already saved stay until you clear them.',
      confirmLabel: 'Stop reading',
    );
    if (confirmed == true && mounted) await _save(readAlong: false);
  }

  void _snack(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        toolbarHeight: 68,
        titleSpacing: 0,
        title: Row(
          children: [
            // A green ring and dot while Jarvis is reading along live.
            _LiveAvatar(
              live:
                  _chat.readAlong &&
                  (_connectionState == null || _connectionState == 'open'),
              child: ChatAvatar(
                chat: _chat,
                http: widget.http,
                channelId: widget.channelId,
                size: 40,
              ),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    _chat.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.titleMedium,
                  ),
                  Text(
                    [
                      ?(_account ?? widget.account),
                      _connectionState != null && _connectionState != 'open'
                          ? whatsAppConnectionLabel(_connectionState!)
                          : _chat.readAlong
                          ? 'Read along'
                          : 'Reading is off',
                    ].join(' · '),
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: colors.muted,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
        actions: [
          HeaderAction(
            key: const Key('whatsapp-ask-jarvis'),
            label: 'Ask Jarvis',
            collapsesWhenNarrow: true,
            onPressed: () => unawaited(_ask()),
            icon: PhosphorIconsRegular.sparkle,
          ),
          PopupMenuButton<String>(
            key: const Key('whatsapp-chat-menu'),
            tooltip: 'More',
            icon: const Icon(PhosphorIconsRegular.dotsThree),
            onSelected: (value) => switch (value) {
              'reminders' => unawaited(
                _save(autoReminders: !_chat.autoReminders),
              ),
              'clear' => unawaited(_clearHistory()),
              'stop' => unawaited(_stopReading()),
              'start' => unawaited(_startReading()),
              'history' => unawaited(_requestHistory()),
              _ => null,
            },
            itemBuilder: (_) => [
              CheckedPopupMenuItem(
                value: 'reminders',
                checked: _chat.autoReminders,
                child: const Text('Automatic reminders'),
              ),
              const PopupMenuItem(
                value: 'clear',
                child: Text('Clear saved messages'),
              ),
              if (_chat.readAlong)
                PopupMenuItem(
                  value: 'history',
                  enabled: !_requestingHistory,
                  child: Text(
                    _requestingHistory
                        ? 'Requesting history…'
                        : 'Load WhatsApp history',
                  ),
                ),
              PopupMenuItem(
                value: _chat.readAlong ? 'stop' : 'start',
                child: Text(_chat.readAlong ? 'Stop reading' : 'Start reading'),
              ),
            ],
          ),
        ],
      ),
      body: Column(
        children: [
          if (_connectionState != null && _connectionState != 'open')
            InlineNotice(message: whatsAppConnectionMessage(_connectionState!)),
          if (_refreshError != null) InlineNotice(message: _refreshError!),
          if (!_chat.readAlong) ...[
            const InlineNotice(
              message:
                  'Reading is off for this chat. Jarvis is not receiving its messages.',
            ),
            TextButton(
              onPressed: () => unawaited(_startReading()),
              child: const Text('Start reading this chat'),
            ),
          ],
          if (_catchUp != null && _chat.readAlong)
            ContentWidth(
              child: CatchUpCard(
                catchUp: _catchUp!,
                onDismiss: () => setState(() => _catchUp = null),
                onDraft: (item) => unawaited(
                  _draft(
                    preset: item == null
                        ? null
                        : 'Reply to ${item.who.isEmpty ? 'them' : item.who} about: ${item.about}',
                  ),
                ),
              ),
            ),
          Expanded(
            child: _loading
                ? const SkeletonList(rows: 7, shape: SkeletonShape.chat)
                : _error != null && _messages.isEmpty
                ? ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  )
                : _messages.isEmpty
                ? EmptyState(
                    icon: PhosphorIconsRegular.chatsCircle,
                    title: 'Waiting for messages',
                    message: _chat.readAlong
                        ? 'New messages appear as they arrive. Use More → Load WhatsApp history to request older messages from your phone.'
                        : 'Turn on reading to receive messages from this chat.',
                  )
                : WhatsAppWallpaper(
                    child: Stack(
                      children: [
                        Positioned.fill(
                          child: ContentWidth(
                            child: _MessageList(
                              messages: _messages,
                              isGroup: _chat.isGroup,
                              http: widget.http,
                              channelId: widget.channelId,
                              controller: _scroll,
                              hasOlder: _hasOlder,
                              loadingOlder: _loadingOlder,
                              onLoadOlder: () => unawaited(_loadOlder()),
                              onReply: _canSend ? _startReply : null,
                              onHold: (message, anchor) =>
                                  unawaited(_messageActions(message, anchor)),
                            ),
                          ),
                        ),
                        Positioned(
                          right: 16,
                          bottom: 12,
                          child: JumpToLatest(controller: _scroll),
                        ),
                      ],
                    ),
                  ),
          ),
          _Composer(
            controller: _composer,
            focus: _composerFocus,
            replyTo: _replyTo == null
                ? null
                : ReplyPreview(
                    key: ValueKey(_replyTo!.id),
                    author: _replyTo!.fromMe
                        ? 'You'
                        : _replyTo!.sender ?? _chat.name,
                    text: _replyTo!.text,
                    color: _replyTo!.fromMe
                        ? whatsAppGreen
                        : whatsAppSenderColor(
                            _replyTo!.senderId ?? _replyTo!.sender ?? '',
                            JarvisColors.of(context),
                          ),
                    onCancel: () => setState(() => _replyTo = null),
                  ),
            sending: _sending,
            drafting: _drafting,
            onDraft: () => unawaited(_draft()),
            onSend: () => unawaited(_send()),
            canSend: _canSend,
          ),
        ],
      ),
    );
  }
}

class _LiveAvatar extends StatelessWidget {
  const _LiveAvatar({required this.live, required this.child});

  final bool live;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Stack(
      clipBehavior: Clip.none,
      children: [
        AnimatedContainer(
          duration: JarvisMotion.of(context, JarvisMotion.slow),
          curve: JarvisMotion.standard,
          padding: const EdgeInsets.all(2),
          decoration: BoxDecoration(
            shape: BoxShape.circle,
            border: Border.all(
              color: live ? whatsAppGreen : Colors.transparent,
              width: 1.6,
            ),
          ),
          child: child,
        ),
        if (live)
          Positioned(
            right: 0,
            bottom: 0,
            child: PopIn(
              delay: const Duration(milliseconds: 200),
              child: Container(
                width: 12,
                height: 12,
                decoration: BoxDecoration(
                  color: whatsAppGreen,
                  shape: BoxShape.circle,
                  border: Border.all(color: colors.canvas, width: 2),
                ),
              ),
            ),
          ),
      ],
    );
  }
}

class _MessageList extends StatelessWidget {
  const _MessageList({
    required this.messages,
    required this.isGroup,
    required this.http,
    required this.channelId,
    required this.controller,
    required this.hasOlder,
    required this.loadingOlder,
    required this.onLoadOlder,
    this.onReply,
    this.onHold,
  });

  /// Swipe a bubble right to reply; null when sending is not possible.
  final ValueChanged<WhatsAppMessage>? onReply;

  /// Hold a bubble for reactions; gets the bubble's place on screen.
  final void Function(WhatsAppMessage message, Rect anchor)? onHold;

  final List<WhatsAppMessage> messages;
  final bool isGroup;
  final Dio http;
  final String channelId;
  final ScrollController controller;
  final bool hasOlder;
  final bool loadingOlder;
  final VoidCallback onLoadOlder;

  @override
  Widget build(BuildContext context) {
    // Newest at the bottom; the list is reversed so it opens there.
    final items = <Widget>[];
    for (var index = messages.length - 1; index >= 0; index--) {
      final message = messages[index];
      final previous = index > 0 ? messages[index - 1] : null;
      final next = index + 1 < messages.length ? messages[index + 1] : null;
      bool joined(WhatsAppMessage? a, WhatsAppMessage b) =>
          a != null &&
          a.fromMe == b.fromMe &&
          a.sender == b.sender &&
          a.senderId == b.senderId &&
          _sameDay(a.sentAt, b.sentAt) &&
          b.sentAt.difference(a.sentAt).inMinutes.abs() < 5;
      final grouped = next != null && joined(message, next);
      // Messages arrive from the bottom up: the newest first, then the rest
      // in quick succession, each growing out of its own side.
      items.add(
        FadeSlideIn(
          key: ValueKey(message.id),
          index: messages.length - 1 - index,
          offset: 12,
          scale: .9,
          alignment: message.fromMe
              ? Alignment.bottomRight
              : Alignment.bottomLeft,
          child: _Bubble(
            onReply: onReply == null ? null : () => onReply!(message),
            onHold: onHold == null
                ? null
                : (anchor) => onHold!(message, anchor),
            message: message,
            isGroup: isGroup,
            http: http,
            channelId: channelId,
            showSender:
                isGroup &&
                !message.fromMe &&
                (previous == null ||
                    previous.fromMe ||
                    previous.sender != message.sender ||
                    previous.senderId != message.senderId),
            tight: grouped,
            joinedAbove: previous != null && joined(previous, message),
          ),
        ),
      );
      if (previous == null || !_sameDay(previous.sentAt, message.sentAt)) {
        items.add(
          WhatsAppDayPill(
            key: ValueKey((
              'day',
              message.sentAt.year,
              message.sentAt.month,
              message.sentAt.day,
            )),
            label: whatsAppDayLabel(message.sentAt),
          ),
        );
      }
    }
    if (hasOlder) {
      items.add(
        Center(
          child: TextButton(
            key: const Key('whatsapp-load-older'),
            onPressed: loadingOlder ? null : onLoadOlder,
            child: Text(loadingOlder ? 'Loading…' : 'Load older messages'),
          ),
        ),
      );
    }
    return ListView(
      key: const Key('whatsapp-messages'),
      reverse: true,
      controller: controller,
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
      children: items,
    );
  }

  static bool _sameDay(DateTime a, DateTime b) =>
      a.year == b.year && a.month == b.month && a.day == b.day;
}

class _Bubble extends StatelessWidget {
  const _Bubble({
    required this.message,
    required this.isGroup,
    required this.http,
    required this.channelId,
    required this.showSender,
    required this.tight,
    this.joinedAbove = false,
    this.onReply,
    this.onHold,
  });

  final VoidCallback? onReply;
  final ValueChanged<Rect>? onHold;

  final WhatsAppMessage message;
  final bool isGroup;
  final Dio http;
  final String channelId;
  final bool showSender;

  /// The next (newer) message continues this run from the same sender.
  final bool tight;

  /// The message above is from the same sender, moments earlier.
  final bool joinedAbove;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final mine = message.fromMe;
    final naked = whatsAppNakedSticker(message);
    final tint = whatsAppMineBubble(colors);
    const radius = Radius.circular(20);
    const joinedCorner = Radius.circular(6);
    // Runs from one sender read as one block: inner corners tighten and only
    // the last bubble keeps the little tail.
    final upper = joinedAbove ? joinedCorner : radius;
    final lower = tight ? joinedCorner : const Radius.circular(4);
    final showFace = isGroup && !mine;
    final senderColor = whatsAppSenderColor(
      message.senderId ?? message.sender ?? '',
      colors,
    );
    return SwipeToReply(
      onReply: onReply,
      child: Padding(
        padding: EdgeInsets.only(top: joinedAbove ? 2 : 8),
        child: Align(
          alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
          child: Builder(
            builder: (bubble) => GestureDetector(
              onLongPress: onHold == null
                  ? null
                  : () {
                      final box = bubble.findRenderObject();
                      if (box is! RenderBox || !box.hasSize) return;
                      onHold!(box.localToGlobal(Offset.zero) & box.size);
                    },
              child: ConstrainedBox(
                constraints: BoxConstraints(
                  maxWidth: (MediaQuery.sizeOf(context).width * .86).clamp(
                    0,
                    620,
                  ),
                ),
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (showFace) ...[
                      SizedBox(
                        width: 28,
                        child: showSender
                            ? ChatAvatar(
                                http: http,
                                channelId: channelId,
                                subject: message.senderId,
                                initials: whatsAppInitials(
                                  message.sender ?? '',
                                ),
                                size: 28,
                              )
                            : null,
                      ),
                      const SizedBox(width: 8),
                    ],
                    Flexible(
                      child: Container(
                        padding: naked
                            ? const EdgeInsets.only(bottom: 2)
                            : const EdgeInsets.fromLTRB(16, 11, 16, 8),
                        decoration: naked
                            ? null
                            : BoxDecoration(
                                color: mine ? null : colors.surface,
                                gradient: mine
                                    ? LinearGradient(
                                        begin: Alignment.topCenter,
                                        end: Alignment.bottomCenter,
                                        colors: [tint.top, tint.bottom],
                                      )
                                    : null,
                                border: mine
                                    ? null
                                    : Border.all(
                                        color: colors.outline.withValues(
                                          alpha: .55,
                                        ),
                                      ),
                                borderRadius: BorderRadius.only(
                                  topLeft: mine ? radius : upper,
                                  topRight: mine ? upper : radius,
                                  bottomLeft: mine ? radius : lower,
                                  bottomRight: mine ? lower : radius,
                                ),
                                boxShadow: [
                                  BoxShadow(
                                    color: Colors.black.withValues(
                                      alpha: colors.isDark ? .25 : .06,
                                    ),
                                    blurRadius: 6,
                                    offset: const Offset(0, 2),
                                  ),
                                ],
                              ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            if (showSender && message.sender != null)
                              Padding(
                                padding: const EdgeInsets.only(bottom: 2),
                                child: Text(
                                  message.sender!,
                                  style: theme.textTheme.labelMedium?.copyWith(
                                    color: senderColor,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                              ),
                            WhatsAppMessageContent(
                              message: message,
                              http: http,
                              channelId: channelId,
                              selectable: onHold == null,
                            ),
                            const SizedBox(height: 2),
                            Align(
                              alignment: Alignment.centerRight,
                              child: Text(
                                whatsAppClock(message.sentAt),
                                style: theme.textTheme.labelSmall?.copyWith(
                                  color: mine ? tint.meta : colors.muted,
                                  fontSize: 10.5,
                                  fontFeatures: const [
                                    FontFeature.tabularFigures(),
                                  ],
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _Composer extends StatelessWidget {
  const _Composer({
    required this.controller,
    required this.sending,
    required this.drafting,
    required this.onDraft,
    required this.onSend,
    required this.canSend,
    this.focus,
    this.replyTo,
  });

  final FocusNode? focus;

  /// The message being answered, shown above the text field.
  final Widget? replyTo;
  final TextEditingController controller;
  final bool sending;
  final bool drafting;
  final VoidCallback onDraft;
  final VoidCallback onSend;
  final bool canSend;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final hasText = controller.text.trim().isNotEmpty;
    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
        child: ContentWidth(
          child: Container(
            padding: const EdgeInsets.fromLTRB(6, 4, 8, 8),
            decoration: BoxDecoration(
              color: colors.surface,
              border: Border.all(color: colors.outline),
              borderRadius: BorderRadius.circular(26),
              boxShadow: JarvisShadows.floating(Theme.of(context).brightness),
            ),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                // The quote slides open above the text when you start a reply.
                MotionSize(
                  child: MotionSwitcher(
                    child: replyTo ?? const SizedBox(width: double.infinity),
                  ),
                ),
                TextField(
                  key: const Key('whatsapp-composer'),
                  controller: controller,
                  focusNode: focus,
                  minLines: 1,
                  maxLines: 6,
                  maxLength: 4000,
                  textCapitalization: TextCapitalization.sentences,
                  style: const TextStyle(fontSize: 16, height: 1.4),
                  decoration: const InputDecoration(
                    hintText: 'Write a reply…',
                    counterText: '',
                    filled: false,
                    contentPadding: EdgeInsets.fromLTRB(12, 12, 8, 8),
                    border: InputBorder.none,
                    enabledBorder: InputBorder.none,
                    focusedBorder: InputBorder.none,
                  ),
                ),
                Row(
                  children: [
                    TextButton.icon(
                      key: const Key('whatsapp-draft'),
                      onPressed: drafting ? null : onDraft,
                      icon: drafting
                          ? const SizedBox.square(
                              dimension: 16,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : const Icon(
                              PhosphorIconsRegular.magicWand,
                              size: 17,
                            ),
                      label: const Text('Draft a reply'),
                      style: TextButton.styleFrom(
                        foregroundColor: colors.inkSoft,
                      ),
                    ),
                    const Spacer(),
                    AnimatedScale(
                      scale: hasText ? 1 : .86,
                      duration: JarvisMotion.of(
                        context,
                        const Duration(milliseconds: 420),
                      ),
                      curve: hasText ? JarvisSprings.pop : JarvisMotion.exit,
                      child: IconButton.filled(
                        key: const Key('whatsapp-send'),
                        tooltip: 'Send from your WhatsApp',
                        style: IconButton.styleFrom(
                          backgroundColor: whatsAppDeepGreen,
                          foregroundColor: Colors.white,
                          disabledBackgroundColor: colors.surfaceMuted,
                          disabledForegroundColor: colors.muted,
                        ),
                        onPressed: hasText && !sending && canSend
                            ? onSend
                            : null,
                        icon: AnimatedSwitcher(
                          duration: JarvisMotion.of(
                            context,
                            const Duration(milliseconds: 380),
                          ),
                          transitionBuilder: JarvisMotion.morph,
                          child: sending
                              ? const SizedBox.square(
                                  key: ValueKey('sending'),
                                  dimension: 18,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                    color: Colors.white,
                                  ),
                                )
                              : const Icon(
                                  PhosphorIconsBold.arrowUp,
                                  key: ValueKey('send'),
                                  size: 20,
                                ),
                        ),
                      ),
                    ),
                  ],
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// Optional steer for the drafted reply; returns '' for "just answer".
class _DraftSheet extends StatefulWidget {
  const _DraftSheet({required this.name});

  final String name;

  @override
  State<_DraftSheet> createState() => _DraftSheetState();
}

class _DraftSheetState extends State<_DraftSheet> {
  final _instruction = TextEditingController();

  static const _ideas = [
    'Say yes',
    'Politely say no',
    'Suggest another time',
    'Ask a follow-up question',
  ];

  @override
  void dispose() {
    _instruction.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Draft a reply', style: theme.textTheme.titleLarge),
          const SizedBox(height: 4),
          Text(
            'Jarvis writes it in your tone, for ${widget.name}. You can edit '
            'it before sending.',
            style: theme.textTheme.bodySmall?.copyWith(
              color: JarvisColors.of(context).inkSoft,
            ),
          ),
          const SizedBox(height: 14),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              for (final idea in _ideas)
                ActionChip(
                  label: Text(idea),
                  onPressed: () => Navigator.of(context).pop(idea),
                ),
            ],
          ),
          const SizedBox(height: 12),
          TextField(
            key: const Key('whatsapp-draft-instruction'),
            controller: _instruction,
            minLines: 1,
            maxLines: 3,
            maxLength: 500,
            decoration: const InputDecoration(
              hintText: 'Or say what you want to answer (optional)',
              counterText: '',
            ),
            onSubmitted: (value) => Navigator.of(context).pop(value.trim()),
          ),
          const SizedBox(height: 14),
          SizedBox(
            width: double.infinity,
            child: FilledButton.icon(
              key: const Key('whatsapp-draft-submit'),
              onPressed: () =>
                  Navigator.of(context).pop(_instruction.text.trim()),
              icon: const Icon(PhosphorIconsRegular.magicWand, size: 18),
              label: const Text('Write draft'),
            ),
          ),
        ],
      ),
    );
  }
}

/// Ask Jarvis anything about the chat; the answer can become the reply.
class _AskSheet extends StatefulWidget {
  const _AskSheet({required this.http, required this.path, required this.chat});

  final Dio http;
  final String path;
  final WhatsAppChat chat;

  @override
  State<_AskSheet> createState() => _AskSheetState();
}

class _AskSheetState extends State<_AskSheet> {
  final _question = TextEditingController();
  bool _busy = false;
  String? _answer;
  String? _notice;

  static const _ideas = [
    'What should I reply?',
    'Summarize this chat',
    'Did we agree on a date or time?',
    'Is there anything I still need to do?',
  ];

  @override
  void dispose() {
    _question.dispose();
    super.dispose();
  }

  Future<void> _ask(String question) async {
    final text = question.trim();
    if (text.isEmpty || _busy) return;
    _question.text = text;
    setState(() {
      _busy = true;
      _answer = null;
      _notice = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        widget.path,
        queryParameters: whatsAppChatQuery(widget.chat.chatId),
        data: {'question': text},
        options: Options(receiveTimeout: const Duration(minutes: 6)),
      );
      final body = jsonObject(response.data);
      if (!mounted) return;
      setState(() {
        _answer = asJsonString(body?['answer']);
        if (asJsonBool(body?['needsApproval'])) {
          _notice =
              'Jarvis wants to do something that needs your approval. Open '
              'Approvals from the menu to decide.';
        }
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _notice =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not answer right now. Try again.',
      );
    } catch (_) {
      if (mounted) {
        setState(() => _notice = 'Jarvis could not answer right now.');
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final colors = JarvisColors.of(context);
    final answer = _answer;
    return Padding(
      padding: EdgeInsets.fromLTRB(
        20,
        0,
        20,
        20 + MediaQuery.viewInsetsOf(context).bottom,
      ),
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * .8,
        ),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('Ask Jarvis', style: theme.textTheme.titleLarge),
              const SizedBox(height: 4),
              Text(
                'About your chat with ${widget.chat.name}. The answer is also '
                'kept as a conversation in Jarvis.',
                style: theme.textTheme.bodySmall?.copyWith(
                  color: colors.inkSoft,
                ),
              ),
              const SizedBox(height: 14),
              Wrap(
                spacing: 8,
                runSpacing: 6,
                children: [
                  for (final idea in _ideas)
                    ActionChip(
                      label: Text(idea),
                      onPressed: _busy ? null : () => unawaited(_ask(idea)),
                    ),
                ],
              ),
              const SizedBox(height: 12),
              TextField(
                key: const Key('whatsapp-ask-question'),
                controller: _question,
                minLines: 1,
                maxLines: 4,
                maxLength: 4000,
                enabled: !_busy,
                textInputAction: TextInputAction.send,
                decoration: InputDecoration(
                  hintText: 'Ask anything, for example "when is the party?"',
                  counterText: '',
                  suffixIcon: IconButton(
                    key: const Key('whatsapp-ask-submit'),
                    tooltip: 'Ask',
                    onPressed: _busy
                        ? null
                        : () => unawaited(_ask(_question.text)),
                    icon: const Icon(PhosphorIconsRegular.paperPlaneTilt),
                  ),
                ),
                onSubmitted: (value) => unawaited(_ask(value)),
              ),
              if (_busy)
                const Padding(
                  padding: EdgeInsets.symmetric(vertical: 24),
                  child: Center(child: CircularProgressIndicator()),
                ),
              if (_notice != null)
                InlineNotice(
                  message: _notice!,
                  margin: const EdgeInsets.only(top: 12),
                ),
              if (answer != null && answer.trim().isNotEmpty) ...[
                const SizedBox(height: 12),
                SurfaceCard(
                  key: const Key('whatsapp-ask-answer'),
                  padding: const EdgeInsets.all(14),
                  color: colors.surfaceMuted,
                  child: SelectableText(
                    answer.trim(),
                    style: theme.textTheme.bodyMedium?.copyWith(height: 1.4),
                  ),
                ),
                const SizedBox(height: 12),
                Row(
                  children: [
                    Expanded(
                      child: OutlinedButton(
                        onPressed: () => Navigator.of(context).pop(),
                        child: const Text('Done'),
                      ),
                    ),
                    const SizedBox(width: 10),
                    Expanded(
                      child: FilledButton(
                        key: const Key('whatsapp-ask-use'),
                        onPressed: () =>
                            Navigator.of(context).pop(answer.trim()),
                        child: const Text('Use as reply'),
                      ),
                    ),
                  ],
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
