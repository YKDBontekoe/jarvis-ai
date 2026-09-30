import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home/home_overview.dart';
import 'package:jarvis_mobile/task_details_screen.dart';

class _FixtureAdapter implements HttpClientAdapter {
  final Map<String, Object> responses = {};
  final List<String> requests = [];
  int status = 200;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options.path);
    return ResponseBody.fromString(
      jsonEncode(responses[options.path] ?? []),
      status,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

Map<String, Object> _task(String id, String title, String status) => {
  'id': id,
  'title': title,
  'status': status,
  'createdAt': '2026-09-26T12:00:00Z',
  'conversationId': 'task-conversation',
};

void main() {
  late _FixtureAdapter adapter;
  late Dio http;

  setUp(() {
    adapter = _FixtureAdapter();
    http = Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
      ..httpClientAdapter = adapter;
  });
  tearDown(() => http.close());

  Future<void> showHome(
    WidgetTester tester, {
    bool ready = true,
    VoidCallback? onTalk,
    VoidCallback? onOpenTasks,
    VoidCallback? onOpenUsage,
  }) async {
    tester.view.physicalSize = const Size(800, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: HomeOverview(
            http: http,
            mark: const Icon(Icons.blur_on),
            ready: ready,
            voiceStarting: false,
            onTalk: onTalk,
            onOpenTasks: onOpenTasks ?? () {},
            onOpenUsage: onOpenUsage,
            refreshRevision: 0,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'home prioritizes approvals and shows at most three active tasks',
    (tester) async {
      adapter.responses['/api/v1/tasks'] = [
        _task('queued', 'Queued task', 'queued'),
        _task('complete', 'Finished task', 'completed'),
        _task('running', 'Running task', 'running'),
        _task('waiting', 'Waiting task', 'waiting'),
        _task('approval', 'Review this task', 'needs_approval'),
      ];
      await showHome(tester);
      expect(find.text('Active tasks (4)'), findsOneWidget);
      expect(find.text('Review this task'), findsOneWidget);
      expect(find.text('Running task'), findsOneWidget);
      expect(find.text('Waiting task'), findsOneWidget);
      expect(find.text('Queued task'), findsNothing);
      expect(find.text('Finished task'), findsNothing);
      expect(
        tester.getTopLeft(find.text('Review this task')).dy,
        lessThan(tester.getTopLeft(find.text('Running task')).dy),
      );
    },
  );

  testWidgets('offline home avoids requests and exposes task navigation', (
    tester,
  ) async {
    var openedTasks = false;
    await showHome(tester, ready: false, onOpenTasks: () => openedTasks = true);
    expect(adapter.requests, isEmpty);
    expect(
      find.text('Connect to Jarvis to see your active tasks.'),
      findsOneWidget,
    );
    expect(
      tester.widget<FilledButton>(find.byType(FilledButton)).onPressed,
      isNull,
    );
    await tester.tap(find.text('View all'));
    expect(openedTasks, isTrue);
  });

  testWidgets('voice action delegates to the current conversation', (
    tester,
  ) async {
    var startedVoice = false;
    await showHome(tester, onTalk: () => startedVoice = true);
    await tester.tap(find.text('Talk to Jarvis'));
    expect(startedVoice, isTrue);
  });

  testWidgets('task preview recovers after an API failure', (tester) async {
    adapter.status = 503;
    await showHome(tester);
    expect(
      find.text('Could not load active tasks. Pull down to retry.'),
      findsOneWidget,
    );
    adapter.status = 200;
    adapter.responses['/api/v1/tasks'] = [
      _task('running', 'Recovered task', 'running'),
    ];
    await tester.tap(find.byTooltip('Refresh active tasks'));
    await tester.pumpAndSettle();
    expect(find.text('Recovered task'), findsOneWidget);
    expect(
      find.text('Could not load active tasks. Pull down to retry.'),
      findsNothing,
    );
  });

  testWidgets('a non-list tasks payload does not crash the home screen', (
    tester,
  ) async {
    adapter.responses['/api/v1/tasks'] = {'items': <Object>[]};
    await showHome(tester);
    expect(tester.takeException(), isNull);
    expect(find.text('Active tasks'), findsOneWidget);
    expect(
      find.text('Could not load active tasks. Pull down to retry.'),
      findsNothing,
    );
  });

  testWidgets('home skips invalid task rows instead of crashing', (
    tester,
  ) async {
    adapter.responses['/api/v1/tasks'] = [
      'nope',
      {
        'id': 1,
        'title': 'Numeric id',
        'status': 'running',
        'createdAt': '2026-09-28T12:00:00Z',
      },
      _task('running', 'Valid running task', 'running'),
    ];
    await showHome(tester);
    expect(tester.takeException(), isNull);
    expect(find.text('Valid running task'), findsOneWidget);
    expect(find.text('Numeric id'), findsNothing);
  });

  testWidgets('home usage card summarizes the week and opens usage', (
    tester,
  ) async {
    adapter.responses['/api/v1/tasks'] = [];
    adapter.responses['/api/v1/usage'] = {
      'personalization': {
        'score': 58,
        'band': 'Familiar',
        'activeMemories': 12,
      },
      'codex': {'totalTokens': 15400},
      'openRouter': {'totalTokens': 1000, 'estimatedCostUsd': 6.03},
    };
    var opened = false;
    await showHome(tester, onOpenUsage: () => opened = true);
    expect(find.byKey(const Key('home-usage')), findsOneWidget);
    expect(find.text('Familiar · 58'), findsOneWidget);
    expect(find.textContaining('12 memories'), findsOneWidget);
    expect(find.textContaining('16k tokens this week'), findsOneWidget);
    expect(find.textContaining('\$6.03'), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-usage')));
    expect(opened, isTrue);
  });

  testWidgets('home briefing shows reminders, approvals, and calendar', (
    tester,
  ) async {
    adapter.responses['/api/v1/tasks'] = [];
    adapter.responses['/api/v1/home'] = {
      'portrait': 'Robin likes quiet mornings.',
      'reminders': [
        {
          'id': 'r1',
          'title': 'Call the dentist',
          'dueAt': '2026-09-28T18:00:00Z',
          'status': 'pending',
          'recurrence': 'none',
        },
      ],
      'approvals': [
        {
          'id': 'a1',
          'toolName': 'RunCodingTask',
          'createdAt': '2026-09-28T12:00:00Z',
        },
      ],
      'calendar': {
        'connected': true,
        'source': 'ics',
        'events': [
          {'title': 'Standup', 'startAt': '2026-09-28T09:00:00Z'},
        ],
      },
      'device': {
        'batteryPercent': 64,
        'charging': true,
        'hasLocation': true,
        'reportedAt': '2026-09-28T12:00:00Z',
      },
      'packs': [
        {'id': 'mail', 'name': 'Mail', 'category': 'mail', 'installed': false},
      ],
    };
    var openedApprovals = false;
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: HomeOverview(
            http: http,
            mark: const Icon(Icons.blur_on),
            ready: true,
            voiceStarting: false,
            onTalk: () {},
            onOpenTasks: () {},
            onOpenApprovals: () => openedApprovals = true,
            onOpenReminders: () {},
            onOpenIntegrations: () {},
            refreshRevision: 0,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Robin likes quiet mornings.'), findsOneWidget);
    expect(find.textContaining('approval'), findsOneWidget);
    expect(find.text('Call the dentist'), findsOneWidget);
    expect(find.textContaining('Standup'), findsOneWidget);
    expect(find.textContaining('64%'), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-approvals')));
    expect(openedApprovals, isTrue);
  });

  testWidgets('task details show only approvals belonging to that task', (
    tester,
  ) async {
    adapter.responses['/api/v1/tasks/approval'] = _task(
      'approval',
      'Review this task',
      'needs_approval',
    );
    adapter.responses['/api/v1/approvals'] = [
      {
        'id': 'one',
        'conversationId': 'task-conversation',
        'toolName': 'task_write',
        'status': 'pending',
        'argumentsJson': '{}',
      },
      {
        'id': 'two',
        'conversationId': 'unrelated',
        'toolName': 'unrelated_write',
        'status': 'pending',
        'argumentsJson': '{}',
      },
    ];
    await tester.pumpWidget(
      MaterialApp(
        home: TaskDetailsScreen(http: http, taskId: 'approval'),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Jarvis needs your approval'));
    await tester.pumpAndSettle();
    expect(find.text('Task approvals'), findsOneWidget);
    expect(find.text('task_write'), findsOneWidget);
    expect(find.text('unrelated_write'), findsNothing);
  });

  testWidgets('a new account sees a get-started checklist that hides once done', (
    tester,
  ) async {
    final prompts = <String>[];
    var revision = 0;
    adapter.responses['/api/v1/usage'] = {
      'activity': {
        'messagesSent': {'total': 0},
      },
      'personalization': {'activeMemories': 0},
      'codex': {},
      'openRouter': {},
    };
    adapter.responses['/api/v1/home'] = {'packs': <Object>[]};

    Future<void> pump() async {
      tester.view.physicalSize = const Size(800, 2000);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: HomeOverview(
              http: http,
              mark: const Icon(Icons.blur_on),
              ready: true,
              voiceStarting: false,
              onTalk: () {},
              onOpenTasks: () {},
              onOpenUsage: () {},
              onSuggestion: prompts.add,
              refreshRevision: revision,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    await pump();
    expect(find.text('Get started'), findsOneWidget);
    expect(find.text('0 of 3'), findsOneWidget);
    await tester.tap(find.text('Say hello'));
    expect(prompts, hasLength(1));

    // Once every step is done the card goes away.
    adapter.responses['/api/v1/usage'] = {
      'activity': {
        'messagesSent': {'total': 3},
      },
      'personalization': {'activeMemories': 4},
      'codex': {},
      'openRouter': {},
    };
    adapter.responses['/api/v1/home'] = {
      'packs': [
        {'name': 'Calendar', 'installed': true},
      ],
    };
    revision++;
    await pump();
    expect(find.text('Get started'), findsNothing);
  });
}
