import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/chats/chats_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/read_along_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_models.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

// A 1x1 transparent PNG.
final _png = Uint8List.fromList([
  0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, //
  0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
  0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
  0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
  0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
  0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
]);

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
    WhatsAppPictures.clear();
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

  Future<void> show(
    WidgetTester tester, {
    bool withManage = true,
    Dio? pictures,
    ValueNotifier<bool>? reducedMotion,
  }) async {
    tester.view.physicalSize = const Size(400, 900);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        builder: (context, child) => reducedMotion == null
            ? child!
            : ValueListenableBuilder<bool>(
                valueListenable: reducedMotion,
                builder: (context, reduced, _) => MediaQuery(
                  data: MediaQuery.of(
                    context,
                  ).copyWith(disableAnimations: reduced),
                  child: child!,
                ),
              ),
        home: Scaffold(
          body: ChatsScreen(
            chats: chats,
            http: pictures,
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
    expect(find.text('Can we push dinner?'), findsOneWidget);
    expect(find.byKey(const Key('chat-unread-dot')), findsOneWidget);
    // Newest first: Sam, Family, then the Jarvis chat from hours ago.
    final sam = tester.getTopLeft(find.text('Sam')).dy;
    final family = tester.getTopLeft(find.text('Family')).dy;
    final lisbon = tester.getTopLeft(find.text('Lisbon trip')).dy;
    expect(sam, lessThan(family));
    expect(family, lessThan(lisbon));
  });

  testWidgets('catch-ups are one row that opens every summary in full', (
    tester,
  ) async {
    const long =
        'Jarvis heeft de trainingsskill adaptief-trainingsschema verbeterd. '
        'Om 14:30 kreeg je een melding over je schema van deze week.';
    http.on('GET', '/api/v1/channels', [
      {'id': 'wa', 'kind': 'whatsapp_linked'},
    ]);
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [
        {
          'chatId': '+31611',
          'name': 'Jarvis AI',
          'lastMessageAt': _iso(now.subtract(const Duration(minutes: 5))),
          'catchUp': {'summary': long, 'messageCount': 2},
        },
        {
          'chatId': '+31622',
          'name': 'rosa',
          'lastMessageAt': _iso(now.subtract(const Duration(minutes: 9))),
          'catchUp': {
            'summary': 'Rosa belde per ongeluk.',
            'toReply': [
              {'who': 'Rosa', 'about': 'Hoe laat kom je?'},
            ],
          },
        },
      ],
    });
    await tester.runAsync(() => chats.loadWhatsApp(http.client()));
    await show(tester);

    expect(find.byKey(const Key('whatsapp-catch-up-digest')), findsNothing);
    expect(find.byKey(const Key('whatsapp-catch-up-row')), findsOneWidget);
    expect(find.text('Jarvis AI, rosa'), findsOneWidget);
    expect(find.text('2 chats · 1 to reply'), findsOneWidget);
    expect(find.text(long), findsNothing);

    await tester.tap(find.byKey(const Key('whatsapp-catch-up-row')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('whatsapp-catch-up-sheet')), findsOneWidget);
    expect(find.text(long), findsOneWidget);
    expect(find.text('Rosa: Hoe laat kom je?'), findsOneWidget);

    await tester.tap(
      find.byKey(const Key('whatsapp-catch-up-whatsapp:wa:+31622')),
    );
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('whatsapp-catch-up-sheet')), findsNothing);
    expect(opened.single.title, 'rosa');
  });

  testWidgets('no catch-up row when no chat has a catch-up', (tester) async {
    await linkWhatsApp(tester);
    await show(tester);
    expect(find.byKey(const Key('whatsapp-catch-up-row')), findsNothing);
  });

  testWidgets('repeated names show session and channel context', (
    tester,
  ) async {
    chats.setConversations([
      for (var i = 0; i < 2; i++)
        {
          'id': 'c$i',
          'title': 'Jarvis AI',
          'profileName': i == 0 ? 'Personal' : 'Work',
          'createdAt': _iso(DateTime(2026, 10, i + 1, 10)),
          'updatedAt': _iso(now),
        },
    ]);
    http.on('GET', '/api/v1/channels', [
      {'id': 'wa1', 'kind': 'whatsapp_linked', 'account': 'Personal'},
      {'id': 'wa2', 'kind': 'whatsapp_linked', 'account': 'Work'},
    ]);
    for (final id in ['wa1', 'wa2']) {
      http.on('GET', '/api/v1/channels/$id/chats', {
        'chats': [
          {'chatId': 'rosa', 'name': 'rosa', 'lastMessageAt': _iso(now)},
        ],
      });
    }
    await tester.runAsync(() => chats.loadWhatsApp(http.client()));
    await show(tester);
    expect(find.text('Jarvis · Personal · 1 Oct · 10:00'), findsOneWidget);
    expect(find.text('Jarvis · Work · 2 Oct · 10:00'), findsOneWidget);
    expect(find.text('WhatsApp · Personal'), findsOneWidget);
    expect(find.text('WhatsApp · Work'), findsOneWidget);
    expect(find.text('Tap to open chat'), findsNWidgets(2));
    expect(find.text('No messages yet'), findsNothing);
  });

  testWidgets('WhatsApp rows show the profile picture the open chat uses', (
    tester,
  ) async {
    await linkWhatsApp(tester);
    final pictures = _PictureHttp({'+31611': _png});
    await show(tester, pictures: pictures.client());
    expect(find.byKey(const Key('whatsapp-picture-+31611')), findsOneWidget);
    expect(find.byKey(const Key('whatsapp-picture-fam@g.us')), findsNothing);
    expect(pictures.subjects, ['+31611', 'fam@g.us']);
    // Jarvis conversations have no WhatsApp picture.
    expect(pictures.subjects.where((id) => id.contains('c1')), isEmpty);

    // A picture already loaded in a chat is reused, without asking again.
    pictures.subjects.clear();
    await show(tester, pictures: pictures.client());
    expect(find.byKey(const Key('whatsapp-picture-+31611')), findsOneWidget);
    expect(pictures.subjects, isEmpty);
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
    await tester.enterText(
      find.byKey(const Key('chats-filter-field')),
      'dinner',
    );
    await tester.pumpAndSettle();
    expect(find.text('Sam'), findsOneWidget);
    expect(find.text('Family'), findsNothing);

    await tester.enterText(find.byKey(const Key('chats-filter-field')), 'zzz');
    await tester.pumpAndSettle();
    expect(find.text('No chats match'), findsOneWidget);
    await tester.tap(find.byKey(const Key('chats-clear-search')));
    await tester.pumpAndSettle();
    expect(find.text('Sam'), findsOneWidget);
    expect(find.text('Family'), findsOneWidget);
    expect(find.byKey(const Key('chats-clear-search')), findsNothing);
  });

  testWidgets('changing Reduce Motion keeps filters working without a slide', (
    tester,
  ) async {
    final reduced = ValueNotifier(false);
    addTearDown(reduced.dispose);
    await linkWhatsApp(tester);
    await show(tester, reducedMotion: reduced);
    await tester.tap(find.byKey(const Key('chats-tab-unread')));
    await tester.pump(const Duration(milliseconds: 50));
    reduced.value = true;
    await tester.pumpAndSettle();
    expect(
      tester.widget<TabBar>(find.byType(TabBar)).controller!.animationDuration,
      Duration.zero,
    );
    await tester.tap(find.byKey(const Key('chats-tab-jarvis')));
    await tester.pumpAndSettle();
    expect(find.text('Lisbon trip'), findsOneWidget);
    expect(find.text('Sam'), findsNothing);
    expect(tester.hasRunningAnimations, isFalse);
    reduced.value = false;
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.byKey(const Key('chats-tab-all')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('chats-tab-all')));
    await tester.pumpAndSettle();
    expect(find.text('Sam'), findsOneWidget);
    expect(tester.takeException(), isNull);
  });

  testWidgets('header buttons call back', (tester) async {
    await show(tester);
    expect(find.byKey(const Key('chats-search')), findsNothing);
    await tester.tap(find.byKey(const Key('chats-more')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Search everything'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('chats-new')));
    await tester.tap(find.byKey(const Key('chats-more')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Manage conversations'));
    await tester.pumpAndSettle();
    expect((searched, newChat, managed), (1, 1, 1));
  });

  testWidgets('manage is hidden when not offered', (tester) async {
    await show(tester, withManage: false);
    await tester.tap(find.byKey(const Key('chats-more')));
    await tester.pumpAndSettle();
    expect(find.text('Manage conversations'), findsNothing);
    expect(find.text('Search everything'), findsOneWidget);
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

/// Serves profile-picture bytes the way `GET …/chats/open/picture` does.
class _PictureHttp implements HttpClientAdapter {
  _PictureHttp(this.pictures);

  final Map<String, Uint8List> pictures;
  final subjects = <String>[];

  Dio client() =>
      Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
        ..httpClientAdapter = this;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final subject = whatsAppChatIdFromToken(
      '${options.queryParameters['subject']}',
    );
    subjects.add(subject);
    final bytes = pictures[subject];
    if (bytes == null) {
      return ResponseBody.fromString(
        '{}',
        404,
        headers: {
          Headers.contentTypeHeader: [Headers.jsonContentType],
        },
      );
    }
    return ResponseBody.fromBytes(
      bytes,
      200,
      headers: {
        Headers.contentTypeHeader: ['image/png'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
