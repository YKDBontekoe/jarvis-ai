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
  'embeddingModel': embeddingModel,
  'openRouterKeyConfigured': keyConfigured,
  'providers': ['codex', 'openrouter'],
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

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

  testWidgets('Codex with embeddings can save an OpenRouter key and embedding model', (
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

    await tester.enterText(
      find.text('Embedding model (optional)'),
      'openai/text-embedding-3-small',
    );
    await tester.tap(find.byKey(const Key('save-models')));
    await tester.pumpAndSettle();

    final saved =
        http.sent('PUT', '/api/v1/settings/models').single.body
            as Map<String, dynamic>;
    expect(saved['provider'], 'codex');
    expect(saved['embeddingModel'], 'openai/text-embedding-3-small');
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
}
