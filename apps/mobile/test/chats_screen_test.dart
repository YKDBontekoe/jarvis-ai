import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/chats/chats_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

String _iso(DateTime time) => time.toUtc().toIso8601String();

void main() {
  final now = DateTime.now();
  late FixtureHttp http;
  late ChatList chats;
  late List<ChatListItem> opened;
  var newChat = 0;
  var searched = 0;
  var managed = 0;
  var connected = 0;
  var refreshed = 0;

  setUp(() {
    http = FixtureHttp();
    chats = ChatList();
    opened = [];
    newChat = searched = managed = connected = refreshed = 0;
    chats.setConversations([
      {
        'id': 'c1',
        'title': 'Lisbon trip',
        'updatedAt': _iso(now.subtract(const Duration(hours: 4))),
      },
    ]);
  });

  Future<void> linkWhatsApp(WidgetTester tester) async {
    http.on('GET', '/api/v1/channels', [
      {'id': 'wa', 'kind': 'whatsapp_linked'},
    ]);
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [
        {
          'chatId': '+31611',
          'name': 'Sam',
          'isGroup': false,
          'lastMessageAt': _iso(now.subtract(const Duration(minutes: 5))),
          'preview': 'Can we push dinner?',
          'unreadCount': 2,
        },
        {
          'chatId': 'fam@g.us',
          'name': 'Family',
          'isGroup': true,
          'lastMessageAt': _iso(now.subtract(const Duration(hours: 1))),
          'preview': 'Photos',
          'unreadCount': 0,
        },
      ],
    });
    await tester.runAsync(() => chats.loadWhatsApp(http.client()));
  }

  Future<void> show(WidgetTester tester, {bool withManage = true}) async {
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
            onOpen: opened.add,
            onNewChat: () => newChat++,
            onSearch: () => searched++,
            onManage: withManage ? () => managed++ : null,
            onRefresh: () async => refreshed++,
            onConnectWhatsApp: () => connected++,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows Jarvis and WhatsApp chats together', (tester) async {
    await linkWhatsApp(tester);
    await show(tester);
    expect(find.text('Lisbon trip'), findsOneWidget);
    expect(find.text('Sam'), findsOneWidget);
    expect(find.text('Family'), findsOneWidget);
    expect(find.text('WhatsApp · Can we push dinner?'), findsOneWidget);
    expect(find.byKey(const Key('chat-unread-dot')), findsOneWidget);
    // Newest first: Sam, Family, then the Jarvis chat from hours ago.
    final sam = tester.getTopLeft(find.text('Sam')).dy;
    final family = tester.getTopLeft(find.text('Family')).dy;
    final lisbon = tester.getTopLeft(find.text('Lisbon trip')).dy;
    expect(sam, lessThan(family));
    expect(family, lessThan(lisbon));
  });

  testWidgets('tapping a row reports the chat', (tester) async {
    await linkWhatsApp(tester);
    await show(tester);
    await tester.tap(find.text('Sam'));
    await tester.tap(find.text('Lisbon trip'));
    expect(opened.map((i) => i.title), ['Sam', 'Lisbon trip']);
    expect(opened.first.isJarvis, isFalse);
    expect(opened.last.conversationId, 'c1');
  });

  testWidgets('filters switch between kinds and show the unread count', (
    tester,
  ) async {
    await linkWhatsApp(tester);
    await show(tester);
    expect(find.text('Unread 2'), findsOneWidget);

    await tester.tap(find.byKey(const Key('chats-tab-unread')));
    await tester.pumpAndSettle();
    expect(find.text('Sam'), findsOneWidget);
    expect(find.text('Family'), findsNothing);
    expect(find.text('Lisbon trip'), findsNothing);

    await tester.tap(find.byKey(const Key('chats-tab-jarvis')));
    await tester.pumpAndSettle();
    expect(find.text('Lisbon trip'), findsOneWidget);
    expect(find.text('Sam'), findsNothing);

    await tester.tap(find.byKey(const Key('chats-tab-whatsapp')));
    await tester.pumpAndSettle();
    expect(find.text('Sam'), findsOneWidget);
    expect(find.text('Family'), findsOneWidget);
    expect(find.text('Lisbon trip'), findsNothing);
  });

  testWidgets('the search field filters by title and preview', (tester) async {
    await linkWhatsApp(tester);
    await show(tester);
    await tester.enterText(find.byKey(const Key('chats-filter-field')), 'dinner');
    await tester.pumpAndSettle();
    expect(find.text('Sam'), findsOneWidget);
    expect(find.text('Family'), findsNothing);

    await tester.enterText(find.byKey(const Key('chats-filter-field')), 'zzz');
    await tester.pumpAndSettle();
    expect(find.text('No chats match'), findsOneWidget);
  });

  testWidgets('header buttons call back', (tester) async {
    await show(tester);
    await tester.tap(find.byKey(const Key('chats-search')));
    await tester.tap(find.byKey(const Key('chats-new')));
    await tester.tap(find.byKey(const Key('chats-manage')));
    expect((searched, newChat, managed), (1, 1, 1));
  });

  testWidgets('manage is hidden when not offered', (tester) async {
    await show(tester, withManage: false);
    expect(find.byKey(const Key('chats-manage')), findsNothing);
  });

  testWidgets('WhatsApp offers to connect while nothing is linked', (
    tester,
  ) async {
    await show(tester);
    await tester.tap(find.byKey(const Key('chats-tab-whatsapp')));
    await tester.pumpAndSettle();
    expect(find.text('Bring your WhatsApp in'), findsOneWidget);
    await tester.tap(find.byKey(const Key('chats-connect-whatsapp')));
    expect(connected, 1);
  });

  testWidgets('no unread chats says so', (tester) async {
    await show(tester);
    await tester.tap(find.byKey(const Key('chats-tab-unread')));
    await tester.pumpAndSettle();
    expect(find.text('You are all caught up'), findsOneWidget);
  });

  testWidgets('pulling down refreshes', (tester) async {
    await show(tester);
    await tester.fling(find.text('Lisbon trip'), const Offset(0, 400), 1000);
    await tester.pumpAndSettle();
    expect(refreshed, 1);
  });

  testWidgets('the list follows the shared chat list', (tester) async {
    await show(tester);
    expect(find.text('Energy contracts'), findsNothing);
    chats.setConversations([
      {'id': 'c2', 'title': 'Energy contracts', 'updatedAt': _iso(now)},
    ]);
    await tester.pumpAndSettle();
    expect(find.text('Energy contracts'), findsOneWidget);
    expect(find.text('Lisbon trip'), findsNothing);
  });
}
