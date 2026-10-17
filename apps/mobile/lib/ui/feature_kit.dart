import 'package:flutter/material.dart';

import '../theme.dart';
import 'jarvis_ui.dart';

/// The one saturated surface on a screen: a caps label, a headline, a line of
/// context and an optional segmented progress bar, on the same ink card with
/// a soft accent glow that Home's Right now card uses.
class HeroCard extends StatelessWidget {
  const HeroCard({
    required this.label,
    required this.title,
    this.subtitle,
    this.segments,
    this.doneSegments = 0,
    this.footer,
    super.key,
  });

  final String label;
  final String title;
  final String? subtitle;

  /// Number of progress segments, with [doneSegments] filled.
  final int? segments;
  final int doneSegments;
  final Widget? footer;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(28),
        color: colors.ink,
        gradient: RadialGradient(
          center: const Alignment(.9, -1),
          radius: 1.3,
          colors: [colors.accent.withValues(alpha: .38), colors.ink],
        ),
        boxShadow: JarvisShadows.soft(colors.brightness),
      ),
      child: Padding(
        padding: const EdgeInsets.fromLTRB(22, 20, 22, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              label.toUpperCase(),
              style: JarvisType.capsOf(context)
                  .copyWith(color: colors.onInk.withValues(alpha: .6)),
            ),
            const SizedBox(height: 10),
            Text(
              title,
              style: TextStyle(
                fontFamily: 'Geist',
                fontSize: 26,
                height: 1.12,
                fontWeight: FontWeight.w700,
                letterSpacing: -.8,
                color: colors.onInk,
              ),
            ),
            if (subtitle != null) ...[
              const SizedBox(height: 8),
              Text(
                subtitle!,
                style: TextStyle(
                  fontFamily: 'Geist',
                  fontSize: 15,
                  height: 1.4,
                  color: colors.onInk.withValues(alpha: .72),
                ),
              ),
            ],
            if (segments != null && segments! > 0) ...[
              const SizedBox(height: 16),
              Row(
                children: [
                  for (var i = 0; i < segments!; i++)
                    Expanded(
                      child: AnimatedContainer(
                        duration: JarvisMotion.of(context, JarvisMotion.slow),
                        curve: JarvisMotion.standard,
                        height: 5,
                        margin: EdgeInsets.only(
                          right: i == segments! - 1 ? 0 : 5,
                        ),
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(3),
                          color: i < doneSegments
                              ? colors.onInk
                              : colors.onInk.withValues(alpha: .2),
                        ),
                      ),
                    ),
                ],
              ),
            ],
            if (footer != null) ...[const SizedBox(height: 14), footer!],
          ],
        ),
      ),
    );
  }
}

/// A list row in the app's vocabulary: tinted icon badge, title and a line of
/// detail, optional trailing control, arriving in sequence. Tappable rows ease
/// down under the finger.
class FeatureRow extends StatelessWidget {
  const FeatureRow({
    required this.icon,
    required this.title,
    this.subtitle,
    this.caption,
    this.trailing,
    this.actions,
    this.onTap,
    this.index = 0,
    this.quiet = false,
    this.strike = false,
    this.badgeColor,
    super.key,
  });

  final IconData icon;
  final String title;
  final String? subtitle;

  /// A small meta line above the title (a date, a kind).
  final String? caption;
  final Widget? trailing;

  /// Pill actions under the text.
  final List<Widget>? actions;
  final VoidCallback? onTap;
  final int index;

  /// Dims the row, for things the owner put away.
  final bool quiet;
  final bool strike;
  final Color? badgeColor;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    // At large text sizes a side control would squeeze the words, so it
    // drops under them.
    final stacked = MediaQuery.textScalerOf(context).scale(10) > 15;
    return FadeSlideIn(
      index: index,
      child: Opacity(
        opacity: quiet ? .6 : 1,
        child: SurfaceCard(
          margin: const EdgeInsets.only(bottom: 10),
          padding: const EdgeInsets.fromLTRB(14, 12, 12, 12),
          onTap: onTap,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _layout(
                stacked: stacked,
                badge: IconBadge(
                  icon: icon,
                  color: badgeColor ?? colors.accent,
                ),
                trailing: trailing,
                body: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    if (caption != null)
                      Text(
                        caption!.toUpperCase(),
                        style: JarvisType.capsOf(context)
                            .copyWith(fontSize: 10.5),
                      ),
                    Text(
                      title,
                      style: TextStyle(
                        fontFamily: 'Geist',
                        fontSize: 15.5,
                        height: 1.3,
                        fontWeight: FontWeight.w600,
                        letterSpacing: -.25,
                        color: colors.ink,
                        decoration: strike ? TextDecoration.lineThrough : null,
                      ),
                    ),
                    if (subtitle != null) ...[
                      const SizedBox(height: 2),
                      Text(
                        subtitle!,
                        style: TextStyle(
                          fontFamily: 'Geist',
                          fontSize: 13.5,
                          height: 1.35,
                          color: colors.inkSoft,
                        ),
                      ),
                    ],
                  ],
                ),
              ),
              if (actions != null && actions!.isNotEmpty) ...[
                const SizedBox(height: 10),
                Padding(
                  padding: EdgeInsets.only(left: stacked ? 0 : 48),
                  child: Wrap(spacing: 8, runSpacing: 8, children: actions!),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

Widget _layout({
  required bool stacked,
  required Widget badge,
  required Widget body,
  Widget? trailing,
}) {
  if (stacked) {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        badge,
        const SizedBox(height: 10),
        body,
        if (trailing != null) ...[const SizedBox(height: 8), trailing],
      ],
    );
  }
  return Row(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      badge,
      const SizedBox(width: 12),
      Expanded(child: body),
      if (trailing != null) ...[const SizedBox(width: 8), trailing],
    ],
  );
}

/// A pill button: ink-filled when [primary], tonal otherwise; with an optional
/// icon. Always at least 44 high.
class PillAction extends StatelessWidget {
  const PillAction({
    required this.label,
    required this.onPressed,
    this.icon,
    this.primary = false,
    this.onDark = false,
    super.key,
  });

  final String label;
  final VoidCallback? onPressed;
  final IconData? icon;
  final bool primary;

  /// For use on a [HeroCard]: light pill when primary, translucent otherwise.
  final bool onDark;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final style = FilledButton.styleFrom(
      backgroundColor: onDark
          ? (primary ? colors.onInk : colors.onInk.withValues(alpha: .16))
          : (primary ? colors.ink : colors.surfaceMuted),
      foregroundColor: onDark
          ? (primary ? colors.ink : colors.onInk)
          : (primary ? colors.onInk : colors.ink),
      disabledBackgroundColor: colors.surfaceMuted,
      minimumSize: const Size(0, 44),
      padding: EdgeInsets.fromLTRB(icon == null ? 18 : 14, 0, 18, 0),
      shape: const StadiumBorder(),
      textStyle: const TextStyle(
        fontFamily: 'Geist',
        fontSize: 14,
        fontWeight: FontWeight.w600,
        letterSpacing: -.1,
      ),
    );
    final child = Text(label);
    final leading = icon == null ? null : Icon(icon, size: 18);
    if (primary) {
      return leading == null
          ? FilledButton(onPressed: onPressed, style: style, child: child)
          : FilledButton.icon(
              onPressed: onPressed,
              style: style,
              icon: leading,
              label: child,
            );
    }
    return leading == null
        ? FilledButton.tonal(onPressed: onPressed, style: style, child: child)
        : FilledButton.tonalIcon(
            onPressed: onPressed,
            style: style,
            icon: leading,
            label: child,
          );
  }
}

/// Caps label above a group of rows, with an optional count pill.
class GroupLabel extends StatelessWidget {
  const GroupLabel(this.text, {this.count, super.key});

  final String text;
  final int? count;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 22, 4, 10),
      child: Row(
        children: [
          Flexible(
            child: Text(text.toUpperCase(), style: JarvisType.capsOf(context)),
          ),
          if (count != null) ...[
            const SizedBox(width: 8),
            Container(
              padding: const EdgeInsets.symmetric(horizontal: 7, vertical: 1),
              decoration: BoxDecoration(
                color: colors.accentSoft,
                borderRadius: BorderRadius.circular(999),
              ),
              child: Text(
                '$count',
                style: TextStyle(
                  fontFamily: 'Geist',
                  fontSize: 11,
                  fontWeight: FontWeight.w600,
                  color: colors.accentDeep,
                  fontFeatures: const [FontFeature.tabularFigures()],
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}
