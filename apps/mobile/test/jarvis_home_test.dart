import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/home/jarvis_home.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_visuals.dart';
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
    suggestions.clear();
    http.on('GET', '/api/v1/persona', {'preferredName': 'Youri'});
    http.on('GET', '/api/v1/home', {
      'calendar': {
        'connected': true,
        'events': [
          {
            'title': 'Dinner with Sanne',
            'startAt': _iso(DateTime(2026, 10, 3, 19, 45)),
            'location': 'Café Loetje',
          },
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
    bool busy = false,
    List<TilePlacement>? tiles,
    DateTime? at,
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
        // The reply waveform moves forever; hold it still so tests can settle.
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: busy),
          child: child!,
        ),
        home: Scaffold(
          body: JarvisHome(
            source: source,
            layout: layout,
            chats: chats,
            ready: ready,
            refreshRevision: 0,
            clock: () => at ?? now,
            onOpen: opened.add,
            onOpenChat: openedChats.add,
            onAddTile: () => addTile++,
            onSuggestion: suggestions.add,
            jarvisBusy: busy,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    // Setup cards mount once the briefing arrives; let their requests finish.
    await tester.pump(const Duration(milliseconds: 50));
    await tester.pumpAndSettle();
  }

  Future<void> enterEdit(WidgetTester tester) async {
    await tester.ensureVisible(find.byKey(const Key('home-edit')));
    await tester.tap(find.byKey(const Key('home-edit')));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const Key('home-edit-hint')));
    await tester.pumpAndSettle();
  }

  testWidgets('the clock shows the next thing on the day', (tester) async {
    await show(tester);
    expect(find.text('Saturday 3 October'), findsOneWidget);
    expect(
      tester.widget<Text>(find.byKey(const Key('home-clock-time'))).data,
      '19:45',
    );
    expect(find.text('in 1 h 29 min'), findsOneWidget);
    expect(
      tester.widget<Text>(find.byKey(const Key('home-clock-title'))).data,
      'Dinner with Sanne',
    );
    expect(find.text('Café Loetje'), findsOneWidget);
  });

  testWidgets('the greeting follows the local time and omits an unset name', (
    tester,
  ) async {
    http.on('GET', '/api/v1/persona', {'preferredName': ''});
    await show(tester, at: DateTime(2026, 10, 3, 9));
    expect(find.text('Good morning'), findsOneWidget);
    expect(find.byKey(const Key('home-name')), findsNothing);
    await show(tester, at: DateTime(2026, 10, 3, 14));
    expect(find.text('Good afternoon'), findsOneWidget);
    await show(tester, at: DateTime(2026, 10, 3, 21));
    expect(find.text('Good evening'), findsOneWidget);
  });

  testWidgets('with nothing coming up it shows the time and says so', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home', {
      'calendar': {'connected': true, 'events': <Object>[]},
      'reminders': <Object>[],
    });
    await show(tester);
    expect(
      tester.widget<Text>(find.byKey(const Key('home-clock-time'))).data,
      '18:16',
    );
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

  testWidgets('Chats tile distinguishes repeated names by profile', (
    tester,
  ) async {
    chats.setConversations([
      {
        'id': 'work',
        'title': 'Jarvis AI',
        'profileName': 'Work',
        'updatedAt': _iso(now),
      },
      {
        'id': 'personal',
        'title': 'Jarvis AI',
        'profileName': 'Personal',
        'updatedAt': _iso(now),
      },
    ]);
    await show(tester, tiles: const [TilePlacement('chats', TileSize.wide)]);
    expect(find.text('Jarvis AI'), findsNWidgets(2));
    expect(find.text('Jarvis · Work'), findsOneWidget);
    expect(find.text('Jarvis · Personal'), findsOneWidget);
    await tester.tap(find.text('Jarvis · Work'));
    expect(openedChats, ['jarvis:work']);
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

  testWidgets('a personal greeting replaces the header buttons', (
    tester,
  ) async {
    await show(tester);
    expect(find.text('Good evening'), findsOneWidget);
    expect(find.text('Youri'), findsOneWidget);
    expect(find.byKey(const Key('home-settings')), findsNothing);
    await enterEdit(tester);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-edit-done')), findsOneWidget);
    expect(find.byKey(const Key('home-edit-hint')), findsOneWidget);
    expect(find.byKey(const Key('home-approvals')), findsNothing);
    await tester.tap(find.byKey(const Key('home-edit-done')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-edit')), findsOneWidget);
  });

  testWidgets('long press opens a menu to resize, edit or remove', (
    tester,
  ) async {
    await show(tester, tiles: const [TilePlacement('tasks', TileSize.square)]);
    await tester.longPress(find.text('Tasks'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('menu-size-wide')), findsOneWidget);
    expect(
      find.byKey(const Key('menu-size-square')),
      findsNothing,
      reason: 'the current size is not offered',
    );

    await tester.tap(find.byKey(const Key('menu-size-wide')));
    await tester.pumpAndSettle();
    expect(layout.sizeOf('tasks'), TileSize.wide);

    await tester.longPress(find.text('Tasks'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('menu-edit')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-edit-done')), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-edit-done')));
    await tester.pumpAndSettle();

    await tester.longPress(find.text('Tasks'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('menu-remove')));
    await tester.pumpAndSettle();
    expect(layout.contains('tasks'), isFalse);
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
    await enterEdit(tester);
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
    await enterEdit(tester);
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
    await enterEdit(tester);
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

  group('quick actions', () {
    Map<String, Object?> habit(String id, String name, bool done, int streak) =>
        {
          'id': id,
          'name': name,
          'stats': {'doneToday': done, 'currentStreak': streak},
        };

    void habits() => http.on('GET', '/api/v1/habits', {
      'habits': [
        habit('h1', 'Run', true, 12),
        habit('h2', 'Read 20 pages', false, 4),
        habit('h3', 'Water', false, 2),
      ],
    });

    testWidgets('a habit is checked in from its tile', (tester) async {
      habits();
      http.on('POST', '/api/v1/habits/h2/check-ins', {'id': 'h2'});
      await show(
        tester,
        tiles: const [TilePlacement('habits', TileSize.square)],
      );
      expect(find.text('1/3'), findsOneWidget);
      expect(find.text('Check in'), findsOneWidget);

      habits2() => http.on('GET', '/api/v1/habits', {
        'habits': [
          habit('h1', 'Run', true, 12),
          habit('h2', 'Read 20 pages', true, 5),
          habit('h3', 'Water', false, 2),
        ],
      });
      habits2();
      await tester.tap(find.byKey(const Key('tile-action-check')));
      await tester.pump();
      // The tile answers at once, before the server does.
      expect(find.text('2/3'), findsOneWidget);
      await tester.pumpAndSettle();

      final sent = http.sent('POST', '/api/v1/habits/h2/check-ins').single;
      expect(sent.body, {'done': true});
      expect(find.text('Checked in'), findsOneWidget);
      expect(http.sent('GET', '/api/v1/habits'), hasLength(2));
    });

    testWidgets('habit rows toggle from the wide tile', (tester) async {
      habits();
      http.on('POST', '/api/v1/habits/h1/check-ins', {'id': 'h1'});
      await show(tester, tiles: const [TilePlacement('habits', TileSize.wide)]);
      await tester.tap(find.byKey(const Key('row-action-h1-uncheck')));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/habits/h1/check-ins').single.body, {
        'done': false,
      });
      expect(find.text('Check-in removed'), findsOneWidget);
    });

    void reminders() => http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Call mum',
        'status': 'pending',
        'dueAt': _iso(DateTime(2026, 10, 3, 19, 30)),
      },
      {
        'id': 'r2',
        'title': 'Water the basil',
        'status': 'pending',
        'dueAt': _iso(DateTime(2026, 10, 3, 21)),
      },
    ]);

    testWidgets('a reminder is finished or snoozed from its tile', (
      tester,
    ) async {
      reminders();
      http.on('POST', '/api/v1/reminders/r1/complete', {});
      http.on('POST', '/api/v1/reminders/r1/snooze', {});
      await show(
        tester,
        tiles: const [TilePlacement('reminders', TileSize.square)],
      );
      expect(find.text('Call mum'), findsOneWidget);
      expect(find.text('in 1 h 14 min'), findsOneWidget);

      await tester.tap(find.byKey(const Key('tile-action-snooze')));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/reminders/r1/snooze').single.body, {
        'minutes': 10,
      });
      expect(find.text('Snoozed for 10 minutes'), findsOneWidget);

      await tester.tap(find.byKey(const Key('tile-action-done')));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/reminders/r1/complete'), hasLength(1));
    });

    testWidgets('a reminder row is ticked off in the wide tile', (
      tester,
    ) async {
      reminders();
      http.on('POST', '/api/v1/reminders/r2/complete', {});
      await show(
        tester,
        tiles: const [TilePlacement('reminders', TileSize.wide)],
      );
      await tester.tap(find.byKey(const Key('row-action-r2-done')));
      await tester.pump();
      expect(
        find.text('Water the basil'),
        findsNothing,
        reason: 'the row leaves at once',
      );
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/reminders/r2/complete'), hasLength(1));
    });

    testWidgets('a tool call can be declined or reviewed from its tile', (
      tester,
    ) async {
      http.on('GET', '/api/v1/approvals', [
        {'id': 'a1', 'toolName': 'CreateCalendarEvent'},
      ]);
      http.on('POST', '/api/v1/approvals/a1/decision', {});
      await show(
        tester,
        tiles: const [TilePlacement('approvals', TileSize.square)],
      );
      expect(find.text('Create calendar event'), findsWidgets);
      expect(
        find.text('Approve'),
        findsNothing,
        reason: 'approving always goes through the details page',
      );

      await tester.tap(find.byKey(const Key('tile-action-review')));
      expect(opened, ['approvals']);
      expect(http.sent('POST', '/api/v1/approvals/a1/decision'), isEmpty);

      await tester.tap(find.byKey(const Key('tile-action-deny')));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/approvals/a1/decision').single.body, {
        'approved': false,
      });
      expect(find.text('Declined'), findsOneWidget);
    });

    testWidgets('a failed action is reported and the tile comes back', (
      tester,
    ) async {
      habits();
      http.on('POST', '/api/v1/habits/h2/check-ins', {}, status: 500);
      await show(
        tester,
        tiles: const [TilePlacement('habits', TileSize.square)],
      );
      await tester.tap(find.byKey(const Key('tile-action-check')));
      await tester.pumpAndSettle();
      expect(find.text('Could not do that. Try again.'), findsOneWidget);
      expect(find.text('1/3'), findsOneWidget);
    });

    testWidgets('buttons do nothing while editing', (tester) async {
      habits();
      await show(
        tester,
        tiles: const [TilePlacement('habits', TileSize.square)],
      );
      await enterEdit(tester);
      await tester.pumpAndSettle();
      await tester.tap(
        find.byKey(const Key('tile-action-check')),
        warnIfMissed: false,
      );
      await tester.pumpAndSettle();
      expect(http.requests.where((r) => r.method == 'POST'), isEmpty);
    });
  });

  group('live tiles', () {
    testWidgets('a tile shows placeholders until its data arrives', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(400, 1200);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      layout.unpin('chats');
      for (final id in [for (final p in layout.layout) p.id]) {
        layout.unpin(id);
      }
      layout.pin('tasks', TileSize.square);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: Scaffold(
            body: JarvisHome(
              source: source,
              layout: layout,
              chats: chats,
              ready: true,
              refreshRevision: 0,
              clock: () => now,
              onOpen: opened.add,
              onOpenChat: openedChats.add,
              onAddTile: () {},
            ),
          ),
        ),
      );
      expect(find.byType(TileSkeleton), findsOneWidget);
      await tester.pumpAndSettle();
      expect(find.byType(TileSkeleton), findsNothing);
      await tester.pump(const Duration(milliseconds: 50));
      await tester.pumpAndSettle();
      expect(find.text('Compare flights'), findsOneWidget);
    });

    testWidgets('Today draws the day with a marker for now', (tester) async {
      await show(tester, tiles: const [TilePlacement('today', TileSize.wide)]);
      expect(find.byKey(const Key('timeline-now')), findsOneWidget);
      await tester.tap(find.byKey(const Key('span-0')));
      await tester.pump();
      expect(
        tester.widget<Text>(find.byKey(const Key('timeline-caption'))).data,
        contains('Dinner with Sanne'),
      );
    });

    testWidgets('spending shows seven days and reads one when touched', (
      tester,
    ) async {
      http.on('GET', '/api/v1/expenses', {
        'year': 2026,
        'month': 10,
        'currency': 'EUR',
        'total': 100.0,
        'count': 3,
        'previousTotal': 80.0,
        'categories': <Object>[],
        'days': [
          {'date': '2026-10-01', 'total': 20.0},
          {'date': '2026-10-02', 'total': 50.0},
          {'date': '2026-10-03', 'total': 30.0},
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
      await show(
        tester,
        tiles: const [TilePlacement('expenses', TileSize.square)],
      );
      for (var i = 0; i < 7; i++) {
        expect(find.byKey(Key('bar-$i')), findsOneWidget);
      }
      expect(
        tester.widget<Text>(find.byKey(const Key('bars-caption'))).data,
        '+25% vs last month',
      );
      await tester.tap(find.byKey(const Key('bar-5')));
      await tester.pump();
      expect(
        tester.widget<Text>(find.byKey(const Key('bars-caption'))).data,
        'Fri · €50',
      );
      expect(opened, isEmpty, reason: 'touching a bar does not open Expenses');
      await tester.tap(find.byKey(const Key('bar-5')));
      await tester.pump();
      expect(
        tester.widget<Text>(find.byKey(const Key('bars-caption'))).data,
        '+25% vs last month',
      );
    });

    testWidgets('the Chats tile shows when Jarvis is replying', (tester) async {
      await show(
        tester,
        busy: true,
        tiles: const [TilePlacement('chats', TileSize.wide)],
      );
      expect(find.text('Replying…'), findsOneWidget);
      expect(find.byKey(const Key('tile-waveform')), findsOneWidget);
      expect(find.text('Lisbon trip'), findsOneWidget);
    });

    testWidgets('the clock opens today and counts down', (tester) async {
      await show(tester);
      expect(find.text('in 1 h 29 min'), findsOneWidget);
      await tester.tap(find.byKey(const Key('home-clock-tap')));
      expect(opened, ['today']);
    });

    testWidgets('data is fetched again every minute', (tester) async {
      await show(
        tester,
        tiles: const [TilePlacement('tasks', TileSize.square)],
      );
      final before = http.sent('GET', '/api/v1/tasks').length;
      await tester.pump(const Duration(seconds: 31));
      expect(
        http.sent('GET', '/api/v1/tasks'),
        hasLength(before),
        reason: 'only the clock moves on the first tick',
      );
      await tester.pump(const Duration(seconds: 31));
      await tester.pumpAndSettle();
      expect(http.sent('GET', '/api/v1/tasks').length, greaterThan(before));
    });
  });

  testWidgets('scrolling down swaps the greeting for a compact header', (
    tester,
  ) async {
    await show(tester);
    tester.view.physicalSize = const Size(400, 640);
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-compact-header')), findsNothing);
    await tester.drag(
      find.byKey(const Key('home-list')),
      const Offset(0, -400),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-compact-header')), findsOneWidget);

    await tester.tap(find.byKey(const Key('home-compact-header')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-compact-header')), findsNothing);
  });
}
