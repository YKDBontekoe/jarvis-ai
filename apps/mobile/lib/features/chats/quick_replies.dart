import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../whatsapp/whatsapp_models.dart';
import 'chat_list.dart';

/// Replies already fetched, by chat and newest message, so scrolling back to a
/// row does not ask Jarvis again.
final _cache = <String, List<String>>{};

/// Clears what [QuickReplies] remembers (tests).
@visibleForTesting
void clearQuickReplies() => _cache.clear();

/// Up to three short replies Jarvis suggests for a WhatsApp chat. Tapping one
/// opens the chat with it in the composer; nothing is sent from here.
class QuickReplies extends StatefulWidget {
  const QuickReplies({
    required this.http,
    required this.item,
    required this.onPick,
    this.padding = EdgeInsets.zero,
    super.key,
  });

  final Dio http;
  final ChatListItem item;
  final ValueChanged<String> onPick;
  final EdgeInsetsGeometry padding;

  @override
  State<QuickReplies> createState() => _QuickRepliesState();
}

class _QuickRepliesState extends State<QuickReplies> {
  List<String> _replies = const [];

  String get _key =>
      '${widget.item.key}@${widget.item.time?.toIso8601String()}';

  @override
  void initState() {
    super.initState();
    _replies = _cache[_key] ?? const [];
    if (!_cache.containsKey(_key)) unawaited(_load());
  }

  @override
  void didUpdateWidget(QuickReplies oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.item.key != widget.item.key ||
        oldWidget.item.time != widget.item.time) {
      _replies = _cache[_key] ?? const [];
      if (!_cache.containsKey(_key)) unawaited(_load());
    }
  }

  Future<void> _load() async {
    final key = _key;
    final chat = widget.item.whatsApp;
    final channel = widget.item.channelId;
    if (chat == null || channel == null) return;
    try {
      final response = await widget.http.post<dynamic>(
        whatsAppChatPath(channel, action: 'quick-replies'),
        queryParameters: whatsAppChatQuery(chat.chatId),
      );
      final replies = jsonStrings(jsonObject(response.data)?['replies']);
      _cache[key] = replies.take(3).toList();
    } catch (_) {
      // No chips is fine: the row still opens the chat.
      _cache[key] = const [];
    }
    if (mounted && key == _key) setState(() => _replies = _cache[key]!);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return MotionSize(
      child: _replies.isEmpty
          ? const SizedBox(width: double.infinity)
          : Padding(
              padding: widget.padding,
              child: FadeSlideIn(
                offset: 6,
                child: SingleChildScrollView(
                  key: Key('quick-replies-${widget.item.key}'),
                  scrollDirection: Axis.horizontal,
                  child: Row(
                    children: [
                      Icon(
                        PhosphorIconsRegular.sparkle,
                        size: 13,
                        color: colors.accent,
                      ),
                      const SizedBox(width: 6),
                      for (final reply in _replies) ...[
                        PressFeedback(
                          scale: .96,
                          builder: (context, highlight) => Material(
                            color: colors.accentSoft,
                            borderRadius: BorderRadius.circular(999),
                            child: InkWell(
                              key: Key('quick-reply-$reply'),
                              borderRadius: BorderRadius.circular(999),
                              onHighlightChanged: highlight,
                              onTap: () {
                                unawaited(HapticFeedback.selectionClick());
                                widget.onPick(reply);
                              },
                              child: Padding(
                                padding: const EdgeInsets.symmetric(
                                  horizontal: 12,
                                  vertical: 8,
                                ),
                                child: Text(
                                  reply,
                                  style: TextStyle(
                                    fontSize: 13.5,
                                    fontWeight: FontWeight.w500,
                                    color: colors.accentDeep,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        ),
                        const SizedBox(width: 6),
                      ],
                    ],
                  ),
                ),
              ),
            ),
    );
  }
}
