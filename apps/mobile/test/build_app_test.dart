import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/apps/build_app_screen.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/custom-mcp-servers/starters', [
      {
        'id': 'tracker',
        'title': 'A personal tracker',
        'purpose': 'Log something.',
        'starter': 'A tracker for my water intake',
      },
    ]);
  });

  testWidgets(
    'an idea fills the purpose and the contract is sent to chat, not built silently',
    (tester) async {
      http.on('POST', '/api/v1/custom-mcp-servers/starters/compose', {
        'prompt': 'Build me a small personal app',
      });
      String? asked;
      tester.view.physicalSize = const Size(800, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          home: BuildAppScreen(
            http: http.client(),
            onAskInChat: (text) => asked = text,
          ),
        ),
      );
      await tester.pumpAndSettle();

      await tester.tap(find.byKey(const Key('build-example-tracker')));
      await tester.pump();
      expect(find.text('A tracker for my water intake'), findsOneWidget);
      await tester.enterText(find.byKey(const Key('build-inputs')), 'glasses');
      await tester.ensureVisible(find.byKey(const Key('build-outside')));
      await tester.tap(find.byKey(const Key('build-outside')));
      await tester.pump();
      await tester.ensureVisible(find.byKey(const Key('build-start')));
      await tester.tap(find.byKey(const Key('build-start')));
      await tester.pumpAndSettle();

      final body =
          http
                  .sent('POST', '/api/v1/custom-mcp-servers/starters/compose')
                  .single
                  .body
              as Map;
      expect(body['purpose'], 'A tracker for my water intake');
      expect(body['inputs'], 'glasses');
      expect(body['changesOutside'], true);
      expect(asked, 'Build me a small personal app');
    },
  );

  testWidgets('a missing purpose is explained and chat is not opened', (
    tester,
  ) async {
    http.on('POST', '/api/v1/custom-mcp-servers/starters/compose', {
      'detail': 'Say what the app should do.',
    }, status: 400);
    String? asked;
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: BuildAppScreen(
          http: http.client(),
          onAskInChat: (text) => asked = text,
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.ensureVisible(find.byKey(const Key('build-start')));
    await tester.tap(find.byKey(const Key('build-start')));
    await tester.pumpAndSettle();

    expect(find.text('Say what the app should do.'), findsOneWidget);
    expect(asked, isNull);
  });
}
