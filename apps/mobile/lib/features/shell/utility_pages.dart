import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../activity/activity_screen.dart';
import '../approvals/approvals_screen.dart';
import '../watches/condition_watches_screen.dart';
import '../briefing/daily_briefing_screen.dart';
import '../files/files_screen.dart';
import '../finance/finance_screen.dart';
import '../inbox/inbox_screen.dart';
import '../integrations/integrations_screen.dart';
import '../integrations/server_detail_screen.dart';
import '../tiles/app_tiles.dart';
import '../memory/memory_screen.dart';
import '../missions/mission_result_screen.dart';
import '../missions/mission_starter_screen.dart';
import '../missions/missions_screen.dart';
import '../modes/modes_screen.dart';
import '../automations/automations_screen.dart';
import '../reminders/reminders_screen.dart';
import '../tasks/tasks_screen.dart';
import '../agents/agents_screen.dart';
import '../channels/channels_screen.dart';
import '../coding/coding_runs_screen.dart';
import '../comparisons/comparison_screen.dart';
import '../comparisons/comparisons_screen.dart';
import '../apps/app_goals_screen.dart';
import '../apps/build_app_screen.dart';
import '../documents/document_screen.dart';
import '../documents/documents_screen.dart';
import '../notifications/notification_settings_screen.dart';
import '../settings/home_zone_screen.dart';
import '../settings/queued_changes_screen.dart';
import '../setup/setup_screen.dart';
import '../stories/stories_screen.dart';
import '../stories/story_screen.dart';
import '../finance/subscription_review_screen.dart';
import '../goals/goal_screen.dart';
import '../opportunities/opportunities_screen.dart';
import '../library/lesson_screen.dart';
import '../scenarios/scenarios_screen.dart';
import '../goals/goals_screen.dart';
import '../meetings/meeting_screen.dart';
import '../meetings/meetings_screen.dart';
import '../decisions/decisions_screen.dart';
import '../entities/entity_ref.dart';
import '../entities/entity_screen.dart';
import '../devices/devices_screen.dart';
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
  'activity' => ActivityScreen(
    http: http,
    onOpenConversation: onOpenConversation,
  ),
  final entity when entity.startsWith(entityDestinationPrefix) =>
    switch (EntityRef.fromDestination(entity)) {
      final ref? => EntityScreen(
        http: http,
        entity: ref,
        onOpenConversation: onOpenConversation,
      ),
      null => null,
    },
  'memory' => MemoryScreen(http: http, onOpenConversation: onOpenConversation),
  'memory$createDestinationSuffix' => MemoryScreen(
    http: http,
    startCreating: true,
    onCreateDone: onQuickCreateDone,
  ),
  'journal' => JournalScreen(http: http, onTalkAboutDay: onAskInChat),
  'decisions' => DecisionsScreen(http: http),
  'comparisons' => ComparisonsScreen(http: http),
  'find-app' => AppGoalsScreen(http: http, onAskInChat: onAskInChat),
  'build-app' => BuildAppScreen(http: http, onAskInChat: onAskInChat),
  final lesson when lesson.startsWith('lesson:') => LessonScreen(
    http: http,
    itemId: lesson.substring('lesson:'.length),
  ),
  'notification-settings' => NotificationSettingsScreen(http: http),
  'home-zone' => HomeZoneScreen(http: http),
  'queued-changes' => QueuedChangesScreen(http: http),
  'setup' => SetupScreen(
    http: http,
    onAskInChat: onAskInChat,
    pageFor: (destination) =>
        utilityPageFor(destination, http, onAskInChat: onAskInChat),
  ),
  'stories' => StoriesScreen(http: http),
  final story when story.startsWith('story:') => StoryScreen(
    http: http,
    storyId: story.substring('story:'.length),
  ),
  'documents' => DocumentsScreen(http: http),
  final document when document.startsWith('document:') => DocumentScreen(
    http: http,
    documentId: document.substring('document:'.length),
  ),
  'opportunities' => OpportunitiesScreen(
    http: http,
    pageFor: (destination) => utilityPageFor(
      destination == 'plan-today' ? 'today' : destination,
      http,
      onAskInChat: onAskInChat,
    ),
  ),
  final subscription when subscription.startsWith('subscription:') =>
    SubscriptionReviewScreen(
      http: http,
      subscriptionId: subscription.substring('subscription:'.length),
      onAskInChat: onAskInChat,
    ),
  'scenarios' => ScenariosScreen(http: http),
  'goals' => GoalsScreen(http: http),
  final goal when goal.startsWith('goal:') => GoalScreen(
    http: http,
    goalId: goal.substring('goal:'.length),
  ),
  'meetings' => MeetingsScreen(http: http),
  final meeting when meeting.startsWith('meeting:') => MeetingScreen(
    http: http,
    meetingId: meeting.substring('meeting:'.length),
  ),
  final comparison when comparison.startsWith('comparison:') =>
    ComparisonScreen(
      http: http,
      comparisonId: comparison.substring('comparison:'.length),
    ),
  'today' => DayPlannerScreen(http: http, onAskInChat: onAskInChat),
  'timeline' => TimelineScreen(http: http),
  'missions' => MissionsScreen(http: http),
  'mission-starter' => MissionStarterScreen(http: http),
  final result when result.startsWith('mission-result:') => MissionResultScreen(
    http: http,
    missionId: result.substring('mission-result:'.length),
  ),
  'modes' => ModesScreen(http: http),
  'library' => LibraryScreen(http: http),
  'inbox' => InboxScreen(http: http),
  // Expenses live in Finance's Spending tab, and the audit log in Activity.
  'expenses' => FinanceScreen(
    http: http,
    onAskInChat: onAskInChat,
    initialTab: FinanceTab.spending,
  ),
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
  'files' => FilesScreen(http: http, onOpenConversation: onOpenConversation),
  'audit' => ActivityScreen(
    http: http,
    onOpenConversation: onOpenConversation,
    initialFilter: ActivityFilter.audit,
  ),
  'watches' => ConditionWatchesScreen(http: http),
  'automations' => AutomationsScreen(
    http: http,
    onOpenConversation: onOpenConversation,
  ),
  'briefing' => DailyBriefingScreen(http: http),
  'weekly-review' => WeeklyReviewScreen(http: http),
  'integrations' => IntegrationsScreen(http: http, onAskInChat: onAskInChat),
  'integrations$createDestinationSuffix' => IntegrationsScreen(
    http: http,
    onAskInChat: onAskInChat,
    startBrowsing: true,
  ),
  final app when app.startsWith(appDestinationPrefix) => switch (app.substring(
    appDestinationPrefix.length,
  )) {
    final key when key.startsWith(appTileHostPrefix) => McpServerDetailScreen(
      http: http,
      hostName: key.substring(appTileHostPrefix.length),
      onAskInChat: onAskInChat,
    ),
    final key when key.isNotEmpty => McpServerDetailScreen(
      http: http,
      serverId: key,
      onAskInChat: onAskInChat,
    ),
    _ => null,
  },
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
