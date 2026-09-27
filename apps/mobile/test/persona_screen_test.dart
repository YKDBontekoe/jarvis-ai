import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/persona/persona_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _profile({List<Map<String, Object?>> traits = const []}) =>
    {
      'customInstructions': 'I run a bakery.',
      'preferredName': 'Sam',
      'replyLanguage': 'Dutch',
      'traits': traits,
      'lastReflectedAt': null,
    };

Map<String, Object?> _trait(
  String id,
  String statement, {
  bool pinned = false,
  String source = 'learned',
}) => {
  'id': id,
  'category': 'format',
  'statement': statement,
  'confidence': .72,
  'evidence': 3,
  'source': source,
  'pinned': pinned,
  'createdAt': '2026-09-27T10:00:00Z',
  'updatedAt': '2026-09-27T10:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets('shows learned traits with evidence and saves the profile', (
    tester,
  ) async {
    http.on(
      'GET',
      '/api/v1/persona',
      _profile(traits: [_trait('t1', 'Use bullet lists for options.')]),
    );
    http.on('PUT', '/api/v1/persona', _profile());
    await show(tester, PersonaScreen(http: http.client()));

    expect(find.text('Use bullet lists for options.'), findsOneWidget);
    expect(find.textContaining('Format · learned · seen 3×'), findsOneWidget);
    expect(find.text('Sam'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('persona-language')),
      'English',
    );
    await tester.tap(find.byKey(const Key('save-persona')));
    await tester.pumpAndSettle();

    final body =
        http.sent('PUT', '/api/v1/persona').single.body as Map<String, dynamic>;
    expect(body['replyLanguage'], 'English');
    expect(body['preferredName'], 'Sam');
  });

  testWidgets('pinning and forgetting a trait call the trait endpoints', (
    tester,
  ) async {
    http.on(
      'GET',
      '/api/v1/persona',
      _profile(traits: [_trait('t1', 'Use bullet lists for options.')]),
    );
    http.on(
      'PATCH',
      '/api/v1/persona/traits/t1',
      _trait('t1', 'x', pinned: true),
    );
    http.on('DELETE', '/api/v1/persona/traits/t1', null, status: 204);
    await show(tester, PersonaScreen(http: http.client()));

    await tester.tap(find.byTooltip('Pin — never forget'));
    await tester.pumpAndSettle();
    expect(http.sent('PATCH', '/api/v1/persona/traits/t1').single.body, {
      'pinned': true,
    });

    await tester.tap(find.byTooltip('Forget'));
    await tester.pumpAndSettle();
    expect(http.sent('DELETE', '/api/v1/persona/traits/t1'), hasLength(1));
  });

  testWidgets('assistant replies with an id offer thumbs up and down', (
    tester,
  ) async {
    final ratings = <String>[];
    await show(
      tester,
      Scaffold(
        body: MessageBubble(
          message: const MessageEntry(
            role: 'assistant',
            content: 'Here you go.',
            id: 'm1',
            rating: 'up',
          ),
          onRate: ratings.add,
        ),
      ),
    );

    expect(find.byTooltip('Good reply'), findsOneWidget);
    await tester.tap(find.byTooltip('Could be better'));
    expect(ratings, ['down']);
  });
}
