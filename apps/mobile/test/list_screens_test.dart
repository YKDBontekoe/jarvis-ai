import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/approvals_screen.dart';
import 'package:jarvis_mobile/audit_screen.dart';
import 'package:jarvis_mobile/condition_watches_screen.dart';
import 'package:jarvis_mobile/conversations_screen.dart';
import 'package:jarvis_mobile/daily_briefing_screen.dart';
import 'package:jarvis_mobile/files_screen.dart';
import 'package:jarvis_mobile/memory_screen.dart';
import 'package:jarvis_mobile/tasks_screen.dart';
import 'package:jarvis_mobile/features/profiles/profiles_screen.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets('memory skips invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/memory', [
      'nope',
      {
        'id': 'm1',
        'kind': 'fact',
        'content': 'I like trains',
        'isPinned': false,
      },
      3,
    ]);
    await show(tester, MemoryScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('I like trains'), findsOneWidget);
  });

  testWidgets('files skip invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/files', [
      {'fileName': true},
      {
        'id': 'f1',
        'fileName': 'notes.txt',
        'sizeBytes': 12,
        'processingStatus': 'ready',
      },
      'nope',
    ]);
    http.on('GET', '/api/v1/collections', [
      'nope',
      {
        'id': 'c1',
        'name': 'Work docs',
        'fileIds': ['f1'],
      },
    ]);
    await show(tester, FilesScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('notes.txt'), findsOneWidget);
    expect(find.text('Work docs'), findsOneWidget);
  });

  testWidgets('conversations skip invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/conversations', [
      {'title': 'Missing id'},
      {'id': 'c1', 'title': 'Trip planning', 'updatedAt': 'not-a-date'},
      7,
    ]);
    await show(
      tester,
      ConversationsScreen(http: http.client(), selectedConversationId: null),
    );

    expect(tester.takeException(), isNull);
    expect(find.text('Trip planning'), findsOneWidget);
    expect(find.text('Missing id'), findsNothing);
  });

  testWidgets('profiles skip invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/profiles', [
      'nope',
      {
        'id': 'p1',
        'name': 'Work',
        'description': 'Office context',
        'isDefault': false,
        'memoryScope': 'profile',
      },
      3,
    ]);
    await show(tester, ProfilesScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('Work'), findsOneWidget);
    expect(find.textContaining('Office context'), findsOneWidget);
  });

  testWidgets('a non-list memory payload shows the empty state', (
    tester,
  ) async {
    http.on('GET', '/api/v1/memory', 'nope');
    await show(tester, MemoryScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('No memories yet'), findsOneWidget);
  });

  testWidgets('tasks skip invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks', [
      'nope',
      {
        'id': 't1',
        'title': 'Buy milk',
        'status': 'queued',
        'createdAt': '2026-09-28T12:00:00Z',
      },
      3,
    ]);
    await show(tester, TasksScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('Buy milk'), findsOneWidget);
  });

  testWidgets('approvals skip invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/approvals', [
      'nope',
      {
        'id': 'a1',
        'toolName': 'ForgetMemory',
        'status': 'pending',
        'argumentsJson': '{}',
      },
      3,
    ]);
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: ApprovalsScreen(http: http.client())),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 50));

    expect(tester.takeException(), isNull);
    expect(find.text('ForgetMemory'), findsOneWidget);
  });

  testWidgets('audit skips invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/audit', [
      'nope',
      {
        'id': 'e1',
        'action': 'memory.created',
        'success': true,
        'timestamp': '2026-09-28T12:00:00Z',
        'riskClass': 'low',
      },
    ]);
    await show(tester, AuditScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('memory created'), findsOneWidget);
  });

  testWidgets('watches skip invalid list rows instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/watches', [
      1,
      {
        'id': 'w1',
        'title': 'BTC price',
        'status': 'active',
        'url': 'https://example.com/price',
      },
      'nope',
    ]);
    await show(tester, ConditionWatchesScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('BTC price'), findsOneWidget);
  });

  testWidgets('briefing shows an error for a non-object payload', (
    tester,
  ) async {
    http.on('GET', '/api/v1/briefings/daily', 'nope');
    await show(tester, DailyBriefingScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('Could not load briefing settings.'), findsOneWidget);
  });

  testWidgets('task create validates empty fields and posts a valid task', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks', <Object>[]);
    http.on('POST', '/api/v1/tasks', {
      'id': 't2',
      'title': 'Buy milk',
      'status': 'queued',
      'createdAt': '2026-09-28T12:00:00Z',
    });
    await show(tester, TasksScreen(http: http.client()));

    await tester.tap(find.text('New task'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Start task'));
    await tester.pumpAndSettle();

    expect(find.text('Enter a task name.'), findsOneWidget);
    expect(find.text('Describe the task.'), findsOneWidget);
    expect(http.sent('POST', '/api/v1/tasks'), isEmpty);

    await tester.enterText(find.byType(TextFormField).at(0), 'Buy milk');
    await tester.enterText(find.byType(TextFormField).at(1), 'Get 2% milk');
    await tester.tap(find.text('Start task'));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    final body =
        http.sent('POST', '/api/v1/tasks').single.body as Map<String, dynamic>;
    expect(body['title'], 'Buy milk');
    expect(body['prompt'], 'Get 2% milk');
  });

  testWidgets('tasks opened to create show the editor straight away', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks', <Object>[]);
    await show(tester, TasksScreen(http: http.client(), startCreating: true));

    expect(find.text('Start task'), findsOneWidget);
  });

  testWidgets('memory opened to create show the editor straight away', (
    tester,
  ) async {
    http.on('GET', '/api/v1/memory', <Object>[]);
    await show(tester, MemoryScreen(http: http.client(), startCreating: true));

    expect(find.text('Add a memory'), findsWidgets);
  });

  testWidgets('watch create rejects non-https URLs and posts a valid watch', (
    tester,
  ) async {
    http.on('GET', '/api/v1/watches', <Object>[]);
    http.on('POST', '/api/v1/watches', {
      'id': 'w2',
      'title': 'BTC price',
      'status': 'active',
      'url': 'https://example.com/price',
    });
    await show(tester, ConditionWatchesScreen(http: http.client()));

    await tester.tap(find.text('New'));
    await tester.pumpAndSettle();

    final fields = find.byType(TextFormField);
    await tester.enterText(fields.at(0), 'BTC price');
    await tester.enterText(fields.at(1), 'javascript:alert(1)');
    await tester.enterText(fields.at(2), 'data.price');
    await tester.enterText(fields.at(3), '100');
    await tester.tap(find.text('Start watching'));
    await tester.pumpAndSettle();

    expect(find.text('Enter a public HTTPS URL.'), findsOneWidget);
    expect(http.sent('POST', '/api/v1/watches'), isEmpty);

    await tester.enterText(fields.at(1), 'https://user:pass@example.com/price');
    await tester.tap(find.text('Start watching'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/watches'), isEmpty);

    await tester.enterText(fields.at(1), 'https://example.com/price');
    await tester.tap(find.text('Start watching'));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    final body =
        http.sent('POST', '/api/v1/watches').single.body
            as Map<String, dynamic>;
    expect(body['title'], 'BTC price');
    expect(body['url'], 'https://example.com/price');
    expect(body['jsonPath'], 'data.price');
    expect(body['threshold'], 100);
    expect(body['intervalMinutes'], 15);
    expect(body['kind'], 'public_json');
  });
}
