import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/knowing/knowing_screen.dart';
import 'package:jarvis_mobile/features/knowing/special_chats_screen.dart';
import 'package:jarvis_mobile/features/knowing/training_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'screenshots/fixtures.dart'
    show chatModes, knowledgeMap, trainingDeck, trustLadder;
import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp()
      ..on('GET', '/api/v1/knowing/training', trainingDeck())
      ..on('GET', '/api/v1/knowing/map', knowledgeMap())
      ..on('GET', '/api/v1/knowing/trust', trustLadder())
      ..on('GET', '/api/v1/chat-modes', chatModes());
  });

  Future<void> show(WidgetTester tester, Widget home) async {
    tester.view.physicalSize = const Size(900, 3200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(theme: buildJarvisTheme(), home: home));
    await tester.pumpAndSettle();
  }

  testWidgets('shows the score, the map and the trust ladder', (tester) async {
    await show(tester, KnowingScreen(http: http.client()));

    expect(find.text('79% right'), findsOneWidget);
    expect(find.text('42% still in fog'), findsOneWidget);
    expect(find.text('What does Lars do for work?'), findsOneWidget);
    expect(find.text('Sending WhatsApp messages'), findsOneWidget);
    expect(
      find.byKey(const Key('knowing-hand-over-whatsapp.send')),
      findsOneWidget,
    );
    expect(
      find.byKey(const Key('knowing-take-back-graph.change')),
      findsOneWidget,
    );
    // Learning rungs cannot be handed over yet.
    expect(
      find.byKey(const Key('knowing-hand-over-expenses.delete')),
      findsNothing,
    );
  });

  testWidgets('answering today’s question saves it to the map', (tester) async {
    http.on('POST', '/api/v1/knowing/map/answer', {
      'saved': true,
      'map': {...knowledgeMap(), 'fogPercent': 38, 'question': null},
    });
    await show(tester, KnowingScreen(http: http.client()));

    await tester.enterText(find.byKey(const Key('knowing-answer')), 'Nurse');
    await tester.tap(find.byKey(const Key('knowing-answer-save')));
    await tester.pumpAndSettle();

    final sent = http.sent('POST', '/api/v1/knowing/map/answer').single;
    expect(sent.body, {
      'entityId': 'ent-lars',
      'predicate': 'job_title',
      'answer': 'Nurse',
    });
    expect(find.text('38% still in fog'), findsOneWidget);
    expect(find.byKey(const Key('knowing-question')), findsNothing);
  });

  testWidgets('handing a category over asks first', (tester) async {
    http.on(
      'POST',
      '/api/v1/knowing/trust/whatsapp.send/hand-over',
      null,
      status: 204,
    );
    await show(tester, KnowingScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('knowing-hand-over-whatsapp.send')));
    await tester.pumpAndSettle();
    expect(find.text('Hand this over?'), findsOneWidget);
    await tester.tap(find.text('Keep asking'));
    await tester.pumpAndSettle();
    expect(
      http.sent('POST', '/api/v1/knowing/trust/whatsapp.send/hand-over'),
      isEmpty,
    );

    await tester.tap(find.byKey(const Key('knowing-hand-over-whatsapp.send')));
    await tester.pumpAndSettle();
    await tester.tap(
      find.descendant(
        of: find.byType(AlertDialog),
        matching: find.text('Hand it over'),
      ),
    );
    await tester.pumpAndSettle();
    expect(
      http.sent('POST', '/api/v1/knowing/trust/whatsapp.send/hand-over'),
      hasLength(1),
    );
  });

  testWidgets('training answers each card and ends with a summary', (
    tester,
  ) async {
    http
      ..on('POST', '/api/v1/knowing/training/mem-1', {
        'verdict': 'right',
        'stats': trainingDeck()['stats'],
      })
      ..on('POST', '/api/v1/knowing/training/mem-2', {
        'verdict': 'outdated',
        'stats': trainingDeck()['stats'],
      });
    await show(tester, TrainingScreen(http: http.client()));

    expect(
      find.text('You prefer to run in the morning before work.'),
      findsOneWidget,
    );
    await tester.tap(find.byKey(const Key('training-right')));
    await tester.pumpAndSettle();
    expect(find.text('Your sister Noor lives in Rotterdam.'), findsOneWidget);
    await tester.tap(find.byKey(const Key('training-outdated')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/knowing/training/mem-1').single.body, {
      'verdict': 'right',
    });
    expect(http.sent('POST', '/api/v1/knowing/training/mem-2').single.body, {
      'verdict': 'outdated',
    });
    expect(find.text('Right 1 of 2'), findsOneWidget);
  });

  testWidgets('starting the stranger opens its conversation', (tester) async {
    http.on('POST', '/api/v1/chat-modes', {
      'conversationId': 'c-new',
      'kind': 'stranger',
    });
    String? opened;
    await show(
      tester,
      SpecialChatsScreen(
        http: http.client(),
        onOpenConversation: (id) async => opened = id,
      ),
    );

    expect(find.text('Rehearsal with Rosa'), findsOneWidget);
    await tester.tap(find.byKey(const Key('special-stranger')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/chat-modes').single.body, {
      'kind': 'stranger',
    });
    expect(opened, 'c-new');
  });

  testWidgets('the council needs a question before it can sit', (tester) async {
    http.on('POST', '/api/v1/chat-modes', {
      'conversationId': 'c-council',
      'kind': 'council',
    });
    String? opened;
    await show(
      tester,
      SpecialChatsScreen(
        http: http.client(),
        onOpenConversation: (id) async => opened = id,
      ),
    );

    await tester.tap(find.byKey(const Key('special-council')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('council-start')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/chat-modes'), isEmpty);

    await tester.enterText(
      find.byKey(const Key('council-question')),
      'Move to Berlin?',
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('council-start')));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/chat-modes').single.body as Map;
    expect(body['kind'], 'council');
    expect(body['question'], 'Move to Berlin?');
    expect((body['advisers'] as List).length, 4);
    expect(opened, 'c-council');
  });
}
