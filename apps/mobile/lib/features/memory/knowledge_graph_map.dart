part of 'knowledge_graph_screen.dart';

class _MapNode extends StatelessWidget {
  const _MapNode({
    required this.node,
    required this.diameter,
    required this.selected,
    required this.emphasis,
    required this.showLabel,
    required this.recent,
    required this.onTap,
    this.pulse,
  });

  final GraphNode node;
  final double diameter;
  final bool selected;

  /// 1 = in focus, lower values fade the node back.
  final double emphasis;
  final bool showLabel;

  /// First learned in the last week: gets a small spark.
  final bool recent;
  final Animation<double>? pulse;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final color = graphTypeColor(node.type, context);
    final ring = selected ? 14.0 : 8.0;
    final showIcon = diameter >= 22;
    Widget dot = Container(
      width: diameter,
      height: diameter,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: node.isYou ? null : color,
        gradient: node.isYou
            ? RadialGradient(
                center: const Alignment(-.35, -.4),
                colors: [Color.lerp(color, Colors.white, .35)!, color],
              )
            : null,
        border: Border.all(color: colors.surface, width: selected ? 3 : 2),
        boxShadow: [
          BoxShadow(
            color: color.withValues(alpha: node.isYou || selected ? .55 : .28),
            blurRadius: node.isYou || selected ? 18 : 8,
            spreadRadius: node.isYou ? 2 : 0,
          ),
        ],
      ),
      child: showIcon
          ? Icon(
              node.isYou
                  ? PhosphorIconsRegular.sparkle
                  : graphTypeIcon(node.type),
              size: diameter * .48,
              color: colors.surface,
            )
          : null,
    );
    if (recent) {
      dot = Stack(
        clipBehavior: Clip.none,
        children: [
          dot,
          Positioned(
            right: -1,
            top: -1,
            child: Container(
              key: Key('node-new-${node.name}'),
              width: math.max(7, diameter * .3),
              height: math.max(7, diameter * .3),
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: colors.warning,
                border: Border.all(color: colors.surface, width: 1.5),
              ),
            ),
          ),
        ],
      );
    }
    return AnimatedOpacity(
      duration: const Duration(milliseconds: 220),
      opacity: emphasis.clamp(0.0, 1.0),
      child: GestureDetector(
        key: Key('node-${node.name}'),
        behavior: HitTestBehavior.opaque,
        onTap: onTap,
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [
            Semantics(
              button: true,
              label: '${node.name}, ${graphTypeLabel(node.type)}',
              child: SizedBox(
                width: diameter + ring,
                height: diameter + ring,
                child: CustomPaint(
                  painter: selected
                      ? _PulsePainter(color: color, pulse: pulse)
                      : null,
                  child: Center(child: dot),
                ),
              ),
            ),
            if (showLabel)
              Container(
                margin: const EdgeInsets.only(top: 2),
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                constraints: const BoxConstraints(maxWidth: 108),
                decoration: BoxDecoration(
                  color: colors.surface.withValues(alpha: .9),
                  borderRadius: BorderRadius.circular(6),
                  border: Border.all(
                    color: selected
                        ? color
                        : colors.outline.withValues(alpha: .7),
                  ),
                ),
                child: Text(
                  node.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: node.isYou || selected ? 12 : 11,
                    fontWeight: node.isYou || selected
                        ? FontWeight.w700
                        : FontWeight.w600,
                    color: colors.ink,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// A soft ring around the selection plus ripples that expand while [pulse] runs.
class _PulsePainter extends CustomPainter {
  _PulsePainter({required this.color, this.pulse}) : super(repaint: pulse);

  final Color color;
  final Animation<double>? pulse;

  @override
  void paint(Canvas canvas, Size size) {
    final center = size.center(Offset.zero);
    final radius = size.shortestSide / 2;
    canvas.drawCircle(
      center,
      radius,
      Paint()..color = color.withValues(alpha: .18),
    );
    final value = pulse?.value ?? 1;
    if (value >= 1) return;
    for (var ripple = 0; ripple < 2; ripple++) {
      final t = ((value * 3) - ripple * .5) % 1;
      if (t < 0) continue;
      canvas.drawCircle(
        center,
        radius * (1 + t * 1.6),
        Paint()
          ..color = color.withValues(alpha: (1 - t) * .45 * (1 - value))
          ..style = PaintingStyle.stroke
          ..strokeWidth = 2,
      );
    }
  }

  @override
  bool shouldRepaint(_PulsePainter oldDelegate) =>
      oldDelegate.color != color || oldDelegate.pulse != pulse;
}

class _Satellite {
  const _Satellite({
    required this.label,
    required this.tooltip,
    required this.rect,
    required this.delay,
    required this.more,
  });

  final String label;
  final String tooltip;
  final Rect rect;
  final double delay;
  final int more;
}

class _SatelliteChip extends StatelessWidget {
  const _SatelliteChip({
    required this.satellite,
    required this.colors,
    this.onTap,
  });

  final _Satellite satellite;
  final JarvisColors colors;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    label: satellite.tooltip,
    button: onTap != null,
    child: GestureDetector(
      key: Key('satellite-${satellite.label}'),
      onTap: onTap,
      child: Center(
        child: Container(
          padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 3),
          decoration: BoxDecoration(
            color: satellite.more > 0 ? colors.accentSoft : colors.surface,
            borderRadius: BorderRadius.circular(999),
            border: Border.all(color: colors.outline),
            boxShadow: JarvisShadows.soft(colors.brightness),
          ),
          child: Text(
            satellite.label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 10.5,
              fontWeight: FontWeight.w600,
              color: satellite.more > 0 ? colors.accent : colors.inkSoft,
            ),
          ),
        ),
      ),
    ),
  );
}

/// Thin spokes from the selected node out to its detail chips.
class _SpokePainter extends CustomPainter {
  const _SpokePainter({
    required this.center,
    required this.satellites,
    required this.color,
    required this.progress,
  });

  final Offset center;
  final List<_Satellite> satellites;
  final Color color;
  final double progress;

  @override
  void paint(Canvas canvas, Size size) {
    final paint = Paint()
      ..color = color.withValues(alpha: .45)
      ..strokeWidth = 1.2
      ..strokeCap = StrokeCap.round;
    for (final satellite in satellites) {
      final end = Offset.lerp(center, satellite.rect.center, progress)!;
      canvas.drawLine(center, end, paint);
      canvas.drawCircle(end, 2.2, Paint()..color = color.withValues(alpha: .7));
    }
  }

  @override
  bool shouldRepaint(_SpokePainter oldDelegate) =>
      oldDelegate.center != center ||
      oldDelegate.progress != progress ||
      oldDelegate.color != color ||
      oldDelegate.satellites != satellites;
}

class _Halo {
  const _Halo({
    required this.points,
    required this.color,
    required this.strength,
  });

  final List<Offset> points;
  final Color color;
  final double strength;
}

class _MapEdge {
  const _MapEdge({
    required this.from,
    required this.to,
    required this.fromColor,
    required this.toColor,
    required this.alpha,
    required this.width,
    required this.prominent,
    required this.dashed,
    required this.lane,
    required this.lanes,
    required this.bow,
    required this.flowsOut,
    this.label,
  });

  final Offset from;
  final Offset to;
  final Color fromColor;
  final Color toColor;
  final double alpha;
  final double width;
  final bool prominent;

  /// Low-confidence facts are drawn dashed.
  final bool dashed;
  final int lane;
  final int lanes;

  /// Which side a single edge curves to (+1 / -1), so the map looks organic rather than a wire frame.
  final int bow;

  /// Whether particles travel away from the selection (an outgoing fact) or toward it.
  final bool flowsOut;
  final String? label;
}

class _GraphPainter extends CustomPainter {
  _GraphPainter({
    required this.edges,
    required this.halos,
    required this.colors,
    required this.flow,
  }) : super(repaint: flow);

  final List<_MapEdge> edges;
  final List<_Halo> halos;
  final JarvisColors colors;
  final Animation<double> flow;

  @override
  void paint(Canvas canvas, Size size) {
    final dot = Paint()..color = colors.outline.withValues(alpha: .7);
    for (var x = 22.0; x < size.width; x += 28) {
      for (var y = 22.0; y < size.height; y += 28) {
        canvas.drawCircle(Offset(x, y), 1.1, dot);
      }
    }
    _paintHalos(canvas, size);
    final flowing = flow.value < 1;
    final labelRects = <Rect>[];
    for (final edge in edges) {
      final control = _control(edge);
      final path = Path()
        ..moveTo(edge.from.dx, edge.from.dy)
        ..quadraticBezierTo(control.dx, control.dy, edge.to.dx, edge.to.dy);
      final paint = Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = edge.width
        ..strokeCap = StrokeCap.round
        ..shader = ui.Gradient.linear(edge.from, edge.to, [
          edge.fromColor.withValues(alpha: edge.alpha),
          edge.toColor.withValues(alpha: edge.alpha),
        ]);
      canvas.drawPath(edge.dashed ? _dash(path) : path, paint);
      if (!edge.prominent) continue;
      _arrow(canvas, edge, control);
      if (flowing) _particles(canvas, edge, control);
      final label = edge.label;
      if (label == null || label.isEmpty) continue;
      final painter = TextPainter(
        text: TextSpan(
          text: label,
          style: TextStyle(
            fontFamily: 'Geist',
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: colors.inkSoft,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: 120);
      // Away from the selection, so labels of a busy hub don't stack up around it.
      final mid = _point(
        edge.from,
        control,
        edge.to,
        edge.flowsOut ? .62 : .38,
      );
      final origin = mid - Offset(painter.width / 2, painter.height / 2);
      final rect = Rect.fromLTWH(
        origin.dx - 5,
        origin.dy - 2,
        painter.width + 10,
        painter.height + 4,
      );
      if (labelRects.any(rect.inflate(3).overlaps)) continue;
      labelRects.add(rect);
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()..color = colors.surface.withValues(alpha: .94),
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()
          ..color = edge.toColor.withValues(alpha: .5)
          ..style = PaintingStyle.stroke,
      );
      painter.paint(canvas, origin);
    }
  }

  void _paintHalos(Canvas canvas, Size size) {
    for (final halo in halos) {
      // Draw opaque blobs into a translucent layer so overlapping circles merge into one even cloud.
      canvas.saveLayer(
        Offset.zero & size,
        Paint()
          ..color = Color.fromRGBO(
            0,
            0,
            0,
            (colors.isDark ? .14 : .09) * halo.strength,
          ),
      );
      final paint = Paint()
        ..color = halo.color
        ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 28);
      for (final point in halo.points) {
        canvas.drawCircle(point, 78, paint);
      }
      canvas.restore();
    }
  }

  void _particles(Canvas canvas, _MapEdge edge, Offset control) {
    final fade = 1 - Curves.easeIn.transform(flow.value);
    final paint = Paint()..color = edge.toColor.withValues(alpha: .9 * fade);
    for (var index = 0; index < 3; index++) {
      var t = (flow.value * 2.4 + index / 3) % 1;
      if (!edge.flowsOut) t = 1 - t;
      canvas.drawCircle(
        _point(edge.from, control, edge.to, t),
        edge.width + 1.2,
        paint,
      );
    }
  }

  Path _dash(Path source) {
    final dashed = Path();
    for (final metric in source.computeMetrics()) {
      var distance = 0.0;
      while (distance < metric.length) {
        dashed.addPath(
          metric.extractPath(distance, math.min(distance + 7, metric.length)),
          Offset.zero,
        );
        distance += 12;
      }
    }
    return dashed;
  }

  Offset _point(Offset a, Offset control, Offset b, double t) {
    final u = 1 - t;
    return a * (u * u) + control * (2 * u * t) + b * (t * t);
  }

  Offset _control(_MapEdge edge) {
    final mid = Offset.lerp(edge.from, edge.to, .5)!;
    final delta = edge.to - edge.from;
    final length = math.max(delta.distance, 1).toDouble();
    final normal = Offset(-delta.dy / length, delta.dx / length);
    if (edge.lanes <= 1) return mid + normal * (length * .08 * edge.bow);
    final shift = (edge.lane - (edge.lanes - 1) / 2) * 42;
    return mid + normal * shift;
  }

  /// Direction marker part-way along the curve: nodes are drawn at screen size on top of this world-space layer, so
  /// an arrowhead at the end would hide under the node at most zoom levels.
  void _arrow(Canvas canvas, _MapEdge edge, Offset control) {
    final t = edge.flowsOut ? .82 : .18;
    final tip = _point(edge.from, control, edge.to, t);
    final tangent =
        (control - edge.from) * (2 * (1 - t)) + (edge.to - control) * (2 * t);
    final unit = tangent / math.max(tangent.distance, 1).toDouble();
    final color = Color.lerp(edge.fromColor, edge.toColor, t)!;
    final normal = Offset(-unit.dy, unit.dx);
    final base = tip - unit * 9;
    final path = Path()
      ..moveTo(tip.dx, tip.dy)
      ..lineTo(base.dx + normal.dx * 4.5, base.dy + normal.dy * 4.5)
      ..lineTo(base.dx - normal.dx * 4.5, base.dy - normal.dy * 4.5)
      ..close();
    canvas.drawPath(path, Paint()..color = color);
  }

  @override
  bool shouldRepaint(_GraphPainter oldDelegate) =>
      oldDelegate.edges != edges ||
      oldDelegate.halos != halos ||
      oldDelegate.colors != colors ||
      oldDelegate.flow != flow;
}
