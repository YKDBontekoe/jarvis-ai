import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/channels/channels_screen.dart';
import 'package:jarvis_mobile/features/settings/settings_view.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _channel({
  String id = '11111111-1111-1111-1111-111111111111',
  String kind = 'whatsapp',
  String name = 'Home WhatsApp',
  bool enabled = true,
}) => {
  'id': id,
  'kind': kind,
  'displayName': name,
  'account': kind == 'whatsapp' ? '106540352242922' : '+31612345678',
  'enabled': enabled,
  'allowedSenders': ['+31612345678'],
  'forwardNotifications': true,
  'notifyRecipient': '+31612345678',
  'configuredSecrets': kind == 'whatsapp'
      ? ['access_token', 'app_secret', 'verify_token']
      : <String>[],
  'webhookUrl': kind == 'whatsapp'
      ? 'http://localhost:5082/api/v1/channels/whatsapp/abc/webhook'
      : null,
  'lastInboundAt': '2026-09-27T12:00:00Z',
  'lastOutboundAt': null,
  'lastError': null,
  'createdAt': '2026-09-27T10:00:00Z',
};

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

  testWidgets('lists WhatsApp and Signal connections from settings', (
    tester,
  ) async {
    http.on('GET', '/api/v1/channels', [
      _channel(),
      _channel(
        id: '22222222-2222-2222-2222-222222222222',
        kind: 'signal',
        name: 'Signal phone',
      ),
    ]);
    http.on('GET', '/api/v1/channels/signal/status', {
      'configured': true,
      'accounts': ['+31612345678'],
    });
    await show(tester, ChannelsScreen(http: http.client()));

    expect(find.text('Home WhatsApp'), findsOneWidget);
    expect(find.text('Signal phone'), findsOneWidget);
    expect(find.textContaining('WhatsApp'), findsWidgets);
    expect(find.textContaining('Signal'), findsWidgets);
  });

  testWidgets('connects WhatsApp with secrets and an allowlist', (
    tester,
  ) async {
    http.on('GET', '/api/v1/channels', <Object>[]);
    http.on('GET', '/api/v1/channels/signal/status', {
      'configured': false,
      'accounts': <String>[],
    });
    http.on('POST', '/api/v1/channels', _channel());
    await show(tester, ChannelsScreen(http: http.client()));

    await tester.tap(find.text('Connect a channel'));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('connect-whatsapp')));
    await tester.pumpAndSettle();

    await tester.enterText(
      find.byKey(const Key('channel-account')),
      '106540352242922',
    );
    await tester.enterText(
      find.byKey(const Key('channel-senders')),
      '+31612345678',
    );
    await tester.enterText(
      find.byKey(const Key('channel-access-token')),
      'EAAB',
    );
    await tester.enterText(
      find.byKey(const Key('channel-app-secret')),
      'app-secret',
    );
    await tester.enterText(
      find.byKey(const Key('channel-verify-token')),
      'verify-me',
    );
    await tester.tap(find.byKey(const Key('channel-save')));
    await tester.pumpAndSettle();

    final created = http.sent('POST', '/api/v1/channels').single;
    final body = created.body as Map;
    expect(body['kind'], 'whatsapp');
    expect(body['account'], '106540352242922');
    expect(body['allowedSenders'], ['+31612345678']);
    expect((body['secrets'] as Map)['access_token'], 'EAAB');
  });

  testWidgets('settings includes a WhatsApp & Signal destination', (
    tester,
  ) async {
    await show(
      tester,
      SettingsView(connected: true, onOpen: (_) {}),
    );
    expect(find.byKey(const Key('settings-channels')), findsOneWidget);
    expect(find.text('WhatsApp & Signal'), findsOneWidget);
  });
}
