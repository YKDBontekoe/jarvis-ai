import 'package:flutter/widgets.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

/// Which colour pair a notification's icon uses. Calm by default; warning only for something that went wrong or is due.
enum NotificationTone { neutral, accent, info, warning }

/// How one notification type reads in a list: a caps label that says what kind of thing it is, its icon and tone, and
/// the next step tapping the row takes (null when tapping only marks it read).
@immutable
class NotificationLook {
  const NotificationLook(
    this.label,
    this.icon, {
    this.tone = NotificationTone.neutral,
    this.nextStep,
  });

  final String label;
  final IconData icon;
  final NotificationTone tone;
  final String? nextStep;

  (Color foreground, Color background) colors(JarvisColors colors) =>
      switch (tone) {
        NotificationTone.accent => (colors.accent, colors.accentSoft),
        NotificationTone.info => (colors.info, colors.infoSoft),
        NotificationTone.warning => (colors.warning, colors.warningSoft),
        NotificationTone.neutral => (colors.ink, colors.surfaceMuted),
      };
}

/// The look for a server notification type. Unknown types fall back to a plain bell so new server types still show.
NotificationLook notificationLook(String? type) => switch (type) {
  'reminder.due' => const NotificationLook(
    'Reminder',
    PhosphorIconsRegular.alarm,
    tone: NotificationTone.warning,
    nextStep: 'Open',
  ),
  'reminder.failed' => const NotificationLook(
    'Reminder failed',
    PhosphorIconsRegular.alarm,
    tone: NotificationTone.warning,
    nextStep: 'See what happened',
  ),
  'whatsapp.reminder' => const NotificationLook(
    'WhatsApp reminder',
    PhosphorIconsRegular.whatsappLogo,
    nextStep: 'See details',
  ),
  'task.completed' => const NotificationLook(
    'Task done',
    PhosphorIconsRegular.checkCircle,
    tone: NotificationTone.info,
    nextStep: 'See the result',
  ),
  'task.failed' => const NotificationLook(
    'Task failed',
    PhosphorIconsRegular.warningCircle,
    tone: NotificationTone.warning,
    nextStep: 'See what happened',
  ),
  'approval.required' || 'automation.approval' => const NotificationLook(
    'Needs your approval',
    PhosphorIconsRegular.shieldCheck,
    tone: NotificationTone.accent,
    nextStep: 'Review',
  ),
  'coding.pr.ready' || 'coding.run.ready' => const NotificationLook(
    'Code change ready',
    PhosphorIconsRegular.code,
    tone: NotificationTone.info,
    nextStep: 'Review the change',
  ),
  'watch.triggered' => const NotificationLook(
    'Watch',
    PhosphorIconsRegular.pulse,
    tone: NotificationTone.info,
    nextStep: 'See details',
  ),
  'watch.failed' => const NotificationLook(
    'Watch failed',
    PhosphorIconsRegular.pulse,
    tone: NotificationTone.warning,
    nextStep: 'See what happened',
  ),
  'briefing.daily' => const NotificationLook(
    'Morning brief',
    PhosphorIconsRegular.sunHorizon,
    tone: NotificationTone.accent,
    nextStep: 'Read the brief',
  ),
  'briefing.weekly' => const NotificationLook(
    'Weekly review',
    PhosphorIconsRegular.calendarCheck,
    tone: NotificationTone.accent,
    nextStep: 'Open the review',
  ),
  'habit.checkin' => const NotificationLook(
    'Habit check-in',
    PhosphorIconsRegular.repeat,
    nextStep: 'Open habits',
  ),
  'people.birthday' ||
  'people.checkin' ||
  'people.radar' => const NotificationLook(
    'People',
    PhosphorIconsRegular.user,
    nextStep: 'Open person',
  ),
  'routine.suggested' => const NotificationLook(
    'Routine idea',
    PhosphorIconsRegular.arrowsClockwise,
    tone: NotificationTone.accent,
    nextStep: 'Review it',
  ),
  'budget.alert' => const NotificationLook(
    'Budget',
    PhosphorIconsRegular.wallet,
    tone: NotificationTone.warning,
  ),
  'skill.learned' => const NotificationLook(
    'Jarvis learned',
    PhosphorIconsRegular.magicWand,
    tone: NotificationTone.accent,
    nextStep: 'Open skill',
  ),
  'skill.improved' => const NotificationLook(
    'Jarvis improved a skill',
    PhosphorIconsRegular.magicWand,
    tone: NotificationTone.accent,
    nextStep: 'Open skill',
  ),
  'skill.retired' => const NotificationLook(
    'Skill turned off',
    PhosphorIconsRegular.prohibit,
    nextStep: 'Open skill',
  ),
  'improvement.suggested' => const NotificationLook(
    'Suggestion to review',
    PhosphorIconsRegular.lightbulb,
    tone: NotificationTone.accent,
    nextStep: 'Review in Learning',
  ),
  'improvement.capability_built' => const NotificationLook(
    'App ready to switch on',
    PhosphorIconsRegular.puzzlePiece,
    tone: NotificationTone.accent,
    nextStep: 'Review in Learning',
  ),
  'learning.dreamed' || 'learning.reflected' => const NotificationLook(
    'Jarvis learned',
    PhosphorIconsRegular.sparkle,
    tone: NotificationTone.accent,
    nextStep: 'Open Learning',
  ),
  'heartbeat.checkin' => const NotificationLook(
    'Check-in',
    PhosphorIconsRegular.chatCircle,
  ),
  'digest.while_away' => const NotificationLook(
    'While you were away',
    PhosphorIconsRegular.clockCounterClockwise,
  ),
  _ => const NotificationLook('Notification', PhosphorIconsRegular.bell),
};
