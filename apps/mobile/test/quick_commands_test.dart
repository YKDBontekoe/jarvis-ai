import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/quick_commands.dart';
import 'package:jarvis_mobile/features/search/search_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  test('an empty query offers the create commands', () {
    expect(matchQuickCommands('').map((c) => c.label), [
      'New reminder',
      'Start a background task',
      'Add a memory',
    ]);
  });

  test('typing matches commands in English and Dutch', () {
    expect(matchQuickCommands('remind').first.destination, 'reminders/new');
    expect(matchQuickCommands('herinner').first.destination, 'reminders/new');
    expect(
      matchQuickCommands('habit').map((c) => c.destination),
      contains('habits'),
    );
    expect(matchQuickCommands('r'), isEmpty);
    expect(matchQuickCommands('zzzz'), isEmpty);
  });

  testWidgets('search runs a command by leaving and opening its page', (
    tester,
  ) async {
    final http = FixtureHttp()
      ..on('GET', '/api/v1/search', {'results': <Object>[]});
    final opened = <String>[];
    final navigator = GlobalKey<NavigatorState>();
    await tester.pumpWidget(
      MaterialApp(
        navigatorKey: navigator,
        theme: buildJarvisTheme(),
        home: const Scaffold(body: Text('chat')),
      ),
    );
    navigator.currentState!.push(
      MaterialPageRoute<void>(
        builder: (_) => SearchScreen(
          http: http.client(),
          onConversation: (_) async {},
          onUtility: opened.add,
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.text('New reminder'));
    await tester.pumpAndSettle();
    expect(opened, ['reminders/new']);
    expect(find.text('chat'), findsOneWidget);
  });
}
