import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'read_along_screen.dart';
import 'whatsapp_models.dart';

/// One read-along chat: the conversation as Jarvis sees it, a reply drafted in
/// the owner's voice, "Ask Jarvis" about the chat, and sending by tapping Send.
class WhatsAppChatScreen extends StatefulWidget {
  const WhatsAppChatScreen({
    required this.http,
    required this.channelId,
    required this.chat,
    this.pollInterval = const Duration(seconds: 3),
    super.key,
  });

  final Dio http;
  final String channelId;
  final WhatsAppChat chat;
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

  String get _path => whatsAppChatPath(widget.channelId, _chat.chatId);

  @override
  void initState() {
    super.initState();
    unawaited(_load());
    _poll = Timer.periodic(widget.pollInterval, (_) {
      if (mounted) unawaited(_load(quiet: true));
    });
    _composer.addListener(() => setState(() {}));
  }

  @override
  void dispose() {
    _poll?.cancel();
    _composer.dispose();
    super.dispose();
  }

  Future<void> _load({bool quiet = false}) async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>('$_path/messages');
      if (!mounted || revision != _requestRevision) return;
      final messages = [
        for (final item in jsonMaps(response.data))
          ?WhatsAppMessage.fromJson(item),
      ]..sort((a, b) => a.sentAt.compareTo(b.sentAt));
      setState(() {
        _messages = messages;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision || quiet) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this chat.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision || quiet) return;
      setState(() {
        _loading = false;
        _error = 'Could not load this chat.';
      });
    }
  }

  Future<void> _send() async {
    final text = _composer.text.trim();
    if (text.isEmpty || _sending) return;
    setState(() => _sending = true);
    try {
      await widget.http.post<dynamic>('$_path/send', data: {'text': text});
      if (!mounted) return;
      _composer.clear();
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

  Future<void> _draft() async {
    if (_drafting) return;
    final instruction = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _DraftSheet(name: _chat.name),
    );
    if (instruction == null || !mounted) return;
    setState(() => _drafting = true);
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/suggest',
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
      builder: (_) => _AskSheet(http: widget.http, path: _path, chat: _chat),
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
        _path,
        data: {
          'name': _chat.name,
          'readAlong': _chat.readAlong,
          'autoReminders': _chat.autoReminders,
        },
      );
      if (!mounted) return;
      if (readAlong == false) {
        Navigator.of(context).pop();
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
      await widget.http.delete<void>('$_path/messages');
      if (mounted) setState(() => _messages = const []);
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
        titleSpacing: 0,
        title: Row(
          children: [
            ChatAvatar(chat: _chat, size: 34),
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
                    _chat.autoReminders
                        ? 'Jarvis reads along · auto reminders'
                        : 'Jarvis reads along',
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
          IconButton(
            key: const Key('whatsapp-ask-jarvis'),
            tooltip: 'Ask Jarvis about this chat',
            onPressed: () => unawaited(_ask()),
            icon: const Icon(PhosphorIconsRegular.sparkle),
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
              const PopupMenuItem(value: 'stop', child: Text('Stop reading')),
            ],
          ),
        ],
      ),
      body: Column(
        children: [
          Expanded(
            child: _loading
                ? const LoadingState()
                : _error != null && _messages.isEmpty
                ? ErrorState(
                    message: _error!,
                    onRetry: () => unawaited(_load()),
                  )
                : _messages.isEmpty
                ? const EmptyState(
                    icon: PhosphorIconsRegular.chatsCircle,
                    title: 'Waiting for messages',
                    message:
                        'Jarvis sees new messages in this chat from the moment '
                        'you turned it on. They show up here as they arrive.',
                  )
                : _MessageList(messages: _messages, isGroup: _chat.isGroup),
          ),
          _Composer(
            controller: _composer,
            sending: _sending,
            drafting: _drafting,
            onDraft: () => unawaited(_draft()),
            onSend: () => unawaited(_send()),
          ),
        ],
      ),
    );
  }
}

class _MessageList extends StatelessWidget {
  const _MessageList({required this.messages, required this.isGroup});

  final List<WhatsAppMessage> messages;
  final bool isGroup;

  @override
  Widget build(BuildContext context) {
    // Newest at the bottom; the list is reversed so it opens there.
    final items = <Widget>[];
    for (var index = messages.length - 1; index >= 0; index--) {
      final message = messages[index];
      final previous = index > 0 ? messages[index - 1] : null;
      final next = index + 1 < messages.length ? messages[index + 1] : null;
      final grouped =
          next != null &&
          next.fromMe == message.fromMe &&
          next.sender == message.sender &&
          next.sentAt.difference(message.sentAt).inMinutes < 5;
      items.add(
        _Bubble(
          message: message,
          showSender:
              isGroup &&
              !message.fromMe &&
              (previous == null ||
                  previous.fromMe ||
                  previous.sender != message.sender),
          tight: grouped,
        ),
      );
      if (previous == null || !_sameDay(previous.sentAt, message.sentAt)) {
        items.add(_DaySeparator(time: message.sentAt));
      }
    }
    return ListView(
      key: const Key('whatsapp-messages'),
      reverse: true,
      padding: const EdgeInsets.fromLTRB(12, 12, 12, 8),
      children: items,
    );
  }

  static bool _sameDay(DateTime a, DateTime b) =>
      a.year == b.year && a.month == b.month && a.day == b.day;
}

class _DaySeparator extends StatelessWidget {
  const _DaySeparator({required this.time});

  final DateTime time;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: Center(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(
            color: colors.surfaceMuted,
            borderRadius: BorderRadius.circular(999),
          ),
          child: Text(
            whatsAppDayLabel(time),
            style: Theme.of(
              context,
            ).textTheme.labelSmall?.copyWith(color: colors.inkSoft),
          ),
        ),
      ),
    );
  }
}

class _Bubble extends StatelessWidget {
  const _Bubble({
    required this.message,
    required this.showSender,
    required this.tight,
  });

  final WhatsAppMessage message;
  final bool showSender;
  final bool tight;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final mine = message.fromMe;
    final background = mine ? colors.accentSoft : colors.surface;
    final radius = Radius.circular(JarvisRadii.lg);
    return Padding(
      padding: EdgeInsets.only(top: tight ? 2 : 8),
      child: Align(
        alignment: mine ? Alignment.centerRight : Alignment.centerLeft,
        child: ConstrainedBox(
          constraints: BoxConstraints(
            maxWidth: MediaQuery.sizeOf(context).width * .78,
          ),
          child: Container(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 6),
            decoration: BoxDecoration(
              color: background,
              border: mine ? null : Border.all(color: colors.outline),
              borderRadius: BorderRadius.only(
                topLeft: radius,
                topRight: radius,
                bottomLeft: mine ? radius : const Radius.circular(4),
                bottomRight: mine ? const Radius.circular(4) : radius,
              ),
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
                        color: colors.accentDeep,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ),
                SelectableText(
                  message.text,
                  style: theme.textTheme.bodyMedium?.copyWith(height: 1.35),
                ),
                const SizedBox(height: 2),
                Align(
                  alignment: Alignment.centerRight,
                  child: Text(
                    whatsAppClock(message.sentAt),
                    style: theme.textTheme.labelSmall?.copyWith(
                      color: colors.muted,
                      fontSize: 10.5,
                    ),
                  ),
                ),
              ],
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
  });

  final TextEditingController controller;
  final bool sending;
  final bool drafting;
  final VoidCallback onDraft;
  final VoidCallback onSend;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final hasText = controller.text.trim().isNotEmpty;
    return Material(
      color: colors.surface,
      child: SafeArea(
        top: false,
        child: Container(
          decoration: BoxDecoration(
            border: Border(top: BorderSide(color: colors.outline)),
          ),
          padding: const EdgeInsets.fromLTRB(8, 8, 8, 8),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (hasText)
                Padding(
                  padding: const EdgeInsets.fromLTRB(8, 0, 8, 6),
                  child: Row(
                    children: [
                      Icon(
                        PhosphorIconsRegular.whatsappLogo,
                        size: 13,
                        color: colors.muted,
                      ),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          'Sends from your own WhatsApp when you tap Send.',
                          style: Theme.of(
                            context,
                          ).textTheme.labelSmall?.copyWith(color: colors.muted),
                        ),
                      ),
                    ],
                  ),
                ),
              Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  IconButton(
                    key: const Key('whatsapp-draft'),
                    tooltip: 'Draft a reply with Jarvis',
                    onPressed: drafting ? null : onDraft,
                    icon: drafting
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Icon(
                            PhosphorIconsRegular.magicWand,
                            color: colors.accentDeep,
                          ),
                  ),
                  Expanded(
                    child: TextField(
                      key: const Key('whatsapp-composer'),
                      controller: controller,
                      minLines: 1,
                      maxLines: 6,
                      maxLength: 4000,
                      textCapitalization: TextCapitalization.sentences,
                      decoration: const InputDecoration(
                        hintText: 'Message',
                        counterText: '',
                        isDense: true,
                      ),
                    ),
                  ),
                  const SizedBox(width: 6),
                  IconButton.filled(
                    key: const Key('whatsapp-send'),
                    tooltip: 'Send',
                    style: IconButton.styleFrom(
                      backgroundColor: colors.accent,
                      foregroundColor: Colors.white,
                      disabledBackgroundColor: colors.surfaceMuted,
                      disabledForegroundColor: colors.muted,
                    ),
                    onPressed: hasText && !sending ? onSend : null,
                    icon: sending
                        ? const SizedBox.square(
                            dimension: 18,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : const Icon(PhosphorIconsRegular.paperPlaneTilt),
                  ),
                ],
              ),
            ],
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
        '${widget.path}/ask',
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
