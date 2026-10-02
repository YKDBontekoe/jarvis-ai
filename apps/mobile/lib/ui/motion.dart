import 'package:flutter/material.dart';

/// Shared motion language: short, ease-out movements that settle quietly.
/// Every animated surface reads its timing from here so the app moves as one.
abstract final class JarvisMotion {
  /// Presses, toggles, icon swaps.
  static const fast = Duration(milliseconds: 150);

  /// Content arriving, state changes inside a screen.
  static const base = Duration(milliseconds: 220);

  /// Page and pane changes.
  static const slow = Duration(milliseconds: 320);

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

/// Page transition for pushed routes: the new page fades in while it grows from
/// 98% and rises a few pixels.
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
    return FadeTransition(
      opacity: CurvedAnimation(
        parent: animation,
        curve: const Interval(0, .6, curve: Curves.easeOut),
        reverseCurve: const Interval(.2, 1, curve: Curves.easeIn),
      ),
      child: SlideTransition(
        position: Tween(
          begin: const Offset(0, .02),
          end: Offset.zero,
        ).animate(enter),
        child: ScaleTransition(
          scale: Tween(begin: JarvisMotion.startScale, end: 1.0).animate(enter),
          child: child,
        ),
      ),
    );
  }
}
