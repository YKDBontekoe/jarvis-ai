import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/whatsapp/whatsapp_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;
  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: WhatsAppScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('prefers the personal account and switches between accounts', (
    tester,
  ) async {
    http.on('GET', '/api/v1/channels', [
      {
        'id': 'jarvis',
        'kind': 'whatsapp_linked',
        'displayName': 'Jarvis',
        'account': '+31600000000',
        'allowedSenders': ['+31600000000'],
      },
      {
        'id': 'personal',
        'kind': 'whatsapp_linked',
        'displayName': 'My WhatsApp',
        'account': '+31612345678',
        'allowedSenders': <String>[],
      },
      {'id': 'cloud', 'kind': 'whatsapp', 'account': '12345678'},
    ]);
    http.on('GET', '/api/v1/channels/providers', {'whatsAppLink': true});
    http.on('GET', '/api/v1/channels/personal/chats', {
      'live': true,
      'state': 'open',
      'account': '+31612345678',
      'chats': [
        {
          'chatId': '+31622222222',
          'name': 'Sanne',
          'readAlong': true,
          'preview': 'See you tomorrow',
          'previewFromMe': false,
          'unreadCount': 3,
        },
      ],
    });
    http.on('GET', '/api/v1/channels/jarvis/chats', {
      'live': false,
      'state': 'logged_out',
      'account': '+31600000000',
      'chats': <Object>[],
    });
    await show(tester);
    expect(find.text('WhatsApp'), findsOneWidget);
    expect(find.textContaining('+31612345678 · Connected'), findsOneWidget);
    await tester.ensureVisible(find.text('See you tomorrow'));
    expect(
      find.byKey(const Key('whatsapp-unread-+31622222222')),
      findsOneWidget,
    );
    expect(http.sent('GET', '/api/v1/channels/jarvis/chats'), isEmpty);
    await tester.ensureVisible(
      find.byKey(const Key('whatsapp-account-picker')),
    );
    await tester.tap(find.byKey(const Key('whatsapp-account-picker')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Jarvis · +31600000000').last);
    await tester.pumpAndSettle();
    expect(find.textContaining('Link your account again'), findsOneWidget);
    expect(find.textContaining('needs to be linked again'), findsOneWidget);
    expect(http.sent('GET', '/api/v1/channels/jarvis/chats'), hasLength(1));
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets('connects a personal account directly from the empty inbox', (
    tester,
  ) async {
    http.on('GET', '/api/v1/channels', <Object>[]);
    http.on('GET', '/api/v1/channels/providers', {'whatsAppLink': true});
    http.on('POST', '/api/v1/channels/link', {}, status: 503);
    await show(tester);
    await tester.tap(find.byKey(const Key('whatsapp-connect-account')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/channels/link').single.body, {
      'kind': 'whatsapp_linked',
      'readAlong': true,
    });
    expect(find.byKey(const Key('channel-link-retry')), findsOneWidget);
    await tester.pumpWidget(const SizedBox.shrink());
  });

  testWidgets('filters selected chats and keeps selection on its own screen', (
    tester,
  ) async {
    http.on('GET', '/api/v1/channels', [
      {
        'id': 'personal',
        'kind': 'whatsapp_linked',
        'account': '+31612345678',
        'allowedSenders': <String>[],
      },
    ]);
    http.on('GET', '/api/v1/channels/providers', {'whatsAppLink': true});
    http.on('GET', '/api/v1/channels/personal/chats', {
      'live': true,
      'state': 'open',
      'account': '+31612345678',
      'chats': [
        {
          'chatId': 'sanne',
          'name': 'Sanne',
          'readAlong': true,
          'preview': 'Tomorrow?',
          'unreadCount': 2,
        },
        {
          'chatId': 'family@g.us',
          'name': 'Family',
          'readAlong': true,
          'isGroup': true,
          'preview': 'Sunday lunch',
        },
        {'chatId': 'piet', 'name': 'Piet', 'readAlong': false},
      ],
    });
    await show(tester);
    expect(find.text('Sanne'), findsOneWidget);
    expect(find.text('Family'), findsOneWidget);
    expect(find.text('Piet'), findsNothing);
    expect(find.byType(Switch), findsNothing);
    await tester.tap(find.byKey(const Key('whatsapp-filter-Unread')));
    await tester.pumpAndSettle();
    expect(find.text('Sanne'), findsOneWidget);
    expect(find.text('Family'), findsNothing);
    await tester.tap(find.byKey(const Key('whatsapp-filter-Groups')));
    await tester.pumpAndSettle();
    expect(find.text('Sanne'), findsNothing);
    expect(find.text('Family'), findsOneWidget);
    await tester.tap(find.byKey(const Key('whatsapp-choose-chats')));
    await tester.pumpAndSettle();
    expect(find.text('Piet'), findsOneWidget);
    expect(find.byKey(const Key('read-along-switch-piet')), findsOneWidget);
    await tester.pageBack();
    await tester.pumpAndSettle();
    expect(find.byType(Switch), findsNothing);
    expect(find.text('Family'), findsOneWidget);
    expect(tester.takeException(), isNull);
    await tester.pumpWidget(const SizedBox.shrink());
  });
}
