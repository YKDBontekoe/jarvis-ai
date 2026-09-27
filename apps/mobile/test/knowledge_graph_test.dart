import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/memory/graph_layout.dart';
import 'package:jarvis_mobile/features/memory/knowledge_graph_screen.dart';

import 'support/fixture_http.dart';

void main() {
  test(
    'layout is deterministic, bounded, and keeps the pinned node centered',
    () {
      final nodes = ['you', 'anna', 'utrecht', 'philips', 'jazz'];
      final edges = [
        ('you', 'anna'),
        ('you', 'utrecht'),
        ('anna', 'philips'),
        ('you', 'jazz'),
      ];

      final first = layoutGraph(nodes, edges, pinned: 'you');
      final second = layoutGraph(nodes, edges, pinned: 'you');

      expect(first, second);
      expect(first['you'], const Offset(.5, .5));
      for (final point in first.values) {
        expect(point.dx, inInclusiveRange(0, 1));
        expect(point.dy, inInclusiveRange(0, 1));
      }
      final distinct = first.values.map(
        (p) => '${p.dx.toStringAsFixed(3)},${p.dy.toStringAsFixed(3)}',
      );
      expect(distinct.toSet(), hasLength(nodes.length));
    },
  );

  testWidgets(
    'graph screen shows semantic status and opens an entity timeline',
    (tester) async {
      tester.view.physicalSize = const Size(900, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      final http = FixtureHttp();
      http.on('GET', '/api/v1/graph/overview', {
        'entities': [
          {
            'id': 'u',
            'name': 'You',
            'type': 'person',
            'aliases': [],
            'relationCount': 2,
          },
          {
            'id': 'a',
            'name': 'Amsterdam',
            'type': 'place',
            'aliases': [],
            'relationCount': 1,
          },
        ],
        'edges': [
          {'from': 'u', 'to': 'a', 'predicate': 'lives_in'},
        ],
      });
      http.on('GET', '/api/v1/memory/index-status', {
        'active': 9,
        'embedded': 9,
        'graphIndexed': 9,
        'embeddingModel': 'openrouter:openai/text-embedding-3-small',
      });
      http.on('GET', '/api/v1/graph/entities/u', {
        'entity': {'id': 'u', 'name': 'You', 'type': 'person', 'aliases': []},
        'current': [
          {
            'subjectName': 'You',
            'predicate': 'lives_in',
            'objectName': 'Amsterdam',
            'validFrom': '2026-09-27T15:40:54Z',
            'validTo': null,
          },
        ],
        'history': [
          {
            'subjectName': 'You',
            'predicate': 'lives_in',
            'objectName': 'Utrecht',
            'validFrom': '2026-01-10T10:00:00Z',
            'validTo': '2026-09-27T15:40:54Z',
          },
        ],
      });

      await tester.pumpWidget(
        MaterialApp(home: KnowledgeGraphScreen(http: http.client())),
      );
      await tester.pumpAndSettle();

      expect(find.textContaining('Semantic search on · 9/9'), findsOneWidget);
      await tester.tap(find.byKey(const Key('entity-You')));
      await tester.pumpAndSettle();

      expect(find.text('You lives in Amsterdam'), findsOneWidget);
      expect(find.text('You lives in Utrecht'), findsOneWidget);
      expect(find.textContaining('10 Jan 2026 →'), findsOneWidget);
    },
  );
}
