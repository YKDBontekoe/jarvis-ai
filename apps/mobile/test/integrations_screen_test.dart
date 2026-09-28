import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/integrations_screen.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    http.on('GET', '/api/v1/integrations/credentials', <Object>[]);
    http.on('GET', '/api/v1/integrations/connections', [
      {'name': 'github', 'state': 'connected', 'toolCount': 4, 'enabled': true},
    ]);
    http.on('GET', '/api/v1/mcp-servers', [
      {
        'id': 'jarvis-mcp-abc',
        'name': 'Calendar',
        'endpoint': 'https://tools.example.net/mcp',
        'allowedTools': ['*'],
        'hasToken': false,
        'enabled': false,
      },
    ]);
    http.on('PUT', '/api/v1/mcp-servers/jarvis-mcp-abc/state', {
      'id': 'jarvis-mcp-abc',
      'enabled': true,
    });
    http.on('PUT', '/api/v1/mcp-controls/github', {
      'name': 'github',
      'enabled': false,
    });
    await tester.pumpWidget(
      MaterialApp(home: IntegrationsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('pauses a host server and resumes a managed server', (
    tester,
  ) async {
    await show(tester);

    expect(find.text('Calendar'), findsOneWidget);
    expect(find.text('Paused'), findsWidgets);
    expect(find.text('Every exposed tool'), findsOneWidget);
    expect(find.text('Pause'), findsOneWidget);

    await tester.tap(find.byType(Switch));
    await tester.pumpAndSettle();
    expect(
      http.sent('PUT', '/api/v1/mcp-servers/jarvis-mcp-abc/state').single.body,
      {'enabled': true},
    );

    await tester.tap(find.text('Pause'));
    await tester.pumpAndSettle();
    expect(http.sent('PUT', '/api/v1/mcp-controls/github').single.body, {
      'enabled': false,
    });
  });

  testWidgets('guided calendar pack posts an ICS URL without MCP', (
    tester,
  ) async {
    http.on('GET', '/api/v1/integrations/credentials', <Object>[]);
    http.on('GET', '/api/v1/integrations/connections', <Object>[]);
    http.on('GET', '/api/v1/mcp-servers', <Object>[]);
    http.on('GET', '/api/v1/integrations/packs', [
      {
        'pack': {
          'id': 'calendar',
          'name': 'Calendar',
          'category': 'calendar',
          'description': 'Subscribe to an ICS feed.',
          'authKind': 'ics',
          'supportsIcs': true,
        },
        'installed': false,
        'hasToken': false,
        'hasIcs': false,
      },
    ]);
    http.on('POST', '/api/v1/integrations/packs/calendar', {
      'installed': true,
    });
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: IntegrationsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Set up'));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byType(TextFormField).first,
      'https://calendar.example.com/basic.ics',
    );
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();
    expect(
      http.sent('POST', '/api/v1/integrations/packs/calendar').single.body,
      {'icsUrl': 'https://calendar.example.com/basic.ics'},
    );
  });
}
