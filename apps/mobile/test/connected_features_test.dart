import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/activity/activity_screen.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/entities/entity_ref.dart';
import 'package:jarvis_mobile/features/entities/entity_screen.dart';
import 'package:jarvis_mobile/features/shell/utility_pages.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _task = '0199c3a1-1111-7000-8000-000000000001';
const _reminder = '0199c3a1-2222-7000-8000-000000000002';
const _chat = '0199c3a1-3333-7000-8000-000000000003';

Widget _app(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: child),
);

Map<String, Object?> _event(
  String id,
  String summary, {
  String kind = 'watch.fired',
  String origin = 'System',
  String? subject,
}) => {
  'id': id,
  'ownerId': 'o',
  'kind': kind,
  'summary': summary,
  'subjectRef': subject,
  'data': null,
  'conversationId': null,
  'origin': origin,
  'causedByTaskId': null,
  'at': DateTime.now().toUtc().toIso8601String(),
};

void main() {
  group('entity refs', () {
    test('parse type:id and know where each kind opens', () {
      final ref = EntityRef.tryParse('Task:$_task')!;
      expect(ref.type, 'task');
      expect(ref.toString(), 'task:$_task');
      expect(ref.destination, 'entity:task:$_task');
      expect(EntityRef.fromDestination(ref.destination), ref);
      expect(featureDestinationFor(ref), 'tasks');
      expect(
        featureDestinationFor(EntityRef.tryParse('project:$_task')!),
        'project:$_task',
      );
      expect(
        featureDestinationFor(EntityRef.tryParse('conversation:$_chat')!),
        isNull,
      );
    });

    test('anything else is not a ref', () {
      expect(EntityRef.tryParse(null), isNull);
      expect(EntityRef.tryParse('task'), isNull);
      expect(EntityRef.tryParse('task:not-an-id'), isNull);
      expect(EntityRef.tryParse('spaceship:$_task'), isNull);
      expect(parseEntityRefs(['task:$_task', 'task:$_task', 'nope', 7]), [
        EntityRef.tryParse('task:$_task'),
      ]);
    });

    test('the entity destination opens its own page', () {
      final http = FixtureHttp();
      expect(
        utilityPageFor('entity:reminder:$_reminder', http.client()),
        isA<EntityScreen>(),
      );
      expect(utilityPageFor('entity:nope:1', http.client()), isNull);
      expect(utilityPageFor('activity', http.client()), isA<ActivityScreen>());
    });
  });

  testWidgets('a step that made something shows a card that opens it', (
    tester,
  ) async {
    EntityRef? opened;
    await tester.pumpWidget(
      _app(
        ToolRunView(
          run: const ToolRunEntry([
            ToolStep(
              'CreateReminder',
              ToolStepStatus.completed,
              refs: ['reminder:$_reminder'],
            ),
            ToolStep('SearchMemories', ToolStepStatus.completed),
            ToolStep(
              'CreateTask',
              ToolStepStatus.failed,
              refs: ['task:$_task'],
            ),
          ]),
          onOpenEntity: (ref) => opened = ref,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.byKey(const Key('entity-card-reminder:$_reminder')),
      findsOneWidget,
    );
    expect(find.byKey(const Key('entity-card-task:$_task')), findsNothing);
    await tester.tap(find.byKey(const Key('entity-card-reminder:$_reminder')));
    expect(opened, EntityRef.tryParse('reminder:$_reminder'));
  });

  testWidgets('a thing shows what is related to it and what happened', (
    tester,
  ) async {
    final http = FixtureHttp()
      ..on('GET', '/api/v1/entities/task/$_task/related', {
        'ref': 'task:$_task',
        'title': 'Research flights',
        'related': [
          {
            'ref': 'conversation:$_chat',
            'type': 'conversation',
            'relation': 'created',
            'direction': 'in',
            'title': 'Trip planning',
            'at': null,
          },
          {
            'ref': 'reminder:$_reminder',
            'type': 'reminder',
            'relation': 'follows_up',
            'direction': 'out',
            'title': 'Book the hotel',
            'at': null,
          },
        ],
      })
      ..on('GET', '/api/v1/events', [
        _event('e1', 'Task failed: Research flights', kind: 'task.failed'),
      ]);
    String? openedChat;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: EntityScreen(
          http: http.client(),
          entity: EntityRef.tryParse('task:$_task')!,
          onOpenConversation: (id) async => openedChat = id,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Research flights'), findsOneWidget);
    expect(find.text('Trip planning'), findsOneWidget);
    expect(find.text('Book the hotel'), findsOneWidget);
    expect(find.text('Task failed: Research flights'), findsOneWidget);
    expect(find.text('Open in Tasks'), findsOneWidget);
    expect(
      http.sent('GET', '/api/v1/events').single.query['subject'],
      'task:$_task',
    );

    await tester.tap(find.byKey(const Key('related-conversation:$_chat')));
    await tester.pumpAndSettle();
    expect(openedChat, _chat);
  });

  testWidgets('a removed thing says so instead of failing', (tester) async {
    final http = FixtureHttp()
      ..on('GET', '/api/v1/entities/task/$_task/related', null, status: 404);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: EntityScreen(
          http: http.client(),
          entity: EntityRef.tryParse('task:$_task')!,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Not here any more'), findsOneWidget);
  });

  testWidgets('activity can show only what Jarvis did on its own', (
    tester,
  ) async {
    final http = FixtureHttp()
      ..on('GET', '/api/v1/events', [
        _event(
          'e1',
          'Jarvis ran SendWhatsAppMessage on its own',
          kind: 'agent.acted',
          origin: 'Agent',
        ),
        _event('e2', 'Battery low', subject: 'reminder:$_reminder'),
      ]);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: ActivityScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Battery low'), findsOneWidget);
    expect(find.textContaining('Acted on its own'), findsOneWidget);

    await tester.tap(
      find.byKey(const Key('activity-filter-ActivityFilter.jarvis')),
    );
    await tester.pumpAndSettle();
    expect(
      http.sent('GET', '/api/v1/events').last.query['origins'],
      'Agent,AgentReaction',
    );
  });

  test('the activity tile counts what Jarvis did and links each row', () async {
    final http = FixtureHttp()
      ..on('GET', '/api/v1/events', [
        _event('e1', 'Followed up on the watch', origin: 'AgentReaction'),
        _event('e2', 'Message from Sam', kind: 'message.received'),
        _event(
          'e3',
          'Reminder due: Call mum',
          kind: 'reminder.due',
          subject: 'reminder:$_reminder',
        ),
      ]);
    final spec = tileSpecFor('activity')!;
    final data = (await spec.load!(
      TileEnv(http: http.client(), briefing: () async => null),
    ))!;

    expect(data.stat, '1');
    expect(data.rows.map((row) => row.text), [
      '✦ Followed up on the watch',
      'Reminder due: Call mum',
    ]);
    expect(data.rows.last.target, 'entity:reminder:$_reminder');
  });
}
