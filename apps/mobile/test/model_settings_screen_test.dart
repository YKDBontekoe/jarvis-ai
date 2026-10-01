import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/settings/model_settings_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _settings({
  String provider = 'codex',
  String? chatModel,
  String? embeddingModel,
  bool keyConfigured = false,
}) => {
  'provider': provider,
  'chatModel': chatModel,
  'fastModel': null,
  'reasoningEffort': null,
  'embeddingModel': embeddingModel,
  'openRouterKeyConfigured': keyConfigured,
  'providers': ['codex', 'openrouter'],
};

Map<String, Object?> _codex({
  String installed = '0.145.0',
  String latest = '0.145.0',
  bool updateAvailable = false,
  List<Map<String, Object?>> models = const [],
}) => {
  'installedVersion': installed,
  'latestVersion': latest,
  'updateAvailable': updateAvailable,
  'canUpdate': true,
  'usingManagedInstall': false,
  'updateBlockedReason': null,
  'error': null,
  'models': models,
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/settings/models/codex', _codex());
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: ModelSettingsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('switching to OpenRouter saves the key and chosen model', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on(
      'PUT',
      '/api/v1/settings/models/openrouter-key',
      _settings(keyConfigured: true),
    );
    http.on(
      'PUT',
      '/api/v1/settings/models',
      _settings(
        provider: 'openrouter',
        chatModel: 'anthropic/claude-sonnet-4.5',
        keyConfigured: true,
      ),
    );
    await show(tester);

    expect(find.text('OpenRouter API key (embeddings)'), findsOneWidget);
    await tester.tap(find.text('OpenRouter'));
    await tester.pumpAndSettle();
    expect(find.text('Not set'), findsOneWidget);

    await tester.enterText(
      find.byKey(const Key('openrouter-key')),
      'sk-or-secret',
    );
    await tester.tap(find.text('Save key'));
    await tester.pumpAndSettle();
    expect(find.text('Saved'), findsOneWidget);
    expect(
      http.sent('PUT', '/api/v1/settings/models/openrouter-key').single.body,
      {'value': 'sk-or-secret'},
    );

    await tester.enterText(
      find.byKey(const Key('chat-model')),
      'anthropic/claude-sonnet-4.5',
    );
    await tester.tap(find.byKey(const Key('save-models')));
    await tester.pumpAndSettle();

    final saved =
        http.sent('PUT', '/api/v1/settings/models').single.body
            as Map<String, dynamic>;
    expect(saved['provider'], 'openrouter');
    expect(saved['chatModel'], 'anthropic/claude-sonnet-4.5');
    expect(find.textContaining('Model settings saved'), findsOneWidget);
  });

  testWidgets('shows the server validation message when saving fails', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on('PUT', '/api/v1/settings/models', {
      'errors': {
        'provider': ['Save an OpenRouter API key before selecting OpenRouter.'],
      },
    }, status: 400);
    await show(tester);

    await tester.tap(find.text('OpenRouter'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('chat-model')), 'x/model');
    await tester.tap(find.byKey(const Key('save-models')));
    await tester.pumpAndSettle();

    expect(
      find.text('Save an OpenRouter API key before selecting OpenRouter.'),
      findsOneWidget,
    );
  });

  testWidgets(
    'Codex with embeddings can save an OpenRouter key and embedding model',
    (tester) async {
      http.on('GET', '/api/v1/settings/models', _settings());
      http.on(
        'PUT',
        '/api/v1/settings/models/openrouter-key',
        _settings(keyConfigured: true),
      );
      http.on(
        'PUT',
        '/api/v1/settings/models',
        _settings(
          provider: 'codex',
          keyConfigured: true,
          embeddingModel: 'openai/text-embedding-3-small',
        ),
      );
      await show(tester);

      await tester.enterText(
        find.byKey(const Key('openrouter-key')),
        'sk-or-secret',
      );
      await tester.tap(find.text('Save key'));
      await tester.pumpAndSettle();

      final embedding = find.widgetWithText(
        TextField,
        'Embedding model (optional)',
      );
      await tester.ensureVisible(embedding);
      await tester.enterText(embedding, 'openai/text-embedding-3-small');
      await tester.tap(find.byKey(const Key('save-models')));
      await tester.pumpAndSettle();

      final saved =
          http.sent('PUT', '/api/v1/settings/models').single.body
              as Map<String, dynamic>;
      expect(saved['provider'], 'codex');
      expect(saved['embeddingModel'], 'openai/text-embedding-3-small');
    },
  );

  testWidgets('shows that the local embedding model is active with progress', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on('GET', '/api/v1/settings/models/embedding', {
      'source': 'local',
      'model': 'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2',
      'activeMemories': 900,
      'embeddedMemories': 840,
    });
    await show(tester);

    final notice = find.byKey(const Key('embedding-status'));
    await tester.ensureVisible(notice);
    expect(notice, findsOneWidget);
    expect(find.textContaining('Local embedding model active'), findsOneWidget);
    expect(find.textContaining('840 of 900 memories indexed'), findsOneWidget);
  });

  testWidgets('warns when the embedding server does not answer', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on('GET', '/api/v1/settings/models/embedding', {
      'source': 'local',
      'model': 'sentence-transformers/paraphrase-multilingual-MiniLM-L12-v2',
      'activeMemories': 51,
      'embeddedMemories': 0,
      'reachable': false,
    });
    await show(tester);

    await tester.ensureVisible(find.byKey(const Key('embedding-status')));
    expect(find.textContaining('does not answer'), findsOneWidget);
    expect(find.textContaining('0 of 51 memories indexed'), findsOneWidget);
  });

  testWidgets('warns when no embedding model is active', (tester) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on('GET', '/api/v1/settings/models/embedding', {
      'source': 'none',
      'model': null,
      'activeMemories': 3,
      'embeddedMemories': 0,
    });
    await show(tester);

    final notice = find.byKey(const Key('embedding-status'));
    await tester.ensureVisible(notice);
    expect(find.textContaining('keywords only'), findsOneWidget);
  });

  testWidgets('hides the embedding status when the server has no endpoint', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    await show(tester);

    expect(find.byKey(const Key('embedding-status')), findsNothing);
  });

  testWidgets('connection test reports the answering model and latency', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on('POST', '/api/v1/settings/models/test', {
      'ok': true,
      'provider': 'codex',
      'model': 'gpt-5',
      'latencyMs': 842,
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('test-models')));
    await tester.pumpAndSettle();

    expect(find.text('Connected to gpt-5 in 842 ms.'), findsOneWidget);
  });

  testWidgets(
    'loads Codex models from the installed CLI and saves the choice',
    (tester) async {
      http.on('GET', '/api/v1/settings/models', _settings());
      http.on(
        'GET',
        '/api/v1/settings/models/codex',
        _codex(
          models: [
            {
              'id': 'gpt-5.4',
              'model': 'gpt-5.4',
              'displayName': 'GPT-5.4',
              'description': 'Default',
              'isDefault': true,
              'hidden': false,
              'supportsImages': true,
              'inputModalities': ['text', 'image'],
            },
            {
              'id': 'gpt-5.4-mini',
              'model': 'gpt-5.4-mini',
              'displayName': 'GPT-5.4 Mini',
              'description': 'Faster',
              'isDefault': false,
              'hidden': false,
              'supportsImages': false,
              'inputModalities': ['text'],
            },
          ],
        ),
      );
      http.on(
        'PUT',
        '/api/v1/settings/models',
        _settings(chatModel: 'gpt-5.4-mini'),
      );
      await show(tester);

      expect(find.textContaining('Installed 0.145.0'), findsOneWidget);
      await tester.ensureVisible(find.byKey(const Key('browse-codex-models')));
      await tester.tap(find.byKey(const Key('browse-codex-models')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('GPT-5.4 Mini'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('save-models')));
      await tester.pumpAndSettle();

      final saved =
          http.sent('PUT', '/api/v1/settings/models').single.body
              as Map<String, dynamic>;
      expect(saved['provider'], 'codex');
      expect(saved['chatModel'], 'gpt-5.4-mini');
    },
  );

  testWidgets('updates Codex when a newer release is published', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on(
      'GET',
      '/api/v1/settings/models/codex',
      _codex(latest: '0.157.0', updateAvailable: true),
    );
    http.on(
      'POST',
      '/api/v1/settings/models/codex/update',
      _codex(installed: '0.157.0', latest: '0.157.0'),
    );
    await show(tester);

    await tester.ensureVisible(find.byKey(const Key('update-codex')));
    await tester.tap(find.byKey(const Key('update-codex')));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/settings/models/codex/update'),
      hasLength(1),
    );
    expect(find.textContaining('Codex 0.157.0 is installed'), findsOneWidget);
    expect(find.text('Up to date'), findsOneWidget);
  });

  testWidgets('a non-object Codex payload does not crash model settings', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/models', _settings());
    http.on('GET', '/api/v1/settings/models/codex', <Object>[]);
    await show(tester);
    expect(tester.takeException(), isNull);
    expect(
      find.text(
        'Could not load the models supported by the installed Codex CLI.',
      ),
      findsOneWidget,
    );
  });
}
