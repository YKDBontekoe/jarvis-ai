import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../automations_screen.dart';
import '../../json_maps.dart';
import '../../memory_screen.dart';
import '../../reminders_screen.dart';
import '../../task_details_screen.dart';
import '../channels/channels_screen.dart';
import '../memory/knowledge_graph_screen.dart';
import '../skills/skills_screen.dart';
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
  for (final key in ['conversationId', 'memoryId', 'fileId', 'taskId', 'reminderId', 'automationId', 'skillId', 'entityId', 'connectionId', 'peer', 'runId']) {
    final value = asJsonString(data[key]);
    if (value != null) params.putIfAbsent(_routeParamForKey(key), () => value);
  }
  return SearchRouteTarget(kind: kind, parameters: params);
}

String _routeParamForKey(String key) => switch (key) {
  'conversationId' => 'conversationId',
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
    default:
      return;
  }
}
