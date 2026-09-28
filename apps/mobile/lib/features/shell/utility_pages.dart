import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../approvals_screen.dart';
import '../../audit_screen.dart';
import '../../condition_watches_screen.dart';
import '../../daily_briefing_screen.dart';
import '../../files_screen.dart';
import '../../integrations_screen.dart';
import '../../memory_screen.dart';
import '../../reminders_screen.dart';
import '../../tasks_screen.dart';
import '../agents/agents_screen.dart';
import '../channels/channels_screen.dart';
import '../devices/devices_screen.dart';
import '../learning/learning_screen.dart';
import '../memory/knowledge_graph_screen.dart';
import '../persona/persona_screen.dart';
import '../settings/model_settings_screen.dart';
import '../settings/voice_settings_screen.dart';
import '../skills/skills_screen.dart';
import '../usage/usage_screen.dart';

Widget? utilityPageFor(String destination, Dio http) => switch (destination) {
  'tasks' => TasksScreen(http: http),
  'memory' => MemoryScreen(http: http),
  'approvals' => ApprovalsScreen(http: http),
  'reminders' => RemindersScreen(http: http),
  'files' => FilesScreen(http: http),
  'audit' => AuditScreen(http: http),
  'watches' => ConditionWatchesScreen(http: http),
  'briefing' => DailyBriefingScreen(http: http),
  'integrations' => IntegrationsScreen(http: http),
  'models' => ModelSettingsScreen(http: http),
  'skills' => SkillsScreen(http: http),
  'persona' => PersonaScreen(http: http),
  'learning' => LearningScreen(http: http),
  'graph' => KnowledgeGraphScreen(http: http),
  'channels' => ChannelsScreen(http: http),
  'agents' => AgentsScreen(http: http),
  'devices' => DevicesScreen(http: http),
  'voice-settings' => VoiceSettingsScreen(http: http),
  'usage' => UsageScreen(http: http),
  _ => null,
};
