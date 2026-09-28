import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
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

  test(
    'UiSurfaceEntry.fromJson keeps string schema keys and skips junk items',
    () {
      final surface = UiSurfaceEntry.fromJson(<dynamic, dynamic>{
        'id': 's1',
        'title': 'Hi',
        'status': 'open',
        'schema': <dynamic, dynamic>{
          1: 'ignored',
          'kind': 'choice',
          'items': [
            {'id': 'a', 'title': 'A'},
            'nope',
            3,
          ],
        },
      })!;
    expect(surface.schema['kind'], 'choice');
    expect(surface.schema.length, 2);
    expect(surfaceAwaitsReply(surface), isTrue);
    },
  );

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

  testWidgets('clears action spinners when the card is no longer interactive', (
    tester,
  ) async {
    final pending = Completer<void>();
    addTearDown(() {
      if (!pending.isCompleted) pending.complete();
    });
    await show(
      tester,
      Scaffold(
        body: UiSurfaceCard(
          key: const ValueKey('surf-busy'),
          surface: const UiSurfaceEntry(
            id: 'surf-busy',
            title: 'Pick a plan',
            status: 'open',
            schema: {
              'kind': 'choice',
              'title': 'Pick a plan',
              'actions': [
                {'id': 'train', 'label': 'Train', 'style': 'primary'},
              ],
              'items': [
                {'id': 'train', 'title': 'Train'},
              ],
            },
          ),
          onAction: (_, __) => pending.future,
        ),
      ),
    );

    await tester.tap(find.widgetWithText(FilledButton, 'Train'));
    await tester.pump();
    expect(find.byType(CircularProgressIndicator), findsOneWidget);

    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: UiSurfaceCard(
            key: const ValueKey('surf-busy'),
            surface: const UiSurfaceEntry(
              id: 'surf-busy',
              title: 'Pick a plan',
              status: 'completed',
              schema: {
                'kind': 'choice',
                'title': 'Pick a plan',
                'actions': [
                  {'id': 'train', 'label': 'Train', 'style': 'primary'},
                ],
                'items': [
                  {'id': 'train', 'title': 'Train'},
                ],
              },
            ),
          ),
        ),
      ),
    );
    await tester.pump();
    expect(find.byType(CircularProgressIndicator), findsNothing);
    pending.complete();
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

  testWidgets('choice field menus skip malformed option lists', (tester) async {
    Map<String, String>? values;
    await show(
      tester,
      Scaffold(
        body: UiSurfaceCard(
          surface: const UiSurfaceEntry(
            id: 'surf-options',
            title: 'Pick a color',
            status: 'open',
            schema: {
              'kind': 'form',
              'title': 'Pick a color',
              'fields': [
                {
                  'id': 'color',
                  'label': 'Color',
                  'type': 'choice',
                  'options': 'red',
                },
              ],
              'actions': [
                {'id': 'share', 'label': 'Share', 'style': 'primary'},
              ],
            },
          ),
          onAction: (_, submitted) async {
            values = submitted;
          },
        ),
      ),
    );

    expect(find.byType(DropdownButtonFormField<String>), findsOneWidget);
    await tester.tap(find.widgetWithText(FilledButton, 'Share'));
    await tester.pump();
    expect(values, isNull);
    expect(find.text('Choose an option in each menu.'), findsOneWidget);
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
    final saved =
        http.sent('PUT', '/api/v1/settings/devices').single.body as Map;
    expect(saved['location'], isTrue);
  });

  testWidgets('a failed device save restores the previous toggle', (
    tester,
  ) async {
    http.on('GET', '/api/v1/settings/devices', {
      'location': false,
      'battery': true,
      'clipboard': false,
      'openUrl': true,
      'notify': true,
      'online': <Object>[],
    });
    http.on('PUT', '/api/v1/settings/devices', {
      'message': 'Not saved.',
    }, status: 503);
    await show(tester, DevicesScreen(http: http.client()));
    final toggle = tester.widget<Switch>(find.byType(Switch).first);
    expect(toggle.value, isFalse);
    await tester.tap(find.byType(Switch).first);
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(tester.widget<Switch>(find.byType(Switch).first).value, isFalse);
    expect(find.text('Not saved.'), findsOneWidget);
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

  testWidgets('failed agent delete shows an error instead of crashing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/agents', [
      {
        'id': 'aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa',
        'name': 'Travel',
        'url': 'https://travel.example/a2a',
      },
    ]);
    http.on('GET', '/api/v1/a2a/tokens', <Object>[]);
    http.on('DELETE', '/api/v1/agents/aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa', {
      'message': 'Still in use.',
    }, status: 409);
    await show(tester, AgentsScreen(http: http.client()));
    await tester.tap(find.byTooltip('Remove'));
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.text('Still in use.'), findsOneWidget);
    expect(find.text('Travel'), findsOneWidget);
  });

  testWidgets('copying a new inbound token clears it from the screen', (
    tester,
  ) async {
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      SystemChannels.platform,
      (call) async => null,
    );
    addTearDown(
      () => tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        SystemChannels.platform,
        null,
      ),
    );
    http.on('GET', '/api/v1/agents', <Object>[]);
    http.on('GET', '/api/v1/a2a/tokens', <Object>[]);
    http.on('POST', '/api/v1/a2a/tokens', {
      'id': 'tok-1',
      'name': 'App 2026-09-28',
      'token': 'secret-a2a-token',
    });
    await show(tester, AgentsScreen(http: http.client()));
    await tester.tap(find.text('Create token'));
    await tester.pumpAndSettle();
    expect(find.textContaining('secret-a2a-token'), findsOneWidget);
    await tester.tap(find.text('Copy'));
    await tester.pumpAndSettle();
    expect(find.textContaining('secret-a2a-token'), findsNothing);
    expect(
      find.text('Token copied. It will not be shown again.'),
      findsOneWidget,
    );
  });

  testWidgets('cancelling add-agent disposes the dialog without leaking', (
    tester,
  ) async {
    http.on('GET', '/api/v1/agents', <Object>[]);
    http.on('GET', '/api/v1/a2a/tokens', <Object>[]);
    await show(tester, AgentsScreen(http: http.client()));
    await tester.tap(find.text('Add'));
    await tester.pumpAndSettle();
    expect(find.text('Add a remote agent'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(find.text('Add a remote agent'), findsNothing);
    expect(tester.takeException(), isNull);
  });

  testWidgets('settings includes agents, this-device, and voice destinations', (
    tester,
  ) async {
    await show(tester, SettingsView(connected: true, onOpen: (_) {}));
    expect(find.byKey(const Key('settings-usage')), findsOneWidget);
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
      'voice': 'cove',
      'defaultVoice': 'cove',
      'voices': [
        {'id': 'juniper', 'name': 'Juniper', 'isDefault': false},
        {'id': 'cove', 'name': 'Cove', 'isDefault': true},
      ],
    });
    http.on('PUT', '/api/v1/settings/voice', {
      'handsFree': true,
      'captions': false,
      'voice': 'cove',
      'defaultVoice': 'cove',
      'voices': [
        {'id': 'juniper', 'name': 'Juniper', 'isDefault': false},
        {'id': 'cove', 'name': 'Cove', 'isDefault': true},
      ],
    });
    await show(tester, VoiceSettingsScreen(http: http.client()));
    expect(find.text('Cove'), findsOneWidget);
    expect(find.text('Codex default'), findsOneWidget);
    await tester.tap(find.byType(Switch).last);
    await tester.pumpAndSettle();
    final saved = http.sent('PUT', '/api/v1/settings/voice').single.body as Map;
    expect(saved['captions'], isFalse);
    expect(saved['voice'], 'cove');
  });
}
