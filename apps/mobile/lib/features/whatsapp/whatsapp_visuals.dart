import 'dart:math' as math;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// WhatsApp's own green, for the bits of the chat that should feel like it.
const whatsAppGreen = Color(0xff25d366);

/// The tint of your own bubbles: WhatsApp's pale green by day, its deep teal
/// at night, so a WhatsApp chat reads as WhatsApp at a glance.
({Color top, Color bottom, Color meta}) whatsAppMineBubble(
  JarvisColors colors,
) => colors.isDark
    ? (
        top: const Color(0xff0b6b58),
        bottom: const Color(0xff075548),
        meta: const Color(0xffa3d9c9),
      )
    : (
        top: const Color(0xffe4fcd9),
        bottom: const Color(0xffd5f7c6),
        meta: const Color(0xff5c7a66),
      );

/// A stable colour per group member, as WhatsApp does for sender names.
Color whatsAppSenderColor(String key, JarvisColors colors) {
  const light = [
    Color(0xff1f7aec),
    Color(0xffd6336c),
    Color(0xff0c9d6a),
    Color(0xffe8590c),
    Color(0xff7048e8),
    Color(0xff0b8a9c),
    Color(0xffc2255c),
    Color(0xff5c940d),
  ];
  const dark = [
    Color(0xff74b3ff),
    Color(0xffff8fb5),
    Color(0xff5fe0ad),
    Color(0xffffa94d),
    Color(0xffb197fc),
    Color(0xff66d9e8),
    Color(0xfff783ac),
    Color(0xffa9e34b),
  ];
  var hash = 0;
  for (final unit in key.codeUnits) {
    hash = (hash * 31 + unit) & 0x7fffffff;
  }
  final palette = colors.isDark ? dark : light;
  return palette[hash % palette.length];
}

/// The chat backdrop: a soft wash with a faint, hand-drawn doodle pattern,
/// like WhatsApp's wallpaper but in Jarvis's colours.
class WhatsAppWallpaper extends StatelessWidget {
  const WhatsAppWallpaper({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        gradient: LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: colors.isDark
              ? [
                  Color.lerp(colors.canvas, const Color(0xff0b3d33), .35)!,
                  colors.canvas,
                ]
              : [
                  Color.lerp(colors.canvas, const Color(0xffdff3e6), .7)!,
                  Color.lerp(colors.canvas, const Color(0xffefe7da), .45)!,
                ],
        ),
      ),
      child: Stack(
        children: [
          Positioned.fill(
            child: RepaintBoundary(
              child: CustomPaint(
                painter: _DoodlePainter(
                  color: colors.isDark
                      ? Colors.white.withValues(alpha: .035)
                      : const Color(0xff1d4d3a).withValues(alpha: .05),
                ),
              ),
            ),
          ),
          child,
        ],
      ),
    );
  }
}

class _DoodlePainter extends CustomPainter {
  _DoodlePainter({required this.color});

  final Color color;

  static const _tile = 150.0;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.clipRect(Offset.zero & size);
    final stroke = Paint()
      ..color = color
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.4
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    for (var y = -_tile / 2; y < size.height + _tile; y += _tile) {
      final row = (y / _tile).round();
      var column = 0;
      for (var x = row.isEven ? 0.0 : -_tile / 2; x < size.width; x += _tile) {
        // A handful of layouts, mixed so the repeat is hard to spot.
        _tileAt(canvas, Offset(x, y), stroke, (row * 3 + column++) % 5);
      }
    }
  }

  void _tileAt(Canvas canvas, Offset origin, Paint paint, int variant) {
    final random = math.Random(7 + variant * 101);
    for (var i = 0; i < 7; i++) {
      final at =
          origin +
          Offset(
            12 + random.nextDouble() * (_tile - 24),
            12 + random.nextDouble() * (_tile - 24),
          );
      final turn = random.nextDouble() * math.pi * 2;
      canvas.save();
      canvas.translate(at.dx, at.dy);
      canvas.rotate(turn);
      switch ((i + variant) % 6) {
        case 0: // speech bubble
          final r = RRect.fromRectAndRadius(
            const Rect.fromLTWH(-9, -7, 18, 13),
            const Radius.circular(5),
          );
          canvas.drawRRect(r, paint);
          canvas.drawLine(const Offset(-3, 6), const Offset(-6, 10), paint);
        case 1: // sparkle
          for (var k = 0; k < 4; k++) {
            canvas.drawLine(Offset.zero, const Offset(0, -7), paint);
            canvas.rotate(math.pi / 2);
          }
        case 2: // heart
          final path = Path()
            ..moveTo(0, 6)
            ..cubicTo(-10, -1, -5, -9, 0, -3)
            ..cubicTo(5, -9, 10, -1, 0, 6);
          canvas.drawPath(path, paint);
        case 3: // ring
          canvas.drawCircle(Offset.zero, 5, paint);
        case 4: // squiggle
          final path = Path()..moveTo(-10, 0);
          for (var k = 0; k < 4; k++) {
            path.relativeQuadraticBezierTo(2.5, k.isEven ? -5 : 5, 5, 0);
          }
          canvas.drawPath(path, paint);
        default: // dot trio
          for (var k = -1; k <= 1; k++) {
            canvas.drawCircle(Offset(k * 5.0, 0), 1.2, paint);
          }
      }
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(_DoodlePainter old) => old.color != color;
}

/// A day label floating over the wallpaper on a frosted pill.
class WhatsAppDayPill extends StatelessWidget {
  const WhatsAppDayPill({required this.label, super.key});

  final String label;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 12),
      child: Center(
        child: PopIn(
          from: .8,
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 5),
            decoration: BoxDecoration(
              color: colors.surface.withValues(alpha: colors.isDark ? .8 : .9),
              borderRadius: BorderRadius.circular(999),
              border: Border.all(color: colors.outline.withValues(alpha: .6)),
              boxShadow: JarvisShadows.hairline(colors.brightness),
            ),
            child: Text(
              label,
              style: Theme.of(context).textTheme.labelSmall?.copyWith(
                color: colors.inkSoft,
                fontWeight: FontWeight.w600,
                letterSpacing: .2,
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// A round button that pops up over the list once you scroll away from the
/// newest messages, and takes you back down.
class JumpToLatest extends StatefulWidget {
  const JumpToLatest({required this.controller, super.key});

  final ScrollController controller;

  @override
  State<JumpToLatest> createState() => _JumpToLatestState();
}

class _JumpToLatestState extends State<JumpToLatest> {
  bool _away = false;

  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_scrolled);
  }

  @override
  void dispose() {
    widget.controller.removeListener(_scrolled);
    super.dispose();
  }

  void _scrolled() {
    final controller = widget.controller;
    // The list is reversed: offset 0 is the newest message.
    final away = controller.hasClients && controller.offset > 360;
    if (away != _away) setState(() => _away = away);
  }

  void _jump() {
    final controller = widget.controller;
    if (!controller.hasClients) return;
    if (JarvisMotion.reduced(context)) {
      controller.jumpTo(0);
    } else {
      controller.animateTo(
        0,
        duration: const Duration(milliseconds: 520),
        curve: JarvisMotion.emphasized,
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return IgnorePointer(
      ignoring: !_away,
      child: AnimatedScale(
        scale: _away ? 1 : .4,
        duration: JarvisMotion.of(context, const Duration(milliseconds: 420)),
        curve: _away ? JarvisSprings.pop : JarvisMotion.exit,
        child: AnimatedOpacity(
          opacity: _away ? 1 : 0,
          duration: JarvisMotion.of(context, JarvisMotion.fast),
          child: Material(
            color: colors.surface,
            shape: CircleBorder(side: BorderSide(color: colors.outline)),
            elevation: 3,
            shadowColor: Colors.black26,
            child: InkWell(
              key: const Key('whatsapp-jump-latest'),
              customBorder: const CircleBorder(),
              onTap: _jump,
              child: SizedBox.square(
                dimension: 42,
                child: Icon(
                  PhosphorIconsRegular.caretDown,
                  size: 18,
                  color: colors.inkSoft,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Drag a bubble to the right to reply to it, as in WhatsApp: a reply arrow
/// grows in behind it, a tick of haptics marks the point of no return, and
/// the bubble springs back when you let go.
class SwipeToReply extends StatefulWidget {
  const SwipeToReply({required this.child, this.onReply, super.key});

  final Widget child;

  /// Null turns the gesture off.
  final VoidCallback? onReply;

  /// How far the bubble must travel before letting go replies.
  static const threshold = 64.0;

  @override
  State<SwipeToReply> createState() => _SwipeToReplyState();
}

class _SwipeToReplyState extends State<SwipeToReply>
    with SingleTickerProviderStateMixin {
  late final AnimationController _back = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 420),
  )..addListener(_settle);
  double _offset = 0;
  double _releasedAt = 0;
  bool _armed = false;

  void _settle() => setState(
    () =>
        _offset = _releasedAt * (1 - JarvisSprings.soft.transform(_back.value)),
  );

  void _update(DragUpdateDetails details) {
    _back.stop();
    // Past the threshold the bubble moves at a quarter speed, like a stretch.
    final raw = math.max(0.0, _offset + details.delta.dx);
    final next = raw <= SwipeToReply.threshold
        ? raw
        : SwipeToReply.threshold +
              (raw - SwipeToReply.threshold) * (details.delta.dx > 0 ? .25 : 1);
    final armed = next >= SwipeToReply.threshold;
    if (armed && !_armed) HapticFeedback.selectionClick();
    setState(() {
      _offset = math.min(next, SwipeToReply.threshold * 1.6);
      _armed = armed;
    });
  }

  void _end(DragEndDetails details) => _release(reply: _armed);

  /// Springs back; a cancelled drag (another gesture took over) never replies.
  void _release({required bool reply}) {
    if (reply) widget.onReply?.call();
    _armed = false;
    _releasedAt = _offset;
    if (JarvisMotion.reduced(context)) {
      setState(() => _offset = 0);
    } else {
      _back.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _back.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (widget.onReply == null) return widget.child;
    final colors = JarvisColors.of(context);
    final progress = (_offset / SwipeToReply.threshold).clamp(0.0, 1.0);
    return GestureDetector(
      behavior: HitTestBehavior.translucent,
      onHorizontalDragUpdate: _update,
      onHorizontalDragEnd: _end,
      onHorizontalDragCancel: () => _release(reply: false),
      child: Stack(
        alignment: Alignment.centerLeft,
        children: [
          if (_offset > 0)
            Positioned(
              left: 4,
              child: Opacity(
                opacity: progress,
                child: Transform.scale(
                  scale: .5 + .5 * progress + (_armed ? .12 : 0),
                  child: Container(
                    width: 32,
                    height: 32,
                    decoration: BoxDecoration(
                      color: _armed ? whatsAppGreen : colors.surfaceRaised,
                      shape: BoxShape.circle,
                    ),
                    child: Icon(
                      PhosphorIconsRegular.arrowBendUpLeft,
                      size: 16,
                      color: _armed ? Colors.white : colors.inkSoft,
                    ),
                  ),
                ),
              ),
            ),
          Transform.translate(offset: Offset(_offset, 0), child: widget.child),
        ],
      ),
    );
  }
}

/// The emoji WhatsApp offers first when you hold a message.
const whatsAppQuickReactions = ['👍', '❤️', '😂', '😮', '😢', '🙏'];

/// What the reaction bar returned: an emoji, or one of the actions.
sealed class MessageAction {
  const MessageAction();
}

final class ReactWith extends MessageAction {
  const ReactWith(this.emoji);
  final String emoji;
}

final class ReplyTo extends MessageAction {
  const ReplyTo();
}

final class CopyText extends MessageAction {
  const CopyText();
}

/// Holds a message: the screen dims, and a bar of reactions springs open
/// above it (below when there is no room), with Reply and Copy underneath.
Future<MessageAction?> showMessageActions(
  BuildContext context, {
  required Rect anchor,
  required bool mine,
  bool canReply = true,
}) {
  HapticFeedback.mediumImpact();
  return Navigator.of(context).push<MessageAction>(
    PageRouteBuilder<MessageAction>(
      opaque: false,
      barrierDismissible: true,
      barrierLabel: 'Close',
      barrierColor: Colors.black.withValues(alpha: .18),
      transitionDuration: JarvisMotion.of(context, JarvisMotion.base),
      reverseTransitionDuration: JarvisMotion.of(context, JarvisMotion.fast),
      pageBuilder: (context, animation, _) => _MessageActions(
        anchor: anchor,
        mine: mine,
        canReply: canReply,
        animation: animation,
      ),
    ),
  );
}

class _MessageActions extends StatelessWidget {
  const _MessageActions({
    required this.anchor,
    required this.mine,
    required this.canReply,
    required this.animation,
  });

  final Rect anchor;
  final bool mine;
  final bool canReply;
  final Animation<double> animation;

  static const _barHeight = 52.0;
  static const _menuHeight = 48.0;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final screen = MediaQuery.sizeOf(context);
    final padding = MediaQuery.paddingOf(context);
    final width = math.min(320.0, screen.width - 24);
    final above = anchor.top - padding.top > _barHeight + 24;
    final barTop = above
        ? anchor.top - _barHeight - 10
        : math.min(anchor.bottom + 10, screen.height - 140);
    final left = (mine ? anchor.right - width : anchor.left).clamp(
      12.0,
      screen.width - width - 12,
    );
    final menuTop = above
        ? math.min(anchor.bottom + 10, screen.height - padding.bottom - 60)
        : barTop + _barHeight + 8;
    final curved = CurvedAnimation(parent: animation, curve: JarvisSprings.pop);
    Widget surface(Widget child) => Material(
      color: colors.surface,
      elevation: 8,
      shadowColor: Colors.black38,
      borderRadius: BorderRadius.circular(28),
      child: child,
    );
    return Stack(
      children: [
        Positioned(
          left: left,
          top: barTop,
          width: width,
          height: _barHeight,
          child: ScaleTransition(
            scale: curved,
            alignment: mine ? Alignment.bottomRight : Alignment.bottomLeft,
            child: surface(
              Row(
                mainAxisAlignment: MainAxisAlignment.spaceEvenly,
                children: [
                  for (final (index, emoji) in whatsAppQuickReactions.indexed)
                    PopIn(
                      delay: Duration(milliseconds: 30 * index),
                      from: .2,
                      child: _EmojiButton(
                        emoji: emoji,
                        onTap: () =>
                            Navigator.of(context).pop(ReactWith(emoji)),
                      ),
                    ),
                ],
              ),
            ),
          ),
        ),
        Positioned(
          left: mine ? null : left,
          right: mine ? screen.width - left - width : null,
          top: menuTop,
          height: _menuHeight,
          child: FadeTransition(
            opacity: animation,
            child: surface(
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  if (canReply)
                    _MenuButton(
                      key: const Key('whatsapp-action-reply'),
                      icon: PhosphorIconsRegular.arrowBendUpLeft,
                      label: 'Reply',
                      onTap: () => Navigator.of(context).pop(const ReplyTo()),
                    ),
                  _MenuButton(
                    key: const Key('whatsapp-action-copy'),
                    icon: PhosphorIconsRegular.copy,
                    label: 'Copy',
                    onTap: () => Navigator.of(context).pop(const CopyText()),
                  ),
                ],
              ),
            ),
          ),
        ),
      ],
    );
  }
}

class _EmojiButton extends StatelessWidget {
  const _EmojiButton({required this.emoji, required this.onTap});

  final String emoji;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    label: 'React with $emoji',
    excludeSemantics: true,
    child: PressFeedback(
      scale: .8,
      builder: (context, highlight) => InkResponse(
        key: Key('whatsapp-react-$emoji'),
        onTap: onTap,
        onHighlightChanged: highlight,
        radius: 22,
        child: SizedBox.square(
          dimension: 40,
          child: Center(
            child: Text(emoji, style: const TextStyle(fontSize: 26)),
          ),
        ),
      ),
    ),
  );
}

class _MenuButton extends StatelessWidget {
  const _MenuButton({
    required this.icon,
    required this.label,
    required this.onTap,
    super.key,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) => InkWell(
    onTap: onTap,
    borderRadius: BorderRadius.circular(24),
    child: Padding(
      padding: const EdgeInsets.symmetric(horizontal: 16),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(icon, size: 18),
          const SizedBox(width: 8),
          Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
        ],
      ),
    ),
  );
}

/// Sends an emoji floating up off [from] (global coordinates), growing and
/// fading as it goes: the reaction is on its way to WhatsApp.
void floatEmoji(BuildContext context, Rect from, String emoji) {
  if (JarvisMotion.reduced(context)) return;
  final overlay = Overlay.maybeOf(context, rootOverlay: true);
  final box = overlay?.context.findRenderObject();
  if (overlay == null || box is! RenderBox) return;
  final start = box.globalToLocal(
    Offset(from.center.dx, from.top + math.min(24, from.height / 2)),
  );
  late final OverlayEntry entry;
  entry = OverlayEntry(
    builder: (_) => IgnorePointer(
      child: TweenAnimationBuilder<double>(
        tween: Tween(begin: 0, end: 1),
        duration: const Duration(milliseconds: 820),
        onEnd: () {
          if (entry.mounted) entry.remove();
        },
        builder: (context, t, _) {
          final rise = Curves.easeOutCubic.transform(t);
          final pop = JarvisSprings.pop.transform(math.min(1, t * 2));
          return Stack(
            children: [
              Positioned(
                left: start.dx - 20,
                top: start.dy - 20 - 80 * rise,
                child: Opacity(
                  opacity: 1 - const Interval(.55, 1).transform(t),
                  child: Transform.scale(
                    scale: .4 + 1.2 * pop,
                    child: Text(emoji, style: const TextStyle(fontSize: 32)),
                  ),
                ),
              ),
            ],
          );
        },
      ),
    ),
  );
  overlay.insert(entry);
}

/// Above the composer while replying: who and what you are answering, with
/// a button to stop replying.
class ReplyPreview extends StatelessWidget {
  const ReplyPreview({
    required this.author,
    required this.text,
    required this.color,
    required this.onCancel,
    super.key,
  });

  final String author;
  final String text;
  final Color color;
  final VoidCallback onCancel;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    return Container(
      key: const Key('whatsapp-reply-preview'),
      margin: const EdgeInsets.fromLTRB(6, 4, 0, 2),
      padding: const EdgeInsets.fromLTRB(10, 6, 2, 6),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(14),
        border: Border(left: BorderSide(color: color, width: 3)),
      ),
      child: Row(
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  author,
                  style: theme.textTheme.labelMedium?.copyWith(
                    color: color,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                Text(
                  text,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall?.copyWith(
                    color: colors.inkSoft,
                  ),
                ),
              ],
            ),
          ),
          IconButton(
            key: const Key('whatsapp-reply-cancel'),
            tooltip: 'Stop replying',
            visualDensity: VisualDensity.compact,
            onPressed: onCancel,
            icon: const Icon(PhosphorIconsRegular.x, size: 16),
          ),
        ],
      ),
    );
  }
}
