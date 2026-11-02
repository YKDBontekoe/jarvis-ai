import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/entities/entity_ref.dart';
import 'package:jarvis_mobile/features/entities/sources_panel.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/source_row.dart';

import 'support/fixture_http.dart';

const _memory = '11111111-1111-4111-8111-111111111111';
const _chat = '22222222-2222-4222-8222-222222222222';
const _project = '33333333-3333-4333-8333-333333333333';
const _gone = '44444444-4444-4444-8444-444444444444';

void main() {
  late FixtureHttp http;
  late List<String> openedChats;

  setUp(() {
    http = FixtureHttp();
    openedChats = [];
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(600, 1200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: Padding(padding: const EdgeInsets.all(16), child: child),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  group('SourceRow', () {
    testWidgets('shows the name, kind, date and an Open button', (
      tester,
    ) async {
      var opened = 0;
      await show(
        tester,
        SourceRow(
          name: 'Planning the trip',
          kind: 'Chat',
          date: DateTime(2026, 10, 2),
          onOpen: () => opened++,
        ),
      );
      expect(find.text('Planning the trip'), findsOneWidget);
      expect(find.textContaining('Chat · '), findsOneWidget);
      await tester.tap(find.byKey(const Key('source-open')));
      expect(opened, 1);
    });

    testWidgets('a deleted source says so and offers no Open', (tester) async {
      await show(
        tester,
        SourceRow(name: 'Old chat', unavailable: true, onOpen: () {}),
      );
      expect(find.text('Source no longer available'), findsOneWidget);
      expect(find.byKey(const Key('source-open')), findsNothing);
      expect(find.text('Old chat'), findsNothing);
    });
  });

  group('linked sources', () {
    void relate(List<Map<String, Object?>> items) =>
        http.on('GET', '/api/v1/entities/memory/$_memory/related', {
          'ref': 'memory:$_memory',
          'title': 'Rosa likes Fridays',
          'related': items,
        });

    Widget panel({String? collapsed}) => SourcesPanel.of(
      http: http.client(),
      entity: EntityRef.tryParse('memory:$_memory')!,
      collapsedLabel: collapsed,
      onOpenConversation: (id) async => openedChats.add(id),
    );

    testWidgets('the chat a memory came from shows its name and date and '
        'opens', (tester) async {
      relate([
        {
          'ref': 'conversation:$_chat',
          'type': 'conversation',
          'relation': 'source',
          'direction': 'in',
          'title': 'Planning the trip',
          'at': null,
        },
      ]);
      http.on('GET', '/api/v1/conversations/$_chat', {
        'id': _chat,
        'title': 'Planning the trip',
        'createdAt': '2026-10-02T09:00:00Z',
      });
      await show(tester, panel());

      expect(find.text('Planning the trip'), findsOneWidget);
      expect(find.textContaining('Chat · '), findsOneWidget);
      await tester.tap(find.byKey(const Key('source-open')));
      await tester.pumpAndSettle();
      expect(openedChats, [_chat]);
    });

    testWidgets('a source that was deleted is shown as unavailable', (
      tester,
    ) async {
      relate(const []);
      await show(tester, panel());
      expect(find.text('Source no longer available'), findsOneWidget);
      expect(find.byKey(const Key('source-open')), findsNothing);
    });

    testWidgets('other relations are not evidence', (tester) async {
      relate([
        {
          'ref': 'reminder:$_gone',
          'type': 'reminder',
          'relation': 'reminds',
          'direction': 'out',
          'title': 'Call',
        },
      ]);
      await show(tester, panel());
      expect(find.text('Call'), findsNothing);
    });

    testWidgets('a collapsed panel loads only when asked', (tester) async {
      relate([
        {
          'ref': 'conversation:$_chat',
          'type': 'conversation',
          'relation': 'source',
          'direction': 'in',
          'title': 'Planning the trip',
          'at': '2026-10-02T09:00:00Z',
        },
      ]);
      await show(tester, panel(collapsed: 'Learned from a conversation'));

      expect(find.text('Learned from a conversation'), findsOneWidget);
      expect(http.requests, isEmpty);

      await tester.tap(find.byKey(const Key('sources-expand')));
      await tester.pumpAndSettle();
      expect(find.text('Planning the trip'), findsOneWidget);
      expect(http.requests, isNotEmpty);
    });
  });

  group('cited references', () {
    testWidgets('each one is resolved; a missing one is unavailable and an '
        'unknown format is kept as text', (tester) async {
      http.on('GET', '/api/v1/entities/project/$_project/related', {
        'ref': 'project:$_project',
        'title': 'Home renovation',
        'related': <Object>[],
      });
      // $_gone is not stored: the fixture answers 404.
      await show(
        tester,
        SourcesPanel.refs(
          http: http.client(),
          refs: ['project:$_project', 'project:$_gone', 'library:abc'],
        ),
      );

      expect(find.text('Home renovation'), findsOneWidget);
      expect(find.text('Source no longer available'), findsOneWidget);
      expect(find.text('library:abc'), findsOneWidget);
      // Only the real one can be opened.
      expect(find.byKey(const Key('source-open')), findsOneWidget);
    });
  });
}
