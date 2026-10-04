import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/home/jarvis_home.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

String _iso(DateTime time) => time.toUtc().toIso8601String();

void main() {
  final now = DateTime(2026, 10, 3, 18, 16);
  late FixtureHttp http;
  late TileDataSource source;
  late TileLayoutController layout;
  late ChatList chats;
  late List<String> opened;
  late List<String> openedChats;
  var addTile = 0;
  var settings = 0;
  final suggestions = <String>[];

  setUp(() {
    SharedPreferences.setMockInitialValues({});
    http = FixtureHttp();
    source = TileDataSource(http.client(), clock: () => now);
    layout = TileLayoutController();
    chats = ChatList();
    opened = [];
    openedChats = [];
    addTile = 0;
    settings = 0;
    suggestions.clear();
    http.on('GET', '/api/v1/home', {
      'calendar': {
        'connected': true,
        'events': [
          {'title': 'Dinner with Sanne', 'startAt': _iso(DateTime(2026, 10, 3, 19, 45)), 'location': 'Café Loetje'},
        ],
      },
      'reminders': [
        {'title': 'Call mum', 'dueAt': _iso(DateTime(2026, 10, 3, 21))},
      ],
      'approvals': <Object>[],
      'packs': <Object>[],
    });
    http.on('GET', '/api/v1/tasks', [
      {'id': 't', 'title': 'Compare flights', 'status': 'running'},
    ]);
    http.on('GET', '/api/v1/memory', [
      {'id': 'm', 'content': 'Prefers window seats'},
    ]);
    chats.setConversations([
      {'id': 'c1', 'title': 'Lisbon trip', 'updatedAt': _iso(now)},
    ]);
  });

  Future<void> show(
    WidgetTester tester, {
    bool ready = true,
    List<TilePlacement>? tiles,
  }) async {
    tester.view.physicalSize = const Size(400, 1200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    if (tiles != null) {
      for (final id in [for (final p in layout.layout) p.id]) {
        layout.unpin(id);
      }
      for (final tile in tiles) {
        layout.pin(tile.id, tile.size);
      }
    }
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: JarvisHome(
            source: source,
            layout: layout,
            chats: chats,
            ready: ready,
            refreshRevision: 0,
            clock: () => now,
            onOpen: opened.add,
            onOpenChat: openedChats.add,
            onAddTile: () => addTile++,
            onSettings: () => settings++,
            onSuggestion: suggestions.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    // Setup cards mount once the briefing arrives; let their requests finish.
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pumpAndSettle();
  }

  testWidgets('the clock shows the next thing on the day', (tester) async {
    await show(tester);
    expect(find.text('Saturday 3 October'), findsOneWidget);
    expect(tester.widget<Text>(find.byKey(const Key('home-clock-time'))).data, '19:45');
    expect(find.text('in 1 h 29 min'), findsOneWidget);
    expect(tester.widget<Text>(find.byKey(const Key('home-clock-title'))).data, 'Dinner with Sanne');
    expect(find.text('Café Loetje'), findsOneWidget);
  });

  testWidgets('with nothing coming up it shows the time and says so', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home', {
      'calendar': {'connected': true, 'events': <Object>[]},
      'reminders': <Object>[],
    });
    await show(tester);
    expect(tester.widget<Text>(find.byKey(const Key('home-clock-time'))).data, '18:16');
    expect(find.text('Nothing else today'), findsOneWidget);
  });

  testWidgets('without a calendar the empty clock offers to connect one', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home', {
      'calendar': {'connected': false, 'events': <Object>[]},
      'reminders': <Object>[],
    });
    await show(tester);
    await tester.tap(find.text('Connect a calendar'));
    expect(suggestions.single, contains('calendar'));
  });

  testWidgets('tiles show live data and open their page', (tester) async {
    await show(
      tester,
      tiles: const [
        TilePlacement('tasks', TileSize.square),
        TilePlacement('memory', TileSize.strip),
      ],
    );
    expect(find.text('Compare flights'), findsOneWidget);
    expect(find.text('1 active'), findsOneWidget);
    expect(find.text('1 memory'), findsOneWidget);
    expect(find.textContaining('Prefers window seats'), findsOneWidget);
    await tester.tap(find.text('Compare flights'));
    await tester.tap(find.textContaining('Prefers window seats'));
    expect(opened, ['tasks', 'memory']);
  });

  testWidgets('a tile with no data shows what it is for', (tester) async {
    await show(tester, tiles: const [TilePlacement('skills', TileSize.strip)]);
    expect(find.text('What Jarvis can do'), findsOneWidget);
  });

  testWidgets('the Chats tile reads the shared list and opens a chat', (
    tester,
  ) async {
    await show(tester, tiles: const [TilePlacement('chats', TileSize.wide)]);
    expect(find.text('Lisbon trip'), findsOneWidget);
    await tester.tap(find.text('Lisbon trip'));
    expect(openedChats, ['jarvis:c1']);
    await tester.tap(find.text('Chats'));
    expect(opened, ['chats']);

    chats.setConversations([
      {'id': 'c9', 'title': 'Energy contracts', 'updatedAt': _iso(now)},
    ]);
    await tester.pump();
    expect(find.text('Energy contracts'), findsOneWidget);
  });

  testWidgets('offline Home makes no requests', (tester) async {
    await show(tester, ready: false);
    expect(http.requests, isEmpty);
    expect(find.byKey(const Key('home-clock')), findsOneWidget);
  });

  testWidgets('pending approvals stay in view and open Approvals', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home', {
      'approvals': [
        {'id': 'a', 'toolName': 'CreateCalendarEvent'},
      ],
    });
    await show(tester);
    expect(find.text('1 approval waiting'), findsOneWidget);
    expect(find.text('Create calendar event'), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-approvals')));
    expect(opened, ['approvals']);
  });

  testWidgets('the header opens settings and edit mode', (tester) async {
    await show(tester);
    await tester.tap(find.byKey(const Key('home-settings')));
    expect(settings, 1);
    await tester.tap(find.byKey(const Key('home-edit')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-edit-done')), findsOneWidget);
    expect(find.byKey(const Key('home-edit-hint')), findsOneWidget);
    expect(find.byKey(const Key('home-approvals')), findsNothing);
    await tester.tap(find.byKey(const Key('home-edit-done')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-edit')), findsOneWidget);
  });

  testWidgets('long press starts editing', (tester) async {
    await show(tester, tiles: const [TilePlacement('tasks', TileSize.square)]);
    await tester.longPress(find.text('Tasks'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-edit-done')), findsOneWidget);
  });

  testWidgets('editing removes and resizes tiles and saves the layout', (
    tester,
  ) async {
    await show(
      tester,
      tiles: const [
        TilePlacement('tasks', TileSize.square),
        TilePlacement('memory', TileSize.icon),
      ],
    );
    await tester.tap(find.byKey(const Key('home-edit')));
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('tile-resize-tasks')));
    await tester.pumpAndSettle();
    expect(layout.sizeOf('tasks'), isNot(TileSize.square));

    await tester.tap(find.byKey(const Key('tile-remove-memory')));
    await tester.pumpAndSettle();
    expect(layout.contains('memory'), isFalse);
    expect(find.byKey(const Key('tile-remove-memory')), findsNothing);
    expect(opened, isEmpty, reason: 'editing never opens a page');
  });

  testWidgets('dragging one tile onto another reorders them', (tester) async {
    await show(
      tester,
      tiles: const [
        TilePlacement('tasks', TileSize.icon),
        TilePlacement('memory', TileSize.icon),
        TilePlacement('journal', TileSize.icon),
      ],
    );
    await tester.tap(find.byKey(const Key('home-edit')));
    await tester.pumpAndSettle();

    final from = tester.getCenter(find.text('Tasks'));
    final to = tester.getCenter(find.text('Journal'));
    final gesture = await tester.startGesture(from);
    await tester.pump(const Duration(milliseconds: 400));
    await gesture.moveTo(to);
    await tester.pump(const Duration(milliseconds: 100));
    await gesture.moveTo(to + const Offset(2, 0));
    await tester.pump(const Duration(milliseconds: 100));
    await gesture.up();
    await tester.pumpAndSettle();

    final ids = layout.layout.map((p) => p.id).toList();
    expect(ids.indexOf('tasks'), greaterThan(ids.indexOf('memory')));
    expect(ids.toSet(), {'tasks', 'memory', 'journal'});
  });

  testWidgets('Add tile in edit mode asks for Everything', (tester) async {
    await show(tester);
    await tester.tap(find.byKey(const Key('home-edit')));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const Key('home-add-tile')));
    await tester.tap(find.byKey(const Key('home-add-tile')));
    expect(addTile, 1);
  });

  testWidgets('a tile pinned later loads its data', (tester) async {
    await show(tester, tiles: const [TilePlacement('tasks', TileSize.strip)]);
    expect(http.sent('GET', '/api/v1/memory'), isEmpty);
    layout.pin('memory', TileSize.strip);
    await tester.pumpAndSettle();
    expect(http.sent('GET', '/api/v1/memory'), hasLength(1));
    expect(find.textContaining('Prefers window seats'), findsOneWidget);
  });
}
