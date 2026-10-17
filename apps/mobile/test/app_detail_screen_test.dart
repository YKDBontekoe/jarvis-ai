import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/integrations/app_presentation.dart';
import 'package:jarvis_mobile/features/integrations/server_detail_screen.dart';
import 'package:jarvis_mobile/features/tiles/app_tiles.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:jarvis_mobile/features/tiles/tile_registry.dart';

import 'support/fixture_http.dart';

const _serverId = 'jarvis-mcp-0123456789abcdef0123456789abcdef';

Map<String, dynamic> _weatherServer({bool keySet = false}) => {
  'id': _serverId,
  'name': 'Weather',
  'endpoint': '',
  'transport': 'stdio',
  'enabled': true,
  'allowedTools': ['*'],
  'secrets': [
    {
      'name': 'weather_api_key',
      'label': 'Weather API key',
      'required': true,
      'isSet': keySet,
    },
  ],
};

Map<String, dynamic> _connection({String state = 'connected'}) => {
  'id': _serverId,
  'name': 'Weather',
  'state': state,
  'toolCount': 2,
  'tools': ['weather_current', 'weather_forecast'],
  'readOnlyTools': ['weather_current'],
  'selfBuilt': true,
  'hostManaged': false,
  'callsLast7Days': 4,
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/mcp-servers', [_weatherServer()]);
    http.on('GET', '/api/v1/integrations/connections', [_connection()]);
    http.on('GET', '/api/v1/custom-mcp-servers', [
      {'id': 'c1', 'registryServerId': _serverId},
    ]);
    http.on('GET', '/api/v1/custom-mcp-servers/c1', {
      'server': {
        'id': 'c1',
        'name': 'Weather',
        'description': 'Current weather and forecasts.',
        'revision': 2,
        'activeRevision': 1,
        'draftSha256': 'b' * 64,
        'activeSha256': 'a' * 64,
        'registryServerId': _serverId,
        'lastTest': {
          'ok': true,
          'sha256': 'b' * 64,
          'tools': ['weather_current', 'weather_forecast'],
        },
      },
      'draftSource': '# /// script\nprint("hi")',
      'activeSource': '# old',
      'revisions': [
        {
          'revision': 2,
          'sha256': 'b' * 64,
          'createdAt': '2026-10-15T10:00:00Z',
          'active': false,
        },
        {
          'revision': 1,
          'sha256': 'a' * 64,
          'createdAt': '2026-10-14T10:00:00Z',
          'active': true,
        },
      ],
    });
  });

  Future<void> show(WidgetTester tester, {TileLayoutController? tiles}) async {
    tester.view.physicalSize = const Size(900, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: McpServerDetailScreen(
          http: http.client(),
          serverId: _serverId,
          tiles: tiles,
          onAskInChat: (_) {},
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('shows health, keys, actions and the waiting draft', (
    tester,
  ) async {
    await show(tester);

    expect(find.text('Working'), findsOneWidget);
    expect(find.text('Built by Jarvis'), findsWidgets);
    expect(find.textContaining('Used 4 times this week'), findsOneWidget);
    expect(find.text('Weather API key'), findsOneWidget);
    expect(find.textContaining('Missing'), findsOneWidget);
    // Tool names drop the app prefix and say whether they ask first.
    expect(find.text('Current'), findsOneWidget);
    expect(find.text('Forecast'), findsOneWidget);
    expect(find.text('Only reads · can run without asking'), findsOneWidget);
    expect(
      find.textContaining('Version 2 is waiting for your OK'),
      findsOneWidget,
    );
    expect(find.textContaining('Test passed'), findsOneWidget);
  });

  testWidgets('turning one action off narrows an allow-all list', (
    tester,
  ) async {
    http.on('PUT', '/api/v1/mcp-servers/$_serverId/state', _weatherServer());
    await show(tester);

    await tester.tap(find.byKey(const Key('app-tool-weather_forecast')));
    await tester.pumpAndSettle();

    expect(
      http.sent('PUT', '/api/v1/mcp-servers/$_serverId/state').single.body,
      {
        'toolMode': 'replace',
        'allowedTools': ['weather_current'],
      },
    );
  });

  testWidgets('check reports the result in plain words', (tester) async {
    http.on('POST', '/api/v1/mcp-servers/$_serverId/test', {
      'ok': false,
      'state': 'unavailable',
      'toolCount': 0,
      'tools': <String>[],
      'errorKind': 'crashed',
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('app-detail-check')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('app-detail-test-result')), findsOneWidget);
    expect(find.text('The app stopped while starting.'), findsOneWidget);
  });

  testWidgets('approving sends the hash of the reviewed draft', (tester) async {
    http.on('POST', '/api/v1/custom-mcp-servers/c1/activate', {'id': 'c1'});
    await show(tester);

    await tester.tap(find.byKey(const Key('app-detail-approve')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Switch on').last);
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/custom-mcp-servers/c1/activate').single.body,
      {'sha256': 'b' * 64},
    );
  });

  testWidgets(
    'code that has not passed a test needs a reason before it is switched on',
    (tester) async {
      http.on('GET', '/api/v1/custom-mcp-servers/c1', {
        'server': {
          'id': 'c1',
          'name': 'Weather',
          'revision': 2,
          'activeRevision': 1,
          'draftSha256': 'b' * 64,
          'activeSha256': 'a' * 64,
          'registryServerId': _serverId,
          'lastTest': null,
        },
        'draftSource': '# /// script',
        'revisions': <Object>[],
      });
      http.on('POST', '/api/v1/custom-mcp-servers/c1/activate', {'id': 'c1'});
      await show(tester);

      await tester.tap(find.byKey(const Key('app-detail-approve')));
      await tester.pumpAndSettle();
      expect(find.text('This code has not passed a test'), findsOneWidget);
      await tester.enterText(
        find.byKey(const Key('app-override-reason')),
        'short',
      );
      await tester.tap(find.byKey(const Key('app-override-confirm')));
      await tester.pumpAndSettle();
      expect(
        http.sent('POST', '/api/v1/custom-mcp-servers/c1/activate'),
        isEmpty,
      );

      await tester.enterText(
        find.byKey(const Key('app-override-reason')),
        'It only does arithmetic and I read it.',
      );
      await tester.tap(find.byKey(const Key('app-override-confirm')));
      await tester.pumpAndSettle();

      expect(
        http.sent('POST', '/api/v1/custom-mcp-servers/c1/activate').single.body,
        {
          'sha256': 'b' * 64,
          'overrideReason': 'It only does arithmetic and I read it.',
        },
      );
    },
  );

  testWidgets(
    'the review says what changed and what the test did and did not prove',
    (tester) async {
      http.on('GET', '/api/v1/custom-mcp-servers/c1/review', {
        'firstVersion': false,
        'added': 12,
        'removed': 3,
        'dependenciesAdded': ['httpx==0.28.1'],
        'dependenciesRemoved': <Object>[],
        'keys': ['weather_api_key'],
        'testLabel': 'Startup test: the app starts and lists its tools. It does not prove each tool works.',
      });
      await show(tester);

      expect(
        find.textContaining(
          '+12 −3 lines since the live version · adds httpx==0.28.1 · uses weather_api_key',
        ),
        findsOneWidget,
      );
      expect(
        find.textContaining('does not prove each tool works'),
        findsOneWidget,
      );
    },
  );

  testWidgets('pins the app to Home as its own tile', (tester) async {
    final tiles = TileLayoutController();
    await show(tester, tiles: tiles);

    await tester.tap(find.byKey(const Key('app-detail-pin')));
    await tester.pumpAndSettle();

    final id = appTileId(_serverId, 'Weather');
    expect(tiles.contains(id), isTrue);
    expect(find.text('On Home'), findsOneWidget);
  });

  test('app tile ids round-trip and resolve to a spec that opens the app', () {
    final id = appTileId(_serverId, 'My Weather~App');
    final parsed = parseAppTileId(id);
    expect(parsed?.key, _serverId);
    expect(parsed?.name, 'My Weather~App');
    final spec = tileSpecFor(id);
    expect(spec?.destination, 'app:$_serverId');
    expect(spec?.supports(TileSize.square), isTrue);
    expect(parseAppTileId('app:~x'), isNull);
    expect(
      tileSpecFor(appTileId('host:GitHub', 'GitHub'))?.destination,
      'app:host:GitHub',
    );
  });

  test('app tiles summarise the connection', () async {
    final data = await loadAppTile(
      TileEnv(http: http.client(), briefing: () async => null),
      _serverId,
      'Weather',
    );
    expect(data?.stat, '2');
    expect(data?.subtitle, 'Used 4 times this week');
    expect(data?.attention, isFalse);
    expect(data?.rows.map((row) => row.text), ['Current', 'Forecast']);
  });

  test('tool names read as plain words', () {
    expect(
      humanizeAppTool('notion-search_pages', appName: 'Notion'),
      'Search pages',
    );
    expect(humanizeAppTool('createIssue'), 'Create issue');
    expect(humanizeAppTool('weather'), 'Weather');
  });

  testWidgets('a reply that builds an app shows how far it got', (
    tester,
  ) async {
    var opened = false;
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: ToolRunView(
            run: const ToolRunEntry([
              ToolStep('WriteCustomMcpServer', ToolStepStatus.completed),
              ToolStep('TestCustomMcpServer', ToolStepStatus.completed),
            ]),
            onOpenApps: () => opened = true,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('app-build-card')), findsOneWidget);
    expect(find.text('Your new app is ready for your OK'), findsOneWidget);
    await tester.tap(find.text('Review the code'));
    expect(opened, isTrue);
  });
}
