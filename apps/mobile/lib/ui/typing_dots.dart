import 'package:flutter/material.dart';

import '../theme.dart';
import 'motion.dart';

/// Three dots that wave while Jarvis is writing. With Reduce Motion they hold
/// still, so the label beside them carries the meaning.
class TypingDots extends StatefulWidget {
  const TypingDots({this.color, this.size = 5, super.key});

  final Color? color;
  final double size;

  /// One full wave.
  static const period = Duration(milliseconds: 1100);

  @override
  State<TypingDots> createState() => _TypingDotsState();
}

class _TypingDotsState extends State<TypingDots>
    with SingleTickerProviderStateMixin {
  late final _wave = AnimationController(
    vsync: this,
    duration: TypingDots.period,
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (JarvisMotion.reduced(context)) {
      _wave.stop();
    } else if (!_wave.isAnimating) {
      _wave.repeat();
    }
  }

  @override
  void dispose() {
    _wave.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final color = widget.color ?? JarvisColors.of(context).accent;
    return ExcludeSemantics(
      child: AnimatedBuilder(
        animation: _wave,
        builder: (context, _) => Row(
          mainAxisSize: MainAxisSize.min,
          children: [
            for (var i = 0; i < 3; i++) ...[
              if (i > 0) SizedBox(width: widget.size * .6),
              _dot(color, i),
            ],
          ],
        ),
      ),
    );
  }

  Widget _dot(Color color, int index) {
    // Each dot lifts a little later than the one before it.
    final phase = (_wave.value - index * .14) % 1;
    final lift = phase < .3 ? Curves.easeOutCubic.transform(phase / .3) : 0.0;
    final fall = phase >= .3 && phase < .6
        ? 1 - Curves.easeInCubic.transform((phase - .3) / .3)
        : 0.0;
    final t = _wave.isAnimating ? lift + fall : .5;
    return Transform.translate(
      offset: Offset(0, -widget.size * .6 * t),
      child: Container(
        width: widget.size,
        height: widget.size,
        decoration: BoxDecoration(
          color: color.withValues(alpha: .35 + .65 * t),
          shape: BoxShape.circle,
        ),
      ),
    );
  }
}
