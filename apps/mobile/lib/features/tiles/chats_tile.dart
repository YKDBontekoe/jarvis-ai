import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'tile_models.dart';
import 'tile_visuals.dart';

/// Conversation previews use the same saved grid and tap targets as other
/// tiles, with a layout suited to people and conversation context.
class ChatsTileContent extends StatelessWidget {
  const ChatsTileContent({
    required this.info,
    required this.size,
    this.onRowTap,
    super.key,
  });

  final TileData info;
  final TileSize size;
  final ValueChanged<TileRow>? onRowTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final live = info.visual == TileVisual.waveform;
    return Padding(
      padding: const EdgeInsets.all(12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              Text(
                'Chats',
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -.25,
                  color: colors.ink,
                ),
              ),
              const SizedBox(width: 6),
              Expanded(
                child: Align(
                  alignment: Alignment.centerRight,
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: live
                        ? Semantics(
                            label: 'Jarvis is replying',
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                if (size != TileSize.square) ...[
                                  Text(
                                    'Replying…',
                                    style: TextStyle(
                                      fontSize: 11,
                                      color: colors.accentDeep,
                                    ),
                                  ),
                                  const SizedBox(width: 6),
                                ],
                                TileWaveform(color: colors.accent, height: 12),
                              ],
                            ),
                          )
                        : info.stat != null
                        ? Text(
                            '${info.stat} unread',
                            style: TextStyle(
                              fontSize: 11,
                              fontWeight: FontWeight.w500,
                              color: colors.accentDeep,
                            ),
                          )
                        : Icon(
                            PhosphorIconsRegular.caretRight,
                            size: 12,
                            color: colors.inkSoft,
                          ),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Expanded(
            child: info.chats.isEmpty
                ? Align(
                    alignment: Alignment.centerLeft,
                    child: Text(
                      'Jarvis and WhatsApp, in one place.',
                      style: TextStyle(
                        fontSize: 13,
                        height: 1.4,
                        color: colors.inkSoft,
                      ),
                    ),
                  )
                : size == TileSize.square
                ? _CompactChat(chat: info.chats.first)
                : LayoutBuilder(
                    builder: (context, box) {
                      final scaler = MediaQuery.textScalerOf(context);
                      // Three-line WhatsApp rows and two-line Jarvis rows
                      // remain comfortable at the grid's clamped text scale.
                      final rowHeight = scaler.scale(56);
                      final count = (box.maxHeight / rowHeight).floor().clamp(
                        1,
                        info.chats.length,
                      );
                      final rows = info.chats.take(count).toList();
                      return Column(
                        children: [
                          for (var i = 0; i < rows.length; i++)
                            Expanded(
                              child: _ConversationRow(
                                chat: rows[i],
                                divider: i > 0,
                                onTap: onRowTap == null
                                    ? null
                                    : () => onRowTap!(
                                        TileRow(
                                          rows[i].title,
                                          target: rows[i].target,
                                        ),
                                      ),
                              ),
                            ),
                        ],
                      );
                    },
                  ),
          ),
        ],
      ),
    );
  }
}

class _ConversationRow extends StatelessWidget {
  const _ConversationRow({
    required this.chat,
    required this.divider,
    required this.onTap,
  });

  final TileChat chat;
  final bool divider;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: [
        chat.title,
        chat.context,
        if (chat.preview?.isNotEmpty == true) chat.preview!,
        if (chat.time?.isNotEmpty == true) chat.time!,
        if (chat.unread > 0) '${chat.unread} unread',
      ].join(', '),
      excludeSemantics: true,
      child: InkWell(
        key: Key('home-chat-${chat.target}'),
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Row(
          children: [
            _ChatMark(chat: chat),
            const SizedBox(width: 10),
            Expanded(
              child: Container(
                height: double.infinity,
                alignment: Alignment.centerLeft,
                decoration: BoxDecoration(
                  border: divider
                      ? Border(
                          top: BorderSide(
                            width: .5,
                            color: colors.outline.withValues(alpha: .6),
                          ),
                        )
                      : null,
                ),
                child: Column(
                  mainAxisAlignment: MainAxisAlignment.center,
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            chat.title,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w600,
                              letterSpacing: -.1,
                              height: 1.2,
                              color: colors.ink,
                            ),
                          ),
                        ),
                        const SizedBox(width: 8),
                        Text(
                          chat.time ?? '',
                          style: TextStyle(
                            fontSize: 11,
                            color: colors.inkSoft,
                            fontFeatures: const [FontFeature.tabularFigures()],
                          ),
                        ),
                        if (chat.unread > 0) ...[
                          const SizedBox(width: 6),
                          Container(
                            key: const Key('home-chat-unread'),
                            width: 6,
                            height: 6,
                            decoration: BoxDecoration(
                              color: colors.accent,
                              shape: BoxShape.circle,
                            ),
                          ),
                        ],
                      ],
                    ),
                    const SizedBox(height: 2),
                    Text(
                      chat.context,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 11.5,
                        height: 1.2,
                        color: colors.inkSoft,
                      ),
                    ),
                    if (chat.preview?.isNotEmpty == true) ...[
                      const SizedBox(height: 1),
                      Text(
                        chat.preview!,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(
                          fontSize: 12,
                          height: 1.2,
                          color: colors.inkSoft,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

class _CompactChat extends StatelessWidget {
  const _CompactChat({required this.chat});

  final TileChat chat;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Row(
          children: [
            _ChatMark(chat: chat, size: 28),
            const Spacer(),
            Text(
              chat.time ?? '',
              style: TextStyle(fontSize: 11, color: colors.inkSoft),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Flexible(
          child: Text(
            chat.title,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 14,
              height: 1.2,
              fontWeight: FontWeight.w600,
              color: colors.ink,
            ),
          ),
        ),
        const SizedBox(height: 3),
        Text(
          chat.context,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(fontSize: 11.5, color: colors.inkSoft),
        ),
      ],
    );
  }
}

class _ChatMark extends StatelessWidget {
  const _ChatMark({required this.chat, this.size = 32});

  final TileChat chat;
  final double size;

  @override
  Widget build(BuildContext context) {
    if (chat.isJarvis) return JarvisOrb(size: size, glow: false);
    final colors = JarvisColors.of(context);
    final foreground = colors.isDark ? colors.sky : colors.accentDeep;
    return Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: colors.sky.withValues(alpha: colors.isDark ? .14 : .1),
        shape: BoxShape.circle,
      ),
      child: chat.group
          ? Icon(PhosphorIconsRegular.users, size: size * .5, color: foreground)
          : Text(
              chat.title.isEmpty
                  ? '?'
                  : chat.title.characters.first.toUpperCase(),
              style: TextStyle(
                fontSize: size * .42,
                fontWeight: FontWeight.w600,
                color: foreground,
              ),
            ),
    );
  }
}
