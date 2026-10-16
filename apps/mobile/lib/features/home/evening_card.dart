import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart' show tileRadius;
import 'home_context.dart';
import 'next_up.dart' show clockTime;

/// The evening: what got done today, and what tomorrow starts with.
class EveningCard extends StatelessWidget {
  const EveningCard({
    required this.recap,
    required this.tomorrow,
    this.onWrite,
    this.onPlan,
    super.key,
  });

  final HomeRecap recap;
  final List<HomeAgendaItem> tomorrow;

  /// Starts a journal entry with Jarvis.
  final VoidCallback? onWrite;

  /// Asks Jarvis to plan tomorrow.
  final VoidCallback? onPlan;

  /// Worth showing: something got done or tomorrow has plans.
  static bool shows(HomeRecap recap, List<HomeAgendaItem> tomorrow) =>
      recap.remindersDone + recap.tasksDone > 0 || tomorrow.isNotEmpty;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final done = recap.remindersDone + recap.tasksDone;
    return Container(
      key: const Key('home-evening'),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(tileRadius),
        boxShadow: JarvisShadows.soft(colors.brightness),
      ),
      padding: const EdgeInsets.fromLTRB(18, 18, 18, 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('YOUR DAY', style: JarvisType.capsOf(context)),
          const SizedBox(height: 10),
          Wrap(
            spacing: 22,
            runSpacing: 6,
            children: [
              _Stat(
                value: recap.remindersDone,
                label: recap.remindersDone == 1 ? 'reminder' : 'reminders',
              ),
              _Stat(
                value: recap.tasksDone,
                label: recap.tasksDone == 1 ? 'task' : 'tasks',
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            done == 0
                ? 'A quiet day. Nothing needed ticking off.'
                : recap.done.isEmpty
                ? 'Done today.'
                : 'Done: ${recap.done.take(3).join(', ')}',
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 14, height: 1.4, color: colors.inkSoft),
          ),
          if (tomorrow.isNotEmpty) ...[
            const SizedBox(height: 16),
            Divider(height: 1, color: colors.outline),
            const SizedBox(height: 14),
            Text('TOMORROW', style: JarvisType.capsOf(context)),
            const SizedBox(height: 8),
            for (final item in tomorrow.take(3))
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 5),
                child: Row(
                  children: [
                    SizedBox(
                      width: 52,
                      child: Text(
                        item.at == null ? 'All day' : clockTime(item.at!),
                        style: TextStyle(
                          fontSize: 13,
                          fontWeight: FontWeight.w600,
                          fontFeatures: const [FontFeature.tabularFigures()],
                          color: colors.accent,
                        ),
                      ),
                    ),
                    Icon(
                      item.kind == 'event'
                          ? PhosphorIconsRegular.calendarBlank
                          : PhosphorIconsRegular.bell,
                      size: 14,
                      color: colors.muted,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        item.title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: TextStyle(fontSize: 14.5, color: colors.ink),
                      ),
                    ),
                  ],
                ),
              ),
          ],
          const SizedBox(height: 10),
          Wrap(
            spacing: 6,
            runSpacing: 4,
            children: [
              if (onWrite != null)
                _Action(
                  key: const Key('home-evening-write'),
                  icon: PhosphorIconsRegular.pencilSimple,
                  label: 'Write with Jarvis',
                  onTap: onWrite!,
                ),
              if (onPlan != null && tomorrow.isNotEmpty)
                _Action(
                  key: const Key('home-evening-plan'),
                  icon: PhosphorIconsRegular.sparkle,
                  label: 'Plan tomorrow',
                  onTap: onPlan!,
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _Stat extends StatelessWidget {
  const _Stat({required this.value, required this.label});

  final int value;
  final String label;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Row(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.baseline,
      textBaseline: TextBaseline.alphabetic,
      children: [
        RollingNumber(
          '$value',
          style: TextStyle(
            fontFamily: 'Geist',
            fontSize: 34,
            height: 1,
            fontWeight: FontWeight.w700,
            letterSpacing: -1.2,
            fontFeatures: const [FontFeature.tabularFigures()],
            color: colors.ink,
          ),
        ),
        const SizedBox(width: 6),
        Flexible(
          child: Text(
            label,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(fontSize: 14, color: colors.inkSoft),
          ),
        ),
      ],
    );
  }
}

class _Action extends StatelessWidget {
  const _Action({
    required this.icon,
    required this.label,
    required this.onTap,
    super.key,
  });

  final IconData icon;
  final String label;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return TextButton.icon(
      onPressed: () {
        unawaited(HapticFeedback.selectionClick());
        onTap();
      },
      style: TextButton.styleFrom(
        foregroundColor: colors.accent,
        minimumSize: const Size(44, 44),
        padding: const EdgeInsets.symmetric(horizontal: 10),
        textStyle: const TextStyle(
          fontFamily: 'Geist',
          fontSize: 14,
          fontWeight: FontWeight.w600,
        ),
      ),
      icon: Icon(icon, size: 16),
      label: Text(label),
    );
  }
}
