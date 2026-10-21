import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/message_context_screen.dart';
import 'package:jarvis_mobile/features/search/search_models.dart';
import 'package:jarvis_mobile/features/search/search_navigation.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _message(int index, {String? role}) => {
  'id': 'm$index',
  'role': role ?? (index.isEven ? 'user' : 'assistant'),
  'content': 'Message number $index',
  'createdAt': '2026-10-01T10:${index.toString().padLeft(2, '0')}:00Z',
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, {List<String>? opened}) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      _host(
        MessageContextScreen(
          http: http.client(),
          conversationId: 'c1',
          messageId: 'm5',
          title: 'Weekend plans',
          onOpenConversation: (id) async => opened?.add(id),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'opens at the hit, highlights it for a moment, hides tool traffic',
    (tester) async {
      http.on('GET', '/api/v1/conversations/c1/messages', {
        'items': [
          _message(4),
          _message(5),
          _message(6),
          _message(7, role: 'tool'),
        ],
        'nextCursor': 'older',
        'hasMore': true,
        'newerCursor': null,
        'hasNewer': false,
        'anchorId': 'm5',
      });
      await show(tester);

      final sent = http.sent('GET', '/api/v1/conversations/c1/messages').single;
      expect(sent.query['around'], 'm5');
      expect(find.text('Message number 5'), findsOneWidget);
      expect(find.text('Message number 7'), findsNothing);
      expect(find.byKey(const Key('message-context-older')), findsOneWidget);
      expect(find.byKey(const Key('message-context-newer')), findsNothing);
      expect(find.text('End of the conversation'), findsOneWidget);

      var decoration =
          tester
                  .widget<AnimatedContainer>(
                    find.byKey(const Key('message-context-hit')),
                  )
                  .decoration!
              as BoxDecoration;
      expect(decoration.border, isNotNull);
      expect(decoration.color, isNot(Colors.transparent));

      await tester.pump(const Duration(seconds: 4));
      await tester.pumpAndSettle();
      decoration =
          tester
                  .widget<AnimatedContainer>(
                    find.byKey(const Key('message-context-hit')),
                  )
                  .decoration!
              as BoxDecoration;
      expect(decoration.color, Colors.transparent);
    },
  );

  testWidgets('earlier and later messages load without repeating any', (
    tester,
  ) async {
    http.on('GET', '/api/v1/conversations/c1/messages', {
      'items': [_message(4), _message(5), _message(6)],
      'nextCursor': 'older-cursor',
      'hasMore': true,
      'newerCursor': 'newer-cursor',
      'hasNewer': true,
      'anchorId': 'm5',
    });
    await show(tester);

    http.on('GET', '/api/v1/conversations/c1/messages', {
      'items': [_message(2), _message(3), _message(4)],
      'nextCursor': null,
      'hasMore': false,
    });
    await tester.tap(find.byKey(const Key('message-context-older')));
    await tester.pumpAndSettle();
    expect(find.text('Message number 2'), findsOneWidget);
    expect(find.text('Message number 4'), findsOneWidget);
    expect(find.byKey(const Key('message-context-older')), findsNothing);
    final older = http.sent('GET', '/api/v1/conversations/c1/messages').last;
    expect(older.query['cursor'], 'older-cursor');

    http.on('GET', '/api/v1/conversations/c1/messages', {
      'items': [_message(6), _message(7)],
      'newerCursor': null,
      'hasNewer': false,
    });
    await tester.ensureVisible(find.byKey(const Key('message-context-newer')));
    await tester.tap(find.byKey(const Key('message-context-newer')));
    await tester.pumpAndSettle();
    expect(find.text('Message number 7'), findsOneWidget);
    expect(find.text('Message number 6'), findsOneWidget);
    final newer = http.sent('GET', '/api/v1/conversations/c1/messages').last;
    expect(newer.query['after'], 'newer-cursor');
    expect(find.text('End of the conversation'), findsOneWidget);
  });

  testWidgets('Open chat continues the conversation itself', (tester) async {
    http.on('GET', '/api/v1/conversations/c1/messages', {
      'items': [_message(5)],
      'hasMore': false,
      'hasNewer': false,
    });
    final opened = <String>[];
    await show(tester, opened: opened);

    await tester.tap(find.byKey(const Key('message-context-open-chat')));
    await tester.pumpAndSettle();
    expect(opened, ['c1']);
  });

  testWidgets('a deleted message says so instead of failing', (tester) async {
    http.on('GET', '/api/v1/conversations/c1/messages', null, status: 404);
    await show(tester);
    expect(find.text('That message is gone'), findsOneWidget);
    expect(find.byKey(const Key('message-context-open-chat')), findsNothing);
  });

  test('message, person, commitment and decision hits carry typed routes', () {
    final hit = FederatedSearchHit.fromJson({
      'kind': 'message',
      'id': 'm5',
      'title': 'Weekend plans',
      'summary': 'You: the boiler',
      'route': {
        'kind': 'message',
        'parameters': {'conversationId': 'c1', 'messageId': 'm5'},
      },
    });
    expect(hit?.route.parameters['messageId'], 'm5');
    for (final kind in ['message', 'person', 'commitment', 'decision']) {
      expect(searchKindLabels, contains(kind));
    }
    final route = searchRouteFromNotification({
      'routeKind': 'commitment',
      'commitmentId': 'c9',
    });
    expect(route?.parameters['commitmentId'], 'c9');
  });
}
