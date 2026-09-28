part of 'knowledge_graph_screen.dart';

class _MapNode extends StatelessWidget {
  const _MapNode({
    required this.node,
    required this.selected,
    required this.dimmed,
    required this.showLabel,
    required this.onTap,
  });

  final GraphNode node;
  final bool selected;
  final bool dimmed;
  final bool showLabel;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final color = graphTypeColor(node.type);
    final diameter = selected ? 26.0 : (node.isYou ? 20.0 : 16.0);
    return Opacity(
      opacity: dimmed ? .32 : 1,
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
              child: Container(
                width: diameter + (selected ? 10 : 6),
                height: diameter + (selected ? 10 : 6),
                alignment: Alignment.center,
                decoration: selected
                    ? BoxDecoration(
                        shape: BoxShape.circle,
                        color: color.withValues(alpha: .18),
                      )
                    : null,
                child: Container(
                  width: diameter,
                  height: diameter,
                  decoration: BoxDecoration(
                    color: color,
                    shape: BoxShape.circle,
                    border: Border.all(
                      color: JarvisColors.surface,
                      width: selected ? 3 : 2,
                    ),
                    boxShadow: JarvisShadows.soft,
                  ),
                ),
              ),
            ),
            if (showLabel)
              Container(
                margin: const EdgeInsets.only(top: 2),
                padding: const EdgeInsets.symmetric(horizontal: 6, vertical: 1),
                constraints: const BoxConstraints(maxWidth: 108),
                decoration: BoxDecoration(
                  color: JarvisColors.surface.withValues(alpha: .94),
                  borderRadius: BorderRadius.circular(6),
                  border: Border.all(
                    color: selected ? color : JarvisColors.outline,
                  ),
                ),
                child: Text(
                  node.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: node.isYou || selected ? 12 : 11,
                    fontWeight: FontWeight.w600,
                    color: JarvisColors.ink,
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

class _MapEdge {
  const _MapEdge({
    required this.from,
    required this.to,
    required this.color,
    required this.prominent,
    required this.lane,
    required this.lanes,
    this.label,
  });

  final Offset from;
  final Offset to;
  final Color color;
  final bool prominent;
  final int lane;
  final int lanes;
  final String? label;
}

class _GraphPainter extends CustomPainter {
  const _GraphPainter(this.edges);

  final List<_MapEdge> edges;

  @override
  void paint(Canvas canvas, Size size) {
    final dot = Paint()..color = const Color(0xffe3dfd6);
    for (var x = 22.0; x < size.width; x += 28) {
      for (var y = 22.0; y < size.height; y += 28) {
        canvas.drawCircle(Offset(x, y), 1.15, dot);
      }
    }
    for (final edge in edges) {
      final control = _control(edge);
      final path = Path()
        ..moveTo(edge.from.dx, edge.from.dy)
        ..quadraticBezierTo(control.dx, control.dy, edge.to.dx, edge.to.dy);
      canvas.drawPath(
        path,
        Paint()
          ..color = edge.color
          ..style = PaintingStyle.stroke
          ..strokeWidth = edge.prominent ? 2.2 : 1.25
          ..strokeCap = StrokeCap.round,
      );
      if (!edge.prominent) continue;
      _arrow(canvas, control, edge.to, edge.color);
      final label = edge.label;
      if (label == null || label.isEmpty) continue;
      final painter = TextPainter(
        text: TextSpan(
          text: label,
          style: const TextStyle(
            fontSize: 11,
            fontWeight: FontWeight.w600,
            color: JarvisColors.inkSoft,
          ),
        ),
        textDirection: TextDirection.ltr,
      )..layout(maxWidth: 120);
      final origin = control - Offset(painter.width / 2, painter.height / 2);
      final rect = Rect.fromLTWH(
        origin.dx - 5,
        origin.dy - 2,
        painter.width + 10,
        painter.height + 4,
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()..color = JarvisColors.surface.withValues(alpha: .94),
      );
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()
          ..color = JarvisColors.outline
          ..style = PaintingStyle.stroke,
      );
      painter.paint(canvas, origin);
    }
  }

  Offset _control(_MapEdge edge) {
    final mid = Offset.lerp(edge.from, edge.to, .5)!;
    if (edge.lanes <= 1) return mid;
    final delta = edge.to - edge.from;
    final length = math.max(delta.distance, 1).toDouble();
    final normal = Offset(-delta.dy / length, delta.dx / length);
    final shift = (edge.lane - (edge.lanes - 1) / 2) * 42;
    return mid + normal * shift;
  }

  void _arrow(Canvas canvas, Offset control, Offset to, Color color) {
    final direction = to - control;
    final length = math.max(direction.distance, 1).toDouble();
    final unit = direction / length;
    final tip = to - unit * 16;
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
  bool shouldRepaint(_GraphPainter oldDelegate) => oldDelegate.edges != edges;
}
