import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/find_in_chat_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _hit(String messageId, String text, String at) => {
  'kind': 'message',
  'id': messageId,
  'title': 'Trip',
  'summary': text,
  'timestamp': at,
  'route': {
    'kind': 'message',
    'parameters': {'conversationId': 'c1', 'messageId': messageId},
  },
  'relevance': .6,
  'isPinned': false,
};

Map<String, Object?> _page(String id, String content) => {
  'items': [
    {
      'id': id,
      'role': 'user',
      'content': content,
      'createdAt': '2026-10-01T10:00:00Z',
    },
  ],
  'nextCursor': null,
  'hasMore': false,
  'newerCursor': null,
  'hasNewer': false,
  'anchorId': id,
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/search', {
      'results': [
        _hit('m9', 'You: hotel is booked', '2026-10-09T10:00:00Z'),
        _hit('m5', 'Jarvis: three hotel options', '2026-10-05T10:00:00Z'),
        _hit('m1', 'You: any hotel yet?', '2026-10-01T10:00:00Z'),
      ],
      'providers': [
        {'providerId': 'messages', 'succeeded': true},
      ],
    });
    for (final id in ['m9', 'm5', 'm1']) {
      http.on(
        'GET',
        '/api/v1/conversations/c1/messages',
        _page(id, 'Body $id'),
      );
    }
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: FindInChatScreen(
          http: http.client(),
          conversationId: 'c1',
          title: 'Trip',
          onOpenConversation: (_) async {},
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> type(WidgetTester tester, String text) async {
    await tester.enterText(find.byKey(const Key('find-in-chat-field')), text);
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();
  }

  testWidgets('searches only this conversation on the server and lists '
      'matches newest first', (tester) async {
    await show(tester);
    await type(tester, 'hotel');

    final sent = http.sent('GET', '/api/v1/search').single.query;
    expect(sent['conversationId'], 'c1');
    expect(sent['query'], 'hotel');
    expect(find.text('3 matches'), findsOneWidget);
    final first = tester.getTopLeft(find.text('You: hotel is booked')).dy;
    final last = tester.getTopLeft(find.text('You: any hotel yet?')).dy;
    expect(first, lessThan(last));
  });

  testWidgets('opening a match shows it in context with Previous and Next', (
    tester,
  ) async {
    await show(tester);
    await type(tester, 'hotel');

    await tester.tap(find.byKey(const Key('find-in-chat-hit-1')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('hit-position')), findsOneWidget);
    expect(find.text('2 of 3'), findsOneWidget);
    expect(
      http
          .sent('GET', '/api/v1/conversations/c1/messages')
          .last
          .query['around'],
      'm5',
    );

    await tester.tap(find.byKey(const Key('hit-next')));
    await tester.pumpAndSettle();
    expect(find.text('1 of 3'), findsOneWidget);
    // At the newest match there is nothing further to go to.
    expect(
      tester.widget<IconButton>(find.byKey(const Key('hit-next'))).onPressed,
      isNull,
    );

    await tester.tap(find.byKey(const Key('hit-previous')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('hit-previous')));
    await tester.pumpAndSettle();
    expect(find.text('3 of 3'), findsOneWidget);
    expect(
      tester
          .widget<IconButton>(find.byKey(const Key('hit-previous')))
          .onPressed,
      isNull,
    );
    // Stepping replaces the page: one Back returns to the list.
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('find-in-chat-field')), findsOneWidget);
  });

  testWidgets('an older answer never replaces a newer one', (tester) async {
    final slow = Completer<void>();
    http.beforeRespond = (options) async {
      if (options.queryParameters['query'] == 'old') await slow.future;
    };
    http.on('GET', '/api/v1/search?query=old&conversationId=c1', {
      'results': [_hit('m1', 'You: OLD', '2026-10-01T10:00:00Z')],
      'providers': <Object>[],
    });
    http.on('GET', '/api/v1/search?query=new&conversationId=c1', {
      'results': [_hit('m9', 'You: NEW', '2026-10-09T10:00:00Z')],
      'providers': <Object>[],
    });
    await show(tester);
    await tester.enterText(find.byKey(const Key('find-in-chat-field')), 'old');
    await tester.pump(const Duration(milliseconds: 400));
    await tester.enterText(find.byKey(const Key('find-in-chat-field')), 'new');
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();
    slow.complete();
    await tester.pumpAndSettle();

    expect(find.text('You: NEW'), findsOneWidget);
    expect(find.text('You: OLD'), findsNothing);
  });

  testWidgets('no matches and an unavailable chat are explained', (
    tester,
  ) async {
    http.on('GET', '/api/v1/search', {
      'results': <Object>[],
      'providers': <Object>[],
    });
    await show(tester);
    await type(tester, 'zzz');
    expect(find.byKey(const Key('find-in-chat-empty')), findsOneWidget);

    http.on('GET', '/api/v1/search', {'title': 'Not found'}, status: 404);
    await type(tester, 'again');
    expect(find.text('This chat is no longer available.'), findsOneWidget);
  });
}
