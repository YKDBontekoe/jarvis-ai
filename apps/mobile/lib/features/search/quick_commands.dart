import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import '../shell/utility_pages.dart';

/// Something search can do rather than find: start a reminder, jump to a page.
class QuickCommand {
  const QuickCommand({
    required this.label,
    required this.icon,
    required this.destination,
    required this.keywords,
    this.creates = false,
  });

  final String label;
  final IconData icon;

  /// A utility destination, e.g. `reminders/new` or `habits`.
  final String destination;

  /// Lower-case words (English and Dutch) that should surface this command.
  final List<String> keywords;

  /// Creation commands are offered before anything is typed.
  final bool creates;
}

const quickCommands = [
  QuickCommand(
    label: 'New reminder',
    icon: PhosphorIconsRegular.bell,
    destination: 'reminders$createDestinationSuffix',
    keywords: ['new reminder', 'remind', 'reminder', 'herinner', 'herinnering'],
    creates: true,
  ),
  QuickCommand(
    label: 'Start a background task',
    icon: PhosphorIconsRegular.listChecks,
    destination: 'tasks$createDestinationSuffix',
    keywords: ['new task', 'task', 'background', 'taak', 'opdracht'],
    creates: true,
  ),
  QuickCommand(
    label: 'Add a memory',
    icon: PhosphorIconsRegular.notebook,
    destination: 'memory$createDestinationSuffix',
    keywords: ['add memory', 'remember', 'memory', 'onthoud', 'geheugen'],
    creates: true,
  ),
  QuickCommand(
    label: 'Go to Today',
    icon: PhosphorIconsRegular.sunHorizon,
    destination: 'today',
    keywords: ['today', 'plan', 'day', 'vandaag', 'planning'],
  ),
  QuickCommand(
    label: 'Go to Reminders',
    icon: PhosphorIconsRegular.bell,
    destination: 'reminders',
    keywords: ['reminders', 'herinneringen'],
  ),
  QuickCommand(
    label: 'Go to Tasks',
    icon: PhosphorIconsRegular.listChecks,
    destination: 'tasks',
    keywords: ['tasks', 'taken'],
  ),
  QuickCommand(
    label: 'Go to Memory',
    icon: PhosphorIconsRegular.notebook,
    destination: 'memory',
    keywords: ['memory', 'memories', 'geheugen'],
  ),
  QuickCommand(
    label: 'Go to Habits',
    icon: PhosphorIconsRegular.target,
    destination: 'habits',
    keywords: ['habits', 'habit', 'streak', 'gewoonte', 'gewoontes'],
  ),
  QuickCommand(
    label: 'Go to Library',
    icon: PhosphorIconsRegular.bookOpen,
    destination: 'library',
    keywords: ['library', 'saved', 'flashcards', 'research', 'bibliotheek', 'leren'],
  ),
  QuickCommand(
    label: 'Go to Inbox',
    icon: PhosphorIconsRegular.chatsCircle,
    destination: 'inbox',
    keywords: ['inbox', 'reply', 'promises', 'commitments', 'beloofd', 'berichten'],
  ),
  QuickCommand(
    label: 'Go to Timeline',
    icon: PhosphorIconsRegular.clockCounterClockwise,
    destination: 'timeline',
    keywords: ['timeline', 'history', 'rewind', 'on this day', 'tijdlijn'],
  ),
  QuickCommand(
    label: 'Go to Expenses',
    icon: PhosphorIconsRegular.wallet,
    destination: 'expenses',
    keywords: ['expenses', 'expense', 'spend', 'uitgaven', 'kosten'],
  ),
  QuickCommand(
    label: 'Go to Budgets & subscriptions',
    icon: PhosphorIconsRegular.chartLine,
    destination: 'finance',
    keywords: ['finance', 'budget', 'budgets', 'subscriptions', 'abonnementen', 'forecast', 'import'],
  ),
  QuickCommand(
    label: 'Go to Journal',
    icon: PhosphorIconsRegular.pencilSimple,
    destination: 'journal',
    keywords: ['journal', 'dagboek'],
  ),
  QuickCommand(
    label: 'Go to People',
    icon: PhosphorIconsRegular.users,
    destination: 'people',
    keywords: ['people', 'person', 'birthday', 'mensen', 'verjaardag'],
  ),
  QuickCommand(
    label: 'Connected apps',
    icon: PhosphorIconsRegular.plugsConnected,
    destination: 'integrations',
    keywords: ['apps', 'integrations', 'connect', 'koppel'],
  ),
];

/// Creation commands for an empty query; otherwise up to three commands whose
/// label or keywords start with, or contain, what was typed.
List<QuickCommand> matchQuickCommands(String query) {
  final text = query.trim().toLowerCase();
  if (text.isEmpty) return quickCommands.where((c) => c.creates).toList();
  if (text.length < 2) return const [];
  bool matches(QuickCommand command) =>
      command.label.toLowerCase().contains(text) ||
      command.keywords.any(
        (keyword) => keyword.startsWith(text) || text.startsWith(keyword),
      );
  return quickCommands.where(matches).take(3).toList();
}

/// A tappable command row shared by the search screen and the palette.
class QuickCommandTile extends StatelessWidget {
  const QuickCommandTile({
    required this.command,
    required this.onTap,
    super.key,
  });

  final QuickCommand command;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return ListTile(
      leading: Container(
        width: 34,
        height: 34,
        decoration: BoxDecoration(
          color: command.creates ? colors.accentSoft : colors.surfaceMuted,
          borderRadius: BorderRadius.circular(10),
        ),
        child: Icon(
          command.icon,
          size: 18,
          color: command.creates ? colors.accent : colors.inkSoft,
        ),
      ),
      title: Text(command.label),
      trailing: Icon(
        command.creates
            ? PhosphorIconsRegular.plus
            : PhosphorIconsRegular.caretRight,
        size: 16,
        color: colors.muted,
      ),
      onTap: onTap,
    );
  }
}

/// "Do" heading above command rows.
class QuickCommandHeader extends StatelessWidget {
  const QuickCommandHeader(this.label, {super.key});

  final String label;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
    child: Text(
      label.toUpperCase(),
      style: TextStyle(
        fontSize: 11.5,
        fontWeight: FontWeight.w600,
        letterSpacing: .6,
        color: JarvisColors.of(context).muted,
      ),
    ),
  );
}
