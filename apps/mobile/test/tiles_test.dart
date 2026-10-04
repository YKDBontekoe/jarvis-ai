import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/utility_pages.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

List<TilePlacement> _items(List<TileSize> sizes) => [
  for (var i = 0; i < sizes.length; i++) TilePlacement('t$i', sizes[i]),
];

void main() {
  group('packTiles', () {
    test('four icons fill one row', () {
      final placed = packTiles(_items(List.filled(4, TileSize.icon)));
      expect([for (final p in placed) (p.column, p.row)], [
        (0, 0),
        (1, 0),
        (2, 0),
        (3, 0),
      ]);
      expect(gridRows(placed), 1);
    });

    test('a small tile fills the gap beside a square', () {
      final placed = packTiles(
        _items([TileSize.square, TileSize.wide, TileSize.icon]),
      );
      expect((placed[0].column, placed[0].row), (0, 0));
      expect((placed[1].column, placed[1].row), (0, 2));
      expect((placed[2].column, placed[2].row), (2, 0));
      expect(gridRows(placed), 4);
    });

    test('two strips share a row and two squares sit side by side', () {
      final strips = packTiles(_items([TileSize.strip, TileSize.strip]));
      expect([for (final p in strips) p.column], [0, 2]);
      final squares = packTiles(_items([TileSize.square, TileSize.square]));
      expect([for (final p in squares) p.column], [0, 2]);
      expect(gridRows(squares), 2);
    });

    test('a large tile takes the whole width for four rows', () {
      final placed = packTiles(_items([TileSize.large, TileSize.icon]));
      expect(placed[0].columns, 4);
      expect(placed[0].rows, 4);
      expect(placed[1].row, 4);
    });

    test('an empty layout needs no rows', () {
      expect(gridRows(packTiles(const [])), 0);
    });

    test('no two tiles overlap in the default layout', () {
      final placed = packTiles(defaultTileLayout);
      final cells = <(int, int)>{};
      for (final tile in placed) {
        for (var r = tile.row; r < tile.row + tile.rows; r++) {
          for (var c = tile.column; c < tile.column + tile.columns; c++) {
            expect(cells.add((c, r)), isTrue, reason: 'cell $c,$r reused');
          }
        }
      }
    });
  });

  group('layout storage', () {
    test('normalising drops unknown and repeated tiles and fixes sizes', () {
      final result = normalizeTileLayout(const [
        TilePlacement('tasks', TileSize.square),
        TilePlacement('nope', TileSize.icon),
        TilePlacement('tasks', TileSize.wide),
        TilePlacement('voice', TileSize.large),
      ]);
      expect([for (final p in result) p.id], ['tasks', 'voice']);
      expect(result.first.size, TileSize.square);
      // Voice only offers icon and strip.
      expect(result.last.size, TileSize.strip);
    });

    test('decoding ignores junk and returns null for broken JSON', () {
      expect(decodeTileLayout(null), isNull);
      expect(decodeTileLayout('{'), isNull);
      expect(decodeTileLayout('{"a":1}'), isNull);
      final result = decodeTileLayout(
        '[{"id":"tasks","size":"wide"},{"id":3},{"id":"memory","size":"huge"}]',
      );
      expect([for (final p in result!) p.id], ['tasks']);
      expect(result.single.size, TileSize.wide);
    });

    test('every default tile exists and supports its size', () {
      for (final item in defaultTileLayout) {
        final spec = tileSpecFor(item.id);
        expect(spec, isNotNull, reason: item.id);
        expect(spec!.supports(item.size), isTrue, reason: item.id);
      }
    });

    test('the controller saves changes and a new one reads them back', () async {
      SharedPreferences.setMockInitialValues({});
      final first = TileLayoutController();
      await first.open();
      expect(first.layout.map((p) => p.id), defaultTileLayout.map((p) => p.id));

      first.unpin('memory');
      first.pin('finance', TileSize.square);
      first.cycleSize('tasks');
      first.move(0, 2);
      await Future<void>.delayed(Duration.zero);

      final second = TileLayoutController();
      await second.open();
      expect(second.layout.map((p) => p.id), first.layout.map((p) => p.id));
      expect(second.contains('memory'), isFalse);
      expect(second.sizeOf('finance'), TileSize.square);
      expect(second.sizeOf('tasks'), first.sizeOf('tasks'));
      expect(second.sizeOf('tasks'), isNot(TileSize.square));
    });

    test('pinning twice updates the size, and resizing wraps around', () {
      final controller = TileLayoutController();
      controller.pin('today', TileSize.icon);
      controller.pin('today', TileSize.wide);
      expect(controller.layout.where((p) => p.id == 'today'), hasLength(1));
      expect(controller.sizeOf('today'), TileSize.wide);
      for (var i = 0; i < tileSpecFor('today')!.sizes.length; i++) {
        controller.cycleSize('today');
      }
      expect(controller.sizeOf('today'), TileSize.wide);
      controller.pin('nope', TileSize.icon);
      expect(controller.contains('nope'), isFalse);
    });

    test('moving keeps every tile and ignores bad indexes', () {
      final controller = TileLayoutController();
      final before = controller.layout.map((p) => p.id).toList();
      controller.move(-1, 2);
      controller.move(0, 99);
      controller.move(1, 1);
      expect(controller.layout.map((p) => p.id), before);
      controller.move(0, before.length - 1);
      expect(controller.layout.last.id, before.first);
      expect(controller.layout, hasLength(before.length));
    });

    test('reset restores the default layout', () {
      final controller = TileLayoutController()..unpin('tasks');
      controller.reset();
      expect(
        controller.layout.map((p) => p.id),
        defaultTileLayout.map((p) => p.id),
      );
    });
  });

  group('registry', () {
    test('ids are unique and every tile offers a size', () {
      final ids = tileSpecs.map((s) => s.id).toList();
      expect(ids.toSet(), hasLength(ids.length));
      for (final spec in tileSpecs) {
        expect(spec.sizes, isNotEmpty, reason: spec.id);
        expect(spec.sizes.toSet(), hasLength(spec.sizes.length));
        expect(spec.fallback, isNotEmpty, reason: spec.id);
      }
    });

    test('every tile opens a page the app can show', () {
      final http = Dio();
      const handledByShell = {'chats', 'voice', 'settings'};
      for (final spec in tileSpecs) {
        if (handledByShell.contains(spec.destination)) continue;
        expect(
          utilityPageFor(spec.destination, http),
          isNotNull,
          reason: '${spec.id} → ${spec.destination}',
        );
      }
    });

    test('nearest picks the closest size a tile offers', () {
      final voice = tileSpecFor('voice')!;
      expect(voice.nearest(TileSize.large), TileSize.strip);
      expect(voice.nearest(TileSize.icon), TileSize.icon);
      expect(tileSpecFor('graph')!.nearest(TileSize.wide), TileSize.strip);
    });
  });

  group('loaders', () {
    late FixtureHttp http;
    late TileDataSource source;
    final now = DateTime(2026, 10, 3, 12);
    String iso(DateTime time) => time.toUtc().toIso8601String();

    setUp(() {
      http = FixtureHttp();
      source = TileDataSource(http.client(), clock: () => now);
    });

    Future<TileData?> load(String id) => source.load(tileSpecFor(id)!);

    test('tasks count the active ones and flag those waiting on you', () async {
      http.on('GET', '/api/v1/tasks', [
        {'id': '1', 'title': 'Compare flights', 'status': 'running'},
        {'id': '2', 'title': 'Bank summary', 'status': 'needs_approval'},
        {'id': '3', 'title': 'Old one', 'status': 'completed'},
      ]);
      final data = (await load('tasks'))!;
      expect(data.stat, '2');
      expect(data.unit, 'active');
      expect(data.attention, isTrue);
      expect(data.rows.map((r) => r.text), ['Compare flights', 'Bank summary']);
      expect(data.rows.last.attention, isTrue);
      expect(data.rows.first.meta, 'Running');
    });

    test('no active tasks reads as quiet', () async {
      http.on('GET', '/api/v1/tasks', const []);
      final data = (await load('tasks'))!;
      expect(data.stat, '0');
      expect(data.attention, isFalse);
      expect(data.subtitle, 'All quiet');
    });

    test('reminders show what is due today first', () async {
      http.on('GET', '/api/v1/reminders', [
        {
          'id': '1',
          'title': 'Call mum',
          'status': 'pending',
          'dueAt': iso(DateTime(2026, 10, 3, 19, 30)),
        },
        {
          'id': '2',
          'title': 'Renew passport',
          'status': 'pending',
          'dueAt': iso(DateTime(2026, 10, 15, 9)),
        },
        {
          'id': '3',
          'title': 'Done already',
          'status': 'delivered',
          'dueAt': iso(DateTime(2026, 10, 3, 8)),
        },
      ]);
      final data = (await load('reminders'))!;
      expect(data.stat, '1');
      expect(data.unit, 'today');
      expect(data.attention, isTrue);
      expect(data.rows.map((r) => r.text), ['Call mum', 'Renew passport']);
      expect(data.rows.first.meta, '19:30');
      expect(data.rows.last.meta, '15 Oct');
    });

    test('habits show progress for today', () async {
      http.on('GET', '/api/v1/habits', {
        'habits': [
          {
            'name': 'Run',
            'stats': {'doneToday': true, 'currentStreak': 12},
          },
          {
            'name': 'Read',
            'stats': {'doneToday': false, 'currentStreak': 4},
          },
          {
            'name': 'Water',
            'stats': {'doneToday': true, 'currentStreak': 21},
          },
        ],
      });
      final data = (await load('habits'))!;
      expect(data.stat, '2/3');
      expect(data.progress, closeTo(2 / 3, 1e-9));
      expect(data.subtitle, 'Read left');
      expect(data.rows[1].meta, '4 day streak');
      expect(data.rows[1].attention, isTrue);
    });

    test('approvals name the tool in plain words', () async {
      http.on('GET', '/api/v1/approvals', [
        {'id': 'a', 'toolName': 'CreateCalendarEvent'},
      ]);
      final data = (await load('approvals'))!;
      expect(data.stat, '1');
      expect(data.subtitle, 'Create calendar event');
      expect(data.attention, isTrue);
    });

    test('expenses show the month total and the change', () async {
      http.on('GET', '/api/v1/expenses', {
        'year': 2026,
        'month': 10,
        'currency': 'EUR',
        'total': 412.3,
        'count': 14,
        'previousTotal': 380.0,
        'categories': <Object>[],
        'days': <Object>[],
        'topMerchants': [
          {'merchant': 'Albert Heijn', 'total': 128.4, 'count': 6},
        ],
        'otherCurrencies': <Object>[],
        'expenses': [
          {
            'id': 'e1',
            'amount': 10.0,
            'currency': 'EUR',
            'category': 'groceries',
            'spentOn': '2026-10-01',
          },
        ],
      });
      final data = (await load('expenses'))!;
      expect(data.stat, '€412');
      expect(data.unit, 'this month');
      expect(data.subtitle, '+9% vs last month');
      expect(data.rows.single.text, 'Albert Heijn');
      expect(data.rows.single.meta, '€128.40');
      expect(http.sent('GET', '/api/v1/expenses').single.query['month'], '2026-10');
    });

    test('notifications count the unread ones', () async {
      http.on('GET', '/api/v1/notifications', [
        {'id': '1', 'title': 'Water the basil', 'readAt': null},
        {'id': '2', 'title': 'Old', 'readAt': '2026-10-01T00:00:00Z'},
      ]);
      final data = (await load('notifications'))!;
      expect(data.stat, '1');
      expect(data.subtitle, 'Water the basil');
      expect(data.attention, isTrue);
    });

    test('today lists what is coming up from the briefing', () async {
      http.on('GET', '/api/v1/home', {
        'calendar': {
          'connected': true,
          'events': [
            {'title': 'Design review', 'startAt': iso(DateTime(2026, 10, 3, 13, 30))},
            {'title': 'Yesterday', 'startAt': iso(DateTime(2026, 10, 2, 9))},
          ],
        },
        'reminders': [
          {'title': 'Call mum', 'dueAt': iso(DateTime(2026, 10, 3, 19, 30))},
        ],
      });
      final data = (await load('today'))!;
      expect(data.stat, '2');
      expect(data.subtitle, 'Design review · 13:30');
      expect(data.rows.map((r) => r.text), ['Design review', 'Call mum']);
    });

    test('devices read the battery from the briefing', () async {
      http.on('GET', '/api/v1/home', {
        'device': {'batteryPercent': 63, 'charging': true},
      });
      final data = (await load('devices'))!;
      expect(data.stat, '63%');
      expect(data.unit, 'charging');
    });

    test('a failing endpoint gives no data instead of an error', () async {
      expect(await load('tasks'), isNull);
      expect(await load('memory'), isNull);
    });

    test('tiles without a loader return null', () async {
      expect(await load('settings'), isNull);
      expect(await load('chats'), isNull);
    });

    test('the briefing is fetched once until it is invalidated', () async {
      http.on('GET', '/api/v1/home', {'device': {'batteryPercent': 1}});
      await load('today');
      await load('devices');
      expect(http.sent('GET', '/api/v1/home'), hasLength(1));
      source.invalidate();
      await load('today');
      expect(http.sent('GET', '/api/v1/home'), hasLength(2));
    });

    test('loaded data is cached for the next time Home opens', () async {
      http.on('GET', '/api/v1/skills', [
        {'id': '1'},
        {'id': '2'},
      ]);
      await load('skills');
      expect(source.cache['skills']?.stat, '2');
      source.reset();
      expect(source.cache, isEmpty);
    });
  });
}
