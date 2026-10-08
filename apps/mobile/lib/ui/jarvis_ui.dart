import 'dart:async';
import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import 'effects.dart';
import 'motion.dart';
import 'phosphor_icons.dart';

import '../theme.dart';

export 'effects.dart';
export 'motion.dart';

part 'jarvis_orb.dart';

/// Fades and lifts its child into place once, staggered by [index] so rows
/// arrive in sequence. Skips the motion when the device asks to reduce it.
class FadeSlideIn extends StatefulWidget {
  const FadeSlideIn({
    required this.child,
    this.index = 0,
    this.offset = JarvisMotion.travel,
    this.animate = true,
    this.scale = 1,
    this.alignment = Alignment.center,
    super.key,
  });

  final Widget child;

  /// Scale the child grows from; 1 keeps it full size.
  final double scale;

  /// Where the growth is anchored, such as the side a chat bubble sits on.
  final Alignment alignment;

  /// When false the child starts in place; for rows that were already on
  /// screen before (history, restored state).
  final bool animate;

  /// Position in a list; later rows start later (capped so long lists stay quick).
  final int index;

  /// Vertical distance, in logical pixels, the child travels while fading in.
  final double offset;

  @override
  State<FadeSlideIn> createState() => _FadeSlideInState();
}

class _FadeSlideInState extends State<FadeSlideIn>
    with SingleTickerProviderStateMixin {
  static const _stepMs = 40;
  static final _travelMs = JarvisMotion.slow.inMilliseconds;

  late final AnimationController _controller;
  late final Animation<double> _progress;

  @override
  void initState() {
    super.initState();
    final delay = _stepMs * widget.index.clamp(0, 8);
    _controller = AnimationController(
      vsync: this,
      duration: Duration(milliseconds: delay + _travelMs),
    );
    _progress = CurvedAnimation(
      parent: _controller,
      curve: Interval(
        delay / (delay + _travelMs),
        1,
        curve: JarvisMotion.standard,
      ),
    );
    if (widget.animate) {
      _controller.forward();
    } else {
      _controller.value = 1;
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (JarvisMotion.reduced(context)) {
      return widget.child;
    }
    return AnimatedBuilder(
      animation: _progress,
      child: widget.child,
      builder: (context, child) {
        final t = _progress.value;
        Widget moved = Transform.translate(
          offset: Offset(0, (1 - t) * widget.offset),
          child: child,
        );
        if (widget.scale != 1) {
          moved = Transform.scale(
            scale: widget.scale + (1 - widget.scale) * t,
            alignment: widget.alignment,
            child: moved,
          );
        }
        return Opacity(opacity: t, child: moved);
      },
    );
  }
}

/// A white, rounded surface with a hairline border and optional tap target.
/// Tappable cards ease down slightly while pressed.
class SurfaceCard extends StatefulWidget {
  const SurfaceCard({
    required this.child,
    this.padding = const EdgeInsets.all(18),
    this.margin = EdgeInsets.zero,
    this.onTap,
    this.color,
    this.borderColor,
    this.radius = JarvisRadii.lg,
    this.elevated = false,
    this.gradient,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final EdgeInsetsGeometry margin;
  final VoidCallback? onTap;
  final Color? color;
  final Color? borderColor;
  final double radius;
  final bool elevated;
  final Gradient? gradient;

  @override
  State<SurfaceCard> createState() => _SurfaceCardState();
}

class _SurfaceCardState extends State<SurfaceCard> {
  var _pressed = false;

  void _setPressed(bool value) {
    if (_pressed != value) setState(() => _pressed = value);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final shape = BorderRadius.circular(widget.radius);
    return Padding(
      padding: widget.margin,
      child: AnimatedScale(
        scale:
            _pressed && widget.onTap != null && !JarvisMotion.reduced(context)
            ? JarvisMotion.startScale
            : 1,
        duration: JarvisMotion.of(context, JarvisMotion.fast),
        curve: JarvisMotion.standard,
        child: DecoratedBox(
          decoration: BoxDecoration(
            color: widget.gradient == null
                ? (widget.color ?? colors.surface)
                : null,
            gradient: widget.gradient,
            borderRadius: shape,
            border: Border.all(
              color:
                  widget.borderColor ?? colors.outline.withValues(alpha: .75),
            ),
            // Every card lifts a hair off the canvas; elevated ones float.
            boxShadow: widget.elevated
                ? JarvisShadows.soft(colors.brightness)
                : widget.gradient == null && widget.color == null
                ? JarvisShadows.hairline(colors.brightness)
                : null,
          ),
          child: Material(
            type: MaterialType.transparency,
            child: InkWell(
              borderRadius: shape,
              onTap: widget.onTap,
              onHighlightChanged: widget.onTap == null ? null : _setPressed,
              child: Padding(padding: widget.padding, child: widget.child),
            ),
          ),
        ),
      ),
    );
  }
}

/// A tinted rounded-square icon used as the leading visual in rows.
class IconBadge extends StatelessWidget {
  const IconBadge({required this.icon, this.color, this.size = 36, super.key});

  final IconData icon;
  final Color? color;
  final double size;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(size * .28),
      ),
      child: Icon(icon, size: size * .52, color: color ?? colors.ink),
    );
  }
}

/// A compact status label with a leading dot.
class StatusPill extends StatelessWidget {
  const StatusPill({required this.label, required this.color, super.key});

  StatusPill.forStatus(String status, {super.key})
    : label = statusStyle(status).label,
      color = statusStyle(status).color;

  final String label;
  final Color color;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      padding: const EdgeInsets.fromLTRB(7, 3, 9, 3),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(6),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Container(
            width: 6,
            height: 6,
            decoration: BoxDecoration(color: color, shape: BoxShape.circle),
          ),
          const SizedBox(width: 6),
          Flexible(
            child: Text(
              label,
              style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w500,
                color: colors.inkSoft,
                height: 1.25,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

typedef StatusStyle = ({String label, Color color, IconData icon});

/// Shared visual language for task, reminder, watch, and file statuses.
StatusStyle statusStyle(String status) => switch (status) {
  'running' || 'processing' => (
    label: status == 'running' ? 'In progress' : 'Processing',
    color: JarvisColors.light.info,
    icon: PhosphorIconsRegular.hourglassMedium,
  ),
  'needs_approval' => (
    label: 'Needs approval',
    color: JarvisColors.light.warning,
    icon: PhosphorIconsRegular.shieldWarning,
  ),
  'completed' ||
  'delivered' ||
  'indexed' ||
  'triggered' ||
  'sent' ||
  'ready' => (
    label: status == 'ready' ? 'Ready' : _titleCase(status),
    color: JarvisColors.light.success,
    icon: PhosphorIconsRegular.checkCircle,
  ),
  'failed' || 'error' || 'rejected' => (
    label: _titleCase(status),
    color: JarvisColors.light.danger,
    icon: PhosphorIconsRegular.warningCircle,
  ),
  'cancelled' || 'canceled' || 'stopped' || 'expired' => (
    label: _titleCase(status),
    color: JarvisColors.light.muted,
    icon: PhosphorIconsRegular.prohibit,
  ),
  'active' || 'pending' || 'scheduled' => (
    label: _titleCase(status),
    color: JarvisColors.light.accent,
    icon: PhosphorIconsRegular.clock,
  ),
  'waiting' => (
    label: 'Waiting',
    color: JarvisColors.light.violet,
    icon: PhosphorIconsRegular.pauseCircle,
  ),
  'queued' => (
    label: 'Queued',
    color: JarvisColors.light.inkSoft,
    icon: PhosphorIconsRegular.clock,
  ),
  _ => (
    label: status.isEmpty ? 'Unknown' : _titleCase(status),
    color: JarvisColors.light.inkSoft,
    icon: PhosphorIconsRegular.circle,
  ),
};

String _titleCase(String value) {
  final text = value.replaceAll('_', ' ');
  return text.isEmpty ? text : text[0].toUpperCase() + text.substring(1);
}

/// Small heading above a group of content, with an optional trailing action.
class SectionHeader extends StatelessWidget {
  const SectionHeader(this.title, {this.trailing, this.padding, super.key});

  final String title;
  final Widget? trailing;
  final EdgeInsetsGeometry? padding;

  @override
  Widget build(BuildContext context) => Padding(
    padding: padding ?? const EdgeInsets.fromLTRB(4, 0, 0, 10),
    child: Row(
      children: [
        Expanded(
          child: Text(title, style: Theme.of(context).textTheme.titleMedium),
        ),
        ?trailing,
      ],
    ),
  );
}

/// The empty-state icon: it springs in over a soft accent glow while a ring
/// ripples out once, so an empty screen still feels alive.
class _EmptyIcon extends StatelessWidget {
  const _EmptyIcon({required this.icon});

  final IconData icon;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final tile = Container(
      width: 52,
      height: 52,
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(14),
        border: Border.all(color: colors.outline),
        boxShadow: [
          ...JarvisShadows.soft(colors.brightness),
          BoxShadow(
            color: colors.accent.withValues(alpha: colors.isDark ? .22 : .12),
            blurRadius: 28,
            spreadRadius: 2,
          ),
        ],
      ),
      child: Icon(icon, size: 24, color: colors.inkSoft),
    );
    if (JarvisMotion.reduced(context)) return tile;
    return SizedBox.square(
      dimension: 52,
      child: Stack(
        clipBehavior: Clip.none,
        alignment: Alignment.center,
        children: [
          TweenAnimationBuilder<double>(
            tween: Tween(begin: 0, end: 1),
            duration: const Duration(milliseconds: 1300),
            curve: const Interval(.2, 1, curve: Curves.easeOutCubic),
            builder: (context, t, _) => t >= 1
                ? const SizedBox.shrink()
                : IgnorePointer(
                    child: Container(
                      width: 52 + 60 * t,
                      height: 52 + 60 * t,
                      decoration: BoxDecoration(
                        borderRadius: BorderRadius.circular(14 + 30 * t),
                        border: Border.all(
                          color: colors.accent.withValues(alpha: .3 * (1 - t)),
                          width: 1.5,
                        ),
                      ),
                    ),
                  ),
          ),
          PopIn(from: .5, child: tile),
        ],
      ),
    );
  }
}

/// Centered illustration + copy for empty lists.
class EmptyState extends StatelessWidget {
  const EmptyState({
    required this.icon,
    required this.title,
    this.message,
    this.action,
    super.key,
  });

  final IconData icon;
  final String title;
  final String? message;
  final Widget? action;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(32, 24, 32, 96),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 360),
          child: FadeSlideIn(
            offset: 14,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                _EmptyIcon(icon: icon),
                const SizedBox(height: 18),
                Text(
                  title,
                  textAlign: TextAlign.center,
                  style: theme.textTheme.titleMedium,
                ),
                if (message != null) ...[
                  const SizedBox(height: 6),
                  Text(
                    message!,
                    textAlign: TextAlign.center,
                    style: theme.textTheme.bodyMedium?.copyWith(
                      color: JarvisColors.of(context).inkSoft,
                    ),
                  ),
                ],
                if (action != null) ...[const SizedBox(height: 20), action!],
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class ErrorState extends StatelessWidget {
  const ErrorState({required this.message, this.onRetry, super.key});

  final String message;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) => EmptyState(
    icon: PhosphorIconsRegular.cloudSlash,
    title: 'Something went wrong',
    message: message,
    action: onRetry == null
        ? null
        : OutlinedButton.icon(
            onPressed: onRetry,
            icon: const Icon(PhosphorIconsRegular.arrowsClockwise, size: 18),
            label: const Text('Retry'),
          ),
  );
}

/// Loading, empty, full-page error, or a list that keeps rows visible when a refresh fails.
class ListScreenBody extends StatelessWidget {
  const ListScreenBody({
    required this.loading,
    required this.error,
    required this.isEmpty,
    required this.empty,
    required this.child,
    required this.onRetry,
    this.onRefresh,
    super.key,
  });

  final bool loading;
  final String? error;
  final bool isEmpty;
  final Widget empty;
  final Widget child;
  final VoidCallback onRetry;

  /// Pull-to-refresh; when null the pull triggers [onRetry] and shows the
  /// indicator briefly.
  final Future<void> Function()? onRefresh;

  @override
  Widget build(BuildContext context) {
    if (loading && isEmpty) return const SkeletonList();
    if (error != null && isEmpty) {
      return ErrorState(message: error!, onRetry: onRetry);
    }
    if (isEmpty) return empty;
    return Column(
      children: [
        if (error != null)
          ContentWidth(
            child: InlineNotice(
              message: error!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              actions: [
                TextButton(onPressed: onRetry, child: const Text('Retry')),
              ],
            ),
          ),
        Expanded(
          child: RefreshIndicator(
            onRefresh:
                onRefresh ??
                () async {
                  onRetry();
                  await Future<void>.delayed(const Duration(milliseconds: 700));
                },
            // Short lists must still be pullable.
            child: ScrollConfiguration(
              behavior: ScrollConfiguration.of(
                context,
              ).copyWith(physics: const AlwaysScrollableScrollPhysics()),
              child: child,
            ),
          ),
        ),
      ],
    );
  }
}

class LoadingState extends StatelessWidget {
  const LoadingState({super.key});

  @override
  Widget build(BuildContext context) => Center(
    // Waits a beat before appearing so fast loads never flash a spinner.
    child: TweenAnimationBuilder<double>(
      tween: Tween(begin: 0, end: 1),
      duration: const Duration(milliseconds: 500),
      curve: const Interval(.3, 1, curve: JarvisMotion.standard),
      builder: (context, value, child) => Opacity(opacity: value, child: child),
      child: const SizedBox.square(
        dimension: 28,
        child: CircularProgressIndicator(strokeWidth: 2.6),
      ),
    ),
  );
}

/// Placeholder rows shaped like the list that is loading, with a soft shimmer.
/// Waits a beat before appearing so fast loads never flash placeholders.
class SkeletonList extends StatefulWidget {
  const SkeletonList({this.rows = 6, super.key});

  final int rows;

  @override
  State<SkeletonList> createState() => _SkeletonListState();
}

class _SkeletonListState extends State<SkeletonList>
    with SingleTickerProviderStateMixin {
  late final AnimationController _shimmer = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1400),
  )..repeat();

  @override
  void dispose() {
    _shimmer.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final reduced = JarvisMotion.reduced(context);
    Widget bar(double widthFactor, double height) => FractionallySizedBox(
      alignment: Alignment.centerLeft,
      widthFactor: widthFactor,
      child: Container(
        height: height,
        decoration: BoxDecoration(
          color: colors.surfaceRaised,
          borderRadius: BorderRadius.circular(height / 2),
        ),
      ),
    );
    final rows = Column(
      children: [
        for (var i = 0; i < widget.rows; i++)
          Padding(
            padding: const EdgeInsets.only(bottom: 10),
            child: SurfaceCard(
              padding: const EdgeInsets.all(16),
              child: Row(
                children: [
                  Container(
                    width: 36,
                    height: 36,
                    decoration: BoxDecoration(
                      color: colors.surfaceRaised,
                      borderRadius: BorderRadius.circular(10),
                    ),
                  ),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        // Vary the widths so the rows read as content.
                        bar(.4 + (i * 17 % 30) / 100, 11),
                        const SizedBox(height: 8),
                        bar(.6 + (i * 13 % 25) / 100, 9),
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
      ],
    );
    final shimmering = reduced
        ? rows
        : AnimatedBuilder(
            animation: _shimmer,
            child: rows,
            builder: (context, child) => ShaderMask(
              blendMode: BlendMode.srcATop,
              shaderCallback: (bounds) {
                final dx = (_shimmer.value * 2 - .5) * bounds.width;
                // Fade from a transparent copy of the highlight, never from
                // transparent black, which would smear grey across the rows.
                final glow = colors.isDark
                    ? const Color(0xffffffff)
                    : colors.surface;
                return LinearGradient(
                  colors: [
                    glow.withValues(alpha: 0),
                    glow.withValues(alpha: colors.isDark ? .06 : .7),
                    glow.withValues(alpha: 0),
                  ],
                  stops: const [.35, .5, .65],
                  transform: _SlideGradient(dx),
                ).createShader(bounds);
              },
              child: child,
            ),
          );
    return Semantics(
      label: 'Loading',
      child: TweenAnimationBuilder<double>(
        tween: Tween(begin: 0, end: 1),
        duration: const Duration(milliseconds: 450),
        curve: const Interval(.4, 1, curve: JarvisMotion.standard),
        builder: (context, value, child) =>
            Opacity(opacity: value, child: child),
        child: SingleChildScrollView(
          physics: const NeverScrollableScrollPhysics(),
          padding: const EdgeInsets.fromLTRB(16, 12, 16, 16),
          child: ContentWidth(child: ExcludeSemantics(child: shimmering)),
        ),
      ),
    );
  }
}

class _SlideGradient extends GradientTransform {
  const _SlideGradient(this.dx);

  final double dx;

  @override
  Matrix4 transform(Rect bounds, {TextDirection? textDirection}) =>
      Matrix4.translationValues(dx, 0, 0);
}

enum NoticeTone { info, warning, danger, success }

/// A soft, tinted inline banner for errors and confirmations.
class InlineNotice extends StatelessWidget {
  const InlineNotice({
    required this.message,
    this.tone = NoticeTone.warning,
    this.actions = const [],
    this.margin = EdgeInsets.zero,
    super.key,
  });

  final String message;
  final NoticeTone tone;
  final List<Widget> actions;
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) {
    final (fg, bg, icon) = switch (tone) {
      NoticeTone.info => (
        JarvisColors.of(context).inkSoft,
        JarvisColors.of(context).surfaceMuted,
        PhosphorIconsRegular.info,
      ),
      NoticeTone.warning => (
        JarvisColors.of(context).warning,
        JarvisColors.of(context).surfaceMuted,
        PhosphorIconsRegular.warningCircle,
      ),
      NoticeTone.danger => (
        JarvisColors.of(context).danger,
        JarvisColors.of(context).dangerSoft,
        PhosphorIconsRegular.warningCircle,
      ),
      NoticeTone.success => (
        JarvisColors.of(context).success,
        JarvisColors.of(context).surfaceMuted,
        PhosphorIconsRegular.checkCircle,
      ),
    };
    // Large text would squeeze the message to nothing beside the buttons.
    final stacked =
        actions.isNotEmpty && MediaQuery.textScalerOf(context).scale(10) > 13;
    final text = Text(
      message,
      style: TextStyle(
        fontSize: 13.5,
        height: 1.4,
        color: JarvisColors.of(context).ink,
      ),
    );
    return Padding(
      padding: margin,
      child: Container(
        padding: EdgeInsets.fromLTRB(14, 12, actions.isEmpty ? 14 : 6, 12),
        decoration: BoxDecoration(
          color: bg,
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          border: tone == NoticeTone.danger
              ? Border.all(color: fg.withValues(alpha: .15))
              : null,
        ),
        child: stacked
            ? Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Icon(icon, size: 20, color: fg),
                      const SizedBox(width: 10),
                      Expanded(child: text),
                    ],
                  ),
                  Align(
                    alignment: Alignment.centerRight,
                    child: Wrap(children: actions),
                  ),
                ],
              )
            : Row(
                children: [
                  Icon(icon, size: 20, color: fg),
                  const SizedBox(width: 10),
                  Expanded(child: text),
                  ...actions,
                ],
              ),
      ),
    );
  }
}

/// Centers content and caps its width on large screens.
class ContentWidth extends StatelessWidget {
  const ContentWidth({required this.child, this.maxWidth = 720, super.key});

  final Widget child;
  final double maxWidth;

  @override
  Widget build(BuildContext context) => Align(
    alignment: Alignment.topCenter,
    child: ConstrainedBox(
      constraints: BoxConstraints(maxWidth: maxWidth),
      child: child,
    ),
  );
}

/// A confirmation dialog; [destructive] paints the confirm action red.
Future<bool> showJarvisConfirm(
  BuildContext context, {
  required String title,
  required String message,
  required String confirmLabel,
  String cancelLabel = 'Cancel',
  bool destructive = false,
  IconData? icon,
}) async {
  final confirmed = await showDialog<bool>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      icon: icon == null
          ? null
          : Align(
              child: IconBadge(
                icon: icon,
                size: 48,
                color: destructive
                    ? JarvisColors.of(context).danger
                    : JarvisColors.of(context).accent,
              ),
            ),
      title: Text(title),
      content: Text(message),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(dialogContext, false),
          style: TextButton.styleFrom(
            foregroundColor: JarvisColors.of(context).inkSoft,
          ),
          child: Text(cancelLabel),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(dialogContext, true),
          style: destructive
              ? FilledButton.styleFrom(
                  backgroundColor: JarvisColors.of(context).danger,
                )
              : null,
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
  return confirmed ?? false;
}

/// Compact primary action for app bars, replacing floating action buttons.
class HeaderAction extends StatelessWidget {
  const HeaderAction({
    required this.label,
    required this.icon,
    required this.onPressed,
    this.busy = false,
    this.collapsesWhenNarrow = false,
    super.key,
  });

  final String label;
  final IconData icon;
  final VoidCallback? onPressed;
  final bool busy;

  /// Marks a secondary action: it drops its label on narrow screens so it never
  /// crowds out the app bar title, and uses a quieter tonal fill.
  final bool collapsesWhenNarrow;

  static const _narrowWidth = 480.0;

  @override
  Widget build(BuildContext context) {
    // Large text leaves no room for a label next to the title, whatever the
    // screen width, so every action falls back to its icon.
    final bigText = MediaQuery.textScalerOf(context).scale(14) > 14 * 1.3;
    final iconOnly =
        bigText ||
        (collapsesWhenNarrow &&
            MediaQuery.sizeOf(context).width < _narrowWidth);
    final leading = busy
        ? const SizedBox.square(
            dimension: 14,
            child: CircularProgressIndicator(strokeWidth: 1.8),
          )
        : Icon(icon, size: iconOnly ? 18 : 16);
    return Padding(
      padding: const EdgeInsets.only(left: 4, right: 12),
      child: iconOnly ? _iconOnly(leading) : _labelled(leading),
    );
  }

  Widget _iconOnly(Widget leading) => Tooltip(
    message: label,
    child: _button(
      onPressed: busy ? null : onPressed,
      style: _style().copyWith(
        minimumSize: const WidgetStatePropertyAll(Size(34, 34)),
        padding: const WidgetStatePropertyAll(EdgeInsets.zero),
      ),
      child: Semantics(label: label, excludeSemantics: true, child: leading),
    ),
  );

  Widget _labelled(Widget leading) => _button(
    onPressed: busy ? null : onPressed,
    style: _style(),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [leading, const SizedBox(width: 8), Text(label)],
    ),
  );

  Widget _button({
    required VoidCallback? onPressed,
    required ButtonStyle style,
    required Widget child,
  }) => collapsesWhenNarrow
      ? Builder(
          builder: (context) => FilledButton.tonal(
            onPressed: onPressed,
            style: style.copyWith(
              backgroundColor: WidgetStatePropertyAll(
                JarvisColors.of(context).surfaceRaised,
              ),
              foregroundColor: WidgetStatePropertyAll(
                JarvisColors.of(context).ink,
              ),
            ),
            child: child,
          ),
        )
      : FilledButton(onPressed: onPressed, style: style, child: child);

  ButtonStyle _style() => FilledButton.styleFrom(
    minimumSize: const Size(0, 34),
    padding: const EdgeInsets.symmetric(horizontal: 12),
    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
    shape: RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(JarvisRadii.sm + 2),
    ),
    textStyle: const TextStyle(
      fontFamily: 'Geist',
      fontSize: 13.5,
      fontWeight: FontWeight.w500,
      letterSpacing: -.1,
    ),
  );
}

/// Rows grouped in one card, separated by inset hairlines.
class GroupedSection extends StatelessWidget {
  const GroupedSection({
    required this.children,
    this.dividerIndent = 16,
    this.margin = EdgeInsets.zero,
    super.key,
  });

  final List<Widget> children;
  final double dividerIndent;
  final EdgeInsetsGeometry margin;

  @override
  Widget build(BuildContext context) => SurfaceCard(
    margin: margin,
    padding: EdgeInsets.zero,
    child: ClipRRect(
      borderRadius: BorderRadius.circular(JarvisRadii.lg),
      child: Column(
        children: [
          for (final (index, child) in children.indexed) ...[
            if (index > 0) Divider(indent: dividerIndent),
            child,
          ],
        ],
      ),
    ),
  );
}

/// Round, softly shadowed icon button used in the top bar. [bare] drops the
/// disc so the button can sit inside a [ToolbarCapsule].
class CircleIconButton extends StatelessWidget {
  const CircleIconButton({
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.size = 42,
    this.bare = false,
    super.key,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  final double size;
  final bool bare;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final button = Material(
      type: MaterialType.transparency,
      shape: const CircleBorder(),
      child: InkWell(
        customBorder: const CircleBorder(),
        onTap: onPressed,
        child: SizedBox.square(
          dimension: size,
          child: Icon(
            icon,
            size: 19,
            color: onPressed == null ? colors.muted : colors.ink,
          ),
        ),
      ),
    );
    return Tooltip(
      message: tooltip,
      child: Semantics(
        button: true,
        enabled: onPressed != null,
        label: tooltip,
        excludeSemantics: true,
        child: bare
            ? button
            : DecoratedBox(
                decoration: BoxDecoration(
                  color: colors.surface,
                  shape: BoxShape.circle,
                  border: Border.all(
                    color: colors.outline.withValues(alpha: .7),
                  ),
                  boxShadow: JarvisShadows.hairline(colors.brightness),
                ),
                child: button,
              ),
      ),
    );
  }
}

/// A pill that groups a few [CircleIconButton]s (with `bare: true`), like a
/// toolbar, so the top bar reads as one calm control instead of many discs.
class ToolbarCapsule extends StatelessWidget {
  const ToolbarCapsule({required this.children, super.key});

  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(999),
        border: Border.all(color: colors.outline.withValues(alpha: .7)),
        boxShadow: JarvisShadows.hairline(colors.brightness),
      ),
      child: Padding(
        padding: const EdgeInsets.symmetric(horizontal: 2),
        child: Row(mainAxisSize: MainAxisSize.min, children: children),
      ),
    );
  }
}

/// Small rounded status label with an optional action, for quiet states such
/// as "Offline · Retry" that should not push content down like a banner.
class StatusChip extends StatelessWidget {
  const StatusChip({
    required this.label,
    required this.color,
    this.actionLabel,
    this.onAction,
    super.key,
  });

  final String label;
  final Color color;
  final String? actionLabel;
  final VoidCallback? onAction;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Material(
      color: colors.surface,
      shape: StadiumBorder(
        side: BorderSide(color: colors.outline.withValues(alpha: .8)),
      ),
      child: InkWell(
        customBorder: const StadiumBorder(),
        onTap: onAction,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(10, 6, 12, 6),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 6,
                height: 6,
                decoration: BoxDecoration(color: color, shape: BoxShape.circle),
              ),
              const SizedBox(width: 7),
              Text(
                label,
                style: TextStyle(
                  fontSize: 12.5,
                  fontWeight: FontWeight.w500,
                  color: colors.inkSoft,
                ),
              ),
              if (actionLabel != null) ...[
                Text(
                  '  ·  ',
                  style: TextStyle(fontSize: 12.5, color: colors.muted),
                ),
                Text(
                  actionLabel!,
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w600,
                    color: colors.accent,
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// Runs [action] once [state]'s route has finished animating in, so an editor
/// opened on arrival does not appear over a page that is still moving.
void afterRouteSettles(State state, Future<void> Function() action) {
  WidgetsBinding.instance.addPostFrameCallback((_) {
    if (!state.mounted) return;
    final animation = ModalRoute.of(state.context)?.animation;
    if (animation == null || animation.isCompleted) {
      action();
      return;
    }
    void listener(AnimationStatus status) {
      if (!status.isCompleted) return;
      animation.removeStatusListener(listener);
      if (state.mounted) action();
    }

    animation.addStatusListener(listener);
  });
}

/// Dissolves scrolling content into the canvas at the top and bottom edges,
/// so text slides softly under the header and composer instead of being cut.
class EdgeFade extends StatelessWidget {
  const EdgeFade({
    required this.child,
    this.top = 14,
    this.bottom = 26,
    super.key,
  });

  final Widget child;
  final double top;
  final double bottom;

  @override
  Widget build(BuildContext context) => LayoutBuilder(
    builder: (context, constraints) {
      final height = constraints.maxHeight;
      if (!height.isFinite || height <= top + bottom) return child;
      return ShaderMask(
        blendMode: BlendMode.dstIn,
        shaderCallback: (bounds) => LinearGradient(
          begin: Alignment.topCenter,
          end: Alignment.bottomCenter,
          colors: const [
            Color(0x00000000),
            Color(0xff000000),
            Color(0xff000000),
            Color(0x00000000),
          ],
          stops: [0, top / height, 1 - bottom / height, 1],
        ).createShader(bounds),
        child: child,
      );
    },
  );
}

/// One side of [SwipeActions].
class SwipeAction {
  const SwipeAction({
    required this.label,
    required this.icon,
    required this.color,
    required this.onTrigger,
  });

  final String label;
  final IconData icon;
  final Color color;
  final Future<void> Function() onTrigger;
}

/// Swipe a row to act on it: the action shows underneath as the row slides,
/// fires past the threshold with a tap of haptics, and the row springs back
/// (the list then reloads into its new state). Menus stay the accessible path.
class SwipeActions extends StatelessWidget {
  const SwipeActions({
    required this.id,
    required this.child,
    this.start,
    this.end,
    this.radius = JarvisRadii.lg,
    this.bottomGap = 10,
    super.key,
  });

  final Object id;
  final Widget child;

  /// Revealed when swiping toward the end (right in left-to-right layouts).
  final SwipeAction? start;

  /// Revealed when swiping toward the start.
  final SwipeAction? end;
  final double radius;

  /// Space below the row that the coloured background must not cover.
  final double bottomGap;

  @override
  Widget build(BuildContext context) {
    if (start == null && end == null) return child;
    final direction = start != null && end != null
        ? DismissDirection.horizontal
        : start != null
        ? DismissDirection.startToEnd
        : DismissDirection.endToStart;
    return Dismissible(
      key: ValueKey(('swipe', id)),
      direction: direction,
      dismissThresholds: const {
        DismissDirection.startToEnd: .32,
        DismissDirection.endToStart: .32,
      },
      movementDuration: JarvisMotion.base,
      confirmDismiss: (swiped) async {
        final action = swiped == DismissDirection.startToEnd ? start : end;
        if (action == null) return false;
        unawaited(HapticFeedback.mediumImpact());
        await action.onTrigger();
        return false;
      },
      background: start == null
          ? const SizedBox.shrink()
          : _SwipeBackground(
              action: start!,
              alignment: Alignment.centerLeft,
              radius: radius,
              bottomGap: bottomGap,
            ),
      secondaryBackground: end == null
          ? null
          : _SwipeBackground(
              action: end!,
              alignment: Alignment.centerRight,
              radius: radius,
              bottomGap: bottomGap,
            ),
      child: child,
    );
  }
}

class _SwipeBackground extends StatelessWidget {
  const _SwipeBackground({
    required this.action,
    required this.alignment,
    required this.radius,
    required this.bottomGap,
  });

  final SwipeAction action;
  final Alignment alignment;
  final double radius;
  final double bottomGap;

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.only(bottom: bottomGap),
    child: DecoratedBox(
      decoration: BoxDecoration(
        color: action.color,
        borderRadius: BorderRadius.circular(radius),
      ),
      child: Align(
        alignment: alignment,
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 22),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Icon(action.icon, color: Colors.white, size: 20),
              const SizedBox(width: 8),
              Text(
                action.label,
                style: const TextStyle(
                  color: Colors.white,
                  fontWeight: FontWeight.w600,
                  fontSize: 14,
                ),
              ),
            ],
          ),
        ),
      ),
    ),
  );
}

/// Soft violet and sky light behind a hero (the orb on home), echoing the
/// sign-in backdrop so the first screen has some atmosphere. Purely visual.
class HeroGlow extends StatelessWidget {
  const HeroGlow({required this.child, this.parallax, super.key});

  final Widget child;

  /// When set, the light lags behind the scroll, so it seems further away.
  final ScrollController? parallax;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget blob(double size, Color color) => Container(
      width: size,
      height: size,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: RadialGradient(colors: [color, color.withValues(alpha: 0)]),
      ),
    );
    final strength = colors.isDark ? 1.4 : 1.0;
    // The light blooms in once: each blob swells and drifts into its spot.
    Widget bloom(int order, Offset drift, Widget blob) =>
        JarvisMotion.reduced(context)
        ? blob
        : TweenAnimationBuilder<double>(
            tween: Tween(begin: 0, end: 1),
            duration: Duration(milliseconds: 1400 + order * 250),
            curve: Curves.easeOutCubic,
            child: blob,
            builder: (context, t, child) => Opacity(
              opacity: t,
              child: Transform.translate(
                offset: drift * (1 - t),
                child: Transform.scale(scale: .6 + .4 * t, child: child),
              ),
            ),
          );
    final scroll = parallax;
    // The light drifts down against the scroll, so it seems further away.
    Widget lag(Widget blob) => scroll == null || JarvisMotion.reduced(context)
        ? blob
        : AnimatedBuilder(
            animation: scroll,
            child: blob,
            builder: (context, child) => Transform.translate(
              offset: Offset(
                0,
                (scroll.hasClients ? scroll.offset.clamp(0, 400) : 0) * .45,
              ),
              child: child,
            ),
          );
    return Stack(
      clipBehavior: Clip.none,
      alignment: Alignment.topCenter,
      children: [
        // Blobs start at the top edge so the scroll viewport never slices
        // through a bright part of the glow.
        Positioned(
          top: -10,
          left: -170,
          child: IgnorePointer(
            child: lag(
              bloom(
                0,
                const Offset(-40, -20),
                blob(380, colors.violet.withValues(alpha: .13 * strength)),
              ),
            ),
          ),
        ),
        Positioned(
          top: 20,
          right: -190,
          child: IgnorePointer(
            child: lag(
              bloom(
                1,
                const Offset(50, -10),
                blob(340, colors.sky.withValues(alpha: .09 * strength)),
              ),
            ),
          ),
        ),
        Positioned(
          top: 170,
          left: -40,
          child: IgnorePointer(
            child: lag(
              bloom(
                2,
                const Offset(-20, 40),
                blob(220, colors.rose.withValues(alpha: .10 * strength)),
              ),
            ),
          ),
        ),
        child,
      ],
    );
  }
}
