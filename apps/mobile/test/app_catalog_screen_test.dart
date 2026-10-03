import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/integrations/app_catalog_screen.dart';

import 'support/fixture_http.dart';

const _brave = {
  'name': 'io.github.brave/brave-search-mcp-server',
  'title': 'Brave Search',
  'description': 'Web results, images, and news.',
  'version': '2.1.3',
  'options': [
    {
      'id': 'package-0',
      'kind': 'npm',
      'summary': '@brave/brave-search-mcp-server@2.1.3',
      'secrets': [
        {
          'name': 'brave_api_key',
          'label': 'BRAVE_API_KEY',
          'description': 'Your API key',
          'required': true,
        },
      ],
    },
  ],
};

void main() {
  late FixtureHttp http;
  AppInstallOutcome? outcome;

  setUp(() {
    http = FixtureHttp();
    outcome = null;
  });

  Future<void> show(WidgetTester tester, {bool address = false}) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () async {
                outcome = await Navigator.of(context).push<AppInstallOutcome>(
                  MaterialPageRoute(
                    builder: (_) => AppCatalogScreen(
                      http: http.client(),
                      startWithAddress: address,
                    ),
                  ),
                );
              },
              child: const Text('open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  testWidgets('finds an app by name, installs it, and saves its key', (
    tester,
  ) async {
    http.on('GET', '/api/v1/mcp-catalog?search=brave&limit=20', {
      'servers': [_brave],
    });
    http.on('POST', '/api/v1/mcp-catalog/install', {
      'server': {
        'id': 'jarvis-mcp-1',
        'name': 'Brave Search',
        'endpoint': '',
        'secrets': [
          {
            'name': 'brave_api_key',
            'label': 'BRAVE_API_KEY',
            'description': 'Your API key',
            'required': true,
            'isSet': false,
          },
        ],
      },
      'nextStep': 'secrets',
    });
    http.on(
      'PUT',
      '/api/v1/integrations/jarvis-mcp-1/credentials/brave_api_key',
      {'provider': 'jarvis-mcp-1'},
    );
    await show(tester);

    expect(find.text('Notion'), findsOneWidget);
    await tester.enterText(
      find.byKey(const Key('app-catalog-search')),
      'brave',
    );
    await tester.pump(const Duration(milliseconds: 400));
    await tester.pumpAndSettle();

    expect(find.text('Brave Search'), findsOneWidget);
    expect(find.textContaining('Runs on your Jarvis server'), findsOneWidget);
    expect(find.textContaining('MCP'), findsNothing);

    await tester.tap(find.text('Brave Search'));
    await tester.pumpAndSettle();
    expect(
      find.textContaining('downloads @brave/brave-search-mcp-server@2.1.3'),
      findsOneWidget,
    );
    await tester.tap(find.byKey(const Key('app-catalog-connect')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/mcp-catalog/install').single.body, {
      'name': 'io.github.brave/brave-search-mcp-server',
      'option': 'package-0',
    });

    // The key is required: saving empty is refused.
    await tester.tap(find.byKey(const Key('app-keys-save')));
    await tester.pumpAndSettle();
    expect(find.text('Fill in BRAVE_API_KEY.'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('app-key-brave_api_key')),
      'secret-key',
    );
    await tester.tap(find.byKey(const Key('app-keys-save')));
    await tester.pumpAndSettle();

    expect(
      http
          .sent(
            'PUT',
            '/api/v1/integrations/jarvis-mcp-1/credentials/brave_api_key',
          )
          .single
          .body,
      {'value': 'secret-key'},
    );
    expect(outcome?.serverId, 'jarvis-mcp-1');
    expect(outcome?.nextStep, 'ready');
  });

  testWidgets('a pasted address that needs sign-in hands that back', (
    tester,
  ) async {
    http.on('POST', '/api/v1/mcp-servers/connect', {
      'server': {
        'id': 'jarvis-mcp-2',
        'name': 'Notion',
        'endpoint': 'https://mcp.notion.com/mcp',
      },
      'nextStep': 'sign_in',
    });
    await show(tester, address: true);

    await tester.enterText(find.byKey(const Key('app-address')), 'http://x');
    await tester.tap(find.text('Connect'));
    await tester.pumpAndSettle();
    expect(
      find.text('Paste an address that starts with https://'),
      findsOneWidget,
    );

    await tester.enterText(
      find.byKey(const Key('app-address')),
      'https://mcp.notion.com/mcp',
    );
    await tester.tap(find.text('Connect'));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/mcp-servers/connect').single.body, {
      'endpoint': 'https://mcp.notion.com/mcp',
    });
    expect(outcome?.nextStep, 'sign_in');
    expect(outcome?.endpoint, 'https://mcp.notion.com/mcp');
  });

  testWidgets('says so when the app directory is not answering', (
    tester,
  ) async {
    http.on('GET', '/api/v1/mcp-catalog?search=Notion&limit=20', {
      'error': 'catalog_unavailable',
      'message': 'The app catalog is not answering right now. Try again soon.',
    }, status: 503);
    await show(tester);

    await tester.tap(find.text('Notion'));
    await tester.pumpAndSettle();

    expect(
      find.text('The app catalog is not answering right now. Try again soon.'),
      findsOneWidget,
    );
  });
}
