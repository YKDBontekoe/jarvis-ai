import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/automations/automation_studio_screen.dart';
import 'package:jarvis_mobile/features/automations/automations_screen.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/automations/templates', [
      {
        'id': 'urgent-message',
        'title': 'Urgent message alert',
        'description': 'Notifies you about urgent chats.',
        'category': 'Messages',
        'trigger': 'A message arrives and mentions "urgent"',
        'actions': 1,
        'needsApproval': false,
      },
      {
        'id': 'file-summary',
        'title': 'Summarize uploaded files',
        'description': 'Summarizes every upload.',
        'category': 'Files',
        'trigger': 'A file is uploaded',
        'actions': 1,
        'needsApproval': true,
      },
    ]);
    http.on('GET', '/api/v1/automations/webhooks', [
      {
        'id': 'w1',
        'name': 'CI server',
        'tokenHint': 'ab12',
        'useCount': 3,
        'createdAt': '2026-10-01T10:00:00Z',
      },
    ]);
    http.on('GET', '/api/v1/automations', [
      {
        'id': 'r1',
        'name': 'Urgent alert',
        'status': 'enabled',
        'definition': {
          'trigger': {'kind': 'event', 'eventKind': 'message_received'},
          'actions': <Object>[],
        },
      },
    ]);
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: AutomationStudioScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  test('event triggers are described in plain words', () {
    expect(
      describeTrigger({'kind': 'event', 'eventKind': 'webhook'}),
      'A webhook is called',
    );
    expect(
      describeTrigger({
        'kind': 'event',
        'eventKind': 'message_received',
        'contains': 'urgent',
      }),
      contains('mentions “urgent”'),
    );
    expect(triggerIcon('event'), isNotNull);
  });

  test('simulations parse steps and skip malformed ones', () {
    final data = SimulationData.fromJson({
      'trigger': 'A file is uploaded',
      'triggerNote': null,
      'steps': [
        {
          'label': 'Send you a notification',
          'willRun': true,
          'needsApproval': false,
          'title': 'Hi',
        },
        {'nope': 1},
      ],
    })!;
    expect(data.steps, hasLength(1));
    expect(data.steps.single.willRun, isTrue);
    expect(SimulationData.fromJson({'steps': <Object>[]}), isNull);
  });

  testWidgets('a template creates a draft', (tester) async {
    http.on('POST', '/api/v1/automations/templates/urgent-message/create', {
      'id': 'r2',
    }, status: 201);
    await show(tester);

    expect(find.text('Urgent message alert'), findsOneWidget);
    expect(find.textContaining('asks for approval'), findsOneWidget);
    await tester.tap(find.byKey(const Key('template-use-urgent-message')));
    await tester.pumpAndSettle();

    expect(
      http
          .sent('POST', '/api/v1/automations/templates/urgent-message/create')
          .length,
      1,
    );
    expect(find.textContaining('Draft created'), findsOneWidget);
  });

  testWidgets('a new webhook shows its url once', (tester) async {
    http.on('POST', '/api/v1/automations/webhooks', {
      'webhook': {'id': 'w2', 'name': 'Form', 'tokenHint': 'zz99'},
      'token': 'jwh_secret',
      'url': 'https://jarvis.example/api/v1/hooks/jwh_secret',
    }, status: 201);
    await show(tester);

    await tester.tap(find.byKey(const Key('studio-tab-webhooks')));
    await tester.pumpAndSettle();
    expect(find.text('CI server'), findsOneWidget);
    await tester.tap(find.byKey(const Key('webhook-create')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('webhook-name')), 'Form');
    await tester.tap(find.byKey(const Key('webhook-name-save')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('webhook-url')), findsOneWidget);
    expect(find.textContaining('jwh_secret'), findsOneWidget);
    expect(
      (http.sent('POST', '/api/v1/automations/webhooks').single.body as Map)['name'],
      'Form',
    );
  });

  testWidgets('try it previews what an automation would do', (tester) async {
    http.on('POST', '/api/v1/automations/r1/simulate', {
      'trigger': 'A message arrives',
      'triggerMatches': true,
      'triggerNote': null,
      'conditionNotes': <Object>[],
      'steps': [
        {
          'index': 0,
          'kind': 'notification',
          'label': 'Send you a notification',
          'willRun': true,
          'skippedReason': null,
          'title': 'Message from Sanne',
          'body': 'Kun je bellen?',
          'needsApproval': false,
        },
        {
          'index': 1,
          'kind': 'agent_run',
          'label': 'Start an agent task',
          'willRun': false,
          'skippedReason': 'Its condition is not met.',
          'title': null,
          'body': null,
          'needsApproval': true,
        },
      ],
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('studio-tab-try')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('studio-title')), 'Sanne');
    await tester.tap(find.byKey(const Key('studio-preview')));
    await tester.pumpAndSettle();

    expect(find.text('Message from Sanne'), findsOneWidget);
    expect(find.text('Its condition is not met.'), findsOneWidget);
    expect(find.byKey(const Key('studio-step')), findsNWidgets(2));
    final body = http.sent('POST', '/api/v1/automations/r1/simulate').single.body as Map;
    expect(body['title'], 'Sanne');
  });
}
