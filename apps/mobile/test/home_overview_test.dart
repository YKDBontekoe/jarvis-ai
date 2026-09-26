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
}
