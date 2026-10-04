import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/whatsapp/read_along_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_chat_screen.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_models.dart';
import 'package:jarvis_mobile/theme.dart';

import 'harness.dart';

const _channel = '11111111-1111-1111-1111-111111111111';

void main() {
  late _PictureHttp http;

  setUpAll(loadAppFonts);
  setUp(() {
    WhatsAppPictures.clear();
    http = _PictureHttp();
  });

  screenshotTest('whatsapp pictures', (tester) async {
    usePhone(tester);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RepaintBoundary(
          key: screenshotKey,
          child: ReadAlongScreen(
            http: http.client(),
            channelId: _channel,
            title: 'WhatsApp',
            selecting: false,
            account: '+31637355914',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Piet de Vries'), findsOneWidget);
    expect(
      find.byKey(const Key('whatsapp-picture-+31611111111')),
      findsOneWidget,
      reason: http.requests.join(' | '),
    );
    expect(
      find.byKey(const Key('whatsapp-picture-+31622222222')),
      findsOneWidget,
    );
    expect(
      find.byKey(const Key('whatsapp-picture-120363025-1@g.us')),
      findsOneWidget,
    );
    await _paint(tester);
    await capture(tester, 'whatsapp-chat-list');

    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RepaintBoundary(
          key: screenshotKey,
          child: WhatsAppChatScreen(
            http: http.client(),
            channelId: _channel,
            account: '+31637355914',
            pollInterval: const Duration(hours: 1),
            chat: const WhatsAppChat(
              chatId: '120363025-1@g.us',
              name: 'Familie',
              isGroup: true,
              readAlong: true,
              autoReminders: true,
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Sanne'), findsWidgets, reason: http.requests.join(' | '));
    expect(find.text('Piet'), findsWidgets);
    expect(
      find.byKey(const Key('whatsapp-picture-+31622222222')),
      findsWidgets,
    );
    expect(
      find.byKey(const Key('whatsapp-picture-+31611111111')),
      findsWidgets,
    );
    expect(
      find.byKey(const Key('whatsapp-picture-120363025-1@g.us')),
      findsOneWidget,
    );
    await _paint(tester);
    await capture(tester, 'whatsapp-group-messages');
  });
}

Future<void> _paint(WidgetTester tester) async {
  await tester.runAsync(() async {
    await Future<void>.delayed(const Duration(milliseconds: 150));
  });
  await tester.pump();
}

class _PictureHttp implements HttpClientAdapter {
  _PictureHttp() {
    pictures = {
      '+31611111111': File(
        'test/screenshots/avatars/piet.png',
      ).readAsBytesSync(),
      '+31622222222': File(
        'test/screenshots/avatars/sanne.png',
      ).readAsBytesSync(),
      '120363025-1@g.us': File(
        'test/screenshots/avatars/familie.png',
      ).readAsBytesSync(),
    };
  }

  late final Map<String, Uint8List> pictures;
  final requests = <String>[];

  Dio client() =>
      Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
        ..httpClientAdapter = this;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(
      '${options.path} ${options.queryParameters} ${options.responseType}',
    );
    if (options.path.endsWith('/picture')) {
      final bytes = pictures[options.queryParameters['subject']];
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
    if (options.path.endsWith('/messages')) {
      final sent = DateTime.now().toUtc();
      return _json([
        {
          'id': '00000000-0000-0000-0000-000000000003',
          'fromMe': true,
          'text': 'Ik neem de taart mee.',
          'sentAt': sent.toIso8601String(),
        },
        {
          'id': '00000000-0000-0000-0000-000000000002',
          'fromMe': false,
          'sender': 'Piet',
          'senderId': '+31611111111',
          'text': 'Ik ben er om zeven.',
          'sentAt': sent.subtract(const Duration(minutes: 2)).toIso8601String(),
        },
        {
          'id': '00000000-0000-0000-0000-000000000001',
          'fromMe': false,
          'sender': 'Sanne',
          'senderId': '+31622222222',
          'text': 'Eten we vrijdag bij oma?',
          'sentAt': sent.subtract(const Duration(minutes: 4)).toIso8601String(),
        },
      ]);
    }
    if (options.path.endsWith('/status')) {
      return _json({'account': '+31637355914', 'state': 'open'});
    }
    return _json({
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
          'preview': 'Ik ben er om zeven.',
          'lastMessageAt': DateTime.now().toUtc().toIso8601String(),
        },
        {
          'chatId': '120363025-1@g.us',
          'name': 'Familie',
          'isGroup': true,
          'readAlong': true,
          'autoReminders': true,
          'preview': 'Eten we vrijdag bij oma?',
          'lastMessageAt': DateTime.now()
              .toUtc()
              .subtract(const Duration(minutes: 4))
              .toIso8601String(),
        },
        {
          'chatId': '+31622222222',
          'name': 'Sanne',
          'isGroup': false,
          'readAlong': true,
          'autoReminders': true,
          'preview': 'Tot straks',
          'lastMessageAt': DateTime.now()
              .toUtc()
              .subtract(const Duration(hours: 2))
              .toIso8601String(),
        },
      ],
    });
  }

  ResponseBody _json(Object body) => ResponseBody.fromString(
    jsonEncode(body),
    200,
    headers: {
      Headers.contentTypeHeader: [Headers.jsonContentType],
    },
  );

  @override
  void close({bool force = false}) {}
}
