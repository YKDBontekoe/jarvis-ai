import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/discover/discover_screen.dart';
import 'package:jarvis_mobile/features/discover/museum_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'screenshots/fixtures.dart' show collision, duels, museum;
import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp()
      ..on('GET', '/api/v1/discover/collision', collision())
      ..on('GET', '/api/v1/duels', duels())
      ..on('GET', '/api/v1/discover/museum', museum())
      ..on('GET', '/api/v1/agents', [
        {'id': 'agent-sam', 'name': 'Sam’s Jarvis'},
      ]);
  });

  Future<void> show(WidgetTester tester, Widget home) async {
    tester.view.physicalSize = const Size(900, 3200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(theme: buildJarvisTheme(), home: home));
    await tester.pumpAndSettle();
  }

  testWidgets('exploring a collision sends its prompt to chat', (tester) async {
    String? asked;
    await show(
      tester,
      DiscoverScreen(http: http.client(), onAskInChat: (text) => asked = text),
    );

    expect(find.text('fermentation · patience'), findsOneWidget);
    expect(find.text('11 – 9'), findsOneWidget);
    expect(find.text('YOU WON'), findsOneWidget);
    await tester.tap(find.byKey(const Key('discover-collision-explore')));
    expect(asked, startsWith('Help me see a connection'));
  });

  testWidgets('another collision replaces today’s', (tester) async {
    http.on('POST', '/api/v1/discover/collision/next', {
      ...collision(),
      'shared': ['harbour', 'rowing'],
    });
    await show(tester, DiscoverScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('discover-collision-next')));
    await tester.pumpAndSettle();

    expect(find.text('harbour · rowing'), findsOneWidget);
  });

  testWidgets('challenging a friend posts the chosen terms', (tester) async {
    http.on('POST', '/api/v1/duels', {'id': 'duel-new', 'code': 'ABCD2345'});
    await show(tester, DiscoverScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('discover-challenge')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Days journalled'));
    await tester.tap(find.text('3 days'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('duel-start')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/duels').single.body, {
      'remoteAgentId': 'agent-sam',
      'metric': 'journal_days',
      'days': 3,
    });
    expect(find.textContaining('ABCD2345'), findsOneWidget);
  });

  testWidgets('joining needs a full code', (tester) async {
    http.on('POST', '/api/v1/duels/join', {'id': 'duel-joined'});
    await show(tester, DiscoverScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('discover-join')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('duel-code')), 'abcd');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('duel-start')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/duels/join'), isEmpty);

    await tester.enterText(find.byKey(const Key('duel-code')), 'abcd2345');
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('duel-start')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/duels/join').single.body, {
      'remoteAgentId': 'agent-sam',
      'code': 'abcd2345',
    });
  });

  testWidgets('the museum walks room by room', (tester) async {
    await show(tester, MuseumScreen(http: http.client()));

    expect(find.text('Museum of 2026'), findsOneWidget);
    expect(find.text('214'), findsOneWidget);
    await tester.drag(
      find.byKey(const Key('museum-rooms')),
      const Offset(-800, 0),
    );
    await tester.pumpAndSettle();
    expect(find.text('10/10'), findsOneWidget);
  });
}
