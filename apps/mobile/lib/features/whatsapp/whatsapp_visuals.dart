import 'dart:math' as math;

import 'package:flutter/material.dart';

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
