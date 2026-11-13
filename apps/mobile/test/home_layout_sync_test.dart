import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/tiles/home_layout_sync.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

const _path = '/api/v1/home/layout';

Map<String, Object?> _remote({
  required List<List<String>> tiles,
  int revision = 1,
  String changedBy = 'app',
  List<String> hidden = const [],
  String? note,
  bool stored = true,
  bool canUndo = false,
}) => {
  'tiles': [
    for (final t in tiles) {'id': t[0], 'size': t[1]},
  ],
  'hiddenCards': hidden,
  'revision': revision,
  'changedBy': changedBy,
  'note': note,
  'canUndo': canUndo,
  'stored': stored,
};

Future<void> _settle() =>
    Future<void>.delayed(const Duration(milliseconds: 30));

void main() {
  group('server catalog', () {
    // The server tells Jarvis which tiles exist from this file.
    final file = File('../../src/Jarvis.Application/Home/home-tiles.json');

    test('lists every tile the app offers, with the same names and sizes', () {
      final server = (jsonDecode(file.readAsStringSync()) as List)
          .cast<Map<String, dynamic>>();
      expect(
        [for (final s in server) s['id']],
        [for (final s in tileSpecs) s.id],
        reason: 'Update home-tiles.json when tile_registry.dart changes.',
      );
      for (final (index, spec) in tileSpecs.indexed) {
        expect(server[index]['name'], spec.name, reason: spec.id);
        expect(server[index]['description'], spec.description, reason: spec.id);
        expect(server[index]['category'], spec.category.name, reason: spec.id);
        expect(server[index]['sizes'], [
          for (final size in spec.sizes) size.code,
        ], reason: spec.id);
      }
    });

    test('the default layout and merged ids match the server', () {
      final service = File('../../src/Jarvis.Application/Home/HomeLayout.cs')
          .readAsStringSync();
      final defaults = RegExp(r'new\("([a-z-]+)", HomeTileSizes\.(\w+)\)')
          .allMatches(service)
          .map((m) => '${m[1]}:${m[2]!.toLowerCase()}');
      expect(defaults, [
        for (final item in defaultTileLayout) '${item.id}:${item.size.code}',
      ]);
      for (final MapEntry(:key, :value) in mergedTileIds.entries) {
        expect(service, contains('["$key"] = "$value"'));
      }
    });
  });

  group('account sync', () {
    late FixtureHttp http;
    late TileLayoutController controller;

    setUp(() async {
      SharedPreferences.setMockInitialValues({});
      http = FixtureHttp();
      controller = TileLayoutController.device(http: http.client());
      await controller.open();
    });

    test('an account without a layout gets the one this device has', () async {
      controller.unpin('voice');
      await _settle();
      http.requests.clear();
      http.on('GET', _path, _remote(tiles: const [], stored: false));
      http.on(
        'PUT',
        _path,
        _remote(
          tiles: const [
            ['today', 'wide'],
          ],
          revision: 1,
        ),
      );

      await controller.sync();

      final put = http.sent('PUT', _path).single.body! as Map;
      expect([
        for (final t in put['tiles'] as List) t['id'],
      ], controller.layout.map((p) => p.id));
      expect(put['tiles'], isNot(contains(containsPair('id', 'voice'))));
      expect(controller.jarvisChange, isNull);
    });

    test('a new device takes the account layout quietly, then announces what '
        'Jarvis changes', () async {
      http.on(
        'GET',
        _path,
        _remote(
          tiles: const [
            ['tasks', 'wide'],
          ],
          revision: 3,
        ),
      );
      await controller.sync();
      expect(controller.layout.map((p) => p.id), ['tasks']);
      expect(controller.jarvisChange, isNull);

      http.on(
        'GET',
        _path,
        _remote(
          tiles: const [
            ['habits', 'wide'],
            ['tasks', 'square'],
          ],
          hidden: const ['brief', 'unknown-card'],
          revision: 4,
          changedBy: 'jarvis',
          note: 'Habits first',
          canUndo: true,
        ),
      );
      await controller.sync();

      expect(controller.layout.map((p) => p.id), ['habits', 'tasks']);
      expect(controller.sizeOf('habits'), TileSize.wide);
      expect(controller.hiddenCards, {'brief'});
      expect(controller.showsCard(HomeCardIds.brief), isFalse);
      expect(controller.jarvisChange?.note, 'Habits first');

      // A reopened app remembers the layout and the cards.
      final again = TileLayoutController.device();
      await again.open();
      expect(again.layout.map((p) => p.id), ['habits', 'tasks']);
      expect(again.hiddenCards, {'brief'});

      controller.keepJarvisChange();
      expect(controller.jarvisChange, isNull);
    });

    test('undo puts back the earlier layout from the server', () async {
      http.on(
        'GET',
        _path,
        _remote(
          tiles: const [
            ['tasks', 'wide'],
          ],
        ),
      );
      await controller.sync();
      http.on(
        'GET',
        _path,
        _remote(
          tiles: const [
            ['finance', 'wide'],
          ],
          revision: 2,
          changedBy: 'jarvis',
          canUndo: true,
        ),
      );
      await controller.sync();
      http.on(
        'POST',
        '$_path/undo',
        _remote(
          tiles: const [
            ['tasks', 'wide'],
          ],
          revision: 3,
        ),
      );

      expect(await controller.undoJarvisChange(), isTrue);

      expect(controller.layout.map((p) => p.id), ['tasks']);
      expect(controller.jarvisChange, isNull);
    });

    test('nothing syncs while Home is being edited, and Done sends tiles and '
        'cards together', () async {
      http.on(
        'GET',
        _path,
        _remote(
          tiles: const [
            ['tasks', 'wide'],
          ],
        ),
      );
      await controller.sync();
      http.requests.clear();

      controller.beginEdit();
      controller.setCardVisible(HomeCardIds.evening, false);
      controller.pin('habits', TileSize.strip);
      await controller.sync();
      expect(http.requests, isEmpty);

      controller.undo();
      expect(controller.contains('habits'), isFalse);
      expect(controller.showsCard(HomeCardIds.evening), isFalse);

      http.on(
        'PUT',
        _path,
        _remote(
          tiles: const [
            ['tasks', 'wide'],
          ],
          revision: 2,
        ),
      );
      controller.commitEdit();
      await _settle();
      final put = http.sent('PUT', _path).single.body! as Map;
      expect(put['hiddenCards'], ['evening']);
    });

    test('Cancel brings hidden cards back too', () {
      controller.beginEdit();
      controller.setCardVisible(HomeCardIds.learning, false);
      controller.cancelEdit();
      expect(controller.showsCard(HomeCardIds.learning), isTrue);
    });

    test('a change that could not be sent is sent on the next sync', () async {
      http.on('PUT', _path, {'error': 'down'}, status: 500);
      controller.unpin('voice');
      await _settle();
      http.requests.clear();
      http.on(
        'PUT',
        _path,
        _remote(
          tiles: const [
            ['today', 'wide'],
          ],
          revision: 5,
        ),
      );
      http.on(
        'GET',
        _path,
        _remote(
          tiles: const [
            ['finance', 'wide'],
          ],
          revision: 4,
        ),
      );

      await controller.sync();

      expect(http.sent('GET', _path), isEmpty);
      expect(http.sent('PUT', _path), hasLength(1));
      expect(controller.contains('voice'), isFalse);
    });

    test(
      'signing out forgets the layout without touching the account',
      () async {
        http.on(
          'GET',
          _path,
          _remote(
            tiles: const [
              ['tasks', 'wide'],
            ],
            hidden: const ['brief'],
          ),
        );
        await controller.sync();
        http.requests.clear();

        controller.forgetAccount();
        await _settle();

        expect(http.requests, isEmpty);
        expect(
          controller.layout.map((p) => p.id),
          defaultTileLayout.map((p) => p.id),
        );
        expect(controller.hiddenCards, isEmpty);
        final fresh = TileLayoutController.device();
        await fresh.open();
        expect(fresh.hiddenCards, isEmpty);
      },
    );

    test('without http the layout stays on the device', () async {
      final local = TileLayoutController();
      local.unpin('voice');
      await local.sync();
      expect(local.contains('voice'), isFalse);
    });
  });
}
