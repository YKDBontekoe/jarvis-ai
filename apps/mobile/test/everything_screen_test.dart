import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/everything/everything_screen.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/phosphor_icons.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;
  late TileLayoutController layout;
  late List<String> opened;

  setUp(() {
    SharedPreferences.setMockInitialValues({});
    http = FixtureHttp();
    layout = TileLayoutController();
    opened = [];
    http.on('GET', '/api/v1/skills', [
      {'id': '1'},
      {'id': '2'},
      {'id': '3'},
    ]);
  });

  Future<void> show(WidgetTester tester, {bool reducedMotion = false}) async {
    tester.view.physicalSize = const Size(400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(
            context,
          ).copyWith(disableAnimations: reducedMotion),
          child: child!,
        ),
        home: Scaffold(
          body: EverythingScreen(
            source: TileDataSource(http.client()),
            layout: layout,
            onOpen: opened.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> openPreview(WidgetTester tester, String id) async {
    final tile = find.byKey(Key('everything-$id'));
    await tester.ensureVisible(tile);
    await tester.pumpAndSettle();
    await tester.tap(tile);
    await tester.pumpAndSettle();
  }

  testWidgets('lists every feature by category', (tester) async {
    await show(tester);
    for (final category in TileCategory.values) {
      expect(find.text(category.label), findsWidgets);
    }
    expect(find.byKey(const Key('everything-today')), findsOneWidget);
    expect(find.byKey(const Key('everything-chats')), findsOneWidget);
    // Far below the first screen, so it is built lazily or off-screen.
    expect(
      find.byKey(const Key('everything-settings'), skipOffstage: false),
      findsOneWidget,
    );
    for (final spec in tileSpecs) {
      expect(
        find.byKey(Key('everything-${spec.id}'), skipOffstage: false),
        findsOneWidget,
        reason: spec.id,
      );
    }
  });

  testWidgets('search narrows by name and by category', (tester) async {
    await show(tester);
    await tester.enterText(find.byKey(const Key('everything-search')), 'mem');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('everything-memory')), findsOneWidget);
    expect(find.byKey(const Key('everything-tasks')), findsNothing);

    await tester.enterText(find.byKey(const Key('everything-search')), 'money');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('everything-expenses')), findsOneWidget);
    expect(find.byKey(const Key('everything-finance')), findsOneWidget);
    expect(find.byKey(const Key('everything-memory')), findsNothing);

    await tester.enterText(find.byKey(const Key('everything-search')), 'zzz');
    await tester.pumpAndSettle();
    expect(find.textContaining('Nothing matches'), findsOneWidget);
    await tester.tap(find.byKey(const Key('everything-clear-search')));
    await tester.pumpAndSettle();
    expect(find.text('On Home'), findsOneWidget);
    expect(find.byKey(const Key('everything-tasks')), findsOneWidget);
    expect(find.byKey(const Key('everything-clear-search')), findsNothing);
  });

  testWidgets('feature search also finds a feature by what it does', (
    tester,
  ) async {
    await show(tester);
    await tester.enterText(
      find.byKey(const Key('everything-search')),
      'hands-free',
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('everything-voice')), findsOneWidget);
    expect(
      find.text('Have a hands-free conversation with Jarvis.'),
      findsOneWidget,
    );
    expect(find.byKey(const Key('everything-tasks')), findsNothing);
    await openPreview(tester, 'voice');
    expect(
      find.text('Have a hands-free conversation with Jarvis.'),
      findsWidgets,
    );
    await tester.tap(find.byKey(const Key('tile-open')));
    await tester.pumpAndSettle();
    expect(opened, ['voice']);
  });

  testWidgets('pinned features carry a pin', (tester) async {
    await show(tester);
    expect(layout.contains('tasks'), isTrue);
    expect(
      find.descendant(
        of: find.byKey(const Key('everything-tasks')),
        matching: find.byWidgetPredicate(
          (w) => w is Icon && w.icon == PhosphorIconsRegular.pushPin,
        ),
      ),
      findsOneWidget,
    );
    expect(
      find.descendant(
        of: find.byKey(const Key('everything-reminders')),
        matching: find.byWidgetPredicate(
          (w) => w is Icon && w.icon == PhosphorIconsRegular.pushPin,
        ),
      ),
      findsNothing,
    );
  });

  testWidgets('On Home shortcuts follow pins and disappear during search', (
    tester,
  ) async {
    await show(tester);
    expect(find.text('On Home'), findsOneWidget);
    expect(find.byKey(const Key('favorite-tasks')), findsOneWidget);
    expect(find.byKey(const Key('favorite-reminders')), findsNothing);
    layout.unpin('tasks');
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('favorite-tasks')), findsNothing);
    expect(find.byKey(const Key('everything-tasks')), findsOneWidget);
    await tester.enterText(
      find.byKey(const Key('everything-search')),
      'weekly',
    );
    await tester.pumpAndSettle();
    expect(find.text('On Home'), findsNothing);
    expect(find.text('Weekly review'), findsOneWidget);
  });

  testWidgets('previewing shows live data and pins at the chosen size', (
    tester,
  ) async {
    await show(tester);
    expect(layout.contains('skills'), isFalse);
    await openPreview(tester, 'skills');
    expect(find.byKey(const Key('tile-preview')), findsOneWidget);
    expect(find.text('3 skills'), findsOneWidget);
    expect(find.byKey(const Key('size-icon')), findsOneWidget);
    expect(find.byKey(const Key('size-strip')), findsOneWidget);
    expect(
      find.byKey(const Key('size-square')),
      findsNothing,
      reason: 'Skills only offers icon and strip',
    );
    expect(find.text('Add to Home'), findsOneWidget);

    await tester.tap(find.byKey(const Key('size-icon')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tile-pin')));
    await tester.pumpAndSettle();

    expect(layout.sizeOf('skills'), TileSize.icon);
    expect(find.text('Skills added to Home'), findsOneWidget);
    expect(find.byKey(const Key('tile-preview')), findsNothing);
  });

  testWidgets('a pinned tile can change size or leave Home', (tester) async {
    await show(tester);
    final tasks = layout.sizeOf('tasks')!;
    await openPreview(tester, 'tasks');
    expect(find.text('Remove from Home'), findsOneWidget);

    final other = tileSpecFor('tasks')!.sizes.firstWhere((s) => s != tasks);
    await tester.tap(find.byKey(Key('size-${other.name}')));
    await tester.pumpAndSettle();
    expect(find.text('Update size'), findsOneWidget);
    await tester.tap(find.byKey(const Key('tile-pin')));
    await tester.pumpAndSettle();
    expect(layout.sizeOf('tasks'), other);
    expect(find.text('Tasks size updated'), findsOneWidget);

    await openPreview(tester, 'tasks');
    await tester.tap(find.byKey(const Key('tile-pin')));
    await tester.pumpAndSettle();
    expect(layout.contains('tasks'), isFalse);
    expect(find.text('Tasks removed from Home'), findsOneWidget);
  });

  testWidgets('tile preview resizes immediately with Reduce Motion', (
    tester,
  ) async {
    await show(tester, reducedMotion: true);
    await openPreview(tester, 'tasks');
    final before = tester.getSize(find.byKey(const Key('tile-preview')));
    await tester.tap(find.byKey(const Key('size-icon')));
    await tester.pumpAndSettle();
    final after = tester.getSize(find.byKey(const Key('tile-preview')));
    expect(after.width, lessThan(before.width));
    expect(tester.hasRunningAnimations, isFalse);
    expect(tester.takeException(), isNull);
  });

  testWidgets('Open from the preview goes to the feature', (tester) async {
    await show(tester);
    await openPreview(tester, 'memory');
    await tester.tap(find.byKey(const Key('tile-open')));
    await tester.pumpAndSettle();
    expect(opened, ['memory']);
    expect(find.byKey(const Key('tile-preview')), findsNothing);
  });

  testWidgets('a failing feature still previews with its description', (
    tester,
  ) async {
    await show(tester);
    await openPreview(tester, 'library');
    expect(find.text('Things you saved'), findsOneWidget);
  });
}
