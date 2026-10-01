import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/conversation_export.dart';
import 'package:jarvis_mobile/conversation_groups.dart';
import 'package:jarvis_mobile/conversations_screen.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;
  final now = DateTime.now().toUtc();

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/conversations', [
      {
        'id': 'c1',
        'title': 'Trip planning',
        'updatedAt': now.toIso8601String(),
        'pinned': false,
      },
      {
        'id': 'c2',
        'title': 'Groceries',
        'updatedAt': now.subtract(const Duration(days: 30)).toIso8601String(),
        'pinned': false,
      },
    ]);
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: ConversationsScreen(
          http: http.client(),
          selectedConversationId: null,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> openMenu(WidgetTester tester, String title) async {
    final row = find.ancestor(of: find.text(title), matching: find.byType(Row));
    await tester.tap(
      find
          .descendant(
            of: row.first,
            matching: find.byTooltip('Conversation actions'),
          )
          .first,
    );
    await tester.pumpAndSettle();
  }

  testWidgets('groups conversations by day', (tester) async {
    await show(tester);

    expect(find.text('Today'), findsOneWidget);
    expect(find.text('Older'), findsOneWidget);
    expect(find.text('Pinned'), findsNothing);
  });

  testWidgets('search filters by title and says when nothing matches', (
    tester,
  ) async {
    await show(tester);

    await tester.enterText(find.byType(TextField), 'groc');
    await tester.pumpAndSettle();
    expect(find.text('Groceries'), findsOneWidget);
    expect(find.text('Trip planning'), findsNothing);

    await tester.enterText(find.byType(TextField), 'zebra');
    await tester.pumpAndSettle();
    expect(find.text('No matching conversations'), findsOneWidget);

    await tester.tap(find.byTooltip('Clear search'));
    await tester.pumpAndSettle();
    expect(find.text('Trip planning'), findsOneWidget);
    expect(find.text('Groceries'), findsOneWidget);
  });

  testWidgets('pinning moves a conversation to the pinned group', (
    tester,
  ) async {
    http.on('PATCH', '/api/v1/conversations/c2', {
      'id': 'c2',
      'title': 'Groceries',
      'pinned': true,
    });
    await show(tester);

    await openMenu(tester, 'Groceries');
    await tester.tap(find.text('Pin to top'));
    await tester.pumpAndSettle();

    expect(http.sent('PATCH', '/api/v1/conversations/c2').single.body, {
      'pinned': true,
    });
    expect(find.text('Pinned'), findsOneWidget);
    expect(find.text('Older'), findsNothing);
    expect(
      tester.getTopLeft(find.text('Groceries')).dy,
      lessThan(tester.getTopLeft(find.text('Trip planning')).dy),
    );
  });

  testWidgets('rename sends the trimmed title and shows it', (tester) async {
    http.on('PATCH', '/api/v1/conversations/c1', {
      'id': 'c1',
      'title': 'Summer trip',
      'pinned': false,
    });
    await show(tester);

    await openMenu(tester, 'Trip planning');
    await tester.tap(find.text('Rename'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.byType(TextField),
      ),
      '  Summer   trip ',
    );
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    expect(http.sent('PATCH', '/api/v1/conversations/c1').single.body, {
      'title': 'Summer trip',
    });
    expect(find.text('Summer trip'), findsOneWidget);
    expect(find.text('Trip planning'), findsNothing);
  });

  testWidgets('a refused rename puts the old title back', (tester) async {
    http.on('PATCH', '/api/v1/conversations/c1', null, status: 500);
    await show(tester);

    await openMenu(tester, 'Trip planning');
    await tester.tap(find.text('Rename'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.byType(TextField),
      ),
      'Something else',
    );
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    expect(find.text('Trip planning'), findsOneWidget);
    expect(find.text('Something else'), findsNothing);
    expect(
      find.text('Jarvis could not rename this conversation.'),
      findsOneWidget,
    );
  });

  test('groupConversations puts pinned rows first whatever their age', () {
    final groups = groupConversations([
      {'id': 'a', 'updatedAt': '2026-10-01T08:00:00Z'},
      {'id': 'b', 'updatedAt': '2020-01-01T08:00:00Z', 'pinned': true},
      {'title': 'no id'},
    ], now: DateTime(2026, 10, 1, 12));

    expect(groups.map((group) => group.$1), ['Pinned', 'Today']);
    expect(groups.first.$2.single['id'], 'b');
  });

  testWidgets('copy as Markdown copies the whole conversation', (tester) async {
    String? copied;
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async {
        if (call.method == 'Clipboard.setData') {
          copied = (call.arguments as Map)['text'] as String?;
        }
        return null;
      },
    );
    addTearDown(
      () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        null,
      ),
    );
    http.on('GET', '/api/v1/conversations/c1', {
      'id': 'c1',
      'title': 'Trip planning',
      'messages': [
        {'role': 'user', 'content': 'Where should we go?'},
        {'role': 'assistant', 'content': 'Lisbon is lovely in October.'},
      ],
    });
    await show(tester);

    await openMenu(tester, 'Trip planning');
    await tester.tap(find.text('Copy as Markdown'));
    await tester.pumpAndSettle();

    expect(copied, contains('# Trip planning'));
    expect(copied, contains('**You**'));
    expect(copied, contains('Lisbon is lovely in October.'));
    expect(find.text('Conversation copied as Markdown'), findsOneWidget);
  });

  test('conversationMarkdown labels authors and skips empty rows', () {
    final markdown = conversationMarkdown(
      title: '  ',
      messages: [
        {
          'role': 'user',
          'content': ' Hi ',
          'createdAt': DateTime(2026, 10, 1, 9, 5).toUtc().toIso8601String(),
        },
        {'role': 'assistant', 'content': ''},
        {'role': 'system', 'content': 'hidden'},
        {'role': 'assistant', 'content': 'Hello!'},
      ],
    );

    expect(
      markdown,
      '# Conversation\n\n**You** · 2026-10-01 09:05\n\nHi\n\n**Jarvis**\n\nHello!\n',
    );
  });
}
