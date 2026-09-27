import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/agents/agents_screen.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/generative_ui.dart';
import 'package:jarvis_mobile/features/devices/devices_screen.dart';
import 'package:jarvis_mobile/features/settings/settings_view.dart';
import 'package:jarvis_mobile/features/settings/voice_settings_screen.dart';

import 'support/fixture_http.dart';

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

  testWidgets('renders a generative choice card and submits the tap', (
    tester,
  ) async {
    http.on('POST', '/api/v1/ui-surfaces/surf-1/actions', {
      'id': 'm1',
      'role': 'assistant',
      'content': 'Train it is.',
    });
    var submitted = false;
    await show(
      tester,
      Scaffold(
        body: UiSurfaceCard(
          surface: UiSurfaceEntry(
            id: 'surf-1',
            title: 'Pick a plan',
            status: 'open',
            schema: {
              'kind': 'choice',
              'title': 'Pick a plan',
              'body': 'How should we go?',
              'items': [
                {'id': 'train', 'title': 'Train'},
              ],
              'actions': [
                {'id': 'train', 'label': 'Train', 'style': 'primary'},
              ],
            },
          ),
          onAction: (action, values) async {
            submitted = action == 'train';
          },
        ),
      ),
    );

    expect(find.text('Pick a plan'), findsOneWidget);
    expect(find.text('Train'), findsWidgets);
    await tester.tap(find.widgetWithText(FilledButton, 'Train'));
    await tester.pumpAndSettle();
    expect(submitted, isTrue);
  });

  test('only the latest open card stays live', () {
    final entries = [
      const UiSurfaceEntry(
        id: 'old',
        title: 'Old',
        status: 'open',
        schema: {'kind': 'form', 'fields': [], 'actions': []},
      ),
      const UiSurfaceEntry(
        id: 'new',
        title: 'New',
        status: 'open',
        schema: {
          'kind': 'choice',
          'items': [
            {'id': 'a', 'title': 'A'},
          ],
        },
      ),
      const UiSurfaceEntry(
        id: 'done',
        title: 'Done',
        status: 'completed',
        schema: {},
      ),
    ];
    expect(liveSurface(entries)?.id, 'new');
    expect(surfaceAwaitsReply(entries[1]), isTrue);
    expect(surfaceAwaitsReply(entries[2]), isFalse);
  });

  testWidgets('a choice card requires a pick before sharing', (tester) async {
    String? action;
    Map<String, String>? values;
    await show(
      tester,
      Scaffold(
        body: UiSurfaceCard(
          surface: const UiSurfaceEntry(
            id: 'surf-2',
            title: 'What should I learn?',
            status: 'open',
            schema: {
              'kind': 'choice',
              'title': 'What should I learn?',
              'body': 'Pick something fun to share.',
              'items': [
                {'id': 'day', 'title': 'A normal day'},
                {'id': 'hobby', 'title': 'Free time'},
              ],
              'actions': [
                {'id': 'share', 'label': 'Share', 'style': 'secondary'},
              ],
            },
          ),
          onAction: (id, submitted) async {
            action = id;
            values = submitted;
          },
        ),
      ),
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Share'));
    await tester.pump();
    expect(action, isNull);
    expect(find.text('Pick one option first.'), findsOneWidget);

    await tester.tap(find.text('Free time'));
    await tester.pump();
    await tester.tap(find.widgetWithText(FilledButton, 'Share'));
    await tester.pump();
    expect(action, 'share');
    expect(values?['choice'], 'hobby');
    expect(values?['label'], 'Free time');
  });

  testWidgets('form answers stay put and empty shares do nothing', (
    tester,
  ) async {
    Map<String, String>? values;
    var submitted = false;
    Widget card() => UiSurfaceCard(
      key: const ValueKey('day-form'),
      surface: const UiSurfaceEntry(
        id: 'surf-3',
        title: 'About you',
        status: 'open',
        schema: {
          'kind': 'form',
          'title': 'About you',
          'fields': [
            {
              'id': 'day',
              'label': 'What does a normal day look like?',
              'type': 'text',
              'placeholder': 'Morning, work, evening…',
            },
          ],
          'actions': [
            {'id': 'share', 'label': 'Share', 'style': 'secondary'},
          ],
        },
      ),
      onAction: (_, submittedValues) async {
        submitted = true;
        values = submittedValues;
      },
    );

    Future<void> pump(String marker) => tester.pumpWidget(
      MaterialApp(
        home: Scaffold(body: Column(children: [Text(marker), card()])),
      ),
    );

    await pump('first');
    await tester.pumpAndSettle();
    await tester.tap(find.widgetWithText(FilledButton, 'Share'));
    await tester.pump();
    expect(submitted, isFalse);
    expect(find.text('Add an answer first.'), findsOneWidget);

    await tester.enterText(find.byType(TextField), 'I walk the dog');
    await pump('second');
    expect(find.text('I walk the dog'), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Share'));
    await tester.pump();
    expect(submitted, isTrue);
    expect(values?['day'], 'I walk the dog');
  });

  testWidgets('a used card collapses to one answered line', (tester) async {
    await show(
      tester,
      Scaffold(
        body: const UiSurfaceCard(
          surface: UiSurfaceEntry(
            id: 'surf-4',
            title: 'About you',
            status: 'completed',
            schema: {
              'kind': 'form',
              'title': 'About you',
              'fields': [
                {'id': 'day', 'label': 'What does a normal day look like?'},
              ],
              'actions': [
                {'id': 'share', 'label': 'Share', 'style': 'primary'},
              ],
            },
          ),
        ),
      ),
    );

    expect(find.text('Answered'), findsOneWidget);
    expect(find.text('Submitted'), findsNothing);
    expect(find.byType(FilledButton), findsNothing);
    expect(find.byType(TextField), findsNothing);
  });

  testWidgets('device settings save location and clipboard toggles', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/devices', {
      'location': false,
      'battery': true,
      'clipboard': false,
      'openUrl': true,
      'notify': true,
      'online': [
        {
          'name': 'Jarvis app',
          'capabilities': ['battery'],
          'lastSeenAt': '2026-09-27T12:00:00Z',
        },
      ],
    });
    http.on('PUT', '/api/v1/settings/devices', {
      'location': true,
      'battery': true,
      'clipboard': false,
      'openUrl': true,
      'notify': true,
      'online': <Object>[],
    });
    await show(tester, DevicesScreen(http: http.client()));
    expect(find.text('This device'), findsOneWidget);
    expect(find.text('Jarvis app'), findsOneWidget);
    await tester.tap(find.byType(Switch).first);
    await tester.pumpAndSettle();
    final saved = http.sent('PUT', '/api/v1/settings/devices').single.body as Map;
    expect(saved['location'], isTrue);
  });

  testWidgets('agents screen lists peers and inbound tokens', (tester) async {
    http.on('GET', '/api/v1/agents', [
      {
        'id': 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        'name': 'Travel',
        'url': 'https://travel.example/a2a',
        'enabled': true,
        'hasToken': true,
        'createdAt': '2026-09-27T10:00:00Z',
      },
    ]);
    http.on('GET', '/api/v1/a2a/tokens', [
      {
        'id': 'bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb',
        'name': 'Home',
        'createdAt': '2026-09-27T10:00:00Z',
      },
    ]);
    await show(tester, AgentsScreen(http: http.client()));
    expect(find.text('Travel'), findsOneWidget);
    expect(find.text('Home'), findsOneWidget);
  });

  testWidgets('settings includes agents, this-device, and voice destinations', (
    tester,
  ) async {
    await show(tester, SettingsView(connected: true, onOpen: (_) {}));
    expect(find.byKey(const Key('settings-agents')), findsOneWidget);
    expect(find.byKey(const Key('settings-devices')), findsOneWidget);
    expect(find.byKey(const Key('settings-voice-settings')), findsOneWidget);
  });

  testWidgets('voice settings save hands-free and caption toggles', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/voice', {
      'handsFree': true,
      'captions': true,
    });
    http.on('PUT', '/api/v1/settings/voice', {
      'handsFree': true,
      'captions': false,
    });
    await show(tester, VoiceSettingsScreen(http: http.client()));
    expect(find.text('Hands-free'), findsOneWidget);
    await tester.tap(find.byType(Switch).last);
    await tester.pumpAndSettle();
    final saved = http.sent('PUT', '/api/v1/settings/voice').single.body as Map;
    expect(saved['captions'], isFalse);
  });
}
