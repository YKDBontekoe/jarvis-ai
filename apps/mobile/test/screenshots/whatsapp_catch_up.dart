import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/whatsapp/read_along_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_chat_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_models.dart';
import 'package:jarvis_mobile/theme.dart';

import '../support/fixture_http.dart';
import 'harness.dart';

const _channel = '11111111-1111-1111-1111-111111111111';
const _chats = '/api/v1/channels/$_channel/chats';

const _catchUp = {
  'summary':
      'Piet wil vrijdag samen eten en stelt Italiaans bij de haven voor. Hij vraagt om een tijd.',
  'toReply': [
    {'who': 'Piet', 'about': 'Welke tijd past je vrijdag?'},
    {'who': 'Sanne', 'about': 'Kom je zondag ook langs?'},
  ],
  'messageCount': 4,
};

void main() {
  setUpAll(loadAppFonts);

  screenshotTest('whatsapp catch up', (tester) async {
    usePhone(tester);
    final http = FixtureHttp();
    final now = DateTime.now().toUtc();
    String ago(int minutes) =>
        now.subtract(Duration(minutes: minutes)).toIso8601String();
    http.on('GET', _chats, {
      'live': true,
      'account': '+31637355914',
      'state': 'open',
      'chats': [
        {
          'chatId': '+31611111111',
          'name': 'Piet de Vries',
          'isGroup': false,
          'readAlong': true,
          'autoReminders': true,
          'unreadCount': 2,
          'preview': 'Welke tijd past je vrijdag?',
          'lastMessageAt': ago(4),
          'catchUp': {
            'summary': _catchUp['summary'],
            'toReply': [(_catchUp['toReply']! as List).first],
            'messageCount': 2,
          },
        },
        {
          'chatId': '+31622222222',
          'name': 'Sanne',
          'isGroup': false,
          'readAlong': true,
          'autoReminders': true,
          'unreadCount': 2,
          'preview': 'Kom je zondag ook langs?',
          'lastMessageAt': ago(9),
          'catchUp': {
            'summary': 'Sanne stuurde de foto’s van afgelopen weekend.',
            'toReply': [(_catchUp['toReply']! as List).last],
            'messageCount': 2,
          },
        },
        {
          'chatId': '120363025-1@g.us',
          'name': 'Familie',
          'isGroup': true,
          'readAlong': true,
          'autoReminders': true,
          'preview': 'Tot zondag!',
          'lastMessageAt': ago(60),
        },
      ],
    });
    http.on('GET', '$_chats/status', {
      'account': '+31637355914',
      'state': 'open',
    });
    http.on('GET', '$_chats/open/messages', [
      {
        'id': 'm4',
        'text': 'Welke tijd past je vrijdag?',
        'sender': 'Piet',
        'fromMe': false,
        'sentAt': ago(4),
      },
      {
        'id': 'm3',
        'text': 'Ik zat te denken aan Italiaans bij de haven',
        'sender': 'Piet',
        'fromMe': false,
        'sentAt': ago(5),
      },
      {
        'id': 'm2',
        'text': 'Misschien, hangt van werk af',
        'fromMe': true,
        'sentAt': ago(40),
      },
      {
        'id': 'm1',
        'text': 'Etentje vrijdag?',
        'sender': 'Piet',
        'fromMe': false,
        'sentAt': ago(45),
      },
    ]);

    Future<void> show(Widget screen) async {
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: RepaintBoundary(key: screenshotKey, child: screen),
        ),
      );
      await tester.pumpAndSettle();
    }

    await show(
      ReadAlongScreen(
        http: http.client(),
        channelId: _channel,
        title: 'WhatsApp',
        selecting: false,
        account: '+31637355914',
      ),
    );
    expect(find.byKey(const Key('whatsapp-catch-up-digest')), findsOneWidget);
    await capture(tester, 'whatsapp-catch-up-digest');

    await show(
      WhatsAppChatScreen(
        http: http.client(),
        channelId: _channel,
        account: '+31637355914',
        pollInterval: const Duration(hours: 1),
        chat: WhatsAppChat.fromJson({
          'chatId': '+31611111111',
          'name': 'Piet de Vries',
          'readAlong': true,
          'catchUp': {
            'summary': _catchUp['summary'],
            'toReply': [(_catchUp['toReply']! as List).first],
            'messageCount': 2,
          },
        })!,
      ),
    );
    expect(find.byKey(const Key('whatsapp-catch-up')), findsOneWidget);
    await capture(tester, 'whatsapp-catch-up-card');
  });
}
