import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/search_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> hit(
  String kind,
  String id,
  String title,
  Map<String, String> params,
) => {
  'kind': kind,
  'id': id,
  'title': title,
  'summary': 's',
  'timestamp': '2026-10-02T00:00:00Z',
  'route': {'kind': kind, 'parameters': params},
  'relevance': .9,
  'isPinned': false,
};

void main() {
  late FixtureHttp http;
  late List<String> opened;

  setUp(() {
    http = FixtureHttp();
    opened = [];
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: SearchScreen(
          http: http.client(),
          onConversation: (_) async {},
          onUtility: opened.add,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('a time window sends a from date and drops nothing by itself', (
    tester,
  ) async {
    http.on('GET', '/api/v1/search', {
      'results': <Object>[],
      'providers': <Object>[],
    });
    await show(tester);

    await tester.enterText(find.byType(TextField), 'canal');
    await tester.pump(const Duration(milliseconds: 300));
    expect(
      http.sent('GET', '/api/v1/search').last.query.containsKey('from'),
      isFalse,
    );

    await tester.tap(find.byKey(const Key('search-window-7')));
    await tester.pump(const Duration(milliseconds: 300));
    await tester.pumpAndSettle();

    final from =
        http.sent('GET', '/api/v1/search').last.query['from'] as String;
    expect(RegExp(r'^\d{4}-\d{2}-\d{2}$').hasMatch(from), isTrue);
  });

  testWidgets('journal, project, goal and trip hits open the right page', (
    tester,
  ) async {
    http.on('GET', '/api/v1/search', {
      'results': [
        hit('journal', 'j1', '2 Oct 2026', {'date': '2026-10-02'}),
        hit('project', 'p1', 'Home renovation', {'projectId': 'p1'}),
        hit('goal', 'g1', 'Run a 10k', {'goalId': 'g1'}),
        hit('trip', 't1', 'Lisbon', {'tripId': 't1'}),
      ],
      'providers': <Object>[],
    });
    await show(tester);

    await tester.enterText(find.byType(TextField), 'a');
    await tester.pump(const Duration(milliseconds: 300));
    await tester.pumpAndSettle();
    for (final title in [
      '2 Oct 2026',
      'Home renovation',
      'Run a 10k',
      'Lisbon',
    ]) {
      await tester.tap(find.text(title));
      await tester.pumpAndSettle();
    }

    expect(opened, ['journal', 'project:p1', 'goal:g1', 'trip:t1']);
  });

  testWidgets('the new kinds have filter chips', (tester) async {
    await show(tester);

    for (final label in ['Journal', 'Projects', 'Missions', 'Goals', 'Trips']) {
      expect(find.text(label), findsOneWidget);
    }
  });
}
