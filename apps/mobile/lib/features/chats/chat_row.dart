import 'dart:math' as math;

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../whatsapp/read_along_screen.dart' show ChatAvatar;
import '../whatsapp/whatsapp_models.dart';
import '../home/home_context.dart';
import 'chat_list.dart';

/// WhatsApp's own green, used only for the small source badge on avatars.
const whatsAppGreen = Color(0xff1fa855);

/// One chat in the list: a face, a name and one line of what was said last.
/// Everything else (source, profile, reminder, typing) rides on those two
/// lines instead of adding more.
class ChatRow extends StatelessWidget {
  const ChatRow({
    required this.item,
    required this.onTap,
    this.onLongPress,
    this.selected = false,
    this.showSource = true,
    this.duplicate = false,
    this.waiting = false,
    this.query = '',
    this.http,
    this.divider = true,
    this.now,
    this.person,
    this.footer,
    super.key,
  });

  final ChatListItem item;

  /// Who this chat is with, from People: their relationship and keeping in touch.
  final HomePerson? person;

  /// Under the preview, such as quick replies.
  final Widget? footer;
  final VoidCallback onTap;
  final VoidCallback? onLongPress;
  final bool selected;

  /// A small WhatsApp badge on the avatar, where both kinds share a list.
  final bool showSource;

  /// Another chat has the same name: say which profile or account this is.
  final bool duplicate;

  /// In "Waiting on you" the corner shows how long they have waited.
  final bool waiting;

  /// Highlighted in the title and preview while searching.
  final String query;
  final Dio? http;
  final bool divider;
  final DateTime? now;

  static const height = 76.0;
  static const avatarSize = 52.0;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final unread = item.unread > 0;
    final preview = _preview();
    final context0 = _context();
    final time = waiting
        ? waitedFor(item.time, now: now)
        : whatsAppListTime(item.time, now: now);
    return Semantics(
      button: true,
      selected: selected,
      label: [
        item.title,
        if (unread) '${item.unread} unread',
        item.isJarvis ? 'Jarvis' : 'WhatsApp',
        if (item.responding) 'Jarvis is replying',
        ?person?.relationship,
        ?preview,
        ?context0,
      ].join(', '),
      onTap: onTap,
      onLongPress: onLongPress,
      excludeSemantics: true,
      child: PressFeedback(
        scale: .985,
        builder: (context, highlight) => InkWell(
          key: Key('chat-${item.key}'),
          borderRadius: BorderRadius.circular(18),
          hoverColor: colors.surfaceMuted.withValues(alpha: .7),
          onTap: onTap,
          onLongPress: onLongPress,
          onHighlightChanged: highlight,
          child: AnimatedContainer(
            duration: JarvisMotion.of(context, JarvisMotion.base),
            curve: JarvisMotion.standard,
            constraints: const BoxConstraints(minHeight: height),
            padding: const EdgeInsets.only(left: 8, right: 8),
            decoration: BoxDecoration(
              color: selected ? colors.accentSoft : Colors.transparent,
              borderRadius: BorderRadius.circular(18),
            ),
            child: Row(
              children: [
                ChatRowAvatar(item: item, http: http, showSource: showSource),
                const SizedBox(width: 14),
                Expanded(
                  child: Container(
                    padding: const EdgeInsets.symmetric(vertical: 14),
                    decoration: BoxDecoration(
                      border: divider && !selected
                          ? Border(bottom: BorderSide(color: colors.outline))
                          : null,
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Row(
                          children: [
                            if (item.pinned) ...[
                              Icon(
                                PhosphorIconsFill.pushPin,
                                size: 12,
                                color: colors.muted,
                              ),
                              const SizedBox(width: 4),
                            ],
                            Expanded(
                              child: Row(
                                children: [
                                  Flexible(
                                    child: _Highlighted(
                                      item.title,
                                      query: query,
                                      style: TextStyle(
                                        fontSize: 16,
                                        height: 21 / 16,
                                        fontWeight: FontWeight.w600,
                                        letterSpacing: -.25,
                                        color: colors.ink,
                                      ),
                                    ),
                                  ),
                                  if (item.profile case final profile?
                                      when duplicate ||
                                          _namedProfile(profile)) ...[
                                    const SizedBox(width: 6),
                                    ChatTag(profile),
                                  ],
                                  if (person?.relationship
                                      case final relationship?) ...[
                                    const SizedBox(width: 6),
                                    Flexible(
                                      child: ChatTag(
                                        relationship,
                                        key: Key(
                                          'chat-relationship-${item.key}',
                                        ),
                                        background: colors.rose.withValues(
                                          alpha: colors.isDark ? .18 : .1,
                                        ),
                                        foreground: colors.rose,
                                      ),
                                    ),
                                  ],
                                ],
                              ),
                            ),
                            const SizedBox(width: 8),
                            Text(
                              time,
                              style: TextStyle(
                                fontSize: 12.5,
                                fontFeatures: const [
                                  FontFeature.tabularFigures(),
                                ],
                                fontWeight: unread || waiting
                                    ? FontWeight.w600
                                    : FontWeight.w400,
                                color: unread || waiting
                                    ? colors.accent
                                    : colors.muted,
                              ),
                            ),
                          ],
                        ),
                        const SizedBox(height: 3),
                        Row(
                          children: [
                            Expanded(child: _line(context, preview, unread)),
                            if (unread) ...[
                              const SizedBox(width: 8),
                              PopIn(child: UnreadCount(item.unread)),
                            ],
                          ],
                        ),
                        if (footer case final footer?) ...[
                          const SizedBox(height: 8),
                          footer,
                        ],
                        if (context0 != null) ...[
                          const SizedBox(height: 2),
                          Text(
                            context0,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 12.5,
                              color: colors.muted,
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
        ),
      ),
    );
  }

  Widget _line(BuildContext context, String? preview, bool unread) {
    final colors = JarvisColors.of(context);
    if (item.responding) {
      return Row(
        key: const Key('chat-typing'),
        children: [
          const TypingDots(),
          const SizedBox(width: 8),
          Flexible(
            child: Text(
              'Jarvis is replying',
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w500,
                color: colors.accent,
              ),
            ),
          ),
        ],
      );
    }
    final style = TextStyle(
      fontSize: 14,
      height: 19 / 14,
      color: unread ? colors.ink : colors.inkSoft,
      fontWeight: unread ? FontWeight.w500 : FontWeight.w400,
    );
    return Row(
      children: [
        if (item.group && item.asksYou) ...[
          Text(
            '@you ',
            style: style.copyWith(
              color: colors.accent,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
        Expanded(
          child: _Highlighted(
            preview ??
                (item.isJarvis
                    ? (item.profile ?? 'Jarvis')
                    : 'Tap to open chat'),
            query: query,
            style: preview == null
                ? style.copyWith(color: colors.muted)
                : style,
          ),
        ),
      ],
    );
  }

  String? _preview() {
    final text = item.preview?.trim();
    return text == null || text.isEmpty ? null : text;
  }

  /// A third, quiet line only when two chats would otherwise look the same.
  String? _context() {
    if (!item.isJarvis && item.unread == 0 && person?.drift != null) {
      return person!.drift;
    }
    if (!duplicate) return null;
    if (item.isJarvis) {
      final started = item.startedAt ?? item.time;
      return started == null ? null : 'Started ${_date(started)}';
    }
    final account = item.account;
    return account == null || account.isEmpty ? null : 'WhatsApp · $account';
  }

  static bool _namedProfile(String profile) =>
      profile.trim().isNotEmpty && profile != 'Jarvis';

  static String _date(DateTime time) =>
      '${time.day} ${_months[time.month - 1]} · ${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}';
}

const _months = [
  'Jan',
  'Feb',
  'Mar',
  'Apr',
  'May',
  'Jun',
  'Jul',
  'Aug',
  'Sep',
  'Oct',
  'Nov',
  'Dec',
];

/// "14 h", "3 d", "2 wk": how long someone has waited on you.
String waitedFor(DateTime? since, {DateTime? now}) {
  if (since == null) return '';
  final gap = (now ?? DateTime.now()).difference(since);
  if (gap.inMinutes < 1) return 'now';
  if (gap.inHours < 1) return '${gap.inMinutes} min';
  if (gap.inDays < 1) return '${gap.inHours} h';
  if (gap.inDays < 14) return '${gap.inDays} d';
  return '${gap.inDays ~/ 7} wk';
}

/// The avatar of a chat row: the person's face or tone, Jarvis's orb in the
/// profile's own hue, or a bell for a chat a reminder started.
class ChatRowAvatar extends StatelessWidget {
  const ChatRowAvatar({
    required this.item,
    this.http,
    this.size = ChatRow.avatarSize,
    this.showSource = true,
    super.key,
  });

  final ChatListItem item;
  final Dio? http;
  final double size;
  final bool showSource;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget avatar;
    if (item.isJarvis && item.fromReminder) {
      avatar = Container(
        key: const Key('chat-avatar-reminder'),
        width: size,
        height: size,
        decoration: BoxDecoration(
          color: colors.warningSoft,
          shape: BoxShape.circle,
        ),
        child: Icon(
          PhosphorIconsRegular.bell,
          size: size * .42,
          color: colors.warning,
        ),
      );
    } else if (item.isJarvis) {
      avatar = ProfileOrb(profile: item.profile, size: size);
    } else if (item.whatsApp != null && item.channelId != null) {
      avatar = ChatAvatar(
        key: ValueKey('chat-avatar-${item.key}'),
        chat: item.whatsApp,
        http: http,
        channelId: item.channelId,
        size: size,
      );
    } else {
      avatar = Container(
        width: size,
        height: size,
        alignment: Alignment.center,
        decoration: BoxDecoration(
          color: colors.surfaceMuted,
          shape: BoxShape.circle,
        ),
        child: item.group
            ? Icon(
                PhosphorIconsRegular.users,
                size: size * .4,
                color: colors.inkSoft,
              )
            : Text(
                item.whatsApp?.initials ?? '#',
                style: TextStyle(
                  fontSize: size * .34,
                  fontWeight: FontWeight.w600,
                  color: colors.inkSoft,
                ),
              ),
      );
    }
    if (!showSource || item.isJarvis) return avatar;
    return SizedBox(
      width: size,
      height: size,
      child: Stack(
        clipBehavior: Clip.none,
        children: [
          avatar,
          Positioned(
            right: -1,
            bottom: -1,
            child: Container(
              key: const Key('chat-source-whatsapp'),
              width: size * .34,
              height: size * .34,
              decoration: BoxDecoration(
                color: whatsAppGreen,
                shape: BoxShape.circle,
                border: Border.all(color: colors.canvas, width: 3),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Jarvis's orb, turned to a hue of its own for each assistant profile, so a
/// chat with "Coach" looks different from one with plain Jarvis.
class ProfileOrb extends StatelessWidget {
  const ProfileOrb({this.profile, this.size = ChatRow.avatarSize, super.key});

  final String? profile;
  final double size;

  @override
  Widget build(BuildContext context) {
    final orb = JarvisOrb(size: size, glow: false);
    final degrees = profileHue(profile);
    if (degrees == 0) return orb;
    return ColorFiltered(colorFilter: hueRotation(degrees), child: orb);
  }
}

/// A steady hue turn for a profile name, between -60 and 60 degrees. Plain
/// Jarvis keeps the brand colour.
double profileHue(String? profile) {
  final name = profile?.trim().toLowerCase() ?? '';
  if (name.isEmpty || name == 'jarvis') return 0;
  var hash = 0;
  for (final unit in name.codeUnits) {
    hash = (hash * 31 + unit) & 0x7fffffff;
  }
  final step = hash % 7; // 0..6
  return (step - 3) * 20.0;
}

/// A colour matrix that turns every hue by [degrees] and keeps lightness.
ColorFilter hueRotation(double degrees) {
  final r = degrees * math.pi / 180;
  final c = math.cos(r);
  final s = math.sin(r);
  const lr = .213, lg = .715, lb = .072;
  return ColorFilter.matrix([
    lr + c * (1 - lr) + s * -lr,
    lg + c * -lg + s * -lg,
    lb + c * -lb + s * (1 - lb),
    0,
    0, //
    lr + c * -lr + s * .143,
    lg + c * (1 - lg) + s * .140,
    lb + c * -lb + s * -.283,
    0,
    0,
    lr + c * -lr + s * -(1 - lr),
    lg + c * -lg + s * lg,
    lb + c * (1 - lb) + s * lb,
    0,
    0,
    0, 0, 0, 1, 0,
  ]);
}

/// A small, quiet tag after a name: a profile, a relationship.
class ChatTag extends StatelessWidget {
  const ChatTag(this.label, {this.background, this.foreground, super.key});

  final String label;
  final Color? background;
  final Color? foreground;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      height: 20,
      padding: const EdgeInsets.symmetric(horizontal: 7),
      decoration: BoxDecoration(
        color: background ?? colors.surfaceMuted,
        borderRadius: BorderRadius.circular(7),
      ),
      // Only as wide as the label, even inside a Flexible.
      child: Center(
        widthFactor: 1,
        child: Text(
          label,
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: foreground ?? colors.inkSoft,
          ),
        ),
      ),
    );
  }
}

/// The unread count in an accent pill; "99+" past that.
class UnreadCount extends StatelessWidget {
  const UnreadCount(this.count, {super.key});

  final int count;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      key: const Key('chat-unread-count'),
      constraints: const BoxConstraints(minWidth: 22),
      height: 22,
      padding: const EdgeInsets.symmetric(horizontal: 7),
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: colors.accent,
        borderRadius: BorderRadius.circular(11),
      ),
      child: Text(
        count > 99 ? '99+' : '$count',
        style: TextStyle(
          fontSize: 12,
          fontWeight: FontWeight.w600,
          fontFeatures: const [FontFeature.tabularFigures()],
          color: colors.onInk,
        ),
      ),
    );
  }
}

/// Text with the search words picked out in the accent colour.
class _Highlighted extends StatelessWidget {
  const _Highlighted(this.text, {required this.style, this.query = ''});

  final String text;
  final String query;
  final TextStyle style;

  @override
  Widget build(BuildContext context) {
    final needle = query.trim().toLowerCase();
    final at = needle.isEmpty ? -1 : text.toLowerCase().indexOf(needle);
    if (at < 0) {
      return Text(
        text,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: style,
      );
    }
    final accent = JarvisColors.of(context).accent;
    return Text.rich(
      TextSpan(
        style: style,
        children: [
          TextSpan(text: text.substring(0, at)),
          TextSpan(
            text: text.substring(at, at + needle.length),
            style: TextStyle(color: accent, fontWeight: FontWeight.w700),
          ),
          TextSpan(text: text.substring(at + needle.length)),
        ],
      ),
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
    );
  }
}
