import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/settings/codex_sign_in_card.dart';

import 'support/fixture_http.dart';

void main() {
  const path = '/api/v1/settings/models/codex/sign-in';

  Future<void> show(WidgetTester tester, FixtureHttp http) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: CodexSignInCard(
            http: http.client(),
            pollInterval: const Duration(milliseconds: 50),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows nothing when the server is signed in', (tester) async {
    final http = FixtureHttp()..on('GET', path, {'signedIn': true});
    await show(tester, http);

    expect(find.byKey(const Key('codex-sign-in')), findsNothing);
  });

  testWidgets('gets a code, shows it, and confirms when sign-in finishes', (
    tester,
  ) async {
    final http = FixtureHttp()..on('GET', path, {'signedIn': false});
    await show(tester, http);
    expect(find.text('Sign Jarvis in to ChatGPT'), findsOneWidget);

    final pending = {
      'signedIn': false,
      'pending': {
        'verificationUrl': 'https://auth.openai.com/codex/device',
        'userCode': 'AB12-CD34E',
        'expiresAt': '2026-10-03T10:15:00Z',
      },
    };
    http.on('POST', path, pending);
    http.on('GET', path, pending);
    await tester.tap(find.byKey(const Key('codex-sign-in-start')));
    for (var i = 0; i < 5; i++) {
      await tester.pump(const Duration(milliseconds: 5));
    }
    expect(find.text('AB12-CD34E'), findsOneWidget);
    expect(find.text('Open sign-in page'), findsOneWidget);

    http.on('GET', path, {'signedIn': true});
    for (var i = 0; i < 5; i++) {
      await tester.pump(const Duration(milliseconds: 30));
    }
    expect(find.byKey(const Key('codex-signed-in')), findsOneWidget);
    expect(find.text('AB12-CD34E'), findsNothing);
  });

  testWidgets('explains when a sign-in cannot start', (tester) async {
    final http = FixtureHttp()
      ..on('GET', path, {'signedIn': false})
      ..on('POST', path, {
        'error': 'codex_sign_in',
        'message':
            'Codex did not return a sign-in code. Try again in a minute.',
      }, status: 409);
    await show(tester, http);

    await tester.tap(find.byKey(const Key('codex-sign-in-start')));
    await tester.pumpAndSettle();

    expect(
      find.text('Codex did not return a sign-in code. Try again in a minute.'),
      findsOneWidget,
    );
  });
}
