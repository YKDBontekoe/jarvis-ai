import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/motion.dart';
import '../../ui/phosphor_icons.dart';
import 'next_up.dart';

/// The top of Home: the date, the time of the next thing on the day in large
/// thin numerals, and what it is. With nothing coming up it shows the time.
class ClockHeader extends StatelessWidget {
  const ClockHeader({
    required this.now,
    required this.next,
    required this.editing,
    required this.onEdit,
    required this.onDone,
    required this.onSettings,
    this.onOpen,
    this.emptyHint,
    this.onEmptyHint,
    super.key,
  });

  final DateTime now;
  final UpNext? next;
  final bool editing;
  final VoidCallback onEdit;
  final VoidCallback onDone;
  final VoidCallback onSettings;

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
        Row(
          children: [
            Expanded(
              child: Text(
                longDate(now),
                style: TextStyle(
                  fontSize: 14,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -.1,
                  color: colors.inkSoft,
                ),
              ),
            ),
            if (editing)
              FilledButton(
                key: const Key('home-edit-done'),
                onPressed: onDone,
                style: FilledButton.styleFrom(
                  minimumSize: const Size(0, 34),
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  shape: const StadiumBorder(),
                  backgroundColor: colors.ink,
                  foregroundColor: colors.onInk,
                ),
                child: const Text('Done'),
              )
            else ...[
              _RoundIcon(
                key: const Key('home-edit'),
                tooltip: 'Edit Home',
                icon: PhosphorIconsRegular.sliders,
                onPressed: onEdit,
              ),
              const SizedBox(width: 8),
              _RoundIcon(
                key: const Key('home-settings'),
                tooltip: 'Settings',
                icon: PhosphorIconsRegular.userCircle,
                onPressed: onSettings,
              ),
            ],
          ],
        ),
        InkWell(
          key: const Key('home-clock-tap'),
          borderRadius: BorderRadius.circular(12),
          onTap: onOpen,
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              const SizedBox(height: 6),
              Row(
                crossAxisAlignment: CrossAxisAlignment.center,
                children: [
                  Text(
                    clockTime(item?.start ?? now),
                    key: const Key('home-clock-time'),
                    // Already large; scaling it again would not fit a phone.
                    textScaler: TextScaler.noScaling,
                    style: JarvisType.clock(colors.ink),
                  ),
                  if (item != null) ...[
                    const SizedBox(width: 12),
                    Flexible(
                      child: AnimatedSwitcher(
                        duration: JarvisMotion.of(context, JarvisMotion.base),
                        child: Container(
                          key: ValueKey(countdownLabel(item.start, now)),
                          padding: const EdgeInsets.symmetric(
                            horizontal: 10,
                            vertical: 4,
                          ),
                          decoration: BoxDecoration(
                            color: colors.accentSoft,
                            borderRadius: BorderRadius.circular(99),
                          ),
                          child: Text(
                            countdownLabel(item.start, now),
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 13.5,
                              fontWeight: FontWeight.w600,
                              color: colors.accent,
                            ),
                          ),
                        ),
                      ),
                    ),
                  ],
                ],
              ),
              const SizedBox(height: 12),
              Text(
                item?.title ?? 'Nothing else today',
                key: const Key('home-clock-title'),
                maxLines: 2,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 20,
                  fontWeight: FontWeight.w600,
                  letterSpacing: -.4,
                  height: 1.25,
                  color: colors.ink,
                ),
              ),
            ],
          ),
        ),
        const SizedBox(height: 6),
        if (item == null && emptyHint != null)
          InkWell(
            onTap: onEmptyHint,
            borderRadius: BorderRadius.circular(6),
            child: Text(
              emptyHint!,
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w500,
                color: colors.accent,
              ),
            ),
          )
        else
          Row(
            children: [
              if (item != null) ...[
                Icon(
                  item.reminder
                      ? PhosphorIconsRegular.bell
                      : item.detail != null
                      ? PhosphorIconsRegular.mapPin
                      : PhosphorIconsRegular.calendarBlank,
                  size: 15,
                  color: colors.muted,
                ),
                const SizedBox(width: 6),
              ],
              Flexible(
                child: Text(
                  item == null
                      ? 'Your calendar and reminders are clear'
                      : item.detail ??
                            (item.reminder
                                ? 'Reminder'
                                : shortDay(item.start, now)),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 14, color: colors.muted),
                ),
              ),
            ],
          ),
      ],
    );
  }
}

/// A small round button on the canvas, such as Settings.
class _RoundIcon extends StatelessWidget {
  const _RoundIcon({
    required this.tooltip,
    required this.icon,
    required this.onPressed,
    super.key,
  });

  final String tooltip;
  final IconData icon;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Tooltip(
      message: tooltip,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: colors.surface,
          shape: BoxShape.circle,
          border: colors.isDark ? Border.all(color: colors.outline) : null,
          boxShadow: JarvisShadows.hairline(colors.brightness),
        ),
        child: Material(
          type: MaterialType.transparency,
          child: InkWell(
            customBorder: const CircleBorder(),
            onTap: onPressed,
            child: SizedBox.square(
              dimension: 40,
              child: Icon(icon, size: 19, color: colors.ink),
            ),
          ),
        ),
      ),
    );
  }
}

/// "Today", "Tomorrow" or the weekday, for the line under the next item.
String shortDay(DateTime start, DateTime now) {
  final today = DateTime(now.year, now.month, now.day);
  final day = DateTime(start.year, start.month, start.day);
  final gap = day.difference(today).inDays;
  if (gap <= 0) return 'Today';
  if (gap == 1) return 'Tomorrow';
  return longDate(start);
}
