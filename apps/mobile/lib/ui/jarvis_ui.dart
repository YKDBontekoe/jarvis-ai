import 'dart:math' as math;
import 'dart:ui' as ui;

import 'package:flutter/material.dart';

import '../theme.dart';

/// The Jarvis mark: an iridescent sphere. It is static unless [animate] is
/// set, so screens that settle (and widget tests using pumpAndSettle) stay idle.
class JarvisOrb extends StatefulWidget {
  const JarvisOrb({
    required this.size,
    this.animate = false,
    this.listening = false,
    this.glow = true,
    super.key,
  });

  final double size;
  final bool animate;

  /// Adds expanding halo rings; implies [animate].
  final bool listening;
  final bool glow;

  @override
  State<JarvisOrb> createState() => _JarvisOrbState();
}

class _JarvisOrbState extends State<JarvisOrb>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 6),
  );

  bool get _running => widget.animate || widget.listening;

  @override
  void initState() {
    super.initState();
    if (_running) _controller.repeat();
  }

  @override
  void didUpdateWidget(JarvisOrb oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (_running && !_controller.isAnimating) {
      _controller.repeat();
    } else if (!_running && _controller.isAnimating) {
      _controller.stop();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final size = widget.size;
    final extent = widget.listening ? size * 1.9 : size;
    return Semantics(
      label: 'Jarvis',
      image: true,
      child: SizedBox.square(
        dimension: extent,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, _) {
            final t = _controller.value;
            final breathe = _running
                ? 1 + .035 * math.sin(t * math.pi * 4)
                : 1.0;
            return Stack(
              alignment: Alignment.center,
              children: [
                if (widget.listening)
                  for (var i = 0; i < 3; i++) _halo(size, (t * 2 + i / 3) % 1),
                Transform.scale(
                  scale: breathe,
                  child: _sphere(size, t * math.pi * 2),
                ),
              ],
            );
          },
        ),
      ),
    );
  }

  Widget _halo(double size, double progress) => IgnorePointer(
    child: Container(
      width: size * (1 + .9 * progress),
      height: size * (1 + .9 * progress),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(
          color: JarvisColors.accent.withValues(alpha: .28 * (1 - progress)),
          width: 1.5,
        ),
        color: JarvisColors.violet.withValues(alpha: .06 * (1 - progress)),
      ),
    ),
  );

  Widget _sphere(double size, double rotation) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      boxShadow: widget.glow
          ? [
              BoxShadow(
                color: JarvisColors.accent.withValues(alpha: .30),
                blurRadius: size * .38,
                offset: Offset(0, size * .12),
              ),
              BoxShadow(
                color: JarvisColors.sky.withValues(alpha: .18),
                blurRadius: size * .5,
                offset: Offset(size * .1, -size * .04),
              ),
            ]
          : null,
    ),
    child: ClipOval(
      child: Stack(
        fit: StackFit.expand,
        children: [
          ImageFiltered(
            imageFilter: ui.ImageFilter.blur(
              sigmaX: size * .08,
              sigmaY: size * .08,
              tileMode: TileMode.mirror,
            ),
            child: DecoratedBox(
              decoration: BoxDecoration(
                gradient: SweepGradient(
                  transform: GradientRotation(rotation),
                  colors: const [
                    Color(0xff5b50f0),
                    Color(0xff9d7bff),
                    Color(0xfff0a6e0),
                    Color(0xff38bdf8),
                    Color(0xff6a5cff),
                    Color(0xff5b50f0),
                  ],
                ),
              ),
            ),
          ),
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: RadialGradient(
                center: Alignment(-.35, -.5),
                radius: .75,
                colors: [
                  Color(0xd9ffffff),
                  Color(0x33ffffff),
                  Color(0x00ffffff),
                ],
                stops: [0, .45, 1],
              ),
            ),
          ),
          DecoratedBox(
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              border: Border.all(
                color: Colors.white.withValues(alpha: .55),
                width: math.max(1, size * .02),
              ),
            ),
          ),
        ],
      ),
    ),
  );
}

/// A white, rounded surface with a hairline border and optional tap target.
class SurfaceCard extends StatelessWidget {
  const SurfaceCard({
    required this.child,
    this.padding = const EdgeInsets.all(18),
    this.margin = EdgeInsets.zero,
    this.onTap,
    this.color = JarvisColors.surface,
    this.borderColor = JarvisColors.outline,
    this.radius = JarvisRadii.lg,
    this.elevated = false,
    this.gradient,
    super.key,
  });

  final Widget child;
  final EdgeInsetsGeometry padding;
  final EdgeInsetsGeometry margin;
  final VoidCallback? onTap;
  final Color color;
  final Color borderColor;
  final double radius;
  final bool elevated;
  final Gradient? gradient;

  @override
  Widget build(BuildContext context) {
    final shape = BorderRadius.circular(radius);
    return Padding(
      padding: margin,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: gradient == null ? color : null,
          gradient: gradient,
          borderRadius: shape,
          border: Border.all(color: borderColor),
          boxShadow: elevated ? JarvisShadows.soft : null,
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            borderRadius: shape,
            onTap: onTap,
            child: Padding(padding: padding, child: child),
          ),
        ),
      ),
    );
  }
}

/// A tinted rounded-square icon used as the leading visual in rows.
class IconBadge extends StatelessWidget {
  const IconBadge({
    required this.icon,
    this.color = JarvisColors.accent,
    this.size = 40,
    super.key,
  });

  final IconData icon;
  final Color color;
  final double size;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      color: color.withValues(alpha: .11),
      borderRadius: BorderRadius.circular(size * .32),
    ),
    child: Icon(icon, size: size * .5, color: color),
  );
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
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.fromLTRB(8, 4, 10, 4),
    decoration: BoxDecoration(
      color: color.withValues(alpha: .1),
      borderRadius: BorderRadius.circular(40),
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
        Text(
          label,
          style: TextStyle(
            fontSize: 12,
            fontWeight: FontWeight.w600,
            color: Color.lerp(color, JarvisColors.ink, .25),
            height: 1.2,
          ),
        ),
      ],
    ),
  );
}

typedef StatusStyle = ({String label, Color color, IconData icon});

/// Shared visual language for task, reminder, watch, and file statuses.
StatusStyle statusStyle(String status) => switch (status) {
  'running' || 'processing' => (
    label: status == 'running' ? 'In progress' : 'Processing',
    color: JarvisColors.info,
    icon: Icons.autorenew_rounded,
  ),
  'needs_approval' => (
    label: 'Needs approval',
    color: JarvisColors.warning,
    icon: Icons.gpp_maybe_outlined,
  ),
  'completed' || 'delivered' || 'indexed' || 'triggered' || 'sent' => (
    label: _titleCase(status),
    color: JarvisColors.success,
    icon: Icons.check_circle_outline_rounded,
  ),
  'failed' || 'error' || 'rejected' => (
    label: _titleCase(status),
    color: JarvisColors.danger,
    icon: Icons.error_outline_rounded,
  ),
  'cancelled' || 'canceled' || 'stopped' || 'expired' => (
    label: _titleCase(status),
    color: JarvisColors.muted,
    icon: Icons.block_rounded,
  ),
  'active' || 'pending' || 'scheduled' => (
    label: _titleCase(status),
    color: JarvisColors.accent,
    icon: Icons.schedule_rounded,
  ),
  'waiting' => (
    label: 'Waiting',
    color: JarvisColors.violet,
    icon: Icons.pause_circle_outline_rounded,
  ),
  'queued' => (
    label: 'Queued',
    color: JarvisColors.inkSoft,
    icon: Icons.schedule_rounded,
  ),
  _ => (
    label: status.isEmpty ? 'Unknown' : _titleCase(status),
    color: JarvisColors.inkSoft,
    icon: Icons.circle_outlined,
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
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              Stack(
                alignment: Alignment.center,
                children: [
                  Container(
                    width: 96,
                    height: 96,
                    decoration: const BoxDecoration(
                      shape: BoxShape.circle,
                      gradient: RadialGradient(
                        colors: [Color(0xffe9e6ff), Color(0x00e9e6ff)],
                      ),
                    ),
                  ),
                  Container(
                    width: 60,
                    height: 60,
                    decoration: BoxDecoration(
                      color: JarvisColors.surface,
                      borderRadius: BorderRadius.circular(20),
                      border: Border.all(color: JarvisColors.outline),
                      boxShadow: JarvisShadows.soft,
                    ),
                    child: Icon(icon, size: 28, color: JarvisColors.accent),
                  ),
                ],
              ),
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
                    color: JarvisColors.inkSoft,
                  ),
                ),
              ],
              if (action != null) ...[const SizedBox(height: 20), action!],
            ],
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
    icon: Icons.cloud_off_rounded,
    title: 'Something went wrong',
    message: message,
    action: onRetry == null
        ? null
        : OutlinedButton.icon(
            onPressed: onRetry,
            icon: const Icon(Icons.refresh_rounded, size: 18),
            label: const Text('Retry'),
          ),
  );
}

class LoadingState extends StatelessWidget {
  const LoadingState({super.key});

  @override
  Widget build(BuildContext context) => const Center(
    child: SizedBox.square(
      dimension: 28,
      child: CircularProgressIndicator(strokeWidth: 2.6),
    ),
  );
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
        JarvisColors.info,
        JarvisColors.infoSoft,
        Icons.info_outline_rounded,
      ),
      NoticeTone.warning => (
        JarvisColors.warning,
        JarvisColors.warningSoft,
        Icons.error_outline_rounded,
      ),
      NoticeTone.danger => (
        JarvisColors.danger,
        JarvisColors.dangerSoft,
        Icons.error_outline_rounded,
      ),
      NoticeTone.success => (
        JarvisColors.success,
        JarvisColors.successSoft,
        Icons.check_circle_outline_rounded,
      ),
    };
    return Padding(
      padding: margin,
      child: Container(
        padding: EdgeInsets.fromLTRB(14, 12, actions.isEmpty ? 14 : 6, 12),
        decoration: BoxDecoration(
          color: bg,
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          border: Border.all(color: fg.withValues(alpha: .18)),
        ),
        child: Row(
          children: [
            Icon(icon, size: 20, color: fg),
            const SizedBox(width: 10),
            Expanded(
              child: Text(
                message,
                style: const TextStyle(
                  fontSize: 13.5,
                  height: 1.4,
                  color: JarvisColors.ink,
                ),
              ),
            ),
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
          : IconBadge(
              icon: icon,
              size: 48,
              color: destructive ? JarvisColors.danger : JarvisColors.accent,
            ),
      title: Text(title),
      content: Text(message),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(dialogContext, false),
          style: TextButton.styleFrom(foregroundColor: JarvisColors.inkSoft),
          child: Text(cancelLabel),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(dialogContext, true),
          style: destructive
              ? FilledButton.styleFrom(backgroundColor: JarvisColors.danger)
              : null,
          child: Text(confirmLabel),
        ),
      ],
    ),
  );
  return confirmed ?? false;
}
