import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';

import 'jarvis_ui.dart';

/// Flies a copy of the orb from [from] (global coordinates) to wherever the
/// widget holding [to] is laid out, along a gentle arc, then removes itself.
/// The destination is re-read every frame, so it can still be moving (for
/// example while its page zooms in). Completes immediately with Reduce Motion
/// or when either end cannot be found.
Future<void> flyOrb({
  required BuildContext context,
  required Rect from,
  required GlobalKey to,
  Duration duration = const Duration(milliseconds: 620),
}) async {
  if (JarvisMotion.reduced(context)) return;
  final overlay = Overlay.maybeOf(context, rootOverlay: true);
  if (overlay == null) return;
  final overlayBox = overlay.context.findRenderObject();
  if (overlayBox is! RenderBox) return;
  final finished = Completer<void>();
  final entry = OverlayEntry(
    builder: (_) => _OrbFlight(
      from: from.shift(-overlayBox.localToGlobal(Offset.zero)),
      to: to,
      overlayBox: overlayBox,
      duration: duration,
      onDone: () {
        if (!finished.isCompleted) finished.complete();
      },
    ),
  );
  overlay.insert(entry);
  try {
    await finished.future;
  } finally {
    if (entry.mounted) entry.remove();
  }
}

class _OrbFlight extends StatefulWidget {
  const _OrbFlight({
    required this.from,
    required this.to,
    required this.overlayBox,
    required this.duration,
    required this.onDone,
  });

  final Rect from;
  final GlobalKey to;
  final RenderBox overlayBox;
  final Duration duration;
  final VoidCallback onDone;

  @override
  State<_OrbFlight> createState() => _OrbFlightState();
}

class _OrbFlightState extends State<_OrbFlight>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: widget.duration,
  );
  Rect? _lastTarget;

  @override
  void initState() {
    super.initState();
    _controller.forward().whenCompleteOrCancel(widget.onDone);
  }

  @override
  void dispose() {
    _controller.dispose();
    widget.onDone();
    super.dispose();
  }

  Rect? _target() {
    final box = widget.to.currentContext?.findRenderObject();
    if (box is! RenderBox || !box.attached || !box.hasSize) return _lastTarget;
    final topLeft = box.localToGlobal(Offset.zero, ancestor: widget.overlayBox);
    return _lastTarget = topLeft & box.size;
  }

  @override
  Widget build(BuildContext context) => IgnorePointer(
    child: AnimatedBuilder(
      animation: _controller,
      builder: (context, _) {
        final target = _target() ?? widget.from;
        final t = Curves.easeInOutCubicEmphasized.transform(_controller.value);
        final from = widget.from.center;
        final to = target.center;
        // A quadratic arc that swings out to the side on the way up.
        final control = Offset(
          math.min(from.dx, to.dx) - (from - to).distance * .18,
          (from.dy + to.dy) / 2,
        );
        final position = Offset(
          _quad(from.dx, control.dx, to.dx, t),
          _quad(from.dy, control.dy, to.dy, t),
        );
        final size =
            widget.from.shortestSide +
            (target.shortestSide - widget.from.shortestSide) * t;
        // A little swell mid-flight, like it is being thrown.
        final swell = 1 + .35 * math.sin(t * math.pi);
        // Hand over to the real orb in the last stretch.
        final opacity = 1 - const Interval(.85, 1).transform(_controller.value);
        return Stack(
          children: [
            Positioned(
              left: position.dx - size * swell / 2,
              top: position.dy - size * swell / 2,
              child: Opacity(
                opacity: opacity,
                child: JarvisOrb(size: size * swell, glow: false),
              ),
            ),
          ],
        );
      },
    ),
  );

  static double _quad(double a, double b, double c, double t) =>
      (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
}
