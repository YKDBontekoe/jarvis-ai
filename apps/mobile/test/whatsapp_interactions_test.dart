import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_chat_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_models.dart';

import 'support/fixture_http.dart';

const _channel = '11111111-1111-1111-1111-111111111111';
const _piet = '/api/v1/channels/$_channel/chats/open';

const _pietChat = WhatsAppChat(
  chatId: '+31611111111',
  name: 'Piet de Vries',
  isGroup: false,
  readAlong: true,
  autoReminders: true,
);

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    final now = DateTime.now().toUtc();
    http.on('GET', '$_piet/messages', [
      {
        'id': 'voice',
        'chatId': '+31611111111',
        'fromMe': false,
        'sender': 'Piet',
        'text': '[Voice message]',
        'sentAt': now.toIso8601String(),
        'media': {
          'kind': 'audio',
          'voice': true,
          'seconds': 4,
          'waveform': [0, 20, 60, 100, 40, 10],
        },
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
    http.on('POST', '$_piet/send', {'sent': true});
    http.on('POST', '$_piet/react', {'sent': true});
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: WhatsAppChatScreen(
          http: http.client(),
          channelId: _channel,
          chat: _pietChat,
          pollInterval: const Duration(hours: 1),
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  Future<void> close(WidgetTester tester) =>
      tester.pumpWidget(const SizedBox.shrink());

  testWidgets('swiping a message right replies to it with a quote', (
    tester,
  ) async {
    await show(tester);
    final bubble = find.text('Etentje vrijdag om 19:00?');
    await tester.drag(bubble, const Offset(120, 0));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('whatsapp-reply-preview')), findsOneWidget);
    expect(find.text('Piet'), findsWidgets);

    await tester.enterText(
      find.byKey(const Key('whatsapp-composer')),
      'Ja, gezellig!',
    );
    await tester.pump();
    await tester.tap(find.byKey(const Key('whatsapp-send')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '$_piet/send').single.body! as Map;
    expect(body['text'], 'Ja, gezellig!');
    expect(body['replyTo'], 'a');
    expect(find.byKey(const Key('whatsapp-reply-preview')), findsNothing);
    await close(tester);
  });

  testWidgets('a short swipe does not start a reply, and it can be cancelled', (
    tester,
  ) async {
    await show(tester);
    final bubble = find.text('Etentje vrijdag om 19:00?');
    await tester.drag(bubble, const Offset(30, 0));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('whatsapp-reply-preview')), findsNothing);

    await tester.drag(bubble, const Offset(120, 0));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('whatsapp-reply-cancel')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('whatsapp-reply-preview')), findsNothing);
    await close(tester);
  });

  testWidgets('holding a message reacts, replies or copies', (tester) async {
    await show(tester);
    final bubble = find.text('Etentje vrijdag om 19:00?');

    await tester.longPress(bubble);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('whatsapp-react-👍')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '$_piet/react').single.body, {
      'messageId': 'a',
      'emoji': '👍',
    });

    String? copied;
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async {
        if (call.method == 'Clipboard.setData') {
          copied = (call.arguments as Map)['text'] as String?;
        }
        return null;
      },
    );
    addTearDown(
      () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        null,
      ),
    );
    await tester.longPress(bubble);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('whatsapp-action-copy')));
    await tester.pumpAndSettle();
    expect(copied, 'Etentje vrijdag om 19:00?');

    await tester.longPress(bubble);
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('whatsapp-action-reply')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('whatsapp-reply-preview')), findsOneWidget);
    await close(tester);
  });

  testWidgets('voice notes show their waveform and length', (tester) async {
    final semantics = tester.ensureSemantics();
    await show(tester);
    expect(find.text('0:04'), findsOneWidget);
    expect(find.bySemanticsLabel('Open Voice message, 0:04'), findsOneWidget);
    await close(tester);
    semantics.dispose();
  });
}
