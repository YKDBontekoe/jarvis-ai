import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'next_up.dart';

/// A personal greeting, followed by the next event and its context.
class ClockHeader extends StatelessWidget {
  const ClockHeader({
    required this.now,
    required this.next,
    this.preferredName,
    this.onOpen,
    this.emptyHint,
    this.onEmptyHint,
    super.key,
  });

  final DateTime now;
  final UpNext? next;
  final String? preferredName;

  /// Tapping the time or the title opens today's plan.
  final VoidCallback? onOpen;

  /// Shown under the time when nothing is coming up, such as "Connect a calendar".
  final String? emptyHint;
  final VoidCallback? onEmptyHint;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final item = next;
    return Column(
      key: const Key('home-clock'),
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 16, 4, 0),
          child: LayoutBuilder(
            builder: (context, box) {
              final style = JarvisType.displayOf(context).copyWith(
                fontSize: box.maxWidth < 330 ? 32 : 38,
                fontWeight: FontWeight.w700,
                letterSpacing: -1.2,
                height: 1.08,
              );
              final name = preferredName?.trim();
              return Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  BlurIn(
                    child: Text(
                      greetingFor(now),
                      key: const Key('home-greeting'),
                      style: style,
                    ),
                  ),
                  MotionSwitcher(
                    resize: true,
                    child: name != null && name.isNotEmpty
                        ? Padding(
                            key: ValueKey(name),
                            padding: const EdgeInsets.only(top: 2),
                            child: BlurIn(
                              delay: const Duration(milliseconds: 140),
                              // The name catches the orb's iridescence.
                              child: ShaderMask(
                                blendMode: BlendMode.srcIn,
                                shaderCallback: (bounds) => LinearGradient(
                                  colors: [
                                    colors.accent,
                                    colors.violet,
                                    Color.lerp(colors.violet, colors.rose, .6)!,
                                  ],
                                ).createShader(bounds),
                                child: Text(
                                  name,
                                  key: const Key('home-name'),
                                  style: style.copyWith(color: colors.accent),
                                ),
                              ),
                            ),
                          )
                        : const SizedBox.shrink(key: ValueKey('no-name')),
                  ),
                  const SizedBox(height: 10),
                  FadeSlideIn(
                    index: 3,
                    child: Text(
                      longDate(now),
                      style: TextStyle(fontSize: 13, color: colors.inkSoft),
                    ),
                  ),
                ],
              );
            },
          ),
        ),
        const SizedBox(height: 24),
        FadeSlideIn(
          index: 2,
          offset: 16,
          scale: .97,
          child: Sheen(
            delay: const Duration(milliseconds: 520),
            strength: .28,
            borderRadius: BorderRadius.circular(JarvisRadii.xl),
            child: SurfaceCard(
              key: const Key('home-clock-tap'),
              radius: JarvisRadii.xl,
              gradient: colors.litSurface(colors.accent, strength: .06),
              borderColor: colors.outline.withValues(
                alpha: colors.isDark ? .6 : .3,
              ),
              padding: const EdgeInsets.fromLTRB(20, 18, 20, 20),
              onTap: onOpen,
              child: MotionSwitcher(
                resize: true,
                child: Column(
                  key: ValueKey((
                    item?.title,
                    item?.start,
                    item?.detail,
                    item?.reminder,
                  )),
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Row(
                      children: [
                        Expanded(
                          child: Text(
                            item == null
                                ? 'Your day'
                                : item.start.isAfter(now)
                                ? 'Next up'
                                : 'Happening now',
                            style: JarvisType.sectionOf(
                              context,
                            ).copyWith(color: colors.accentDeep),
                          ),
                        ),
                        if (onOpen != null)
                          Icon(
                            PhosphorIconsRegular.arrowUpRight,
                            size: 16,
                            color: colors.inkSoft,
                          ),
                      ],
                    ),
                    const SizedBox(height: 14),
                    if (item != null)
                      _EventHeading(item: item)
                    else ...[
                      Text(
                        'Nothing else today',
                        key: const Key('home-clock-title'),
                        style: JarvisType.displayOf(
                          context,
                        ).copyWith(fontSize: 26),
                      ),
                      const SizedBox(height: 8),
                      Text(
                        clockTime(now),
                        key: const Key('home-clock-time'),
                        style: JarvisType.clock(
                          colors.inkSoft,
                        ).copyWith(fontSize: 32, letterSpacing: -1),
                      ),
                    ],
                    const SizedBox(height: 12),
                    if (item != null) ...[
                      Wrap(
                        spacing: 6,
                        runSpacing: 4,
                        children: [
                          Text(
                            shortDay(item.start, now),
                            style: TextStyle(
                              fontSize: 14,
                              color: colors.inkSoft,
                            ),
                          ),
                          Text('·', style: TextStyle(color: colors.inkSoft)),
                          Text(
                            countdownLabel(item.start, now, durationOnly: true),
                            style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w500,
                              color: colors.accent,
                            ),
                          ),
                        ],
                      ),
                      if (item.detail != null || item.reminder) ...[
                        const SizedBox(height: 8),
                        Text(
                          item.detail ?? 'Reminder',
                          style: TextStyle(fontSize: 14, color: colors.inkSoft),
                        ),
                      ],
                    ] else if (emptyHint != null)
                      TextButton(
                        onPressed: onEmptyHint,
                        style: TextButton.styleFrom(
                          padding: EdgeInsets.zero,
                          alignment: Alignment.centerLeft,
                          foregroundColor: colors.accent,
                        ),
                        child: Text(emptyHint!),
                      )
                    else
                      Text(
                        'Your calendar and reminders are clear',
                        style: TextStyle(fontSize: 14, color: colors.inkSoft),
                      ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }
}

/// Move the time to its own line when the complete heading does not fit.
/// The separator belongs to the inline layout, never to a wrapped title.
class _EventHeading extends StatelessWidget {
  const _EventHeading({required this.item});

  final UpNext item;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final titleStyle = JarvisType.displayOf(
      context,
    ).copyWith(fontSize: 30, height: 1.15);
    final timeStyle = TextStyle(
      fontFamily: 'Geist',
      fontSize: 26,
      fontWeight: FontWeight.w300,
      color: colors.inkSoft,
      fontFeatures: const [FontFeature.tabularFigures()],
    );
    final title = Text(
      item.title,
      key: const Key('home-clock-title'),
      style: titleStyle,
    );
    final time = Text(
      clockTime(item.start),
      key: const Key('home-clock-time'),
      style: timeStyle,
    );
    final scaler = MediaQuery.textScalerOf(context);
    double width(String text, TextStyle style) {
      final painter = TextPainter(
        text: TextSpan(text: text, style: style),
        textDirection: Directionality.of(context),
        textScaler: scaler,
      )..layout();
      final result = painter.width;
      painter.dispose();
      return result;
    }

    return LayoutBuilder(
      builder: (context, box) {
        final separatorStyle = titleStyle.copyWith(
          fontSize: 22,
          color: colors.inkSoft,
        );
        final total =
            width(item.title, titleStyle) +
            width(clockTime(item.start), timeStyle) +
            width('·', separatorStyle) +
            16;
        if (total > box.maxWidth) {
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [title, const SizedBox(height: 6), time],
          );
        }
        return Row(
          children: [
            title,
            const SizedBox(width: 8),
            Text('·', style: separatorStyle),
            const SizedBox(width: 8),
            time,
          ],
        );
      },
    );
  }
}

/// Follows the device's local time, including changes while Home stays open.
String greetingFor(DateTime now) => switch (now.hour) {
  < 12 => 'Good morning',
  < 18 => 'Good afternoon',
  _ => 'Good evening',
};

/// "Today", "Tomorrow" or the weekday, for the line under the next item.
String shortDay(DateTime start, DateTime now) {
  final today = DateTime(now.year, now.month, now.day);
  final day = DateTime(start.year, start.month, start.day);
  final gap = day.difference(today).inDays;
  if (gap <= 0) return 'Today';
  if (gap == 1) return 'Tomorrow';
  return longDate(start);
}
