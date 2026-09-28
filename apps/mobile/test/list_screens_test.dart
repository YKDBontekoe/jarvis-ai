import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/conversations_screen.dart';
import 'package:jarvis_mobile/files_screen.dart';
import 'package:jarvis_mobile/memory_screen.dart';

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
    await show(tester, FilesScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('notes.txt'), findsOneWidget);
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

  testWidgets('a non-list memory payload shows the empty state', (
    tester,
  ) async {
    http.on('GET', '/api/v1/memory', 'nope');
    await show(tester, MemoryScreen(http: http.client()));

    expect(tester.takeException(), isNull);
    expect(find.text('No memories yet'), findsOneWidget);
  });
}
