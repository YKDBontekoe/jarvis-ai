import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/approvals/approvals_screen.dart';
import 'package:jarvis_mobile/features/audit/audit_screen.dart';
import 'package:jarvis_mobile/features/watches/condition_watches_screen.dart';
import 'package:jarvis_mobile/features/conversations/conversations_screen.dart';
import 'package:jarvis_mobile/features/briefing/daily_briefing_screen.dart';
import 'package:jarvis_mobile/features/files/files_screen.dart';
import 'package:jarvis_mobile/features/memory/memory_screen.dart';
import 'package:jarvis_mobile/features/tasks/tasks_screen.dart';
import 'package:jarvis_mobile/features/profiles/profiles_screen.dart';
import 'package:jarvis_mobile/ui/phosphor_icons.dart';

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

  testWidgets('Ask Jarvis about a file opens a chat that has only that file attached', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/files', [
        {
          'id': 'f1',
          'fileName': 'quote.pdf',
          'sizeBytes': 12,
          'processingStatus': 'ready',
        },
        {
          'id': 'f2',
          'fileName': 'broken.pdf',
          'sizeBytes': 12,
          'processingStatus': 'failed',
        },
      ])
      ..on('GET', '/api/v1/collections', <Object>[])
      ..on('POST', '/api/v1/files/f1/ask', {
        'conversationId': 'chat-1',
        'fileId': 'f1',
        'fileName': 'quote.pdf',
        'status': 'ready',
      }, status: 201);
    final opened = <String>[];
    await show(
      tester,
      FilesScreen(
        http: http.client(),
        onOpenConversation: (id) async => opened.add(id),
      ),
    );

    await tester.tap(find.byIcon(PhosphorIconsRegular.dotsThree).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Ask Jarvis about this'));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/files/f1/ask'), hasLength(1));
    expect(opened, ['chat-1']);

    // A file that failed to index cannot be asked about.
    await tester.tap(find.byIcon(PhosphorIconsRegular.dotsThree).last);
    await tester.pumpAndSettle();
    expect(find.text('Ask Jarvis about this'), findsNothing);
  });

  testWidgets('a profile that cannot read the file explains why instead of opening a chat', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/files', [
        {
          'id': 'f1',
          'fileName': 'quote.pdf',
          'sizeBytes': 12,
          'processingStatus': 'ready',
        },
      ])
      ..on('GET', '/api/v1/collections', <Object>[])
      ..on('POST', '/api/v1/files/f1/ask', {
        'message': 'That assistant profile cannot read this file.',
      }, status: 409);
    final opened = <String>[];
    await show(
      tester,
      FilesScreen(
        http: http.client(),
        onOpenConversation: (id) async => opened.add(id),
      ),
    );

    await tester.tap(find.byIcon(PhosphorIconsRegular.dotsThree).first);
    await tester.pumpAndSettle();
    await tester.tap(find.text('Ask Jarvis about this'));
    await tester.pumpAndSettle();

    expect(opened, isEmpty);
    expect(find.textContaining('cannot read this file'), findsWidgets);
  });

  testWidgets('without a chat to open, Files offers no Ask action', (tester) async {
    http
      ..on('GET', '/api/v1/files', [
        {
          'id': 'f1',
          'fileName': 'quote.pdf',
          'sizeBytes': 12,
          'processingStatus': 'ready',
        },
      ])
      ..on('GET', '/api/v1/collections', <Object>[]);
    await show(tester, FilesScreen(http: http.client()));
    await tester.tap(find.byIcon(PhosphorIconsRegular.dotsThree).first);
    await tester.pumpAndSettle();
    expect(find.text('Ask Jarvis about this'), findsNothing);
  });

  testWidgets('a new collection needs a name, stops at 80 characters and is created on enter', (
    tester,
  ) async {
    http.on('GET', '/api/v1/files', <Object>[]);
    http.on('GET', '/api/v1/collections', <Object>[]);
    http.on('POST', '/api/v1/collections', {'id': 'c9', 'name': 'Trips'});
    await show(tester, FilesScreen(http: http.client()));

    await tester.tap(find.text('Collection'));
    await tester.pumpAndSettle();
    expect(
      tester.widget<FilledButton>(find.widgetWithText(FilledButton, 'Create')).onPressed,
      isNull,
    );

    await tester.enterText(find.byType(TextField), 'x' * 120);
    await tester.pump();
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text.length,
      80,
    );

    await tester.enterText(find.byType(TextField), '  Trips  ');
    await tester.testTextInput.receiveAction(TextInputAction.done);
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(http.sent('POST', '/api/v1/collections').single.body, {'name': 'Trips'});
  });

  testWidgets('tearing Files down with its new-collection dialog open leaves no error and creates nothing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/files', <Object>[]);
    http.on('GET', '/api/v1/collections', <Object>[]);
    await show(tester, FilesScreen(http: http.client()));

    await tester.tap(find.text('Collection'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextField), 'Trips');
    await tester.pump();
    // The screen goes away behind the dialog (sign-out, deep link), then the dialog is confirmed.
    await tester.pumpWidget(const MaterialApp(home: SizedBox()));
    await tester.pumpAndSettle();

    expect(tester.takeException(), isNull);
    expect(http.sent('POST', '/api/v1/collections'), isEmpty);
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

  testWidgets('a quick-created task hands back a confirmation or a cancel', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks', <Object>[]);
    http.on('POST', '/api/v1/tasks', {'id': 't9'});
    final results = <String?>[];
    await show(
      tester,
      TasksScreen(
        http: http.client(),
        startCreating: true,
        onCreateDone: results.add,
      ),
    );

    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(results, [null]);
    expect(http.sent('POST', '/api/v1/tasks'), isEmpty);
  });

  testWidgets('a failed quick-create keeps the person on the page', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks', <Object>[]);
    http.on('POST', '/api/v1/tasks', {'title': 'Down'}, status: 500);
    final results = <String?>[];
    await show(
      tester,
      TasksScreen(
        http: http.client(),
        startCreating: true,
        onCreateDone: results.add,
      ),
    );
    await tester.enterText(find.byType(TextFormField).at(0), 'Buy milk');
    await tester.enterText(find.byType(TextFormField).at(1), 'Get 2% milk');
    await tester.tap(find.text('Start task'));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/tasks'), hasLength(1));
    expect(results, isEmpty);
  });

  testWidgets('a saved quick-create confirms it', (tester) async {
    http.on('GET', '/api/v1/tasks', <Object>[]);
    http.on('POST', '/api/v1/tasks', {'id': 't9'});
    final results = <String?>[];
    await show(
      tester,
      TasksScreen(
        http: http.client(),
        startCreating: true,
        onCreateDone: results.add,
      ),
    );
    await tester.enterText(find.byType(TextFormField).at(0), 'Buy milk');
    await tester.enterText(find.byType(TextFormField).at(1), 'Get 2% milk');
    await tester.tap(find.text('Start task'));
    await tester.pumpAndSettle();

    expect(results, ['Task started']);
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
