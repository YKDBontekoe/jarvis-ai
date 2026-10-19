import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../automations/automations_screen.dart';
import '../entities/entity_ref.dart';
import '../entities/entity_screen.dart';
import '../../json_maps.dart';
import '../memory/memory_screen.dart';
import '../reminders/reminders_screen.dart';
import '../tasks/task_details_screen.dart';
import '../channels/channels_screen.dart';
import '../memory/knowledge_graph_screen.dart';
import '../skills/skills_screen.dart';
import 'message_context_screen.dart';
import 'search_models.dart';

typedef ConversationOpener = Future<void> Function(String conversationId);
typedef UtilityOpener = void Function(String destination);

SearchRouteTarget? searchRouteFromNotification(Map<String, dynamic> data) {
  final kind = asJsonString(data['routeKind']);
  if (kind == null || kind.isEmpty) return null;
  final params = <String, String>{};
  final raw = data['routeParams'];
  if (raw is Map) {
    for (final entry in raw.entries) {
      final value = asJsonString(entry.value);
      if (value != null) params[entry.key.toString()] = value;
    }
  }
  for (final key in ['conversationId', 'messageId', 'personId', 'commitmentId', 'decisionId', 'memoryId', 'fileId', 'taskId', 'reminderId', 'automationId', 'skillId', 'entityId', 'connectionId', 'peer', 'runId']) {
    final value = asJsonString(data[key]);
    if (value != null) params.putIfAbsent(_routeParamForKey(key), () => value);
  }
  return SearchRouteTarget(kind: kind, parameters: params);
}

String _routeParamForKey(String key) => switch (key) {
  'conversationId' => 'conversationId',
  'messageId' => 'messageId',
  'personId' => 'personId',
  'commitmentId' => 'commitmentId',
  'decisionId' => 'decisionId',
  'memoryId' => 'memoryId',
  'fileId' => 'fileId',
  'taskId' => 'taskId',
  'reminderId' => 'reminderId',
  'automationId' => 'automationId',
  'skillId' => 'skillId',
  'entityId' => 'entityId',
  'connectionId' => 'connectionId',
  'peer' => 'peer',
  'runId' => 'runId',
  _ => key,
};

Future<void> navigateSearchRoute(
  BuildContext context, {
  required Dio http,
  required SearchRouteTarget route,
  required ConversationOpener onConversation,
  required UtilityOpener onUtility,
}) async {
  switch (route.kind) {
    case 'conversation':
      final id = route.parameters['conversationId'];
      if (id != null) {
        await onConversation(id);
        return;
      }
    case 'message':
      final chatId = route.parameters['conversationId'];
      final messageId = route.parameters['messageId'];
      if (chatId != null && messageId != null) {
        await Navigator.of(context).push<void>(
          MaterialPageRoute<void>(
            builder: (_) => MessageContextScreen(
              http: http,
              conversationId: chatId,
              messageId: messageId,
              onOpenConversation: onConversation,
            ),
          ),
        );
        return;
      }
    case 'person' || 'commitment' || 'decision':
      final id = route.parameters['${route.kind}Id'];
      final ref = id == null ? null : EntityRef.tryParse('${route.kind}:$id');
      if (ref != null) {
        await openEntity(context, http, ref, onOpenConversation: onConversation);
        return;
      }
    case 'memory':
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(builder: (_) => MemoryScreen(http: http)),
      );
      return;
    case 'file':
      onUtility('files');
      return;
    case 'task':
      final id = route.parameters['taskId'];
      if (id != null) {
        await Navigator.of(context).push<void>(
          MaterialPageRoute<void>(builder: (_) => TaskDetailsScreen(http: http, taskId: id)),
        );
        return;
      }
    case 'reminder':
      final conversationId = route.parameters['conversationId'];
      if (conversationId != null) {
        await onConversation(conversationId);
        return;
      }
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => RemindersScreen(
            http: http,
            onOpenConversation: onConversation,
          ),
        ),
      );
      return;
    case 'automation':
      final conversationId = route.parameters['conversationId'];
      if (conversationId != null) {
        await onConversation(conversationId);
        return;
      }
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => AutomationsScreen(
            http: http,
            onOpenConversation: onConversation,
          ),
        ),
      );
      return;
    case 'skill':
      final id = route.parameters['skillId'];
      if (id != null) {
        await Navigator.of(context).push<void>(
          MaterialPageRoute<void>(
            builder: (_) => SkillDetailScreen(http: http, skillId: id),
          ),
        );
        return;
      }
      onUtility('skills');
      return;
    case 'graph_entity':
      final id = route.parameters['entityId'];
      if (id != null) {
        await Navigator.of(context).push<void>(
          MaterialPageRoute<void>(
            builder: (_) => GraphEntityScreen(http: http, entityId: id),
          ),
        );
        return;
      }
      onUtility('graph');
      return;
    case 'channel_thread':
      final connectionId = route.parameters['connectionId'];
      final peer = route.parameters['peer'];
      if (connectionId != null && peer != null) {
        await Navigator.of(context).push<void>(
          MaterialPageRoute<void>(
            builder: (_) => ChannelThreadScreen(
              http: http,
              channelId: connectionId,
              peer: peer,
            ),
          ),
        );
        return;
      }
      onUtility('channels');
      return;
    case 'coding_run':
      onUtility('coding');
      return;
    case 'journal':
      onUtility('journal');
      return;
    case 'project':
      final projectId = route.parameters['projectId'];
      onUtility(projectId == null ? 'projects' : 'project:$projectId');
      return;
    case 'mission':
      onUtility('missions');
      return;
    case 'goal':
      final goalId = route.parameters['goalId'];
      onUtility(goalId == null ? 'goals' : 'goal:$goalId');
      return;
    case 'trip':
      final tripId = route.parameters['tripId'];
      onUtility(tripId == null ? 'trips' : 'trip:$tripId');
      return;
    default:
      return;
  }
}
