import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../approvals/approvals_screen.dart';
import '../audit/audit_screen.dart';
import '../watches/condition_watches_screen.dart';
import '../briefing/daily_briefing_screen.dart';
import '../files/files_screen.dart';
import '../finance/finance_screen.dart';
import '../inbox/inbox_screen.dart';
import '../integrations/integrations_screen.dart';
import '../memory/memory_screen.dart';
import '../missions/missions_screen.dart';
import '../modes/modes_screen.dart';
import '../automations/automations_screen.dart';
import '../reminders/reminders_screen.dart';
import '../tasks/tasks_screen.dart';
import '../agents/agents_screen.dart';
import '../channels/channels_screen.dart';
import '../coding/coding_runs_screen.dart';
import '../decisions/decisions_screen.dart';
import '../devices/devices_screen.dart';
import '../expenses/expenses_screen.dart';
import '../habits/habits_screen.dart';
import '../journal/journal_screen.dart';
import '../library/library_screen.dart';
import '../learning/learning_screen.dart';
import '../memory/knowledge_graph_screen.dart';
import '../people/people_screen.dart';
import '../persona/persona_screen.dart';
import '../planner/day_planner_screen.dart';
import '../profiles/profiles_screen.dart';
import '../projects/project_screen.dart';
import '../projects/projects_screen.dart';
import '../review/weekly_review_screen.dart';
import '../settings/app_lock_screen.dart';
import '../settings/appearance_screen.dart';
import '../settings/model_settings_screen.dart';
import '../settings/voice_settings_screen.dart';
import '../skills/skills_screen.dart';
import '../timeline/timeline_screen.dart';
import '../usage/usage_screen.dart';
import '../whatsapp/whatsapp_screen.dart';

/// Destination of one project's page: the prefix followed by its id.
const projectDestinationPrefix = 'project:';

/// Appended to a destination to open its page with the "new" editor already
/// showing, e.g. `reminders/new` from the chat's quick actions.
const createDestinationSuffix = '/new';

/// [destination] without [createDestinationSuffix].
String utilityBaseDestination(String destination) =>
    destination.endsWith(createDestinationSuffix)
    ? destination.substring(
        0,
        destination.length - createDestinationSuffix.length,
      )
    : destination;

Widget? utilityPageFor(
  String destination,
  Dio http, {
  Future<void> Function(String conversationId)? onOpenConversation,
  ValueChanged<String>? onAskInChat,

  /// For `…/new` destinations: called when the editor closes, with a
  /// confirmation when it saved or null when it was cancelled.
  ValueChanged<String?>? onQuickCreateDone,
}) => switch (destination) {
  'tasks' => TasksScreen(http: http),
  'tasks$createDestinationSuffix' => TasksScreen(
    http: http,
    startCreating: true,
    onCreateDone: onQuickCreateDone,
  ),
  'projects' => ProjectsScreen(
    http: http,
    onOpenConversation: onOpenConversation,
  ),
  final project when project.startsWith(projectDestinationPrefix) =>
    ProjectScreen(
      http: http,
      projectId: project.substring(projectDestinationPrefix.length),
      onOpenConversation: onOpenConversation,
    ),
  'memory' => MemoryScreen(http: http),
  'memory$createDestinationSuffix' => MemoryScreen(
    http: http,
    startCreating: true,
    onCreateDone: onQuickCreateDone,
  ),
  'journal' => JournalScreen(http: http, onTalkAboutDay: onAskInChat),
  'decisions' => DecisionsScreen(http: http),
  'today' => DayPlannerScreen(http: http, onAskInChat: onAskInChat),
  'timeline' => TimelineScreen(http: http),
  'missions' => MissionsScreen(http: http),
  'modes' => ModesScreen(http: http),
  'library' => LibraryScreen(http: http),
  'inbox' => InboxScreen(http: http),
  'expenses' => ExpensesScreen(http: http),
  'finance' => FinanceScreen(http: http, onAskInChat: onAskInChat),
  'habits' => HabitsScreen(http: http),
  'people' => PeopleScreen(http: http),
  'approvals' => ApprovalsScreen(http: http),
  'reminders' => RemindersScreen(
    http: http,
    onOpenConversation: onOpenConversation,
  ),
  'reminders$createDestinationSuffix' => RemindersScreen(
    http: http,
    onOpenConversation: onOpenConversation,
    startCreating: true,
    onCreateDone: onQuickCreateDone,
  ),
  'notifications' => RemindersScreen(
    http: http,
    onOpenConversation: onOpenConversation,
    initialTab: RemindersTab.notifications,
  ),
  'files' => FilesScreen(http: http),
  'audit' => AuditScreen(http: http),
  'watches' => ConditionWatchesScreen(http: http),
  'automations' => AutomationsScreen(
    http: http,
    onOpenConversation: onOpenConversation,
  ),
  'briefing' => DailyBriefingScreen(http: http),
  'weekly-review' => WeeklyReviewScreen(http: http),
  'integrations' => IntegrationsScreen(http: http, onAskInChat: onAskInChat),
  'appearance' => const AppearanceScreen(),
  'app-lock' => const AppLockScreen(),
  'models' => ModelSettingsScreen(http: http),
  'skills' => SkillsScreen(http: http),
  'persona' => PersonaScreen(http: http),
  'profiles' => ProfilesScreen(http: http),
  'learning' => LearningScreen(http: http),
  'graph' => KnowledgeGraphScreen(http: http),
  'channels' => ChannelsScreen(http: http),
  'whatsapp' => WhatsAppScreen(http: http),
  'coding' => CodingRunsScreen(http: http),
  'agents' => AgentsScreen(http: http),
  'devices' => DevicesScreen(http: http),
  'voice-settings' => VoiceSettingsScreen(http: http),
  'usage' => UsageScreen(http: http),
  _ => null,
};
