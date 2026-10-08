import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../theme.dart';
import 'jarvis_ui.dart' show JarvisOrb;
import 'motion.dart';

/// Expressive effects built on [JarvisMotion]. Every one of them plays once
/// (or only while touched) and then settles, so screens stay idle afterwards,
/// and each one turns itself off when the device asks to reduce motion.

/// A soft spring: overshoots a touch and settles. For things that pop into
/// place, such as badges, checks and selected tabs.
class SpringCurve extends Curve {
  const SpringCurve({this.damping = 7, this.frequency = 2.2});

  /// Higher settles faster with less wobble.
  final double damping;

  /// Oscillations over the run of the animation.
  final double frequency;

  @override
  double transformInternal(double t) =>
      1 -
      math.exp(-damping * t) * math.cos(frequency * 2 * math.pi * t) * (1 - t);
}

/// Curves used by the effects below.
abstract final class JarvisSprings {
  /// Gentle settle, for moving surfaces such as a sliding tab indicator.
  static const soft = SpringCurve(damping: 8, frequency: 1.4);

  /// A visible bounce, for small things that pop in.
  static const pop = SpringCurve(damping: 6, frequency: 2.4);
}

/// Grows [child] from [from] with a spring the first time it is built, so a
/// badge or a dot lands with a little life. Starts in place when reduced.
class PopIn extends StatefulWidget {
  const PopIn({
    required this.child,
    this.delay = Duration.zero,
    this.from = .4,
    this.duration = const Duration(milliseconds: 520),
    this.alignment = Alignment.center,
    super.key,
  });

  final Widget child;
  final Duration delay;
  final double from;
  final Duration duration;
  final Alignment alignment;

  @override
  State<PopIn> createState() => _PopInState();
}

class _PopInState extends State<PopIn> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: widget.delay + widget.duration,
  );
  late final double _start =
      widget.delay.inMicroseconds /
      (widget.delay + widget.duration).inMicroseconds;
  late final Animation<double> _scale = CurvedAnimation(
    parent: _controller,
    curve: Interval(_start, 1, curve: JarvisSprings.pop),
  );
  late final Animation<double> _fade = CurvedAnimation(
    parent: _controller,
    curve: Interval(_start, math.min(1, _start + .3), curve: Curves.easeOut),
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (JarvisMotion.reduced(context)) {
      _controller.value = 1;
    } else if (_controller.isDismissed) {
      _controller.forward();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (JarvisMotion.reduced(context)) return widget.child;
    return FadeTransition(
      opacity: _fade,
      child: ScaleTransition(
        alignment: widget.alignment,
        scale: Tween(begin: widget.from, end: 1.0).animate(_scale),
        child: widget.child,
      ),
    );
  }
}

/// Brings content in out of focus: it fades, rises and sharpens from a blur.
/// For hero moments (a greeting, a headline), not long lists.
class BlurIn extends StatefulWidget {
  const BlurIn({
    required this.child,
    this.delay = Duration.zero,
    this.duration = const Duration(milliseconds: 620),
    this.blur = 10,
    this.offset = 12,
    super.key,
  });

  final Widget child;
  final Duration delay;
  final Duration duration;
  final double blur;
  final double offset;

  @override
  State<BlurIn> createState() => _BlurInState();
}

class _BlurInState extends State<BlurIn> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: widget.delay + widget.duration,
  );
  late final Animation<double> _t = CurvedAnimation(
    parent: _controller,
    curve: Interval(
      widget.delay.inMicroseconds /
          (widget.delay + widget.duration).inMicroseconds,
      1,
      curve: Curves.easeOutQuart,
    ),
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (JarvisMotion.reduced(context)) {
      _controller.value = 1;
    } else if (_controller.isDismissed) {
      _controller.forward();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (JarvisMotion.reduced(context)) return widget.child;
    return AnimatedBuilder(
      animation: _t,
      child: widget.child,
      builder: (context, child) {
        final t = _t.value;
        final sigma = widget.blur * (1 - t);
        Widget result = Opacity(
          opacity: t.clamp(0, 1),
          child: Transform.translate(
            offset: Offset(0, widget.offset * (1 - t)),
            child: child,
          ),
        );
        if (sigma > .05) {
          result = ImageFiltered(
            imageFilter: ui.ImageFilter.blur(sigmaX: sigma, sigmaY: sigma),
            child: result,
          );
        }
        return result;
      },
    );
  }
}

/// Text that counts to its number instead of jumping: "12" rolls up from
/// the last value it showed. A number may carry a prefix and suffix, decimals
/// and thousands separators ("€1,204.50", "82%", "3 tasks"); the rest of the
/// text stays put. With [countUp] it also counts up from zero the first time
/// it is shown. Text with no single number in it is shown as is.
class RollingNumber extends StatelessWidget {
  const RollingNumber(
    this.text, {
    this.style,
    this.maxLines,
    this.overflow,
    this.textAlign,
    this.countUp = false,
    super.key,
  });

  final String text;
  final TextStyle? style;
  final int? maxLines;
  final TextOverflow? overflow;
  final TextAlign? textAlign;
  final bool countUp;

  static final _number = RegExp(
    r'^([^\d-]*?)(-?)(\d{1,3}(?:,\d{3})+|\d+)(?:\.(\d+))?([^\d]*)$',
  );

  /// The number in [text] and what surrounds it, or null when there is not
  /// exactly one.
  static RollingParts? parse(String text) {
    final match = _number.firstMatch(text.trim());
    if (match == null) return null;
    final whole = match.group(3)!;
    final fraction = match.group(4);
    final value = double.tryParse(
      '${whole.replaceAll(',', '')}${fraction == null ? '' : '.$fraction'}',
    );
    if (value == null) return null;
    return RollingParts(
      prefix: match.group(1)!,
      value: match.group(2) == '-' ? -value : value,
      decimals: fraction?.length ?? 0,
      grouped: whole.contains(','),
      suffix: match.group(5)!,
    );
  }

  @override
  Widget build(BuildContext context) {
    final parts = parse(text);
    Text plain(String value) => Text(
      value,
      style: style,
      maxLines: maxLines,
      overflow: overflow,
      textAlign: textAlign,
    );
    if (parts == null || JarvisMotion.reduced(context)) return plain(text);
    return Semantics(
      label: text,
      excludeSemantics: true,
      child: TweenAnimationBuilder<double>(
        tween: Tween(begin: countUp ? 0 : null, end: parts.value),
        duration: const Duration(milliseconds: 900),
        curve: Curves.easeOutExpo,
        builder: (context, current, _) => plain(parts.format(current)),
      ),
    );
  }
}

/// A number found by [RollingNumber.parse], and how to write it back.
class RollingParts {
  const RollingParts({
    required this.prefix,
    required this.value,
    required this.decimals,
    required this.grouped,
    required this.suffix,
  });

  final String prefix;
  final double value;
  final int decimals;
  final bool grouped;
  final String suffix;

  String format(double current) {
    final fixed = current.abs().toStringAsFixed(decimals);
    final dot = fixed.indexOf('.');
    var whole = dot < 0 ? fixed : fixed.substring(0, dot);
    final fraction = dot < 0 ? '' : fixed.substring(dot);
    if (grouped) {
      final out = StringBuffer();
      for (var i = 0; i < whole.length; i++) {
        if (i > 0 && (whole.length - i) % 3 == 0) out.write(',');
        out.write(whole[i]);
      }
      whole = out.toString();
    }
    final sign = current < 0 && fixed.contains(RegExp('[1-9]')) ? '-' : '';
    return '$prefix$sign$whole$fraction$suffix';
  }
}

/// A band of light that sweeps once across [child] when it first appears
/// (and again whenever [trigger] changes), like light catching glass.
class Sheen extends StatefulWidget {
  const Sheen({
    required this.child,
    this.trigger,
    this.delay = const Duration(milliseconds: 250),
    this.duration = const Duration(milliseconds: 1100),
    this.borderRadius = BorderRadius.zero,
    this.strength = .35,
    super.key,
  });

  final Widget child;
  final Object? trigger;
  final Duration delay;
  final Duration duration;
  final BorderRadius borderRadius;
  final double strength;

  @override
  State<Sheen> createState() => _SheenState();
}

class _SheenState extends State<Sheen> with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: widget.delay + widget.duration,
  );
  late final Animation<double> _sweep = CurvedAnimation(
    parent: _controller,
    curve: Interval(
      widget.delay.inMicroseconds /
          (widget.delay + widget.duration).inMicroseconds,
      1,
      curve: Curves.easeInOutCubic,
    ),
  );

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    if (JarvisMotion.reduced(context)) {
      _controller.value = 1;
    } else if (_controller.isDismissed) {
      _controller.forward();
    }
  }

  @override
  void didUpdateWidget(Sheen oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.trigger != widget.trigger && !JarvisMotion.reduced(context)) {
      _controller.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Stack(
    children: [
      widget.child,
      Positioned.fill(
        child: IgnorePointer(
          child: ClipRRect(
            borderRadius: widget.borderRadius,
            child: AnimatedBuilder(
              animation: _sweep,
              builder: (context, _) {
                final t = _sweep.value;
                if (t <= 0 || t >= 1) return const SizedBox.shrink();
                final white = Colors.white.withValues(
                  alpha:
                      widget.strength *
                      (JarvisColors.of(context).isDark ? .45 : 1),
                );
                return DecoratedBox(
                  decoration: BoxDecoration(
                    gradient: LinearGradient(
                      begin: Alignment(-2.2 + 4.4 * t, -1),
                      end: Alignment(-1.2 + 4.4 * t, 1),
                      colors: [
                        white.withValues(alpha: 0),
                        white,
                        white.withValues(alpha: 0),
                      ],
                      stops: const [0, .5, 1],
                    ),
                  ),
                );
              },
            ),
          ),
        ),
      ),
    ],
  );
}

/// Tilts its child toward the finger, in 3D, while it is held, and springs
/// back flat when released. Taps pass straight through to the child.
class TiltOnPress extends StatefulWidget {
  const TiltOnPress({required this.child, this.maxAngle = .07, super.key});

  final Widget child;

  /// Largest rotation around either axis, in radians.
  final double maxAngle;

  @override
  State<TiltOnPress> createState() => _TiltOnPressState();
}

class _TiltOnPressState extends State<TiltOnPress> {
  Offset _tilt = Offset.zero;

  void _point(PointerEvent event) {
    final box = context.findRenderObject();
    if (box is! RenderBox || !box.hasSize) return;
    final size = box.size;
    final local = event.localPosition;
    final x = ((local.dx / size.width) * 2 - 1).clamp(-1.0, 1.0);
    final y = ((local.dy / size.height) * 2 - 1).clamp(-1.0, 1.0);
    setState(() => _tilt = Offset(x, y));
  }

  void _release(PointerEvent _) {
    if (_tilt != Offset.zero) setState(() => _tilt = Offset.zero);
  }

  @override
  Widget build(BuildContext context) {
    if (JarvisMotion.reduced(context)) return widget.child;
    return Listener(
      behavior: HitTestBehavior.translucent,
      onPointerDown: _point,
      onPointerMove: _point,
      onPointerUp: _release,
      onPointerCancel: _release,
      child: TweenAnimationBuilder<Offset>(
        tween: Tween(end: _tilt),
        duration: _tilt == Offset.zero
            ? const Duration(milliseconds: 520)
            : JarvisMotion.fast,
        curve: _tilt == Offset.zero
            ? JarvisSprings.soft
            : JarvisMotion.standard,
        child: widget.child,
        builder: (context, tilt, child) => Transform(
          alignment: Alignment.center,
          transform: Matrix4.identity()
            ..setEntry(3, 2, .0012)
            ..rotateX(-tilt.dy * widget.maxAngle)
            ..rotateY(tilt.dx * widget.maxAngle),
          child: child,
        ),
      ),
    );
  }
}

/// A one-off burst of confetti from the centre of [child] each time
/// [trigger] changes to a new value; for completing something.
class CelebrationBurst extends StatefulWidget {
  const CelebrationBurst({
    required this.child,
    required this.trigger,
    this.particles = 14,
    this.radius = 34,
    super.key,
  });

  final Widget child;

  /// Bursts when this changes and is truthy (true, or a non-null value).
  final Object? trigger;
  final int particles;
  final double radius;

  @override
  State<CelebrationBurst> createState() => _CelebrationBurstState();
}

class _CelebrationBurstState extends State<CelebrationBurst>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 760),
  );
  int _seed = 0;

  @override
  void didUpdateWidget(CelebrationBurst oldWidget) {
    super.didUpdateWidget(oldWidget);
    final fire = widget.trigger != null && widget.trigger != false;
    if (oldWidget.trigger != widget.trigger &&
        fire &&
        !JarvisMotion.reduced(context)) {
      HapticFeedback.lightImpact();
      _seed++;
      _controller.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final palette = [
      colors.accent,
      colors.violet,
      colors.sky,
      colors.rose,
      colors.success,
      colors.warning,
    ];
    return Stack(
      clipBehavior: Clip.none,
      alignment: Alignment.center,
      children: [
        widget.child,
        Positioned.fill(
          child: IgnorePointer(
            child: AnimatedBuilder(
              animation: _controller,
              builder: (context, _) => _controller.isAnimating
                  ? CustomPaint(
                      painter: _BurstPainter(
                        progress: _controller.value,
                        seed: _seed,
                        count: widget.particles,
                        radius: widget.radius,
                        palette: palette,
                      ),
                    )
                  : const SizedBox.shrink(),
            ),
          ),
        ),
      ],
    );
  }
}

class _BurstPainter extends CustomPainter {
  _BurstPainter({
    required this.progress,
    required this.seed,
    required this.count,
    required this.radius,
    required this.palette,
  });

  final double progress;
  final int seed;
  final int count;
  final double radius;
  final List<Color> palette;

  @override
  void paint(Canvas canvas, Size size) {
    final random = math.Random(seed * 7919);
    final center = size.center(Offset.zero);
    final travel = Curves.easeOutCubic.transform(progress);
    final fade = 1 - Curves.easeIn.transform(progress);
    // A ring that expands and thins out behind the particles.
    canvas.drawCircle(
      center,
      radius * .35 + radius * .7 * travel,
      Paint()
        ..style = PaintingStyle.stroke
        ..strokeWidth = 2.2 * fade
        ..color = palette.first.withValues(alpha: .35 * fade),
    );
    for (var i = 0; i < count; i++) {
      final angle = (i / count) * math.pi * 2 + random.nextDouble() * .5;
      final distance = radius * (.7 + random.nextDouble() * .6) * travel;
      final drop = 10 * progress * progress;
      final position =
          center +
          Offset(math.cos(angle) * distance, math.sin(angle) * distance + drop);
      final paint = Paint()
        ..color = palette[i % palette.length].withValues(alpha: fade);
      final spin = angle + progress * 6;
      canvas.save();
      canvas.translate(position.dx, position.dy);
      canvas.rotate(spin);
      if (i.isEven) {
        canvas.drawRRect(
          RRect.fromRectAndRadius(
            Rect.fromCenter(center: Offset.zero, width: 6, height: 3),
            const Radius.circular(1.5),
          ),
          paint,
        );
      } else {
        canvas.drawCircle(Offset.zero, 2.2, paint);
      }
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(_BurstPainter old) =>
      old.progress != progress || old.seed != seed;
}

/// A ring that ripples out from behind [child] whenever [trigger] changes;
/// the orb uses it to answer a tap.
class Shockwave extends StatefulWidget {
  const Shockwave({
    required this.child,
    required this.trigger,
    this.size = 40,
    this.color,
    super.key,
  });

  final Widget child;
  final int trigger;
  final double size;
  final Color? color;

  @override
  State<Shockwave> createState() => _ShockwaveState();
}

class _ShockwaveState extends State<Shockwave>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 650),
  );

  @override
  void didUpdateWidget(Shockwave oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.trigger != widget.trigger && !JarvisMotion.reduced(context)) {
      _controller.forward(from: 0);
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final color = widget.color ?? JarvisColors.of(context).accent;
    return Stack(
      clipBehavior: Clip.none,
      alignment: Alignment.center,
      children: [
        AnimatedBuilder(
          animation: _controller,
          builder: (context, _) {
            if (!_controller.isAnimating) return const SizedBox.shrink();
            final t = Curves.easeOutCubic.transform(_controller.value);
            final extent = widget.size * (1 + .8 * t);
            return IgnorePointer(
              child: Container(
                width: extent,
                height: extent,
                decoration: BoxDecoration(
                  shape: BoxShape.circle,
                  border: Border.all(
                    color: color.withValues(alpha: .5 * (1 - t)),
                    width: 2 * (1 - t) + .5,
                  ),
                  color: color.withValues(alpha: .12 * (1 - t)),
                ),
              ),
            );
          },
        ),
        widget.child,
      ],
    );
  }
}

/// Pull to refresh with the orb instead of a spinner: it drops in and turns
/// as you pull, pops when letting go would refresh, then thinks until the
/// refresh is done.
class OrbRefresh extends StatefulWidget {
  const OrbRefresh({required this.onRefresh, required this.child, super.key});

  final RefreshCallback onRefresh;
  final Widget child;

  @override
  State<OrbRefresh> createState() => _OrbRefreshState();
}

class _OrbRefreshState extends State<OrbRefresh> {
  RefreshIndicatorStatus? _status;

  /// How far past the top the list has been pulled, in logical pixels.
  double _pull = 0;
  double _extent = 600;

  bool get _refreshing =>
      _status == RefreshIndicatorStatus.snap ||
      _status == RefreshIndicatorStatus.refresh;

  bool _track(ScrollNotification notification) {
    if (notification.depth != 0) return false;
    _extent = notification.metrics.viewportDimension;
    var pull = _pull;
    if (notification is OverscrollNotification &&
        notification.overscroll < 0 &&
        notification.dragDetails != null) {
      pull -= notification.overscroll;
    } else if (notification is ScrollUpdateNotification) {
      final pixels = notification.metrics.pixels;
      if (pixels < 0) {
        // Bouncing platforms move the list itself past the top.
        pull = -pixels;
      } else if ((notification.scrollDelta ?? 0) > 0) {
        pull = math.max(0, pull - notification.scrollDelta!);
      }
    } else if (notification is ScrollEndNotification && !_refreshing) {
      pull = 0;
    }
    if (pull != _pull) setState(() => _pull = pull);
    return false;
  }

  @override
  Widget build(BuildContext context) {
    // The material indicator arms at about a quarter of the viewport.
    final progress = (_pull / (_extent * .25)).clamp(0.0, 1.0);
    final visible = _refreshing || (_status != null && progress > 0);
    final armed = _status == RefreshIndicatorStatus.armed;
    final drop = _refreshing ? 56.0 : 8 + 52 * progress;
    return Stack(
      children: [
        RefreshIndicator.noSpinner(
          onRefresh: widget.onRefresh,
          onStatusChange: (status) {
            if (!mounted) return;
            setState(() {
              _status = status;
              if (status == null || status == RefreshIndicatorStatus.done) {
                _pull = 0;
              }
            });
          },
          child: NotificationListener<ScrollNotification>(
            onNotification: _track,
            child: widget.child,
          ),
        ),
        Positioned(
          top: 0,
          left: 0,
          right: 0,
          child: IgnorePointer(
            child: AnimatedOpacity(
              opacity: visible ? 1 : 0,
              duration: JarvisMotion.of(context, JarvisMotion.base),
              child: Center(
                child: AnimatedContainer(
                  duration: JarvisMotion.of(context, JarvisMotion.fast),
                  curve: JarvisMotion.standard,
                  margin: EdgeInsets.only(top: visible ? drop : 0),
                  child: AnimatedScale(
                    scale: armed || _refreshing ? 1.15 : .5 + .5 * progress,
                    duration: JarvisMotion.of(
                      context,
                      const Duration(milliseconds: 380),
                    ),
                    curve: armed ? JarvisSprings.pop : JarvisMotion.standard,
                    child: Transform.rotate(
                      angle: _refreshing ? 0 : progress * math.pi * 1.5,
                      child: JarvisOrb(
                        size: 30,
                        thinking: _refreshing,
                        semanticLabel: _refreshing ? 'Refreshing' : null,
                      ),
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}
