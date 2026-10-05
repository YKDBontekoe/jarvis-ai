import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/whatsapp/read_along_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_chat_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_models.dart';

import 'support/fixture_http.dart';

const _channel = '11111111-1111-1111-1111-111111111111';
const _chats = '/api/v1/channels/$_channel/chats';
const _piet = '$_chats/open';
const _pietQuery = 'chatId=%2B31611111111';
const _sanneQuery = 'chatId=%2B31622222222';

const _pietChat = WhatsAppChat(
  chatId: '+31611111111',
  name: 'Piet de Vries',
  isGroup: false,
  readAlong: true,
  autoReminders: true,
);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  // Unmounting cancels the screens' refresh timers.
  Future<void> close(WidgetTester tester) =>
      tester.pumpWidget(const SizedBox.shrink());

  testWidgets('read along lists chats by section and turns one on', (
    tester,
  ) async {
    http.on('GET', _chats, {
      'live': true,
      'chats': [
        {
          'chatId': '+31611111111',
          'name': 'Piet de Vries',
          'isGroup': false,
          'readAlong': true,
          'autoReminders': true,
          'lastMessageAt': DateTime.now().toUtc().toIso8601String(),
        },
        {
          'chatId': '+31622222222',
          'name': 'Sanne',
          'isGroup': false,
          'readAlong': false,
          'autoReminders': true,
        },
        {
          'chatId': '120363025-1@g.us',
          'name': 'Familie',
          'isGroup': true,
          'readAlong': false,
          'autoReminders': true,
        },
      ],
    });
    http.on('PUT', '$_piet?$_sanneQuery', {
      'chatId': '+31622222222',
      'name': 'Sanne',
      'isGroup': false,
      'readAlong': true,
      'autoReminders': true,
    });
    await show(
      tester,
      ReadAlongScreen(http: http.client(), channelId: _channel),
    );

    expect(find.text('Reading along'), findsOneWidget);
    expect(find.text('People'), findsOneWidget);
    expect(find.text('Groups'), findsOneWidget);
    expect(find.text('Familie'), findsOneWidget);

    await tester.tap(find.byKey(const Key('read-along-switch-+31622222222')));
    await tester.pumpAndSettle();

    final put = http.sent('PUT', _piet).single;
    expect(put.query['chatId'], '+31622222222');
    expect((put.body! as Map)['readAlong'], isTrue);
    expect(find.textContaining('now reads along with Sanne'), findsOneWidget);

    await tester.enterText(find.byKey(const Key('read-along-search')), 'fam');
    await tester.pumpAndSettle();
    expect(find.text('Familie'), findsOneWidget);
    expect(find.text('Piet de Vries'), findsNothing);
    await close(tester);
  });

  testWidgets('shows when the phone list is not reachable', (tester) async {
    http.on('GET', _chats, {'live': false, 'chats': <Object>[]});
    await show(
      tester,
      ReadAlongScreen(http: http.client(), channelId: _channel),
    );

    expect(
      find.textContaining('WhatsApp could not be reached'),
      findsOneWidget,
    );
    expect(find.text('No saved chats'), findsOneWidget);
    await close(tester);
  });

  testWidgets(
    'loads older messages, retains them after polling and marks only loaded messages read',
    (tester) async {
      final sent = DateTime.utc(2026, 10, 2, 12);
      Map<String, Object?> message(int index) => {
        'id': '00000000-0000-0000-0000-${index.toString().padLeft(12, '0')}',
        'fromMe': false,
        'sender': 'Piet',
        'text': 'Saved message $index',
        'sentAt': sent.toIso8601String(),
        'receivedAt': sent.add(Duration(seconds: index)).toIso8601String(),
      };
      final initial = [for (var i = 60; i >= 1; i--) message(i)];
      final oldest = message(1)['id'];
      http.on('GET', '$_piet/messages', initial);
      http.on('GET', '$_piet/messages?$_pietQuery&beforeId=$oldest', [
        message(0),
      ]);
      http.on('POST', '$_piet/read', {}, status: 204);
      await show(
        tester,
        WhatsAppChatScreen(
          http: http.client(),
          channelId: _channel,
          chat: _pietChat,
        ),
      );
      expect(
        (http.sent('POST', '$_piet/read').single.body as Map)['messageId'],
        message(60)['id'],
      );

      await tester.drag(
        find.byKey(const Key('whatsapp-messages')),
        const Offset(0, 12000),
      );
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.byKey(const Key('whatsapp-load-older')));
      await tester.tap(find.byKey(const Key('whatsapp-load-older')));
      await tester.pumpAndSettle();
      expect(
        http.sent('GET', '$_piet/messages').last.query['beforeId'],
        oldest,
      );
      expect(find.text('Saved message 0'), findsOneWidget);
      expect(find.byKey(const Key('whatsapp-load-older')), findsNothing);
      http.on('GET', '$_piet/messages', [message(61), ...initial.take(59)]);
      await tester.pump(const Duration(seconds: 4));
      await tester.pumpAndSettle();
      expect(find.text('Saved message 0'), findsOneWidget);
      expect(
        (http.sent('POST', '$_piet/read').last.body as Map)['messageId'],
        message(61)['id'],
      );
      expect(tester.takeException(), isNull);
      await close(tester);
    },
  );

  testWidgets(
    'keeps messages visible and reports background refresh failures',
    (tester) async {
      http.on('GET', '$_piet/messages', [
        {
          'id': 'm1',
          'text': 'Still saved',
          'sentAt': DateTime.now().toUtc().toIso8601String(),
        },
      ]);
      http.on('GET', '$_chats/status', {
        'account': '+31612345678',
        'state': 'open',
      });
      await show(
        tester,
        WhatsAppChatScreen(
          http: http.client(),
          channelId: _channel,
          chat: _pietChat,
        ),
      );
      http.on('GET', '$_piet/messages', {}, status: 503);
      await tester.pump(const Duration(seconds: 4));
      await tester.pumpAndSettle();
      expect(find.text('Still saved'), findsOneWidget);
      expect(find.textContaining('Could not refresh messages'), findsOneWidget);
      expect(find.textContaining('+31612345678'), findsOneWidget);
      await close(tester);
    },
  );

  testWidgets('disconnected accounts keep saved messages and disable sending', (
    tester,
  ) async {
    http.on('GET', '$_piet/messages', [
      {
        'id': 'm1',
        'text': 'Still saved',
        'sentAt': DateTime.now().toUtc().toIso8601String(),
      },
    ]);
    http.on('GET', '$_chats/status', {
      'account': '+31612345678',
      'state': 'logged_out',
    });
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: _pietChat,
      ),
    );
    await tester.enterText(
      find.byKey(const Key('whatsapp-composer')),
      'My reply',
    );
    await tester.pumpAndSettle();
    expect(
      tester
          .widget<IconButton>(find.byKey(const Key('whatsapp-send')))
          .onPressed,
      isNull,
    );
    expect(find.text('Still saved'), findsOneWidget);
    expect(find.textContaining('needs to be linked again'), findsOneWidget);
    await close(tester);
  });

  testWidgets('catches up across more than one page of new messages', (
    tester,
  ) async {
    final sent = DateTime.utc(2026, 10, 2, 12);
    Map<String, Object?> message(int index) => {
      'id': '00000000-0000-0000-0000-${index.toString().padLeft(12, '0')}',
      'text': 'Catch up $index',
      'sentAt': sent.add(Duration(seconds: index)).toIso8601String(),
    };
    http.on('GET', '$_piet/messages', [message(0)]);
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: _pietChat,
      ),
    );
    http.on('GET', '$_piet/messages', [
      for (var i = 100; i >= 41; i--) message(i),
    ]);
    http.on(
      'GET',
      '$_piet/messages?$_pietQuery&beforeId=${message(41)['id']}',
      [for (var i = 40; i >= 0; i--) message(i)],
    );
    await tester.pump(const Duration(seconds: 4));
    await tester.pumpAndSettle();
    expect(
      http
          .sent('GET', '$_piet/messages')
          .where((request) => request.query['beforeId'] == message(41)['id']),
      hasLength(1),
    );
    await tester.drag(
      find.byKey(const Key('whatsapp-messages')),
      const Offset(0, 10000),
    );
    await tester.pumpAndSettle();
    expect(find.text('Catch up 0'), findsOneWidget);
    expect(find.textContaining('Could not refresh'), findsNothing);
    await close(tester);
  });

  testWidgets('chat shows both sides, drafts a reply and sends it', (
    tester,
  ) async {
    final now = DateTime.now().toUtc();
    http.on('GET', '$_piet/messages', [
      {
        'id': 'b',
        'chatId': '+31611111111',
        'fromMe': true,
        'sender': null,
        'text': 'Ja leuk!',
        'sentAt': now.subtract(const Duration(minutes: 1)).toIso8601String(),
      },
      {
        'id': 'a',
        'chatId': '+31611111111',
        'fromMe': false,
        'sender': 'Piet',
        'text': 'Etentje vrijdag om 19:00?',
        'sentAt': now.subtract(const Duration(minutes: 2)).toIso8601String(),
      },
    ]);
    http.on('POST', '$_piet/suggest', {'text': 'Top, ik ben er om 7!'});
    http.on('POST', '$_piet/send', {'sent': true});
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: _pietChat,
        pollInterval: const Duration(hours: 1),
      ),
    );

    expect(find.text('Etentje vrijdag om 19:00?'), findsOneWidget);
    expect(find.text('Ja leuk!'), findsOneWidget);
    expect(find.text('Today'), findsOneWidget);

    await tester.tap(find.byKey(const Key('whatsapp-draft')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Say yes'));
    await tester.pumpAndSettle();

    expect(
      (http.sent('POST', '$_piet/suggest').single.body! as Map)['instruction'],
      'Say yes',
    );
    expect(find.text('Top, ik ben er om 7!'), findsOneWidget);
    expect(find.byTooltip('Send from your WhatsApp'), findsOneWidget);

    await tester.tap(find.byKey(const Key('whatsapp-send')));
    await tester.pumpAndSettle();
    expect(
      (http.sent('POST', '$_piet/send').single.body! as Map)['text'],
      'Top, ik ben er om 7!',
    );
    await close(tester);
  });

  testWidgets('a group loads its saved messages without putting @ in the url', (
    tester,
  ) async {
    const group = WhatsAppChat(
      chatId: '120363025-1@g.us',
      name: 'Familie',
      isGroup: true,
      readAlong: true,
      autoReminders: true,
    );
    http.on('GET', '$_piet/messages', [
      {
        'id': 'g1',
        'fromMe': false,
        'sender': 'Piet',
        'senderId': '+31611111111',
        'text': 'Eten we vrijdag bij oma?',
        'sentAt': DateTime.now().toUtc().toIso8601String(),
      },
    ]);
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: group,
        pollInterval: const Duration(hours: 1),
      ),
    );
    final requested =
        http.sent('GET', '$_piet/messages').single.query['chatId'] as String;
    expect(requested.startsWith('b64.'), isTrue);
    expect(requested.contains('@'), isFalse);
    expect(find.text('Eten we vrijdag bij oma?'), findsOneWidget);
    expect(find.text('Piet'), findsOneWidget);
    expect(find.text('Waiting for messages'), findsNothing);
    await close(tester);
  });

  testWidgets('ask Jarvis answers about the chat and can become the reply', (
    tester,
  ) async {
    http.on('GET', '$_piet/messages', <Object>[]);
    http.on('POST', '$_piet/ask', {
      'conversationId': '33333333-3333-3333-3333-333333333333',
      'answer': 'Jullie spraken vrijdag om 19:00 af.',
      'needsApproval': false,
    });
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: _pietChat,
        pollInterval: const Duration(hours: 1),
      ),
    );
    expect(find.text('Waiting for messages'), findsOneWidget);

    await tester.tap(find.byKey(const Key('whatsapp-ask-jarvis')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Did we agree on a date or time?'));
    await tester.pumpAndSettle();

    expect(find.text('Jullie spraken vrijdag om 19:00 af.'), findsOneWidget);
    await tester.tap(find.byKey(const Key('whatsapp-ask-use')));
    await tester.pumpAndSettle();
    final composer = tester.widget<TextField>(
      find.byKey(const Key('whatsapp-composer')),
    );
    expect(composer.controller!.text, 'Jullie spraken vrijdag om 19:00 af.');
    await close(tester);
  });

  test('chat helpers format times and initials', () {
    final now = DateTime(2026, 10, 2, 15);
    expect(whatsAppListTime(DateTime(2026, 10, 2, 9, 5), now: now), '09:05');
    expect(whatsAppListTime(DateTime(2026, 10, 1, 9), now: now), 'Yesterday');
    expect(whatsAppListTime(DateTime(2026, 9, 28, 9), now: now), 'Mon');
    expect(whatsAppListTime(DateTime(2026, 9, 12, 9), now: now), '12 Sep');
    expect(
      whatsAppDayLabel(DateTime(2026, 9, 28), now: now),
      'Monday 28 September',
    );
    expect(whatsAppChatPath(_channel, action: 'messages'), '$_piet/messages');
    expect(
      whatsAppChatPath(_channel, action: 'messages').contains('@'),
      isFalse,
    );
    expect(_pietChat.initials, 'PV');
    expect(_pietChat.phone, '+31611111111');
    expect(
      const WhatsAppChat(
        chatId: '+31611111111',
        name: '+31611111111',
        isGroup: false,
        readAlong: false,
        autoReminders: true,
      ).initials,
      '#',
    );
  });

  const catchUpJson = {
    'summary': 'Piet wil vrijdag eten en vraagt om een tijd.',
    'toReply': [
      {'who': 'Piet', 'about': 'Welke tijd past?'},
    ],
    'messageCount': 3,
  };

  testWidgets('read along list shows a catch-up digest and opens the chat', (
    tester,
  ) async {
    http.on('GET', _chats, {
      'live': true,
      'chats': [
        {
          'chatId': '+31611111111',
          'name': 'Piet de Vries',
          'isGroup': false,
          'readAlong': true,
          'autoReminders': true,
          'unreadCount': 3,
          'lastMessageAt': DateTime.now().toUtc().toIso8601String(),
          'catchUp': catchUpJson,
        },
      ],
    });
    http.on('GET', '$_piet/messages', <Object>[]);
    await show(
      tester,
      ReadAlongScreen(
        http: http.client(),
        channelId: _channel,
        selecting: false,
      ),
    );

    expect(find.byKey(const Key('whatsapp-catch-up-digest')), findsOneWidget);
    expect(find.textContaining('Piet wil vrijdag eten'), findsOneWidget);
    expect(find.text('1 to reply'), findsOneWidget);

    await tester.tap(find.byKey(const Key('whatsapp-catch-up-+31611111111')));
    await tester.pumpAndSettle();
    expect(find.byType(WhatsAppChatScreen), findsOneWidget);
    expect(find.byKey(const Key('whatsapp-catch-up')), findsOneWidget);
    await close(tester);
  });

  testWidgets('chat catch-up drafts a reply about one item and can be hidden', (
    tester,
  ) async {
    http.on('GET', '$_piet/messages', <Object>[]);
    http.on('POST', '$_piet/suggest', {'text': 'Zeven uur?'});
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: WhatsAppChat.fromJson({
          'chatId': '+31611111111',
          'name': 'Piet de Vries',
          'readAlong': true,
          'catchUp': catchUpJson,
        })!,
        pollInterval: const Duration(hours: 1),
      ),
    );

    expect(find.text('Catch up · 3 unread'), findsOneWidget);
    await tester.tap(
      find.byKey(const Key('whatsapp-catch-up-draft-Welke tijd past?')),
    );
    await tester.pumpAndSettle();
    expect(
      (http.sent('POST', '$_piet/suggest').single.body! as Map)['instruction'],
      'Reply to Piet about: Welke tijd past?',
    );
    expect(find.text('Zeven uur?'), findsOneWidget);

    await tester.tap(find.byKey(const Key('whatsapp-catch-up-dismiss')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('whatsapp-catch-up')), findsNothing);
    await close(tester);
  });

  testWidgets('chats without a catch-up show no card', (tester) async {
    http.on('GET', '$_piet/messages', <Object>[]);
    await show(
      tester,
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        chat: _pietChat,
        pollInterval: const Duration(hours: 1),
      ),
    );
    expect(find.byKey(const Key('whatsapp-catch-up')), findsNothing);
    await close(tester);
  });
}
