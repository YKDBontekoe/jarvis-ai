import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:shared_preferences/shared_preferences.dart';
import 'package:jarvis_mobile/api/api_config.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/settings/model_settings_screen.dart';
import 'package:jarvis_mobile/features/home/jarvis_home.dart'
    show debugHomeClock;
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';
import 'package:jarvis_mobile/ui/phosphor_icons.dart';
import 'package:jarvis_mobile/main.dart';
import 'package:jarvis_mobile/schedule_format.dart' show deviceTimeZoneLookup;

import 'fixtures.dart';
import 'proactive_fixtures.dart';
import 'harness.dart';

/// Renders the main screens with sample data into build/screenshots (or
/// $SCREENSHOT_DIR). Run: flutter test test/screenshots/app_screenshots.dart
void main() {
  late ScreenshotHttp http;

  setUpAll(loadAppFonts);
  setUp(() {
    mockPlatformChannels();
    deviceTimeZoneLookup = () async => 'Europe/Amsterdam';
    http = ScreenshotHttp(fixtureRoutes(withApproval: false));
    debugJarvisHttpAdapter = http;
  });
  tearDown(() {
    debugJarvisHttpAdapter = null;
    // ignore: avoid_print
    if (http.missing.isNotEmpty) print('MISSING ${http.missing.join('\n')}');
  });

  Widget app() => RepaintBoundary(
    key: screenshotKey,
    child: const JarvisApp(skipAuthentication: true),
  );

  Future<void> settle(WidgetTester tester) async {
    for (var i = 0; i < 30; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
  }

  Future<void> start(WidgetTester tester, {bool dark = false}) async {
    tester.platformDispatcher.platformBrightnessTestValue = dark
        ? Brightness.dark
        : Brightness.light;
    addTearDown(tester.platformDispatcher.clearPlatformBrightnessTestValue);
    await tester.pumpWidget(app());
    await settle(tester);
  }

  Future<void> openChat(WidgetTester tester) async {
    await tester.tap(find.byKey(const Key('tab-jarvis')));
    await settle(tester);
  }

  Future<void> openTab(WidgetTester tester, String name) async {
    await tester.tap(find.byKey(Key('tab-$name')));
    await settle(tester);
  }

  /// Opens a feature's page through Everything, as a person adding it would.
  Future<void> openFeature(WidgetTester tester, String id) async {
    await openTab(tester, 'everything');
    final tile = find.byKey(Key('everything-$id'));
    await tester.ensureVisible(tile);
    await settle(tester);
    await tester.tap(tile);
    await settle(tester);
    await tester.tap(find.byKey(const Key('tile-open')));
    await settle(tester);
  }

  for (final dark in [false, true]) {
    final mode = dark ? 'dark' : 'light';

    screenshotTest('play $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'play');
      expect(find.byKey(const Key('game-score')), findsOneWidget);
      await capture(tester, '40-play-$mode');
      final list = find.descendant(
        of: find.byKey(const Key('game-list')),
        matching: find.byType(Scrollable),
      );
      final quests = find.byKey(const Key('game-quest-q-boss'));
      await tester.scrollUntilVisible(quests, 200, scrollable: list.first);
      await Scrollable.ensureVisible(tester.element(quests), alignment: .05);
      await settle(tester);
      await capture(tester, '40-play-quests-$mode');
      final ghost = find.byKey(const Key('game-ghost'));
      await tester.scrollUntilVisible(ghost, 200, scrollable: list.first);
      await Scrollable.ensureVisible(tester.element(ghost), alignment: .1);
      await settle(tester);
      await capture(tester, '40-play-ghost-$mode');
    });

    screenshotTest('know me $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'know-me');
      expect(find.byKey(const Key('knowing-hero')), findsOneWidget);
      await capture(tester, '41-know-me-$mode');
      final list = find.descendant(
        of: find.byKey(const Key('knowing-list')),
        matching: find.byType(Scrollable),
      );
      final trust = find.byKey(const Key('knowing-trust-whatsapp.send'));
      await tester.scrollUntilVisible(trust, 200, scrollable: list.first);
      await Scrollable.ensureVisible(tester.element(trust), alignment: .3);
      await settle(tester);
      await capture(tester, '41-know-me-trust-$mode');
      await tester.scrollUntilVisible(
        find.byKey(const Key('knowing-train-start')),
        -200,
        scrollable: list.first,
      );
      await tester.tap(find.byKey(const Key('knowing-train-start')));
      await settle(tester);
      await capture(tester, '41-train-jarvis-$mode');
    });

    screenshotTest('talk differently $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'talk-differently');
      await capture(tester, '42-talk-differently-$mode');
      await tester.tap(find.byKey(const Key('special-council')));
      await settle(tester);
      await capture(tester, '42-council-sheet-$mode');
    });

    screenshotTest('discover $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'discover');
      expect(find.byKey(const Key('discover-collision')), findsOneWidget);
      await capture(tester, '43-discover-$mode');
      final list = find.descendant(
        of: find.byKey(const Key('discover-list')),
        matching: find.byType(Scrollable),
      );
      final duel = find.byKey(const Key('discover-duel-duel-1'));
      await tester.scrollUntilVisible(duel, 200, scrollable: list.first);
      await Scrollable.ensureVisible(tester.element(duel), alignment: .2);
      await settle(tester);
      await capture(tester, '43-discover-duels-$mode');
    });

    screenshotTest('museum $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'museum');
      await capture(tester, '44-museum-$mode');
      await tester.drag(
        find.byKey(const Key('museum-rooms')),
        const Offset(-400, 0),
      );
      await settle(tester);
      await capture(tester, '44-museum-best-days-$mode');
    });

    screenshotTest('ongoing work $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'proactive');
      await capture(tester, 'proactive-list-$mode');
      await tester.tap(
        find.byKey(const ValueKey('proactive-case-$proactiveCaseId')),
      );
      await settle(tester);
      await capture(tester, 'proactive-detail-$mode');
    });

    screenshotTest('ongoing work editor $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'proactive');
      await tester.tap(find.text('New responsibility'));
      await settle(tester);
      await capture(tester, 'proactive-editor-$mode');
      final settings = find.text('Checks and interruptions');
      await tester.ensureVisible(settings);
      await settle(tester);
      await tester.tap(settings);
      await settle(tester);
      await tester.ensureVisible(find.text('Maximum checks per day (1–96)'));
      await settle(tester);
      await capture(tester, 'proactive-settings-$mode');
    });

    screenshotTest('weekly capacity controls $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'proactive');
      final row = find.byKey(
        const ValueKey('proactive-case-$proactiveCapacityCaseId'),
      );
      await tester.ensureVisible(row);
      await settle(tester);
      await tester.tap(row);
      await settle(tester);
      await tester.tap(find.text('Edit responsibility'));
      await settle(tester);
      final settings = find.text('Checks and interruptions');
      await tester.ensureVisible(settings);
      await settle(tester);
      await tester.tap(settings);
      await settle(tester);
      await tester.ensureVisible(
        find.text("Include this week's calendar availability"),
      );
      await settle(tester);
      await capture(tester, 'proactive-calendar-$mode');
    });

    screenshotTest('trip delay controls $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openFeature(tester, 'proactive');
      final row = find.byKey(
        const ValueKey('proactive-case-$proactiveTripCaseId'),
      );
      await tester.ensureVisible(row);
      await settle(tester);
      await tester.tap(row);
      await settle(tester);
      await capture(tester, 'proactive-trip-$mode');
      await tester.tap(find.text('Edit responsibility'));
      await settle(tester);
      final add = find.text('Watch a trip change');
      await tester.ensureVisible(add);
      await settle(tester);
      await tester.tap(add);
      await settle(tester);
      await capture(tester, 'proactive-trip-monitor-$mode');
    });

    screenshotTest('home $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await capture(tester, '01-home-$mode');
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -700));
      await settle(tester);
      await capture(tester, '02-home-scrolled-$mode');
    });

    screenshotTest('chat $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openChat(tester);
      await capture(tester, '03-chat-$mode');
      await tester.enterText(
        find.byType(TextField).last,
        'Also find a good fado bar',
      );
      await settle(tester);
      await capture(tester, '04-chat-typing-$mode');
    });

    screenshotTest('chat question $mode', (tester) async {
      usePhone(tester);
      http.routes['GET /api/v1/conversations/$conversationId/surfaces'] = [
        questionSurface(),
      ];
      await start(tester, dark: dark);
      await openChat(tester);
      expect(find.text('Which tone do you want?'), findsOneWidget);
      expect(find.text('Ask Jarvis anything'), findsNothing);
      await capture(tester, '04b-chat-question-$mode');
      await tester.tap(find.byTooltip('Type an answer instead'));
      await settle(tester);
      expect(find.text('Options'), findsOneWidget);
      expect(find.text('Hard push'), findsNothing);
      await capture(tester, '04c-chat-question-typing-$mode');
      await tester.tap(find.text('Options'));
      await settle(tester);
      expect(find.text('Hard push'), findsOneWidget);

      http.routes['POST /api/v1/ui-surfaces/surf-tone/dismiss'] = {
        'id': 'surf-tone',
      };
      await tester.tap(find.byTooltip('Dismiss card'));
      await settle(tester);
      expect(find.text('Hard push'), findsNothing);
      expect(find.text('Dismissed'), findsOneWidget);
      expect(find.text('Ask Jarvis anything'), findsOneWidget);
      await capture(tester, '04d-chat-question-dismissed-$mode');
    });

    screenshotTest('chat question typed answer $mode', (tester) async {
      usePhone(tester);
      http.routes['GET /api/v1/conversations/$conversationId/surfaces'] = [
        questionSurface(),
      ];
      http.routes['POST /api/v1/ui-surfaces/surf-tone/dismiss'] = {
        'id': 'surf-tone',
      };
      http.routes['POST /api/v1/conversations/$conversationId/messages'] = {
        'id': 'typed-reply',
        'role': 'assistant',
        'content': 'Hard push it is. Shoes on.',
      };
      await start(tester, dark: dark);
      await openChat(tester);
      await tester.tap(find.byTooltip('Type an answer instead'));
      await settle(tester);
      await tester.enterText(find.byType(TextField).last, 'A hard push please');
      await settle(tester);
      await tester.tap(find.byTooltip('Send'));
      await settle(tester);
      expect(find.text('Options'), findsNothing);
      expect(find.text('Dismissed'), findsOneWidget);
      expect(find.text('Hard push it is. Shoes on.'), findsOneWidget);
      await capture(tester, '04e-chat-question-typed-$mode');
    });

    screenshotTest('chats $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openTab(tester, 'chats');
      await capture(tester, '05-chats-$mode');
    });

    screenshotTest('home why $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await tester.tap(find.byKey(const Key('home-right-now-why')));
      await settle(tester);
      await capture(tester, '01b-home-why-$mode');
      await tester.tap(find.byKey(const Key('home-why-not-now')));
      await settle(tester);
      expect(find.byKey(const Key('home-right-now')), findsNothing);
      await capture(tester, '01g-home-after-not-now-$mode');
    });

    screenshotTest('home noticed $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -420));
      await settle(tester);
      await capture(tester, '01c-home-noticed-$mode');
    });

    screenshotTest('home evening $mode', (tester) async {
      usePhone(tester);
      final now = DateTime.now();
      final evening = DateTime(now.year, now.month, now.day, 21, 40);
      debugHomeClock = () => evening;
      addTearDown(() => debugHomeClock = null);
      http.routes['GET /api/v1/home/context'] = homeContext(evening: true);
      await start(tester, dark: dark);
      await capture(tester, '01d-home-evening-$mode');
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -560));
      await settle(tester);
      await capture(tester, '01e-home-evening-scrolled-$mode');
    });

    screenshotTest('home day one $mode', (tester) async {
      usePhone(tester);
      http.routes['GET /api/v1/home/context'] = {
        'dayLine': 'Nothing on your plate today. A good day to get ahead.',
      };
      http.routes['GET /api/v1/channels'] = <Object>[];
      http.routes['GET /api/v1/home'] = {
        ...home(),
        'calendar': {'connected': false},
        'latestBriefing': null,
        'reminders': <Object>[],
        'approvals': <Object>[],
      };
      http.routes['GET /api/v1/briefings/daily'] = {
        'enabled': true,
        'localTime': '08:00:00',
        'timeZoneId': 'Europe/Amsterdam',
      };
      await start(tester, dark: dark);
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -300));
      await settle(tester);
      await capture(tester, '01f-home-day-one-$mode');
    });

    screenshotTest('chats all $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openTab(tester, 'chats');
      await tester.tap(find.byKey(const Key('chats-tab-all')));
      await settle(tester);
      await capture(tester, '05b-chats-all-$mode');
    });

    screenshotTest('chats preview $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openTab(tester, 'chats');
      await tester.longPress(
        find.byKey(const Key('chat-whatsapp:wa1:climb@g.us')),
      );
      await settle(tester);
      await capture(tester, '05c-chats-preview-$mode');
    });

    screenshotTest('chats preview person $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openTab(tester, 'chats');
      await tester.longPress(
        find.byKey(const Key('chat-whatsapp:wa1:+31633333333')),
      );
      await settle(tester);
      await capture(tester, '05d-chats-preview-person-$mode');
    });

    screenshotTest('everything $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openTab(tester, 'everything');
      await capture(tester, '31-everything-$mode');
      final tile = find.byKey(const Key('everything-finance'));
      await tester.ensureVisible(tile);
      await settle(tester);
      await tester.tap(tile);
      await settle(tester);
      await capture(tester, '32-tile-preview-$mode');
    });

    screenshotTest('edit home $mode', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      // Centred, so the tap lands clear of the floating dock.
      await Scrollable.ensureVisible(
        tester.element(find.byKey(const Key('home-edit'))),
        alignment: .5,
      );
      await settle(tester);
      await tester.tap(find.byKey(const Key('home-edit')));
      await settle(tester);
      await capture(tester, '33-edit-home-$mode');
      final cards = find.byKey(const Key('home-cards-editor'));
      await Scrollable.ensureVisible(tester.element(cards), alignment: .2);
      await settle(tester);
      await capture(tester, '33b-edit-home-cards-$mode');
    });

    screenshotTest('home tiles first $mode', (tester) async {
      usePhone(tester);
      http.routes['GET /api/v1/home/layout'] = {
        'tiles': [
          {'id': 'today', 'size': 'wide'},
          {'id': 'habits', 'size': 'square'},
          {'id': 'tasks', 'size': 'square'},
        ],
        'hiddenCards': <String>[],
        'sections': ['tiles', 'needs-you'],
        'revision': 1,
        'changedBy': 'app',
        'stored': true,
      };
      await start(tester, dark: dark);
      await tester.drag(find.byType(Scrollable).first, const Offset(0, -760));
      await settle(tester);
      await capture(tester, '33d-home-tiles-first-$mode');
    });

    screenshotTest('home jarvis change $mode', (tester) async {
      usePhone(tester);
      // This device last saw revision 1; the account is at 2, changed by Jarvis.
      SharedPreferences.setMockInitialValues({
        'home.tiles.meta.v1': '{"hidden":[],"revision":1,"dirty":false}',
      });
      http.routes['GET /api/v1/home/layout'] = {
        'tiles': [
          {'id': 'habits', 'size': 'wide'},
          {'id': 'today', 'size': 'wide'},
          {'id': 'tasks', 'size': 'square'},
          {'id': 'journal', 'size': 'square'},
          {'id': 'people', 'size': 'icon'},
          {'id': 'voice', 'size': 'icon'},
        ],
        'hiddenCards': ['brief'],
        'revision': 2,
        'changedBy': 'jarvis',
        'note': 'Habits and journal up front, finance moved off: you open them most in the evening',
        'canUndo': true,
        'stored': true,
      };
      await start(tester, dark: dark);
      final card = find.byKey(const Key('home-jarvis-change'));
      await Scrollable.ensureVisible(tester.element(card), alignment: .2);
      await settle(tester);
      await capture(tester, '33c-home-jarvis-change-$mode');
    });
  }

  screenshotTest('approval', (tester) async {
    usePhone(tester);
    http.routes.addAll(fixtureRoutes());
    await start(tester);
    await capture(tester, '11-approval');
  });

  screenshotTest('quick actions', (tester) async {
    usePhone(tester);
    await start(tester);
    await openChat(tester);
    await tester.tap(find.byTooltip('More actions'));
    await settle(tester);
    await capture(tester, '06-quick-actions');
  });

  for (final (index, label) in [
    (7, 'reminders'),
    (8, 'memory'),
    (9, 'tasks'),
    (10, 'habits'),
    (12, 'today'),
    (13, 'finance'),
    (14, 'people'),
    (15, 'journal'),
    (16, 'lists'),
  ]) {
    screenshotTest('page $label', (tester) async {
      usePhone(tester);
      await start(tester);
      await openFeature(tester, label);
      await capture(tester, '${index.toString().padLeft(2, '0')}-$label');
    });
  }

  screenshotTest('reminder swipe', (tester) async {
    usePhone(tester);
    await start(tester);
    await openFeature(tester, 'reminders');
    final gesture = await tester.startGesture(
      tester.getCenter(find.text('Call mum')),
    );
    for (var i = 0; i < 6; i++) {
      await gesture.moveBy(const Offset(22, 0));
      await tester.pump(const Duration(milliseconds: 16));
    }
    await capture(tester, '19-reminder-swipe');
    await gesture.up();
    await settle(tester);
  });

  for (final dark in [false, true]) {
    screenshotTest('components ${dark ? 'dark' : 'light'}', (tester) async {
      usePhone(tester);
      final input = TextEditingController(
        text: 'Find a quiet fado bar near Alfama',
      );
      addTearDown(input.dispose);
      await tester.pumpWidget(
        RepaintBoundary(
          key: screenshotKey,
          child: MaterialApp(
            debugShowCheckedModeBanner: false,
            theme: buildJarvisTheme(
              brightness: dark ? Brightness.dark : Brightness.light,
            ),
            home: Scaffold(
              body: SafeArea(
                child: Padding(
                  padding: const EdgeInsets.fromLTRB(18, 16, 18, 14),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      const MessageBubble(
                        message: MessageEntry(
                          role: 'user',
                          content:
                              'Find a quiet fado bar near Alfama for Saturday.',
                        ),
                      ),
                      ToolRunView(
                        run: const ToolRunEntry([
                          ToolStep('SearchMemory', ToolStepStatus.completed),
                          ToolStep('WebSearch', ToolStepStatus.running),
                        ]),
                        onOpenTasks: () {},
                      ),
                      const MessageBubble(
                        message: MessageEntry(
                          role: 'assistant',
                          content: '',
                          pending: true,
                        ),
                        thinkingLabel: 'Looking around Alfama',
                      ),
                      const MessageBubble(
                        message: MessageEntry(
                          role: 'assistant',
                          content: '**Tasca do Chico** in Bairro Alto keeps fado vadio nights — small, candle-lit',
                          pending: true,
                        ),
                      ),
                      const SizedBox(height: 8),
                      const StatusChip(
                        label: 'Realtime updates are offline',
                        color: Color(0xffd97706),
                        actionLabel: 'Retry',
                      ),
                      const Spacer(),
                      ChatComposer(
                        controller: input,
                        onSend: () {},
                        onCancel: () {},
                        onVoice: () {},
                        onAttach: () {},
                        onPhoto: () {},
                        sending: true,
                        voiceActive: false,
                        voiceStarting: false,
                      ),
                      const SizedBox(height: 12),
                      ChatComposer(
                        controller: TextEditingController(
                          text: 'Book it for 21:00',
                        ),
                        onSend: () {},
                        onVoice: () {},
                        onAttach: () {},
                        sending: false,
                        voiceActive: false,
                        voiceStarting: false,
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      );
      for (var i = 0; i < 8; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, '21-components-${dark ? 'dark' : 'light'}');
    });
  }

  // Settings → Models: the Codex CLI version is managed by Jarvis, so the card shows it with no update action.
  for (final (state, installed) in [
    ('managed', '0.167.0'),
    ('switching', '0.145.0'),
  ]) {
    for (final dark in [false, true]) {
      screenshotTest('model settings codex $state ${dark ? 'dark' : 'light'}', (
        tester,
      ) async {
        usePhone(tester);
        final routes = ScreenshotHttp({
          'GET /api/v1/settings/models': {
            'provider': 'codex',
            'chatModel': 'gpt-6.1-terra',
            'fastModel': null,
            'reasoningEffort': null,
            'embeddingModel': null,
            'openRouterKeyConfigured': false,
            'providers': ['codex', 'openrouter'],
          },
          'GET /api/v1/settings/models/codex': {
            'installedVersion': installed,
            'pinnedVersion': '0.167.0',
            'matchesPinnedVersion': installed == '0.167.0',
            'usingManagedInstall': false,
            'error': null,
            'models': const <Object>[],
          },
        });
        await tester.pumpWidget(
          RepaintBoundary(
            key: screenshotKey,
            child: MaterialApp(
              debugShowCheckedModeBanner: false,
              theme: buildJarvisTheme(
                brightness: dark ? Brightness.dark : Brightness.light,
              ),
              home: ModelSettingsScreen(
                http: Dio(BaseOptions(baseUrl: 'http://fixture'))
                  ..httpClientAdapter = routes,
              ),
            ),
          ),
        );
        for (var i = 0; i < 10; i++) {
          await tester.pump(const Duration(milliseconds: 100));
        }
        await capture(
          tester,
          '34-model-settings-codex-$state-${dark ? 'dark' : 'light'}',
        );
      });
    }
  }

  screenshotTest('empty state', (tester) async {
    usePhone(tester);
    await tester.pumpWidget(
      RepaintBoundary(
        key: screenshotKey,
        child: MaterialApp(
          debugShowCheckedModeBanner: false,
          theme: buildJarvisTheme(),
          home: Scaffold(
            appBar: AppBar(title: const Text('Journal')),
            body: EmptyState(
              icon: PhosphorIconsRegular.notebook,
              title: 'Nothing written yet',
              message:
                  'Tell Jarvis about your day and it keeps the notes here.',
              action: FilledButton(
                onPressed: () {},
                child: const Text('Write today'),
              ),
            ),
          ),
        ),
      ),
    );
    for (var i = 0; i < 8; i++) {
      await tester.pump(const Duration(milliseconds: 100));
    }
    await capture(tester, '22-empty-state');
  });

  for (final (index, action) in [
    (23, 'Set a reminder'),
    (24, 'Start a background task'),
    (25, 'Add a memory'),
  ]) {
    screenshotTest('create $action', (tester) async {
      usePhone(tester);
      await start(tester);
      await openChat(tester);
      await tester.tap(find.byTooltip('More actions'));
      await settle(tester);
      await tester.tap(find.text(action));
      await settle(tester);
      await capture(tester, '$index-create-${action.split(' ').last}');
    });
  }

  screenshotTest('quick create round trip', (tester) async {
    usePhone(tester);
    await start(tester);
    await openChat(tester);
    await tester.tap(find.byTooltip('More actions'));
    await settle(tester);
    await tester.tap(find.text('Set a reminder'));
    await settle(tester);
    await tester.enterText(find.byType(TextFormField).first, 'Book Ramiro');
    await tester.tap(find.text('Save'));
    await settle(tester);
    expect(find.text('Reminder set'), findsOneWidget);
    expect(find.text('Ask Jarvis anything'), findsOneWidget);
    await capture(tester, '26-after-quick-create');
  });

  screenshotTest('approval dock', (tester) async {
    usePhone(tester);
    http.routes.addAll(fixtureRoutes());
    await start(tester);
    await openChat(tester);
    await tester.drag(find.byType(Scrollable).first, const Offset(0, 900));
    await settle(tester);
    expect(find.text('Review'), findsOneWidget);
    await capture(tester, '27-approval-dock');
    await tester.tap(find.text('Review'));
    await settle(tester);
    await capture(tester, '28-approval-reviewed');
  });

  screenshotTest('search', (tester) async {
    usePhone(tester);
    await start(tester);
    await openTab(tester, 'chats');
    await tester.tap(find.byKey(const Key('chats-more')));
    await settle(tester);
    await tester.tap(find.text('Search everything'));
    await settle(tester);
    await capture(tester, '29-search');
    await tester.enterText(find.byType(TextField).first, 'herin');
    await settle(tester);
    await capture(tester, '30-search-typing');
  });

  screenshotTest('sign in', (tester) async {
    usePhone(tester);
    await tester.pumpWidget(
      RepaintBoundary(key: screenshotKey, child: const JarvisApp()),
    );
    await settle(tester);
    await capture(tester, '16-sign-in');
  });

  screenshotTest('settings', (tester) async {
    usePhone(tester);
    await start(tester);
    await openTab(tester, 'you');
    await capture(tester, '17-settings');
  });

  for (final dark in [false, true]) {
    screenshotTest('home learning ${dark ? 'dark' : 'light'}', (tester) async {
      usePhone(tester);
      await start(tester, dark: dark);
      final card = find.byKey(const Key('home-learning'));
      // Clear of the compact header that covers the top once Home scrolls.
      await Scrollable.ensureVisible(tester.element(card), alignment: .2);
      await settle(tester);
      await capture(tester, '01i-home-learning-${dark ? 'dark' : 'light'}');
    });
    screenshotTest('home suggestion row ${dark ? 'dark' : 'light'}', (
      tester,
    ) async {
      usePhone(tester);
      // A quieter day: Needs you lists the most urgent eight, so a suggestion only shows when there is room.
      http = ScreenshotHttp({
        ...fixtureRoutes(withApproval: false),
        'GET /api/v1/home': {
          ...home(),
          'approvals': <Object>[],
          'commitments': <Object>[],
          'decisions': <Object>[],
        },
      });
      debugJarvisHttpAdapter = http;
      await start(tester, dark: dark);
      final row = find.byKey(const Key('home-needs-you-improvement-imp1'));
      await Scrollable.ensureVisible(tester.element(row), alignment: .4);
      await settle(tester);
      await capture(tester, '01j-home-suggestion-${dark ? 'dark' : 'light'}');
    });
  }

  screenshotTest('home needs you at large text', (tester) async {
    usePhone(tester, size: const Size(320, 640));
    tester.platformDispatcher.textScaleFactorTestValue = 3;
    addTearDown(tester.platformDispatcher.clearTextScaleFactorTestValue);
    await start(tester);
    final needsYou = find.byKey(const Key('home-needs-you'));
    await tester.ensureVisible(needsYou);
    await settle(tester);
    expect(
      find.byKey(const Key('home-needs-you-stacked-actions')),
      findsWidgets,
    );
    await capture(tester, '01h-home-needs-you-large-text');
    final learning = find.byKey(const Key('home-learning'));
    await Scrollable.ensureVisible(tester.element(learning), alignment: .2);
    await settle(tester);
    await capture(tester, '01h-home-learning-large-text');
  });

  for (final dark in [false, true]) {
    screenshotTest('learning evolution ${dark ? 'dark' : 'light'}', (
      tester,
    ) async {
      usePhone(tester);
      await start(tester, dark: dark);
      await openTab(tester, 'you');
      final entry = find.byKey(const Key('settings-learning'));
      await tester.ensureVisible(entry);
      await settle(tester);
      await tester.tap(entry);
      await settle(tester);
      await tester.ensureVisible(
        find.byKey(const Key('learning-autonomousEvolution')),
      );
      await settle(tester);
      await capture(
        tester,
        '17b-learning-evolution-${dark ? 'dark' : 'light'}',
      );
    });
  }

  screenshotTest('voice', (tester) async {
    usePhone(tester);
    await start(tester, dark: true);
    await tester.tap(find.text('Voice'));
    await settle(tester);
    await capture(tester, '18-voice');
  });

  screenshotTest('desktop', (tester) async {
    useDesktop(tester);
    await start(tester);
    await openChat(tester);
    await capture(tester, '20-desktop-chat');
  });

  for (final dark in [false, true]) {
    final mode = dark ? 'dark' : 'light';
    screenshotTest('desktop chats $mode', (tester) async {
      useDesktop(tester);
      http.routes['GET /api/v1/people/p-rosa'] = {
        'person': {
          'id': 'p-rosa',
          'name': 'Rosa',
          'relationship': 'Partner',
          'daysUntilBirthday': 23,
          'turningAge': 31,
          'notes': 'Allergic to hazelnuts. Loves the Rijksmuseum on Sundays.',
        },
        'facts': [
          {'predicate': 'works_at', 'value': 'Booking.com, product design'},
          {'predicate': 'favourite_food', 'value': 'Ramen from Fou Fow'},
          {'predicate': 'training', 'value': 'Runs on Tuesday mornings'},
        ],
      };
      await start(tester, dark: dark);
      await openTab(tester, 'chats');
      await tester.tap(find.byKey(const Key('chat-whatsapp:wa1:+31633333333')));
      await settle(tester);
      expect(find.byKey(const Key('whatsapp-person-panel')), findsOneWidget);
      await capture(tester, '21-desktop-chats-about-$mode');
    });
  }
}
