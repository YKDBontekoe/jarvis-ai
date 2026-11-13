import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/game/game_format.dart';
import 'package:jarvis_mobile/features/game/game_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'screenshots/fixtures.dart' show gameBoard;
import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 3200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: GameScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();
  }

  test('formats deadlines, levels and ghost values in plain words', () {
    final today = DateTime(2026, 11, 11);
    expect(betDeadline(DateTime(2026, 11, 11), today), 'Settles today');
    expect(betDeadline(DateTime(2026, 11, 12), today), 'Settles tomorrow');
    expect(betDeadline(DateTime(2026, 11, 30), today), 'Until Mon 30 Nov');
    expect(levelSegments(214, 100, 300), 5);
    expect(levelSegments(100, 100, 300), 0);
    expect(levelSegments(999, 100, 300), 10);
    expect(ghostValue(96.4, 'EUR'), '€96');
    expect(ghostValue(7, 'check-ins'), '7');
    expect(ghostValue(3.8, 'of 5'), '3.8');
    expect(ghostValue(12, 'CHF'), '12 CHF');
    expect(ghostSummary(2, 1), 'Ahead on 2, behind on 1');
    expect(ghostSummary(2, 0), 'Ahead of your ghost on 2');
    expect(ghostSummary(0, 0), 'Neck and neck with your ghost');
  });

  testWidgets('shows the score, open bets, quests and the ghost', (
    tester,
  ) async {
    http.on('GET', '/api/v1/game', gameBoard());
    await show(tester);

    expect(find.text('214 points'), findsOneWidget);
    expect(find.text('LEVEL 2 · CONTRARIAN'), findsOneWidget);
    expect(find.text('You’ll skip Exercise on Thursday'), findsOneWidget);
    expect(find.text('JARVIS 88% SURE · SETTLES TOMORROW'), findsOneWidget);
    expect(find.text('Beat it 81'), findsOneWidget);
    expect(find.text('YOU PROVED JARVIS WRONG · +41'), findsOneWidget);
    expect(find.text('BOSS · LONGEST AVOIDED'), findsOneWidget);
    expect(find.text('2 of 3'), findsOneWidget);
    expect(find.text('DONE · +20'), findsOneWidget);
    expect(find.text('Ahead on 2, behind on 1'), findsOneWidget);
    expect(find.text('€96'), findsOneWidget);
    // Finished quests have no buttons.
    expect(find.byKey(const Key('game-quest-done-q-journal')), findsNothing);
  });

  testWidgets('finishing a quest posts it and reloads the board', (
    tester,
  ) async {
    http.on('GET', '/api/v1/game', gameBoard());
    http.on('POST', '/api/v1/game/quests/q-person/complete', {
      'id': 'q-person',
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('game-quest-done-q-person')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/game/quests/q-person/complete'),
      hasLength(1),
    );
    expect(http.sent('GET', '/api/v1/game'), hasLength(2));
    expect(find.text('Quest done. +30 points.'), findsOneWidget);
  });

  testWidgets('calling off a bet asks first', (tester) async {
    http.on('GET', '/api/v1/game', gameBoard());
    http.on('POST', '/api/v1/game/bets/bet1/call-off', {'id': 'bet1'});
    await show(tester);

    await tester.tap(find.byKey(const Key('game-bet-call-off-bet1')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Keep the bet'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/game/bets/bet1/call-off'), isEmpty);

    await tester.tap(find.byKey(const Key('game-bet-call-off-bet1')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.text('Call it off'),
      ),
    );
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/game/bets/bet1/call-off'), hasLength(1));
    expect(find.text('Bet called off.'), findsOneWidget);
  });

  testWidgets('a failed load says what failed', (tester) async {
    http.on('GET', '/api/v1/game', null, status: 500);
    await show(tester);

    expect(find.textContaining('Could not load the game'), findsOneWidget);
  });
}
