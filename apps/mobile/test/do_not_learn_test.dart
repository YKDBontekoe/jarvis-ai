import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/memory/do_not_learn_screen.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: DoNotLearnScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('an empty list says Jarvis learns within the other settings', (
    tester,
  ) async {
    http.on('GET', '/api/v1/memories/do-not-learn', {'topics': <String>[]});
    await show(tester);

    expect(find.byKey(const Key('dnl-empty')), findsOneWidget);
  });

  testWidgets('adding a topic sends the whole list', (tester) async {
    http.on('GET', '/api/v1/memories/do-not-learn', {
      'topics': ['My health'],
    });
    http.on('PUT', '/api/v1/memories/do-not-learn', {
      'topics': ['My health', 'Money'],
    });
    await show(tester);

    await tester.enterText(find.byKey(const Key('dnl-topic')), 'Money');
    await tester.tap(find.byKey(const Key('dnl-add')));
    await tester.pumpAndSettle();

    expect(
      (http.sent('PUT', '/api/v1/memories/do-not-learn').single.body
          as Map)['topics'],
      ['My health', 'Money'],
    );
    expect(find.byKey(const Key('dnl-chip-Money')), findsOneWidget);
  });

  testWidgets('removing a topic sends the rest', (tester) async {
    http.on('GET', '/api/v1/memories/do-not-learn', {
      'topics': ['My health', 'Money'],
    });
    http.on('PUT', '/api/v1/memories/do-not-learn', {
      'topics': ['Money'],
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('dnl-remove-My health')));
    await tester.pumpAndSettle();

    expect(
      (http.sent('PUT', '/api/v1/memories/do-not-learn').single.body
          as Map)['topics'],
      ['Money'],
    );
  });
}
