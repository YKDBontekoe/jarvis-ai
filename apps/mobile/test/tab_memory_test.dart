import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/chats/chats_screen.dart';
import 'package:jarvis_mobile/features/everything/everything_screen.dart';
import 'package:jarvis_mobile/features/shell/jarvis_tab_bar.dart';
import 'package:jarvis_mobile/features/shell/tab_memory.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

/// Two "tabs" over one TabMemory, the way the shell swaps its pages.
class _Shell extends StatefulWidget {
  const _Shell(this.memory);

  final TabMemory memory;

  @override
  State<_Shell> createState() => _ShellState();
}

class _ShellState extends State<_Shell> {
  JarvisTab tab = JarvisTab.home;
  ScrollController? _controller;
  JarvisTab? _controllerTab;

  @override
  void dispose() {
    if (_controller != null) {
      widget.memory.slotFor(_controllerTab!).release(_controller!);
    }
    super.dispose();
  }

  /// A fresh controller each time the tab's page is built, as the real pages do.
  ScrollController _controllerFor(JarvisTab value) {
    if (_controllerTab != value) {
      if (_controller != null) {
        widget.memory.slotFor(_controllerTab!).release(_controller!);
      }
      _controller = widget.memory.slotFor(value).create();
      _controllerTab = value;
    }
    return _controller!;
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Column(
      children: [
        Row(
          children: [
            TextButton(
              onPressed: () => setState(() => tab = JarvisTab.home),
              child: const Text('go-home'),
            ),
            TextButton(
              onPressed: () => setState(() => tab = JarvisTab.chats),
              child: const Text('go-chats'),
            ),
          ],
        ),
        Expanded(
          child: KeyedSubtree(
            key: ValueKey(tab),
            child: ListView.builder(
              controller: _controllerFor(tab),
              itemCount: 100,
              itemBuilder: (_, i) =>
                  SizedBox(height: 60, child: Text('${tab.name} row $i')),
            ),
          ),
        ),
      ],
    ),
  );
}

void main() {
  testWidgets(
    'a tab opens where it was left, and each tab keeps its own spot',
    (tester) async {
      final memory = TabMemory();
      await tester.pumpWidget(MaterialApp(home: _Shell(memory)));

      await tester.drag(find.byType(ListView), const Offset(0, -900));
      await tester.pumpAndSettle();
      final home = memory.offsetOf(JarvisTab.home);
      expect(home, greaterThan(0));

      await tester.tap(find.text('go-chats'));
      await tester.pumpAndSettle();
      expect(memory.offsetOf(JarvisTab.chats), 0);
      expect(find.text('chats row 0'), findsOneWidget);
      await tester.drag(find.byType(ListView), const Offset(0, -300));
      await tester.pumpAndSettle();

      await tester.tap(find.text('go-home'));
      await tester.pumpAndSettle();
      expect(find.text('home row 0'), findsNothing);
      expect(
        tester
            .state<ScrollableState>(find.byType(Scrollable).first)
            .position
            .pixels,
        home,
      );
    },
  );

  testWidgets('tapping the current tab scrolls it back to the top', (
    tester,
  ) async {
    final memory = TabMemory();
    await tester.pumpWidget(MaterialApp(home: _Shell(memory)));
    await tester.drag(find.byType(ListView), const Offset(0, -900));
    await tester.pumpAndSettle();
    expect(memory.offsetOf(JarvisTab.home), greaterThan(0));

    // The animation needs frames, so it is started and then pumped.
    final scrolling = memory.scrollToTop(JarvisTab.home);
    await tester.pumpAndSettle();
    await scrolling;
    expect(memory.offsetOf(JarvisTab.home), 0);
    expect(find.text('home row 0'), findsOneWidget);
  });

  test('clear forgets positions, searches and filters', () {
    final memory = TabMemory();
    memory.slotFor(JarvisTab.chats).offset = 400;
    memory
      ..chatsFilter = ChatFilter.unread
      ..chatsQuery = 'anna'
      ..everythingQuery = 'bills';

    memory.clear();
    expect(memory.offsetOf(JarvisTab.chats), 0);
    expect(memory.chatsFilter, ChatFilter.forYou);
    expect(memory.chatsQuery, isEmpty);
    expect(memory.everythingQuery, isEmpty);
  });

  testWidgets('Chats restores its filter and search and reports changes', (
    tester,
  ) async {
    final chats = ChatList()
      ..setConversations([
        {
          'id': 'c1',
          'title': 'Lisbon trip',
          'updatedAt': '2026-10-02T10:00:00Z',
        },
      ]);
    final filters = <ChatFilter>[];
    final queries = <String>[];
    tester.view.physicalSize = const Size(400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: ChatsScreen(
            chats: chats,
            onOpen: (_) {},
            onNewChat: () {},
            onSearch: () {},
            onRefresh: () async {},
            initialFilter: ChatFilter.jarvis,
            initialQuery: 'lisbon',
            onFilterChanged: filters.add,
            onQueryChanged: queries.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Lisbon trip'), findsOneWidget);
    expect(
      tester.widget<TextField>(find.byType(TextField)).controller!.text,
      'lisbon',
    );
    await tester.enterText(find.byType(TextField), 'tri');
    expect(queries.last, 'tri');
    final pill = find.byKey(const Key('chats-tab-all'));
    await tester.ensureVisible(pill);
    await tester.tap(pill);
    await tester.pumpAndSettle();
    expect(filters.last, ChatFilter.all);
  });

  testWidgets('Everything restores its search text and reports changes', (
    tester,
  ) async {
    SharedPreferences.setMockInitialValues({});
    final queries = <String>[];
    tester.view.physicalSize = const Size(400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: EverythingScreen(
            source: TileDataSource(FixtureHttp().client()),
            layout: TileLayoutController(),
            onOpen: (_) {},
            initialQuery: 'bills',
            onQueryChanged: queries.add,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('everything-finance')), findsOneWidget);
    expect(find.byKey(const Key('everything-memory')), findsNothing);
    await tester.enterText(find.byKey(const Key('everything-search')), 'dag');
    expect(queries.last, 'dag');
  });
}
