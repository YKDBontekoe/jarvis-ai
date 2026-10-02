import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/whatsapp/read_along_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_chat_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_models.dart';

import 'support/fixture_http.dart';

const _channel = '11111111-1111-1111-1111-111111111111';
const _chats = '/api/v1/channels/$_channel/chats';
const _piet = '$_chats/%2B31611111111';

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
    http.on('PUT', '$_chats/%2B31622222222', {
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

    final put = http.sent('PUT', '$_chats/%2B31622222222').single;
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

    expect(find.textContaining('Your phone did not answer'), findsOneWidget);
    expect(find.text('No chats yet'), findsOneWidget);
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
    expect(find.textContaining('Sends from your own WhatsApp'), findsOneWidget);

    await tester.tap(find.byKey(const Key('whatsapp-send')));
    await tester.pumpAndSettle();
    expect(
      (http.sent('POST', '$_piet/send').single.body! as Map)['text'],
      'Top, ik ben er om 7!',
    );
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
}
