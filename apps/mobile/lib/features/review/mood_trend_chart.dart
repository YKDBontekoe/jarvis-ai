import 'package:flutter/material.dart';

import '../../theme.dart';
import 'weekly_review_format.dart';

/// One week on the trend chart; averages are null for weeks without entries.
typedef TrendPoint = ({DateTime week, double? mood, double? energy});

/// Mood (and a quieter energy line) per week on a fixed 1–5 scale, so weeks
/// compare honestly. Gaps stay gaps instead of being bridged.
class MoodTrendChart extends StatelessWidget {
  const MoodTrendChart({required this.points, this.height = 168, super.key});

  final List<TrendPoint> points;
  final double height;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      label: trendSemantics([
        for (final point in points) (week: point.week, mood: point.mood),
      ]),
      child: ExcludeSemantics(
        child: SizedBox(
          height: height,
          child: CustomPaint(
            painter: _TrendPainter(
              points,
              colors,
              DefaultTextStyle.of(context).style,
            ),
            child: const SizedBox.expand(),
          ),
        ),
      ),
    );
  }
}

class _TrendPainter extends CustomPainter {
  _TrendPainter(this.points, this.colors, this.baseText);

  final List<TrendPoint> points;
  final JarvisColors colors;
  final TextStyle baseText;

  static const _left = 22.0;
  static const _bottom = 22.0;
  static const _top = 8.0;

  @override
  void paint(Canvas canvas, Size size) {
    if (points.isEmpty) return;
    final plot = Rect.fromLTRB(
      _left,
      _top,
      size.width - 8,
      size.height - _bottom,
    );
    final grid = Paint()
      ..color = colors.outline
      ..strokeWidth = 1;
    for (final value in const [1, 3, 5]) {
      final y = _y(value.toDouble(), plot);
      canvas.drawLine(Offset(plot.left, y), Offset(plot.right, y), grid);
      _label(canvas, '$value', Offset(0, y - 7), width: _left - 6, end: true);
    }

    _line(canvas, plot, (point) => point.energy, colors.sky, 1.6, dots: false);
    _area(canvas, plot);
    _line(canvas, plot, (point) => point.mood, colors.accent, 2.6, dots: true);

    final labelIndexes = <int>{0, points.length ~/ 2, points.length - 1};
    for (final index in labelIndexes) {
      final x = _x(index, plot);
      final text = index == points.length - 1
          ? 'This week'
          : shortWeekLabel(points[index].week);
      final left = (x - 40).clamp(0.0, size.width - 80);
      _label(canvas, text, Offset(left, plot.bottom + 6), width: 80);
    }
  }

  double _x(int index, Rect plot) => points.length == 1
      ? plot.center.dx
      : plot.left + plot.width * index / (points.length - 1);

  double _y(double value, Rect plot) =>
      plot.bottom - (value.clamp(1, 5) - 1) / 4 * plot.height;

  List<List<Offset>> _segments(Rect plot, double? Function(TrendPoint) pick) {
    final segments = <List<Offset>>[];
    var current = <Offset>[];
    for (final (index, point) in points.indexed) {
      final value = pick(point);
      if (value == null) {
        if (current.isNotEmpty) segments.add(current);
        current = [];
        continue;
      }
      current.add(Offset(_x(index, plot), _y(value, plot)));
    }
    if (current.isNotEmpty) segments.add(current);
    return segments;
  }

  void _line(
    Canvas canvas,
    Rect plot,
    double? Function(TrendPoint) pick,
    Color color,
    double width, {
    required bool dots,
  }) {
    final stroke = Paint()
      ..color = color
      ..strokeWidth = width
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeJoin = StrokeJoin.round;
    final fill = Paint()..color = color;
    final ring = Paint()..color = colors.surface;
    final segments = _segments(plot, pick);
    for (final segment in segments) {
      if (segment.length > 1) {
        final path = Path()..moveTo(segment.first.dx, segment.first.dy);
        for (final offset in segment.skip(1)) {
          path.lineTo(offset.dx, offset.dy);
        }
        canvas.drawPath(path, stroke);
      }
      if (dots || segment.length == 1) {
        for (final offset in segment) {
          canvas.drawCircle(offset, width + 2.2, ring);
          canvas.drawCircle(offset, width + .8, fill);
        }
      }
    }
    final last = pick(points.last);
    if (dots && last != null) {
      final offset = Offset(_x(points.length - 1, plot), _y(last, plot));
      canvas.drawCircle(
        offset,
        8,
        Paint()..color = color.withValues(alpha: .18),
      );
      canvas.drawCircle(offset, width + 1.6, fill);
    }
  }

  void _area(Canvas canvas, Rect plot) {
    final shader = LinearGradient(
      begin: Alignment.topCenter,
      end: Alignment.bottomCenter,
      colors: [
        colors.accent.withValues(alpha: .16),
        colors.accent.withValues(alpha: 0),
      ],
    ).createShader(plot);
    for (final segment in _segments(plot, (point) => point.mood)) {
      if (segment.length < 2) continue;
      final path = Path()..moveTo(segment.first.dx, plot.bottom);
      for (final offset in segment) {
        path.lineTo(offset.dx, offset.dy);
      }
      path
        ..lineTo(segment.last.dx, plot.bottom)
        ..close();
      canvas.drawPath(path, Paint()..shader = shader);
    }
  }

  void _label(
    Canvas canvas,
    String text,
    Offset at, {
    required double width,
    bool end = false,
  }) {
    final painter = TextPainter(
      text: TextSpan(
        text: text,
        style: baseText.copyWith(fontSize: 11, color: colors.muted),
      ),
      textAlign: end ? TextAlign.right : TextAlign.center,
      textDirection: TextDirection.ltr,
      maxLines: 1,
    )..layout(minWidth: width, maxWidth: width);
    painter.paint(canvas, at);
  }

  @override
  bool shouldRepaint(_TrendPainter oldDelegate) =>
      oldDelegate.points != points || oldDelegate.colors != colors;
}
