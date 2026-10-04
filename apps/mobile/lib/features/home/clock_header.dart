import 'package:flutter/material.dart';

import '../../theme.dart';
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
                  fontSize: 13,
                  fontWeight: FontWeight.w500,
                  color: colors.muted,
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
              IconButton(
                key: const Key('home-edit'),
                tooltip: 'Edit Home',
                onPressed: onEdit,
                icon: Icon(
                  PhosphorIconsRegular.sliders,
                  size: 20,
                  color: colors.inkSoft,
                ),
              ),
              IconButton(
                key: const Key('home-settings'),
                tooltip: 'Settings',
                onPressed: onSettings,
                icon: Icon(
                  PhosphorIconsRegular.userCircle,
                  size: 22,
                  color: colors.inkSoft,
                ),
              ),
            ],
          ],
        ),
        const SizedBox(height: 6),
        Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          children: [
            Text(
              clockTime(item?.start ?? now),
              key: const Key('home-clock-time'),
              // Already large; scaling it again would not fit a phone.
              textScaler: TextScaler.noScaling,
              style: JarvisType.clock(colors.ink),
            ),
            if (item != null) ...[
              const SizedBox(width: 10),
              Flexible(
                child: Text(
                  countdownLabel(item.start, now),
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 15,
                    fontWeight: FontWeight.w500,
                    color: colors.accent,
                  ),
                ),
              ),
            ],
          ],
        ),
        const SizedBox(height: 10),
        Text(
          item?.title ?? 'Nothing else today',
          key: const Key('home-clock-title'),
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: 18,
            fontWeight: FontWeight.w600,
            letterSpacing: -.3,
            color: colors.ink,
          ),
        ),
        const SizedBox(height: 2),
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
          Text(
            item == null
                ? 'Your calendar and reminders are clear'
                : item.detail ??
                      (item.reminder ? 'Reminder' : shortDay(item.start, now)),
            style: TextStyle(fontSize: 14, color: colors.muted),
          ),
      ],
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
