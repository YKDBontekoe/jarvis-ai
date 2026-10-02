import 'dart:async';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/command_palette.dart';
import 'package:jarvis_mobile/features/search/intent_navigation.dart';
import 'package:jarvis_mobile/features/search/search_models.dart';
import 'package:jarvis_mobile/features/search/search_navigation.dart';
import 'package:jarvis_mobile/features/search/search_screen.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

Map<String, Object> action(
  String label,
  String kind,
  Map<String, String> params,
) => {
  'label': label,
  'description': 'Your next step',
  'route': {'kind': kind, 'parameters': params},
};

void main() {
  late FixtureHttp http;
  late List<String> destinations;
  late List<String> prompts;
  setUp(() {
    SharedPreferences.setMockInitialValues({});
    http = FixtureHttp();
    destinations = [];
    prompts = [];
    http.on('GET', '/api/v1/navigation', {'actions': <Object>[]});
    http.on('GET', '/api/v1/search', {'results': <Object>[]});
  });

  Future<void> show(WidgetTester tester, {bool palette = false}) async {
    tester.view.physicalSize = palette
        ? const Size(1000, 800)
        : const Size(390, 844);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () {
                if (palette) {
                  unawaited(
                    showJarvisCommandPalette(
                      context,
                      http: http.client(),
                      onConversation: (_) async {},
                      onUtility: destinations.add,
                      onAsk: (prompt) async => prompts.add(prompt),
                    ),
                  );
                } else {
                  Navigator.of(context).push<void>(
                    MaterialPageRoute(
                      builder: (_) => SearchScreen(
                        http: http.client(),
                        onConversation: (_) async {},
                        onUtility: destinations.add,
                        onAsk: (prompt) async => prompts.add(prompt),
                      ),
                    ),
                  );
                }
              },
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
  }

  for (final palette in [false, true]) {
    testWidgets(
      '${palette ? 'palette' : 'mobile'} interprets only on submission and opens after a tap',
      (tester) async {
        http.on('POST', '/api/v1/navigation/resolve', {
          'message': 'Here is your next step',
          'actions': [
            action('Plan my day', 'utility', {'destination': 'today'}),
          ],
        });
        await show(tester, palette: palette);
        await tester.enterText(
          find.byKey(const Key('navigation-request')),
          'What needs my attention?',
        );
        await tester.pump(const Duration(milliseconds: 300));
        await tester.pumpAndSettle();
        expect(http.sent('POST', '/api/v1/navigation/resolve'), isEmpty);
        await tester.tap(find.byKey(const Key('navigation-submit')));
        await tester.pumpAndSettle();
        expect(http.sent('POST', '/api/v1/navigation/resolve').single.body, {
          'request': 'What needs my attention?',
        });
        expect(destinations, isEmpty);
        await tester.tap(find.text('Plan my day'));
        await tester.pumpAndSettle();
        expect(destinations, ['today']);
        expect(find.byKey(const Key('navigation-request')), findsNothing);
        expect(tester.takeException(), isNull);
      },
    );
  }

  testWidgets(
    'assistant handoff preserves the request and waits for a deliberate tap',
    (tester) async {
      const prompt = 'Remind me to call Piet tomorrow';
      http.on('POST', '/api/v1/navigation/resolve', {
        'message': 'Jarvis can work on this with you.',
        'actions': [
          action('Work on this with Jarvis', 'assistant', {'prompt': prompt}),
        ],
      });
      await show(tester);
      await tester.enterText(
        find.byKey(const Key('navigation-request')),
        prompt,
      );
      await tester.tap(find.byKey(const Key('navigation-submit')));
      await tester.pumpAndSettle();
      expect(prompts, isEmpty);
      await tester.tap(find.text('Work on this with Jarvis'));
      await tester.pumpAndSettle();
      expect(prompts, [prompt]);
      expect(tester.takeException(), isNull);
    },
  );

  testWidgets('tools remain accessible when inference is unavailable', (
    tester,
  ) async {
    http.on('POST', '/api/v1/navigation/resolve', {}, status: 503);
    await show(tester);
    await tester.enterText(
      find.byKey(const Key('navigation-request')),
      'Show my spending',
    );
    await tester.tap(find.byKey(const Key('navigation-submit')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Could not work out'), findsOneWidget);
    await tester.tap(find.text('Browse tools'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Expenses'));
    await tester.tap(find.text('Expenses'));
    await tester.pumpAndSettle();
    expect(destinations, ['expenses']);
    expect(tester.takeException(), isNull);
  });

  testWidgets('grounded suggestions show actual unread counts', (tester) async {
    http.on('GET', '/api/v1/navigation', {
      'actions': [
        {
          'label': 'Catch up with Sanne',
          'description': '2 unread in Jarvis · +31612345678',
          'route': {
            'kind': 'whatsapp_chat',
            'parameters': {
              'connectionId': 'personal',
              'chatId': '+31611111111',
            },
          },
        },
      ],
    });
    await show(tester);
    expect(find.text('Catch up with Sanne'), findsOneWidget);
    expect(find.textContaining('2 unread in Jarvis'), findsOneWidget);
    expect(http.sent('POST', '/api/v1/navigation/resolve'), isEmpty);
  });

  testWidgets(
    'selected WhatsApp workflow drafts once and never sends automatically',
    (tester) async {
      const chats = '/api/v1/channels/personal/chats';
      const path = '$chats/%2B31611111111';
      const request = 'Help me reply to Sanne: tomorrow does not work';
      http.on('POST', '/api/v1/navigation/resolve', {
        'message': 'Your conversation',
        'actions': [
          action('Draft a reply to Sanne', 'whatsapp_chat', {
            'connectionId': 'personal',
            'chatId': '+31611111111',
            'workflow': 'draft',
            'request': request,
          }),
        ],
      });
      http.on('GET', chats, {
        'account': '+31612345678',
        'chats': [
          {'chatId': '+31611111111', 'name': 'Sanne', 'readAlong': true},
        ],
      });
      http.on('GET', '$chats/status', {
        'account': '+31612345678',
        'state': 'open',
      });
      http.on('GET', '$path/messages', [
        {
          'id': 'one',
          'text': 'Coffee tomorrow?',
          'fromMe': false,
          'sentAt': DateTime.now().toUtc().toIso8601String(),
        },
      ]);
      http.on('POST', '$path/read', {});
      http.on('POST', '$path/suggest', {
        'text': 'Tomorrow does not work, sorry!',
      });
      await show(tester);
      await tester.enterText(
        find.byKey(const Key('navigation-request')),
        request,
      );
      await tester.tap(find.byKey(const Key('navigation-submit')));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '$path/suggest'), isEmpty);
      await tester.tap(find.text('Draft a reply to Sanne'));
      await tester.pumpAndSettle();
      expect(find.text('Sanne'), findsOneWidget);
      expect(find.text('Tomorrow does not work, sorry!'), findsOneWidget);
      expect(http.sent('POST', '$path/suggest').single.body, {
        'instruction': request,
      });
      expect(http.sent('POST', '$path/send'), isEmpty);
      await tester.pump(const Duration(seconds: 6));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '$path/suggest'), hasLength(1));
      expect(http.sent('POST', '$path/send'), isEmpty);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );

  testWidgets('a chat switched off after resolution cannot trigger a draft', (
    tester,
  ) async {
    http.on('POST', '/api/v1/navigation/resolve', {
      'actions': [
        action('Draft a reply to Sanne', 'whatsapp_chat', {
          'connectionId': 'personal',
          'chatId': '+31611111111',
          'workflow': 'draft',
          'request': 'Reply to Sanne',
        }),
      ],
    });
    http.on('GET', '/api/v1/channels/personal/chats', {
      'chats': [
        {'chatId': '+31611111111', 'name': 'Sanne', 'readAlong': false},
      ],
    });
    await show(tester);
    await tester.enterText(
      find.byKey(const Key('navigation-request')),
      'Reply to Sanne',
    );
    await tester.tap(find.byKey(const Key('navigation-submit')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Draft a reply to Sanne'));
    await tester.pumpAndSettle();
    expect(find.textContaining('no longer selected'), findsOneWidget);
    expect(
      http.requests.where((request) => request.path.endsWith('/suggest')),
      isEmpty,
    );
    expect(tester.takeException(), isNull);
  });

  testWidgets(
    'a chat question opens Ask Jarvis and submits the original question once',
    (tester) async {
      const chats = '/api/v1/channels/personal/chats';
      const path = '$chats/%2B31611111111';
      const question = 'What did Piet say about Friday?';
      http.on('POST', '/api/v1/navigation/resolve', {
        'actions': [
          action('Ask about Piet', 'whatsapp_chat', {
            'connectionId': 'personal',
            'chatId': '+31611111111',
            'workflow': 'ask',
            'request': question,
          }),
        ],
      });
      http.on('GET', chats, {
        'account': '+31612345678',
        'chats': [
          {'chatId': '+31611111111', 'name': 'Piet', 'readAlong': true},
        ],
      });
      http.on('GET', '$chats/status', {'state': 'open'});
      http.on('GET', '$path/messages', <Object>[]);
      http.on('POST', '$path/ask', {
        'answer': 'Dinner at seven on Friday.',
        'needsApproval': false,
      });
      await show(tester);
      await tester.enterText(
        find.byKey(const Key('navigation-request')),
        question,
      );
      await tester.tap(find.byKey(const Key('navigation-submit')));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '$path/ask'), isEmpty);
      await tester.tap(find.text('Ask about Piet'));
      await tester.pumpAndSettle();
      expect(http.sent('POST', '$path/ask').single.body, {
        'question': question,
      });
      expect(find.text('Dinner at seven on Friday.'), findsOneWidget);
      expect(http.sent('POST', '$path/send'), isEmpty);
      expect(tester.takeException(), isNull);
      await tester.pumpWidget(const SizedBox.shrink());
    },
  );

  test('editing a request invalidates a late inference response', () async {
    final delayed = DelayedHttp();
    delayed.on('POST', '/api/v1/navigation/resolve', {
      'actions': [
        action('Old reply', 'utility', {'destination': 'whatsapp'}),
      ],
    });
    final controller = IntentNavigationController(delayed.client());
    final pending = controller.resolve('Reply to Sanne');
    await delayed.started.future;
    controller.changeQuery('Plan my day');
    delayed.release.complete();
    await pending;
    expect(controller.query, 'Plan my day');
    expect(controller.actions, isEmpty);
    expect(controller.loading, isFalse);
    controller.dispose();
  });

  testWidgets('arbitrary utility destinations are rejected by the client', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: Builder(
            builder: (context) => TextButton(
              onPressed: () => unawaited(
                navigateSearchRoute(
                  context,
                  http: http.client(),
                  route: const SearchRouteTarget(
                    kind: 'utility',
                    parameters: {'destination': 'sign_out'},
                  ),
                  onConversation: (_) async {},
                  onUtility: destinations.add,
                ),
              ),
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    expect(destinations, isEmpty);
  });
}

class DelayedHttp extends FixtureHttp {
  final started = Completer<void>();
  final release = Completer<void>();
  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    started.complete();
    await release.future;
    return super.fetch(options, requestStream, cancelFuture);
  }
}
