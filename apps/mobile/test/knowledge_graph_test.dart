import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/memory/graph_layout.dart';
import 'package:jarvis_mobile/features/memory/graph_model.dart';
import 'package:jarvis_mobile/features/memory/knowledge_graph_screen.dart';

import 'support/fixture_http.dart';

Future<void> settle(WidgetTester tester) {
  return tester.pumpAndSettle(
    const Duration(milliseconds: 50),
    EnginePhase.sendSemanticsUpdate,
    const Duration(seconds: 20),
  );
}

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
      await settle(tester);

      expect(find.textContaining('Semantic search on · 9/9'), findsOneWidget);
      expect(find.textContaining('2 entities · 1 link'), findsOneWidget);
      expect(find.textContaining('lives in Amsterdam'), findsOneWidget);

      await tester.tap(find.byKey(const Key('entity-You')));
      await settle(tester);

      expect(
        find.descendant(
          of: find.byKey(const Key('graph-inspector')),
          matching: find.text('You lives in Amsterdam'),
        ),
        findsOneWidget,
      );
      await tester.tap(find.byKey(const Key('open-timeline')));
      await settle(tester);

      expect(find.text('You lives in Utrecht'), findsOneWidget);
      expect(find.textContaining('10 Jan 2026 →'), findsOneWidget);
    },
  );

  testWidgets('map selects a node, shows literal facts, and filters the list', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(900, 1600);
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
          'aliases': ['Robin'],
          'summary': 'Lives in the Netherlands',
          'relationCount': 3,
          'updatedAt': '2026-09-01T12:00:00Z',
        },
        {
          'id': 'a',
          'name': 'Amsterdam',
          'type': 'place',
          'aliases': [],
          'relationCount': 1,
        },
        {
          'id': 'j',
          'name': 'Jazz',
          'type': 'topic',
          'aliases': [],
          'relationCount': 1,
        },
      ],
      'edges': [
        {
          'from': 'u',
          'to': 'a',
          'predicate': 'lives_in',
          'confidence': 0.9,
          'validFrom': '2026-06-01T00:00:00Z',
        },
        {
          'from': 'u',
          'to': 'j',
          'predicate': 'likes',
          'confidence': 0.4,
          'validFrom': '2026-01-01T00:00:00Z',
        },
      ],
      'literals': [
        {
          'entityId': 'u',
          'predicate': 'job_title',
          'value': 'designer',
          'confidence': 0.8,
          'validFrom': '2026-03-01T00:00:00Z',
        },
      ],
    });
    http.on('GET', '/api/v1/memory/index-status', {
      'active': 4,
      'embedded': 0,
      'graphIndexed': 4,
      'embeddingModel': null,
    });

    await tester.pumpWidget(
      MaterialApp(home: KnowledgeGraphScreen(http: http.client())),
    );
    await settle(tester);

    expect(
      find.textContaining('3 entities · 2 links · 1 detail'),
      findsOneWidget,
    );
    expect(find.textContaining('aka Robin'), findsOneWidget);
    expect(find.textContaining('Lives in the Netherlands'), findsOneWidget);
    expect(find.textContaining('job title designer'), findsOneWidget);

    await tester.tap(find.byKey(const Key('node-You')));
    await settle(tester);
    expect(find.text('You job title designer'), findsOneWidget);
    expect(find.textContaining('90%'), findsWidgets);
    expect(find.text('Updated 1 Sep 2026'), findsOneWidget);

    await tester.tap(find.byKey(const Key('type-filter-place')));
    await settle(tester);
    expect(find.byKey(const Key('entity-Amsterdam')), findsOneWidget);
    expect(find.byKey(const Key('entity-You')), findsNothing);
    expect(find.byKey(const Key('node-You')), findsNothing);

    await tester.tap(find.byKey(const Key('type-filter-all')));
    await settle(tester);
    await tester.enterText(find.byKey(const Key('graph-search')), 'designer');
    await settle(tester);
    expect(find.byKey(const Key('entity-You')), findsOneWidget);
    expect(find.byKey(const Key('entity-Amsterdam')), findsNothing);
    expect(find.byKey(const Key('node-Amsterdam')), findsOneWidget);

    await tester.tap(find.byTooltip('Zoom in'));
    await settle(tester);
    expect(find.byKey(const Key('node-You')), findsOneWidget);
  });

  testWidgets('invalid entity payload shows an error instead of crashing', (
    tester,
  ) async {
    final http = FixtureHttp();
    http.on('GET', '/api/v1/graph/entities/u', 'nope');
    await tester.pumpWidget(
      MaterialApp(
        home: GraphEntityScreen(http: http.client(), entityId: 'u'),
      ),
    );
    await settle(tester);
    expect(tester.takeException(), isNull);
    expect(find.text('Could not load this entity.'), findsOneWidget);
  });

  test('overview parsing keeps links, literals, and neighborhood facts', () {
    final snapshot = parseGraphOverview({
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
        {
          'from': 'u',
          'to': 'a',
          'predicate': 'lives_in',
          'confidence': 1,
          'validFrom': '2026-01-01T00:00:00Z',
        },
        {'from': 'missing', 'to': 'a', 'predicate': 'near'},
      ],
      'literals': [
        {
          'entityId': 'u',
          'predicate': 'job_title',
          'value': 'designer',
          'confidence': 0.5,
        },
      ],
    });

    expect(snapshot.links, hasLength(1));
    expect(snapshot.literals, hasLength(1));
    final facts = factsFor('u', snapshot);
    expect(facts.map((fact) => fact.sentence), [
      'You lives in Amsterdam',
      'You job title designer',
    ]);
    expect(nodeMatchesQuery(snapshot.node('u')!, snapshot, 'designer'), isTrue);
    expect(
      nodeMatchesQuery(snapshot.node('a')!, snapshot, 'designer'),
      isFalse,
    );
    expect(nodeMatchesQuery(snapshot.node('a')!, snapshot, 'lives'), isTrue);
    expect(confidenceLabel(1), isNull);
    expect(confidenceLabel(0.9), '90%');
    expect(edgeLanes(snapshot.links..add(snapshot.links.first)), [
      (lane: 0, lanes: 2),
      (lane: 1, lanes: 2),
    ]);
  });

  test('labels skip overlapping names and keep a forced selection', () {
    final labels = chooseLabelIds(const [
      (id: 'a', anchor: Offset(0, 0), force: false),
      (id: 'b', anchor: Offset(8, 0), force: false),
      (id: 'c', anchor: Offset(8, 0), force: true),
    ]);
    expect(labels, {'a', 'c'});
  });
}
