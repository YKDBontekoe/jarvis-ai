import 'dart:math' as math;

import 'package:flutter/material.dart';

/// Shared motion language: short, ease-out movements that settle quietly.
/// Every animated surface reads its timing from here so the app moves as one.
abstract final class JarvisMotion {
  /// Presses, toggles, icon swaps.
  static const fast = Duration(milliseconds: 150);

  /// Content arriving, state changes inside a screen.
  static const base = Duration(milliseconds: 220);

  /// Page and pane changes.
  static const slow = Duration(milliseconds: 380);

  /// Default curve for anything entering or changing.
  static const standard = Curves.easeOutCubic;

  /// For larger moves such as pages, where the start and finish both matter.
  static const emphasized = Curves.easeInOutCubicEmphasized;

  /// For content leaving the screen.
  static const exit = Curves.easeInCubic;

  /// How far content travels while it fades in, in logical pixels.
  static const travel = 8.0;

  /// Scale content starts from when it grows into place.
  static const startScale = .98;

  /// Whether the device asks to reduce motion.
  static bool reduced(BuildContext context) =>
      MediaQuery.maybeDisableAnimationsOf(context) ?? false;

  /// [duration], or no time at all when the device asks to reduce motion.
  static Duration of(BuildContext context, Duration duration) =>
      reduced(context) ? Duration.zero : duration;

  /// Fades and slightly grows the incoming child; for [AnimatedSwitcher].
  static Widget fadeScale(Widget child, Animation<double> animation) {
    final curved = CurvedAnimation(parent: animation, curve: standard);
    return FadeTransition(
      opacity: curved,
      child: ScaleTransition(
        scale: Tween(begin: startScale, end: 1.0).animate(curved),
        child: child,
      ),
    );
  }

  /// Spins and springs the incoming child into place while the outgoing one
  /// shrinks away; for one control turning into another (send ↔ voice ↔ stop).
  static Widget morph(Widget child, Animation<double> animation) {
    final spring = CurvedAnimation(
      parent: animation,
      curve: Curves.easeOutBack,
      reverseCurve: exit,
    );
    return FadeTransition(
      opacity: CurvedAnimation(
        parent: animation,
        curve: const Interval(0, .5, curve: Curves.easeOut),
      ),
      child: RotationTransition(
        turns: Tween(begin: -.18, end: 0.0).animate(spring),
        child: ScaleTransition(
          scale: Tween(begin: .4, end: 1.0).animate(spring),
          child: child,
        ),
      ),
    );
  }

  /// Fades the incoming child while it rises a few pixels; for [AnimatedSwitcher].
  static Widget fadeRise(Widget child, Animation<double> animation) {
    final curved = CurvedAnimation(parent: animation, curve: standard);
    return FadeTransition(
      opacity: curved,
      child: SlideTransition(
        position: Tween(
          begin: const Offset(0, .015),
          end: Offset.zero,
        ).animate(curved),
        child: child,
      ),
    );
  }

  /// Keeps only the incoming child in layout, so a fade-through never shows
  /// two screens stacked at different sizes.
  static Widget topLayout(Widget? current, List<Widget> previous) =>
      Stack(alignment: Alignment.topCenter, children: [...previous, ?current]);
}

/// Quiet content changes. Outgoing content cannot receive taps, announce
/// duplicate labels or keep its own looping animations running.
class MotionSwitcher extends StatelessWidget {
  const MotionSwitcher({
    required this.child,
    this.duration = JarvisMotion.base,
    this.resize = false,
    super.key,
  });

  final Widget child;
  final Duration duration;
  final bool resize;

  @override
  Widget build(BuildContext context) {
    final reduced = JarvisMotion.reduced(context);
    final content = AnimatedSwitcher(
      duration: JarvisMotion.of(context, duration),
      reverseDuration: JarvisMotion.of(context, JarvisMotion.fast),
      layoutBuilder: (current, previous) => Stack(
        alignment: Alignment.topLeft,
        children: [
          for (final old in previous)
            IgnorePointer(
              child: ExcludeSemantics(
                child: TickerMode(enabled: false, child: old),
              ),
            ),
          ?current,
        ],
      ),
      transitionBuilder: (child, animation) => AnimatedBuilder(
        animation: animation,
        child: child,
        builder: (context, child) {
          final leaving = animation.status == AnimationStatus.reverse;
          final curve = leaving
              ? const Interval(.65, 1, curve: JarvisMotion.exit)
              : const Interval(.12, 1, curve: JarvisMotion.standard);
          return Opacity(
            opacity: reduced
                ? (leaving ? 0 : 1)
                : curve.transform(animation.value),
            child: child,
          );
        },
      ),
      child: child,
    );
    return resize ? MotionSize(child: content) : content;
  }
}

/// Resize in place, or lay out immediately with Reduce Motion. A zero-duration
/// AnimatedSize can restart its controller during layout when content changes.
class MotionSize extends StatelessWidget {
  const MotionSize({required this.child, super.key});

  final Widget child;

  @override
  Widget build(BuildContext context) => JarvisMotion.reduced(context)
      ? child
      : AnimatedSize(
          duration: JarvisMotion.base,
          curve: JarvisMotion.standard,
          alignment: Alignment.topLeft,
          child: child,
        );
}

/// Uses the existing InkWell/InkResponse highlight lifecycle, so a cancelled
/// tap or a scroll releases the surface and keyboard activation still works.
class PressFeedback extends StatefulWidget {
  const PressFeedback({required this.builder, this.scale = .98, super.key});

  final Widget Function(BuildContext context, ValueChanged<bool> onHighlight)
  builder;
  final double scale;

  @override
  State<PressFeedback> createState() => _PressFeedbackState();
}

class _PressFeedbackState extends State<PressFeedback> {
  bool _pressed = false;

  void _highlight(bool value) {
    if (mounted && value != _pressed) setState(() => _pressed = value);
  }

  @override
  Widget build(BuildContext context) => AnimatedScale(
    scale: _pressed && !JarvisMotion.reduced(context) ? widget.scale : 1,
    duration: JarvisMotion.of(
      context,
      _pressed ? JarvisMotion.fast : JarvisMotion.base,
    ),
    curve: JarvisMotion.standard,
    child: widget.builder(context, _highlight),
  );
}

/// Page transition for pushed routes: the new page fades in while it grows
/// and rises into place, and the page it covers sinks back.
class JarvisPageTransitionsBuilder extends PageTransitionsBuilder {
  const JarvisPageTransitionsBuilder();

  @override
  Duration get transitionDuration => JarvisMotion.slow;

  @override
  Duration get reverseTransitionDuration => JarvisMotion.base;

  @override
  Widget buildTransitions<T>(
    PageRoute<T> route,
    BuildContext context,
    Animation<double> animation,
    Animation<double> secondaryAnimation,
    Widget child,
  ) {
    if (JarvisMotion.reduced(context)) return child;
    final enter = CurvedAnimation(
      parent: animation,
      curve: JarvisMotion.emphasized,
      reverseCurve: JarvisMotion.exit,
    );
    // The page underneath recedes a little while the new one arrives, so
    // the stack reads as depth rather than a swap.
    final behind = CurvedAnimation(
      parent: secondaryAnimation,
      curve: JarvisMotion.emphasized,
      reverseCurve: JarvisMotion.standard,
    );
    return ScaleTransition(
      scale: Tween(begin: 1.0, end: .955).animate(behind),
      child: FadeTransition(
        opacity: Tween(begin: 1.0, end: .55).animate(behind),
        child: FadeTransition(
          opacity: CurvedAnimation(
            parent: animation,
            curve: const Interval(0, .6, curve: Curves.easeOut),
            reverseCurve: const Interval(.2, 1, curve: Curves.easeIn),
          ),
          child: SlideTransition(
            position: Tween(
              begin: const Offset(0, .035),
              end: Offset.zero,
            ).animate(enter),
            child: ScaleTransition(
              scale: Tween(begin: .965, end: 1.0).animate(enter),
              child: child,
            ),
          ),
        ),
      ),
    );
  }
}

/// How [PageSwitcher] moves between pages.
enum PageMotion {
  /// Sideways, in the direction of travel: for sibling pages such as tabs.
  axis,

  /// The new page grows out of [PageSwitcher.origin] while the old one
  /// swells and fades past the viewer: for entering and leaving a mode.
  zoom,
}

/// Switches whole pages with direction. With [PageMotion.axis] the new page
/// slides in from the side of travel (decided by [index] going up or down)
/// while the old one drifts out the other way. Like [MotionSwitcher], the
/// outgoing page cannot be tapped, announced or keep its tickers running.
class PageSwitcher extends StatefulWidget {
  const PageSwitcher({
    required this.child,
    this.index = 0,
    this.motion = PageMotion.axis,
    this.origin = Alignment.bottomCenter,
    super.key,
  });

  /// The page; give it a key that changes with the page.
  final Widget child;

  /// Position of the page among its siblings, for [PageMotion.axis].
  final int index;
  final PageMotion motion;

  /// Where a [PageMotion.zoom] page grows from.
  final Alignment origin;

  @override
  State<PageSwitcher> createState() => _PageSwitcherState();
}

class _PageSwitcherState extends State<PageSwitcher> {
  /// 1 when moving to a higher index, -1 when moving back.
  double _direction = 1;

  /// Zoom forward (into the new page) or back out of it.
  bool _forward = true;

  @override
  void didUpdateWidget(PageSwitcher oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.index != widget.index) {
      _direction = widget.index > oldWidget.index ? 1 : -1;
      _forward = widget.index > oldWidget.index;
    }
  }

  @override
  Widget build(BuildContext context) {
    final reduced = JarvisMotion.reduced(context);
    final current = widget.child.key;
    return AnimatedSwitcher(
      duration: JarvisMotion.of(context, const Duration(milliseconds: 420)),
      reverseDuration: JarvisMotion.of(
        context,
        const Duration(milliseconds: 260),
      ),
      layoutBuilder: (current, previous) => Stack(
        alignment: Alignment.topLeft,
        children: [
          for (final old in previous)
            IgnorePointer(
              child: ExcludeSemantics(
                child: TickerMode(enabled: false, child: old),
              ),
            ),
          ?current,
        ],
      ),
      transitionBuilder: (child, animation) {
        final incoming = child.key == current;
        if (reduced) {
          return FadeTransition(opacity: animation, child: child);
        }
        return AnimatedBuilder(
          animation: animation,
          child: child,
          builder: (context, child) {
            final raw = animation.value;
            final t = incoming
                ? emphasizedOut.transform(raw)
                : Curves.easeIn.transform(raw);
            final opacity = incoming
                ? const Interval(.1, .7).transform(raw)
                : const Interval(.25, 1).transform(raw);
            if (widget.motion == PageMotion.zoom) {
              // Forward: new page grows in, old one swells away.
              // Back: new page settles down from large, old one shrinks.
              final grow = incoming == _forward;
              final scale = grow ? .9 + .1 * t : 1.06 - .06 * t;
              return Opacity(
                opacity: opacity,
                child: Transform.scale(
                  scale: scale,
                  alignment: widget.origin,
                  child: child,
                ),
              );
            }
            final width = MediaQuery.sizeOf(context).width;
            final travel = math.min(56.0, width * .14);
            final sign = incoming ? _direction : -_direction;
            return Opacity(
              opacity: opacity,
              child: Transform.translate(
                offset: Offset(sign * travel * (1 - t), 0),
                child: Transform.scale(scale: .985 + .015 * t, child: child),
              ),
            );
          },
        );
      },
      child: widget.child,
    );
  }

  static const emphasizedOut = Cubic(.05, .7, .1, 1);
}
