import 'dart:math' as math;

import 'package:flutter/material.dart';

/// The four navigation icons: one stroke weight, rounded joins, drawn on a
/// 24 unit grid. The current tab is filled softly and drawn in the accent.
enum TabGlyph { home, chats, everything, you }

/// Draws a [TabGlyph] at the size it is given.
class TabIcon extends StatelessWidget {
  const TabIcon({
    required this.glyph,
    required this.color,
    this.fill,
    this.size = 25,
    super.key,
  });

  final TabGlyph glyph;
  final Color color;

  /// Soft fill inside the stroke; null leaves the icon open.
  final Color? fill;
  final double size;

  @override
  Widget build(BuildContext context) => CustomPaint(
    size: Size.square(size),
    painter: _TabIconPainter(glyph, color, fill),
  );
}

class _TabIconPainter extends CustomPainter {
  _TabIconPainter(this.glyph, this.color, this.fill);

  final TabGlyph glyph;
  final Color color;
  final Color? fill;

  @override
  void paint(Canvas canvas, Size size) {
    canvas.scale(size.width / 24, size.height / 24);
    final stroke = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1.6
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round
      ..color = color;
    final paint = fill == null
        ? null
        : (Paint()
            ..style = PaintingStyle.fill
            ..color = fill!);
    void draw(Path path) {
      if (paint != null) canvas.drawPath(path, paint);
      canvas.drawPath(path, stroke);
    }

    switch (glyph) {
      case TabGlyph.home:
        draw(parseSvgPath(_home));
      case TabGlyph.chats:
        draw(parseSvgPath(_chatBack));
        draw(parseSvgPath(_chatFront));
      case TabGlyph.everything:
        for (final (x, y, r) in const [
          (4.0, 4.0, 2.0),
          (13.4, 4.0, 2.0),
          (4.0, 13.4, 2.0),
          (13.4, 13.4, 3.3),
        ]) {
          draw(
            Path()..addRRect(
              RRect.fromRectAndRadius(
                Rect.fromLTWH(x, y, 6.6, 6.6),
                Radius.circular(r),
              ),
            ),
          );
        }
      case TabGlyph.you:
        draw(
          Path()..addOval(
            Rect.fromCircle(center: const Offset(12, 8.8), radius: 3.6),
          ),
        );
        canvas.drawPath(parseSvgPath(_shoulders), stroke);
    }
  }

  @override
  bool shouldRepaint(_TabIconPainter old) =>
      old.glyph != glyph || old.color != color || old.fill != fill;
}

const _home =
    'M4.5 10.2 12 4.2l7.5 6V19a1.3 1.3 0 0 1-1.3 1.3H15v-5.2a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v5.2H5.8A1.3 1.3 0 0 1 4.5 19z';
const _chatBack =
    'M15.5 8.5V6.3A2.3 2.3 0 0 0 13.2 4H5.8a2.3 2.3 0 0 0-2.3 2.3v9.2l2.8-2.4h1.4';
const _chatFront =
    'M9.5 11.3A2.3 2.3 0 0 1 11.8 9h6.4a2.3 2.3 0 0 1 2.3 2.3v8.9l-2.8-2.4h-5.9a2.3 2.3 0 0 1-2.3-2.3z';
const _shoulders = 'M5.2 19.6c1.1-3.3 3.7-5.1 6.8-5.1s5.7 1.8 6.8 5.1';

/// Parses the subset of SVG path data the icons use: `M L H V C S A Z` in both
/// cases. Arcs follow the SVG endpoint form, which [Path.arcToPoint] shares.
Path parseSvgPath(String data) {
  final path = Path();
  final tokens = RegExp(
    r'([A-Za-z])|(-?\d*\.?\d+(?:e-?\d+)?)',
  ).allMatches(data).map((m) => m[0]!).toList();
  var i = 0;
  var x = 0.0, y = 0.0, startX = 0.0, startY = 0.0;
  double? lastCx, lastCy;
  String? command;
  double next() => double.parse(tokens[i++]);
  bool isNumber() => i < tokens.length && double.tryParse(tokens[i]) != null;

  while (i < tokens.length) {
    if (!isNumber()) command = tokens[i++];
    final relative = command!.toLowerCase() == command;
    final op = command.toLowerCase();
    if (op == 'z') {
      path.close();
      x = startX;
      y = startY;
      lastCx = lastCy = null;
      continue;
    }
    switch (op) {
      case 'm':
        final nx = next() + (relative ? x : 0);
        final ny = next() + (relative ? y : 0);
        path.moveTo(nx, ny);
        x = startX = nx;
        y = startY = ny;
        // Further pairs after a move are lines.
        command = relative ? 'l' : 'L';
        lastCx = lastCy = null;
      case 'l':
        x = next() + (relative ? x : 0);
        y = next() + (relative ? y : 0);
        path.lineTo(x, y);
        lastCx = lastCy = null;
      case 'h':
        x = next() + (relative ? x : 0);
        path.lineTo(x, y);
        lastCx = lastCy = null;
      case 'v':
        y = next() + (relative ? y : 0);
        path.lineTo(x, y);
        lastCx = lastCy = null;
      case 'c':
        final ox = relative ? x : 0.0, oy = relative ? y : 0.0;
        final x1 = next() + ox, y1 = next() + oy;
        final x2 = next() + ox, y2 = next() + oy;
        final ex = next() + ox, ey = next() + oy;
        path.cubicTo(x1, y1, x2, y2, ex, ey);
        lastCx = x2;
        lastCy = y2;
        x = ex;
        y = ey;
      case 's':
        final ox = relative ? x : 0.0, oy = relative ? y : 0.0;
        final x1 = lastCx == null ? x : 2 * x - lastCx;
        final y1 = lastCy == null ? y : 2 * y - lastCy;
        final x2 = next() + ox, y2 = next() + oy;
        final ex = next() + ox, ey = next() + oy;
        path.cubicTo(x1, y1, x2, y2, ex, ey);
        lastCx = x2;
        lastCy = y2;
        x = ex;
        y = ey;
      case 'a':
        final rx = next(), ry = next();
        final rotation = next();
        final large = next() != 0;
        final sweep = next() != 0;
        final ex = next() + (relative ? x : 0);
        final ey = next() + (relative ? y : 0);
        path.arcToPoint(
          Offset(ex, ey),
          radius: Radius.elliptical(rx, ry),
          rotation: rotation * math.pi / 180,
          largeArc: large,
          clockwise: sweep,
        );
        x = ex;
        y = ey;
        lastCx = lastCy = null;
      default:
        throw FormatException('Unsupported path command $command');
    }
  }
  return path;
}
