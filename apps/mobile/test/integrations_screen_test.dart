import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/integrations/integrations_screen.dart';

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

    expect(find.text('Calendar'), findsWidgets);
    expect(find.text('Paused'), findsOneWidget);
    // The host GitHub server gets its friendly name and plain status.
    expect(find.text('GitHub'), findsWidgets);
    expect(find.text('Working'), findsOneWidget);
    expect(find.textContaining('Jarvis can use 4 actions'), findsOneWidget);
    expect(find.textContaining('MCP'), findsNothing);

    // Technical details stay folded away until asked for.
    expect(find.text('Everything this app offers'), findsNothing);
    await tester.tap(find.text('Details').first);
    await tester.pumpAndSettle();
    expect(find.text('Everything this app offers'), findsOneWidget);
    expect(find.text('https://tools.example.net/mcp'), findsOneWidget);

    await tester.tap(
      find.descendant(
        of: find.byKey(const Key('app-jarvis-mcp-abc')),
        matching: find.byType(Switch),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      http.sent('PUT', '/api/v1/mcp-servers/jarvis-mcp-abc/state').single.body,
      {'enabled': true},
    );

    await tester.tap(
      find.descendant(
        of: find.byKey(const Key('app-github')),
        matching: find.byType(Switch),
      ),
    );
    await tester.pumpAndSettle();
    expect(http.sent('PUT', '/api/v1/mcp-controls/github').single.body, {
      'enabled': false,
    });
  });

  testWidgets('Ask Jarvis returns a chat prompt and leaves the page', (
    tester,
  ) async {
    http.on('GET', '/api/v1/integrations/credentials', <Object>[]);
    http.on('GET', '/api/v1/integrations/connections', <Object>[]);
    http.on('GET', '/api/v1/mcp-servers', <Object>[]);
    String? asked;
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => TextButton(
            onPressed: () {
              Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => IntegrationsScreen(
                    http: http.client(),
                    onAskInChat: (prompt) => asked = prompt,
                  ),
                ),
              );
            },
            child: const Text('Open'),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Add'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Ask Jarvis to set it up'));
    await tester.pumpAndSettle();
    expect(asked, contains('Show the setup card'));
    expect(find.text('Connected apps'), findsNothing);
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
    http.on('POST', '/api/v1/integrations/packs/calendar', {'installed': true});
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: IntegrationsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('Set up').first);
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

  testWidgets('a registered server shows its own live status and can retry', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    http.on('GET', '/api/v1/integrations/credentials', <Object>[]);
    http.on('GET', '/api/v1/integrations/connections', [
      {
        'id': 'jarvis-mcp-abc',
        'name': 'Files',
        'state': 'unavailable',
        'issue': 'connection_timed_out',
        'toolCount': 0,
      },
    ]);
    http.on('GET', '/api/v1/mcp-servers', [
      {
        'id': 'jarvis-mcp-abc',
        'name': 'Files',
        'endpoint': 'npx -y some-package',
        'allowedTools': ['*'],
        'hasToken': false,
        'enabled': true,
      },
    ]);
    await tester.pumpWidget(
      MaterialApp(home: IntegrationsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();

    expect(find.text('Can’t connect'), findsOneWidget);
    expect(find.textContaining('took too long to answer'), findsOneWidget);
    expect(find.text('1 needs you'), findsOneWidget);
    // The status lives on the server's own card, not in a second duplicate card.
    expect(find.text('Files'), findsOneWidget);
    final before = http.sent('GET', '/api/v1/integrations/connections').length;
    await tester.tap(find.text('Try again'));
    await tester.pumpAndSettle();
    expect(
      http.sent('GET', '/api/v1/integrations/connections').length,
      greaterThan(before),
    );
  });
}
