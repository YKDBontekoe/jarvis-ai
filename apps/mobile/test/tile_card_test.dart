import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/tiles/tile_card.dart';
import 'package:jarvis_mobile/features/tiles/tile_grid.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:jarvis_mobile/features/tiles/tile_visuals.dart';
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

const _bars = [
  TileBar('M', 10, 'Mon · €10'),
  TileBar('T', 40, 'Tue · €40'),
  TileBar('W', 0, 'Wed · €0'),
  TileBar('T', 25, 'Thu · €25'),
  TileBar('F', 5, 'Fri · €5'),
  TileBar('S', 60, 'Sat · €60'),
  TileBar('S', 30, 'Sun · €30'),
];

const _actions = [
  TileAction('done', 'Done', icon: Icons.check, primary: true),
  TileAction('snooze', '+10 min', icon: Icons.snooze),
];

/// Every kind of picture and button a tile can be given, with long text.
List<TileData> _rich() => [
  TileData(
    stat: '65%',
    unit: 'battery',
    subtitle: _long,
    visual: TileVisual.ring,
    progress: .65,
  ),
  TileData(
    stat: '2/3',
    unit: 'done',
    subtitle: _long,
    visual: TileVisual.ring,
    progress: 2 / 3,
    focusId: 'h',
    focusLabel: _long,
    actions: const [TileAction('check', 'Check in', primary: true)],
  ),
  const TileData(
    stat: '€412',
    unit: 'this month',
    subtitle: _long,
    visual: TileVisual.bars,
    bars: _bars,
  ),
  TileData(
    stat: '3',
    unit: 'things',
    subtitle: _long,
    focusLabel: _long,
    countdownTo: DateTime(2026, 10, 3, 19, 45),
    visual: TileVisual.timeline,
    timeline: const TileTimeline(
      startMinute: 960,
      endMinute: 1320,
      nowMinute: 1100,
      spans: [
        TileSpan(1000, 1045, 'Gym'),
        TileSpan(1185, 1230, 'Dinner with someone with a long name'),
        TileSpan(1260, 1270, 'Call', reminder: true),
      ],
    ),
    rows: [for (var i = 0; i < 6; i++) TileRow('$_long $i', meta: '19:45')],
  ),
  TileData(
    stat: '2',
    unit: 'today',
    attention: true,
    subtitle: _long,
    focusId: 'r',
    focusLabel: _long,
    countdownTo: DateTime(2026, 10, 3, 19, 45),
    actions: _actions,
    rows: [
      for (var i = 0; i < 6; i++)
        TileRow(
          '$_long $i',
          meta: 'Tomorrow 09:00',
          id: 'r$i',
          done: i == 1,
          actions: const [TileAction('done', 'Done', icon: Icons.check)],
        ),
    ],
  ),
  const TileData(
    subtitle: 'Jarvis is replying…',
    visual: TileVisual.waveform,
    rows: [TileRow('Lisbon trip', meta: '18:04')],
  ),
];

Widget _host(Widget child, {double scale = 1}) => MaterialApp(
  theme: buildJarvisTheme(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(textScaler: TextScaler.linear(scale)),
    child: child!,
  ),
  home: Scaffold(
    body: Align(alignment: Alignment.topLeft, child: child),
  ),
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
          for (final data in [null, _crowded(), TileData.empty, ..._rich()]) {
            await tester.pumpWidget(
              _host(
                SizedBox(
                  width: size.columns * cell + (size.columns - 1) * gap,
                  height: size.rows * cell + (size.rows - 1) * gap,
                  child: TileCard(
                    spec: spec,
                    size: size,
                    data: data,
                    now: DateTime(2026, 10, 3, 18, 16),
                    pending: const {'r2'},
                    onAction: (_, _) {},
                  ),
                ),
                scale: scale,
              ),
            );
            expect(
              tester.takeException(),
              isNull,
              reason: '${spec.id} as ${size.name} with ${data?.visual}',
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

  group('visuals', () {
    testWidgets('the ring eases to a new value', (tester) async {
      Widget ring(double value) =>
          _host(TileRing(progress: value, size: 60, child: const Text('x')));
      await tester.pumpWidget(ring(.2));
      await tester.pumpWidget(ring(.9));
      await tester.pump(const Duration(milliseconds: 60));
      expect(tester.hasRunningAnimations, isTrue);
      await tester.pumpAndSettle();
      expect(tester.hasRunningAnimations, isFalse);
    });

    testWidgets('bars read out on touch and let go on a second touch', (
      tester,
    ) async {
      await tester.pumpWidget(
        _host(
          const SizedBox(
            width: 140,
            height: 90,
            child: TileBarChart(bars: _bars, idleCaption: 'Last 7 days'),
          ),
        ),
      );
      String? caption() =>
          tester.widget<Text>(find.byKey(const Key('bars-caption'))).data;
      expect(caption(), 'Last 7 days');
      await tester.tap(find.byKey(const Key('bar-1')));
      await tester.pump();
      expect(caption(), 'Tue · €40');
      await tester.tap(find.byKey(const Key('bar-3')));
      await tester.pump();
      expect(caption(), 'Thu · €25');
      await tester.tap(find.byKey(const Key('bar-3')));
      await tester.pump();
      expect(caption(), 'Last 7 days');
    });

    testWidgets('bars are taller for bigger values and today is marked', (
      tester,
    ) async {
      await tester.pumpWidget(
        _host(
          const SizedBox(
            width: 140,
            height: 90,
            child: TileBarChart(bars: _bars),
          ),
        ),
      );
      double height(int i) => tester.getSize(find.byKey(Key('bar-$i'))).height;
      expect(height(5), greaterThan(height(1)));
      expect(height(1), greaterThan(height(4)));
      expect(height(2), 2, reason: 'a day with nothing is a hairline');
    });

    testWidgets('an all-zero chart does not divide by zero', (tester) async {
      await tester.pumpWidget(
        _host(
          const SizedBox(
            width: 140,
            height: 90,
            child: TileBarChart(bars: [TileBar('M', 0, 'Mon · €0')]),
          ),
        ),
      );
      expect(tester.takeException(), isNull);
    });

    testWidgets('the timeline names an item when it is touched', (
      tester,
    ) async {
      await tester.pumpWidget(
        _host(
          SizedBox(
            width: 260,
            height: 60,
            child: TileTimelineStrip(timeline: _rich()[3].timeline!),
          ),
        ),
      );
      String? caption() =>
          tester.widget<Text>(find.byKey(const Key('timeline-caption'))).data;
      expect(caption(), '');
      expect(find.byKey(const Key('timeline-now')), findsOneWidget);
      await tester.tap(find.byKey(const Key('span-1')));
      await tester.pump();
      expect(caption(), contains('Dinner with someone'));
      await tester.tap(find.byKey(const Key('span-1')));
      await tester.pump();
      expect(caption(), '');
    });

    testWidgets('an empty timeline says nothing is planned', (tester) async {
      await tester.pumpWidget(
        _host(
          const SizedBox(
            width: 260,
            height: 60,
            child: TileTimelineStrip(
              timeline: TileTimeline(
                startMinute: 600,
                endMinute: 1200,
                nowMinute: 700,
                spans: [],
              ),
            ),
          ),
        ),
      );
      expect(find.text('Nothing planned'), findsOneWidget);
    });

    testWidgets('the waveform moves, and holds still when motion is reduced', (
      tester,
    ) async {
      Widget wave({required bool reduced}) => MaterialApp(
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: reduced),
          child: child!,
        ),
        home: const Scaffold(
          body: TileWaveform(color: Colors.indigo, height: 40),
        ),
      );
      await tester.pumpWidget(wave(reduced: false));
      final first = tester
          .getSize(
            find
                .descendant(
                  of: find.byKey(const Key('tile-waveform')),
                  matching: find.byType(Container),
                )
                .first,
          )
          .height;
      await tester.pump(const Duration(milliseconds: 300));
      final later = tester
          .getSize(
            find
                .descendant(
                  of: find.byKey(const Key('tile-waveform')),
                  matching: find.byType(Container),
                )
                .first,
          )
          .height;
      expect(later, isNot(first));
      expect(tester.hasRunningAnimations, isTrue);

      await tester.pumpWidget(wave(reduced: true));
      await tester.pumpAndSettle();
      expect(tester.hasRunningAnimations, isFalse);
    });

    testWidgets('placeholders fit every size', (tester) async {
      for (final size in TileSize.values) {
        await tester.pumpWidget(
          _host(
            SizedBox(
              width: size.columns * 70.0,
              height: size.rows * 70.0,
              child: DecoratedBox(
                decoration: const BoxDecoration(),
                child: TileSkeleton(size: size),
              ),
            ),
          ),
        );
        expect(tester.takeException(), isNull, reason: size.name);
      }
    });
  });

  group('buttons', () {
    testWidgets('square buttons report the action and the item', (
      tester,
    ) async {
      final seen = <String>[];
      await tester.pumpWidget(
        _host(
          SizedBox(
            width: 150,
            height: 150,
            child: TileCard(
              spec: tileSpecFor('reminders')!,
              size: TileSize.square,
              data: _rich()[4],
              now: DateTime(2026, 10, 3, 18, 16),
              onAction: (action, id) => seen.add('${action.id}:$id'),
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('tile-action-done')));
      await tester.tap(find.byKey(const Key('tile-action-snooze')));
      expect(seen, ['done:r', 'snooze:r']);
    });

    testWidgets('a strip offers its main action as a round button', (
      tester,
    ) async {
      final seen = <String>[];
      await tester.pumpWidget(
        _host(
          SizedBox(
            width: 160,
            height: 70,
            child: TileCard(
              spec: tileSpecFor('reminders')!,
              size: TileSize.strip,
              data: _rich()[4],
              onAction: (action, id) => seen.add('${action.id}:$id'),
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('tile-action-done')));
      expect(seen, ['done:r']);
      expect(find.byKey(const Key('tile-action-snooze')), findsNothing);
    });

    testWidgets('a row is dimmed and cannot be pressed while it works', (
      tester,
    ) async {
      final seen = <String>[];
      await tester.pumpWidget(
        _host(
          SizedBox(
            width: 340,
            height: 300,
            child: TileCard(
              spec: tileSpecFor('reminders')!,
              size: TileSize.large,
              data: _rich()[4],
              pending: const {'r0'},
              onAction: (action, id) => seen.add('${action.id}:$id'),
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('row-action-r0-done')));
      await tester.tap(find.byKey(const Key('row-action-r2-done')));
      expect(seen, ['done:r2']);
    });

    testWidgets('without a handler the buttons are inert', (tester) async {
      await tester.pumpWidget(
        _host(
          SizedBox(
            width: 150,
            height: 150,
            child: TileCard(
              spec: tileSpecFor('reminders')!,
              size: TileSize.square,
              data: _rich()[4],
            ),
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('tile-action-done')));
      expect(tester.takeException(), isNull);
    });
  });

  group('countdown', () {
    testWidgets('reads the clock it is given', (tester) async {
      Future<void> at(DateTime now) => tester.pumpWidget(
        _host(
          SizedBox(
            width: 150,
            height: 150,
            child: TileCard(
              spec: tileSpecFor('today')!,
              size: TileSize.square,
              data: TileData(
                stat: '1',
                unit: 'thing',
                focusLabel: 'Dinner',
                countdownTo: DateTime(2026, 10, 3, 19, 45),
              ),
              now: now,
            ),
          ),
        ),
      );
      await at(DateTime(2026, 10, 3, 18, 16));
      expect(find.text('Dinner · in 1 h 29 min'), findsOneWidget);
      await at(DateTime(2026, 10, 3, 19, 30));
      expect(find.text('Dinner · in 15 min'), findsOneWidget);
      await at(DateTime(2026, 10, 3, 19, 50));
      expect(find.text('Dinner · now'), findsOneWidget);
    });
  });
}
