import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/utility_pages.dart';
import 'package:jarvis_mobile/features/tiles/tile_actions.dart';
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
      expect(
        [for (final p in placed) (p.column, p.row)],
        [(0, 0), (1, 0), (2, 0), (3, 0)],
      );
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

    test(
      'the controller saves changes and a new one reads them back',
      () async {
        SharedPreferences.setMockInitialValues({});
        final first = TileLayoutController();
        await first.open();
        expect(
          first.layout.map((p) => p.id),
          defaultTileLayout.map((p) => p.id),
        );

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
      },
    );

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
      expect(
        http.sent('GET', '/api/v1/expenses').single.query['month'],
        '2026-10',
      );
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
            {
              'title': 'Design review',
              'startAt': iso(DateTime(2026, 10, 3, 13, 30)),
            },
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
      http.on('GET', '/api/v1/home', {
        'device': {'batteryPercent': 1},
      });
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

  group('live loaders', () {
    late FixtureHttp http;
    late TileDataSource source;
    final now = DateTime(2026, 10, 3, 18, 16);
    String iso(DateTime time) => time.toUtc().toIso8601String();
    Future<TileData?> load(String id) => source.load(tileSpecFor(id)!);

    setUp(() {
      http = FixtureHttp();
      source = TileDataSource(http.client(), clock: () => now);
    });

    test(
      'a reminder tile names the next one, counts down and can act',
      () async {
        http.on('GET', '/api/v1/reminders', [
          {
            'id': 'r1',
            'title': 'Call mum',
            'status': 'pending',
            'dueAt': iso(DateTime(2026, 10, 3, 19, 30)),
          },
        ]);
        final data = (await load('reminders'))!;
        expect(data.focusId, 'r1');
        expect(data.focusLabel, 'Call mum');
        expect(data.countdownTo, DateTime(2026, 10, 3, 19, 30));
        expect(data.actions.map((a) => a.id), ['done', 'snooze']);
        expect(data.actions.first.primary, isTrue);
        expect(data.rows.single.id, 'r1');
        expect(data.rows.single.actions.single.id, 'done');
      },
    );

    test(
      'habits show a ring and offer to check in the next open one',
      () async {
        http.on('GET', '/api/v1/habits', {
          'habits': [
            {
              'id': 'h1',
              'name': 'Run',
              'stats': {'doneToday': true, 'currentStreak': 3},
            },
            {
              'id': 'h2',
              'name': 'Read',
              'stats': {'doneToday': false, 'currentStreak': 1},
            },
          ],
        });
        final data = (await load('habits'))!;
        expect(data.visual, TileVisual.ring);
        expect(data.progress, .5);
        expect(data.focusId, 'h2');
        expect(data.actions.single.id, 'check');
        expect(data.rows.first.done, isTrue);
        expect(data.rows.first.actions.single.id, 'uncheck');
        expect(data.rows.last.actions.single.id, 'check');
      },
    );

    test('with every habit done there is nothing to check in', () async {
      http.on('GET', '/api/v1/habits', {
        'habits': [
          {
            'id': 'h1',
            'name': 'Run',
            'stats': {'doneToday': true},
          },
        ],
      });
      final data = (await load('habits'))!;
      expect(data.focusId, isNull);
      expect(data.actions, isEmpty);
      expect(data.subtitle, 'All done today');
    });

    test('approvals can be declined or reviewed but not approved', () async {
      http.on('GET', '/api/v1/approvals', [
        {'id': 'a1', 'toolName': 'CreateCalendarEvent'},
      ]);
      final data = (await load('approvals'))!;
      expect(data.actions.map((a) => a.id), ['deny', 'review']);
      expect(
        [for (final row in data.rows) ...row.actions.map((a) => a.id)],
        ['deny'],
      );
      expect(data.actions.any((a) => a.id == 'approve'), isFalse);
    });

    test('today lays the day out on a timeline around now', () async {
      http.on('GET', '/api/v1/home', {
        'calendar': {
          'events': [
            {'title': 'Dinner', 'startAt': iso(DateTime(2026, 10, 3, 19, 45))},
            {'title': 'Tomorrow', 'startAt': iso(DateTime(2026, 10, 4, 9))},
          ],
        },
        'reminders': [
          {'title': 'Call mum', 'dueAt': iso(DateTime(2026, 10, 3, 21))},
        ],
      });
      final data = (await load('today'))!;
      final line = data.timeline!;
      expect(data.visual, TileVisual.timeline);
      expect(line.nowMinute, 18 * 60 + 16);
      expect(line.spans.map((s) => s.label), [
        'Dinner',
        'Call mum',
      ], reason: 'only what is on today');
      expect(line.spans.last.reminder, isTrue);
      expect(line.startMinute, lessThanOrEqualTo(line.nowMinute));
      expect(line.endMinute, greaterThan(21 * 60 + 10));
      expect(line.endMinute - line.startMinute, greaterThanOrEqualTo(360));
      expect(line.startMinute % 60, 0);
      expect(data.focusLabel, 'Dinner');
      expect(data.countdownTo, DateTime(2026, 10, 3, 19, 45));
    });

    test('a quiet day still draws an empty timeline', () async {
      http.on('GET', '/api/v1/home', {
        'calendar': {'events': <Object>[]},
      });
      final line = (await load('today'))!.timeline!;
      expect(line.spans, isEmpty);
      expect(line.endMinute, lessThanOrEqualTo(1440));
    });

    test('expenses give seven bars ending today', () async {
      http.on('GET', '/api/v1/expenses', {
        'year': 2026,
        'month': 10,
        'currency': 'EUR',
        'total': 100.0,
        'count': 2,
        'previousTotal': 0.0,
        'categories': <Object>[],
        'days': [
          {'date': '2026-10-02', 'total': 40.0},
          {'date': '2026-10-03', 'total': 60.0},
        ],
        'topMerchants': <Object>[],
        'otherCurrencies': <Object>[],
        'expenses': [
          {
            'id': 'e',
            'amount': 1.0,
            'currency': 'EUR',
            'category': 'other',
            'spentOn': '2026-10-03',
          },
        ],
      });
      final data = (await load('expenses'))!;
      expect(data.visual, TileVisual.bars);
      expect(data.bars, hasLength(7));
      expect(data.bars.last.value, 60);
      expect(data.bars.last.text, 'Sat · €60');
      expect(data.bars[5].text, 'Fri · €40');
      expect(data.bars.first.value, 0);
    });

    test('expenses without any daily spending draw no chart', () async {
      http.on('GET', '/api/v1/expenses', {
        'year': 2026,
        'month': 10,
        'currency': 'EUR',
        'total': 0.0,
        'count': 0,
        'previousTotal': 0.0,
        'categories': <Object>[],
        'days': <Object>[],
        'topMerchants': <Object>[],
        'otherCurrencies': <Object>[],
        'expenses': <Object>[],
      });
      expect((await load('expenses'))!.visual, TileVisual.none);
    });

    test('devices draw the battery as a ring', () async {
      http.on('GET', '/api/v1/home', {
        'device': {'batteryPercent': 63, 'charging': true, 'hasLocation': true},
      });
      final data = (await load('devices'))!;
      expect(data.visual, TileVisual.ring);
      expect(data.progress, closeTo(.63, 1e-9));
      expect(data.subtitle, 'Charging · Location available');
    });
  });

  group('tile actions', () {
    late FixtureHttp http;
    setUp(() => http = FixtureHttp());

    Future<TileActionOutcome> run(
      String tile,
      String action, [
      String? id = 'x1',
    ]) => runTileAction(http.client(), tile, action, id);

    test('each action calls the right endpoint', () async {
      for (final path in [
        '/api/v1/reminders/x1/complete',
        '/api/v1/reminders/x1/snooze',
        '/api/v1/habits/x1/check-ins',
        '/api/v1/approvals/x1/decision',
      ]) {
        http.on('POST', path, {});
      }
      expect((await run('reminders', 'done')).message, 'Marked done');
      expect(
        (await run('reminders', 'snooze')).message,
        'Snoozed for 10 minutes',
      );
      expect((await run('habits', 'check')).message, 'Checked in');
      expect((await run('habits', 'uncheck')).message, 'Check-in removed');
      expect((await run('approvals', 'deny')).message, 'Declined');
      expect(http.sent('POST', '/api/v1/reminders/x1/snooze').single.body, {
        'minutes': 10,
      });
      expect(
        http.sent('POST', '/api/v1/habits/x1/check-ins').map((r) => r.body),
        [
          {'done': true},
          {'done': false},
        ],
      );
      expect(http.sent('POST', '/api/v1/approvals/x1/decision').single.body, {
        'approved': false,
      });
    });

    test('approving is not something a tile can do', () async {
      http.on('POST', '/api/v1/approvals/x1/decision', {});
      final outcome = await run('approvals', 'approve');
      expect(outcome.ok, isFalse);
      expect(http.requests, isEmpty);
    });

    test('ids are escaped and a missing id is refused', () async {
      http.on('POST', '/api/v1/reminders/a%2Fb/complete', {});
      expect((await run('reminders', 'done', 'a/b')).ok, isTrue);
      expect((await run('reminders', 'done', null)).ok, isFalse);
      expect((await run('reminders', 'done', '')).ok, isFalse);
    });

    test('a failure and a conflict each say what happened', () async {
      http.on('POST', '/api/v1/habits/x1/check-ins', {}, status: 500);
      final failed = await run('habits', 'check');
      expect(failed.ok, isFalse);
      expect(failed.message, 'Could not do that. Try again.');

      http.on('POST', '/api/v1/approvals/x1/decision', {}, status: 409);
      final decided = await run('approvals', 'deny');
      expect(decided.ok, isFalse);
      expect(decided.message, 'That was already decided.');
    });

    test('the data source runs actions through the same code', () async {
      http.on('POST', '/api/v1/reminders/x1/complete', {});
      final outcome = await TileDataSource(
        http.client(),
      ).act('reminders', 'done', 'x1');
      expect(outcome.ok, isTrue);
    });
  });

  group('optimistic updates', () {
    TileData reminders() => const TileData(
      stat: '2',
      unit: 'today',
      attention: true,
      focusId: 'r1',
      focusLabel: 'Call mum',
      rows: [
        TileRow('Call mum', id: 'r1', attention: true),
        TileRow('Water plants', id: 'r2'),
      ],
    );

    test('finishing a reminder removes it and moves on to the next', () {
      final next = applyTileAction('reminders', reminders(), 'done', 'r1');
      expect(next.rows.map((r) => r.id), ['r2']);
      expect(next.stat, '1');
      expect(next.focusId, 'r2');
      expect(next.focusLabel, 'Water plants');
    });

    test('the last reminder leaves nothing to act on', () {
      var data = reminders();
      data = applyTileAction('reminders', data, 'snooze', 'r1');
      data = applyTileAction('reminders', data, 'done', 'r2');
      expect(data.rows, isEmpty);
      expect(data.stat, '0');
      expect(data.focusId, isNull);
      expect(data.actions, isEmpty);
      expect(data.subtitle, 'All clear');
      expect(
        applyTileAction('reminders', data, 'done', 'r9').stat,
        '0',
        reason: 'the count never goes below zero',
      );
    });

    test('checking a habit updates the count, ring and next habit', () {
      const data = TileData(
        stat: '1/3',
        progress: 1 / 3,
        visual: TileVisual.ring,
        rows: [
          TileRow('Run', id: 'h1', done: true),
          TileRow('Read', id: 'h2'),
          TileRow('Water', id: 'h3'),
        ],
        focusId: 'h2',
      );
      final checked = applyTileAction('habits', data, 'check', 'h2');
      expect(checked.stat, '2/3');
      expect(checked.progress, closeTo(2 / 3, 1e-9));
      expect(checked.rows[1].done, isTrue);
      expect(checked.rows[1].actions.single.id, 'uncheck');
      expect(checked.focusId, 'h3');
      expect(checked.subtitle, 'Water left');

      final all = applyTileAction('habits', checked, 'check', 'h3');
      expect(all.stat, '3/3');
      expect(all.focusId, isNull);
      expect(all.subtitle, 'All done today');

      final undone = applyTileAction('habits', all, 'uncheck', 'h1');
      expect(undone.stat, '2/3');
      expect(undone.focusId, 'h1');
    });

    test('unknown actions and items change nothing', () {
      final data = reminders();
      expect(applyTileAction('reminders', data, 'nope', 'r1'), same(data));
      expect(applyTileAction('reminders', data, 'done', null), same(data));
      expect(applyTileAction('files', data, 'done', 'r1'), same(data));
    });
  });
}
