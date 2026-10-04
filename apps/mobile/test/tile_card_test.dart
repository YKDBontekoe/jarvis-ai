import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/tiles/tile_card.dart';
import 'package:jarvis_mobile/features/tiles/tile_grid.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:jarvis_mobile/theme.dart';

const _long =
    'A very long line of text that would never fit on one line of a tile';

TileData _crowded() => TileData(
  stat: '€12,345',
  unit: 'this month and then some more words',
  subtitle: _long,
  attention: true,
  progress: .6,
  rows: [
    for (var i = 0; i < 10; i++)
      TileRow('$_long $i', meta: 'Tomorrow 09:00', attention: i.isEven),
  ],
);

Widget _host(Widget child, {double scale = 1}) => MaterialApp(
  theme: buildJarvisTheme(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: Scaffold(body: Align(alignment: Alignment.topLeft, child: child)),
);

void main() {
  for (final scale in [1.0, 2.0]) {
    testWidgets('every tile fits every size on a 320 wide phone (text '
        '${(scale * 100).round()}%)', (tester) async {
      tester.view.physicalSize = const Size(320, 700);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);

      const gap = TileGrid.gap;
      final cell = (320 - 32 - gap * (TileGrid.columns - 1)) / TileGrid.columns;
      for (final spec in tileSpecs) {
        for (final size in spec.sizes) {
          for (final data in [null, _crowded(), TileData.empty]) {
            await tester.pumpWidget(
              _host(
                SizedBox(
                  width: size.columns * cell + (size.columns - 1) * gap,
                  height: size.rows * cell + (size.rows - 1) * gap,
                  child: TileCard(spec: spec, size: size, data: data),
                ),
                scale: scale,
              ),
            );
            expect(
              tester.takeException(),
              isNull,
              reason: '${spec.id} as ${size.name}',
            );
          }
        }
      }
    });
  }

  testWidgets('an attention dot shows on small tiles only when needed', (
    tester,
  ) async {
    Future<void> show(TileData data, TileSize size) => tester.pumpWidget(
      _host(
        SizedBox(
          width: 90,
          height: 90,
          child: TileCard(spec: tileSpecFor('tasks')!, size: size, data: data),
        ),
      ),
    );
    await show(const TileData(attention: true), TileSize.icon);
    expect(find.byKey(const Key('tile-attention')), findsOneWidget);
    await show(TileData.empty, TileSize.icon);
    expect(find.byKey(const Key('tile-attention')), findsNothing);
  });

  testWidgets('a wide tile shows only the rows that fit', (tester) async {
    await tester.pumpWidget(
      _host(
        SizedBox(
          width: 340,
          height: 150,
          child: TileCard(
            spec: tileSpecFor('tasks')!,
            size: TileSize.wide,
            data: _crowded(),
          ),
        ),
      ),
    );
    final shown = find.textContaining(_long).evaluate().length;
    expect(shown, inInclusiveRange(2, 5));
  });

  testWidgets('rows with a target report taps, others do not', (tester) async {
    final tapped = <String>[];
    await tester.pumpWidget(
      _host(
        SizedBox(
          width: 340,
          height: 200,
          child: TileCard(
            spec: tileSpecFor('chats')!,
            size: TileSize.wide,
            data: const TileData(
              rows: [
                TileRow('With target', target: 'chat:1'),
                TileRow('Plain'),
              ],
            ),
            onRowTap: (row) => tapped.add(row.text),
          ),
        ),
      ),
    );
    await tester.tap(find.text('With target'));
    await tester.tap(find.text('Plain'));
    expect(tapped, ['With target']);
  });

  testWidgets('the grid keeps its height in step with its rows', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        SizedBox(
          width: 340,
          child: TileGrid(
            layout: const [
              TilePlacement('tasks', TileSize.square),
              TilePlacement('memory', TileSize.icon),
            ],
            data: const {},
            onOpen: (_) {},
          ),
        ),
      ),
    );
    final cell = (340 - TileGrid.gap * 3) / 4;
    expect(
      tester.getSize(find.byType(TileGrid)).height,
      closeTo(2 * cell + TileGrid.gap, .01),
    );
  });
}
