import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../approvals_screen.dart';
import '../../audit_screen.dart';
import '../../condition_watches_screen.dart';
import '../../daily_briefing_screen.dart';
import '../../files_screen.dart';
import '../../integrations_screen.dart';
import '../../memory_screen.dart';
import '../../automations_screen.dart';
import '../../reminders_screen.dart';
import '../../tasks_screen.dart';
import '../agents/agents_screen.dart';
import '../channels/channels_screen.dart';
import '../coding/coding_runs_screen.dart';
import '../devices/devices_screen.dart';
import '../journal/journal_screen.dart';
import '../learning/learning_screen.dart';
import '../memory/knowledge_graph_screen.dart';
import '../persona/persona_screen.dart';
import '../profiles/profiles_screen.dart';
import '../projects/project_screen.dart';
import '../projects/projects_screen.dart';
import '../review/weekly_review_screen.dart';
import '../settings/appearance_screen.dart';
import '../settings/model_settings_screen.dart';
import '../settings/voice_settings_screen.dart';
import '../skills/skills_screen.dart';
import '../usage/usage_screen.dart';

/// Destination of one project's page: the prefix followed by its id.
const projectDestinationPrefix = 'project:';

Widget? utilityPageFor(
  String destination,
  Dio http, {
  Future<void> Function(String conversationId)? onOpenConversation,
  ValueChanged<String>? onAskInChat,
}) => switch (destination) {
  'tasks' => TasksScreen(http: http),
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
  'journal' => JournalScreen(http: http, onTalkAboutDay: onAskInChat),
  'approvals' => ApprovalsScreen(http: http),
  'reminders' => RemindersScreen(
    http: http,
    onOpenConversation: onOpenConversation,
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
  'models' => ModelSettingsScreen(http: http),
  'skills' => SkillsScreen(http: http),
  'persona' => PersonaScreen(http: http),
  'profiles' => ProfilesScreen(http: http),
  'learning' => LearningScreen(http: http),
  'graph' => KnowledgeGraphScreen(http: http),
  'channels' => ChannelsScreen(http: http),
  'coding' => CodingRunsScreen(http: http),
  'agents' => AgentsScreen(http: http),
  'devices' => DevicesScreen(http: http),
  'voice-settings' => VoiceSettingsScreen(http: http),
  'usage' => UsageScreen(http: http),
  _ => null,
};
