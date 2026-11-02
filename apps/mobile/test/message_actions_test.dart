import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/chat/message_actions.dart';
import 'package:jarvis_mobile/features/reminders/reminders_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  group('suggestedNoteTitle', () {
    test('uses the first real line without Markdown marks', () {
      expect(suggestedNoteTitle('## Packing list\n\n- socks'), 'Packing list');
      expect(suggestedNoteTitle('**Book** the hotel'), 'Book the hotel');
      expect(suggestedNoteTitle('\n\n  1. First step'), 'First step');
    });

    test('cuts a long line at a word', () {
      final title = suggestedNoteTitle(
        'Remember to ${'water the plants ' * 10}',
        max: 40,
      );
      expect(title.length, lessThanOrEqualTo(41));
      expect(title, endsWith('…'));
      expect(title.contains('wat…'), isFalse);
    });

    test('an empty message has no title', () {
      expect(suggestedNoteTitle('  \n '), '');
    });
  });

  group('Save to Library', () {
    late FixtureHttp http;

    setUp(() {
      http = FixtureHttp();
      http.on('POST', '/api/v1/library/notes', {'id': 'n1'});
      http.on('POST', '/api/v1/entities/links', {'linked': true}, status: 201);
    });

    Future<String?> open(WidgetTester tester) async {
      String? result;
      tester.view.physicalSize = const Size(800, 1600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: Builder(
            builder: (context) => Scaffold(
              body: TextButton(
                onPressed: () async => result = await showSaveToLibrarySheet(
                  context,
                  http: http.client(),
                  content: '## Hotel options\nThe Lisbon Inn is cheapest.',
                  conversationId: 'c1',
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      );
      await tester.tap(find.text('open'));
      await tester.pumpAndSettle();
      return result;
    }

    testWidgets('title and text are prefilled and editable, then saved with a '
        'link back to the chat', (tester) async {
      await open(tester);
      expect(find.text('Hotel options'), findsOneWidget);
      expect(find.byKey(const Key('save-note-source')), findsOneWidget);

      await tester.enterText(
        find.byKey(const Key('save-note-title')),
        'Lisbon hotels',
      );
      await tester.ensureVisible(find.byKey(const Key('save-note-submit')));
      await tester.tap(find.byKey(const Key('save-note-submit')));
      await tester.pumpAndSettle();

      final note =
          http.sent('POST', '/api/v1/library/notes').single.body as Map;
      expect(note['title'], 'Lisbon hotels');
      expect(note['content'], contains('The Lisbon Inn is cheapest.'));
      final link =
          http.sent('POST', '/api/v1/entities/links').single.body as Map;
      expect(link, {
        'from': 'conversation:c1',
        'to': 'library:n1',
        'relation': 'source',
      });
      expect(find.text('Save to Library'), findsNothing);
    });

    testWidgets('a failed save keeps the sheet and the edits', (tester) async {
      http.on('POST', '/api/v1/library/notes', {'title': 'x'}, status: 500);
      await open(tester);
      await tester.enterText(find.byKey(const Key('save-note-title')), 'Mine');
      await tester.ensureVisible(find.byKey(const Key('save-note-submit')));
      await tester.tap(find.byKey(const Key('save-note-submit')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('submit-error')), findsOneWidget);
      expect(find.text('Mine'), findsOneWidget);
      expect(http.sent('POST', '/api/v1/entities/links'), isEmpty);
    });

    testWidgets('a note is still saved when linking fails', (tester) async {
      http.on('POST', '/api/v1/entities/links', {'x': 1}, status: 500);
      await open(tester);
      await tester.ensureVisible(find.byKey(const Key('save-note-submit')));
      await tester.tap(find.byKey(const Key('save-note-submit')));
      await tester.pumpAndSettle();
      expect(find.text('Save to Library'), findsNothing);
      expect(http.sent('POST', '/api/v1/library/notes'), hasLength(1));
    });
  });

  group('message bubble', () {
    Widget host(Widget child) => MaterialApp(
      theme: buildJarvisTheme(),
      home: Scaffold(body: child),
    );

    testWidgets('a finished reply offers Save to Library and Remind me', (
      tester,
    ) async {
      final calls = <String>[];
      await tester.pumpWidget(
        host(
          MessageBubble(
            message: const MessageEntry(role: 'assistant', content: 'Done.'),
            onSaveToLibrary: () => calls.add('library'),
            onRemindMe: () => calls.add('remind'),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('message-more')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Save to Library'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('message-more')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Remind me'));
      await tester.pumpAndSettle();
      expect(calls, ['library', 'remind']);
    });

    testWidgets('a reply still being written has no such actions', (
      tester,
    ) async {
      await tester.pumpWidget(
        host(
          const MessageBubble(
            message: MessageEntry(
              role: 'assistant',
              content: 'Partial',
              pending: true,
            ),
          ),
        ),
      );
      expect(find.byKey(const Key('message-more')), findsNothing);
    });

    testWidgets('your own message offers them in its long-press sheet', (
      tester,
    ) async {
      var reminded = 0;
      await tester.pumpWidget(
        host(
          MessageBubble(
            message: const MessageEntry(role: 'user', content: 'Call Anna'),
            onSaveToLibrary: () {},
            onRemindMe: () => reminded++,
          ),
        ),
      );
      await tester.longPress(find.text('Call Anna'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('message-save-library')), findsOneWidget);
      await tester.tap(find.byKey(const Key('message-remind-me')));
      await tester.pumpAndSettle();
      expect(reminded, 1);
    });
  });

  group('reminder from a message', () {
    late FixtureHttp http;

    setUp(() {
      http = FixtureHttp();
      deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
      http.on('GET', '/api/v1/reminders', <Object>[]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      http.on('GET', '/api/v1/briefings/daily', {
        'enabled': false,
        'localTime': '08:00:00',
        'timeZoneId': 'UTC',
      });
      http.on('PUT', '/api/v1/briefings/daily', <String, Object>{});
      http.on('POST', '/api/v1/reminders', {'id': 'r1', 'status': 'pending'});
      http.on('POST', '/api/v1/entities/links', {'linked': true}, status: 201);
    });

    Future<void> show(WidgetTester tester) async {
      tester.view.physicalSize = const Size(900, 1800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: RemindersScreen(
            http: http.client(),
            startCreating: true,
            initialTitle: 'Call the plumber about the leak',
            sourceConversationId: 'c1',
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('nothing is created until the owner reviews and saves', (
      tester,
    ) async {
      await show(tester);
      expect(find.text('Call the plumber about the leak'), findsOneWidget);
      expect(find.byKey(const Key('reminder-from-chat')), findsOneWidget);
      expect(find.byKey(const Key('reminder-when')), findsOneWidget);
      expect(http.sent('POST', '/api/v1/reminders'), isEmpty);

      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/reminders'), hasLength(1));
      final link =
          http.sent('POST', '/api/v1/entities/links').single.body as Map;
      expect(link['from'], 'conversation:c1');
      expect(link['to'], 'reminder:r1');
    });
  });
}
