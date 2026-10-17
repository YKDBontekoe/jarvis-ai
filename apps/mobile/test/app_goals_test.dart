import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/apps/app_goals_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _goal(
  String id, {
  bool ready = false,
  bool pinned = false,
  Map<String, Object?>? tryAction,
}) => {
  'id': id,
  'title': id == 'calendar' ? 'Manage my calendar' : 'Deal with my email',
  'purpose': 'Purpose of $id.',
  'status': ready ? 'ready' : 'not_connected',
  'nextStep': ready
      ? 'Connected. Try it below or pin it.'
      : 'Paste your private link.',
  'capabilities': {
    'reads': ['events list'],
    'prepares': <Object>[],
    'changes': ['event create'],
    'needs': 'A private link.',
  },
  'try': ready ? tryAction : null,
  'pinned': pinned,
};

Map<String, Object?> _search(List<Map<String, Object?>> goals) => {
  'goals': goals,
  'unmatched': false,
  'suggestion': null,
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, {ValueChanged<String>? ask}) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: AppGoalsScreen(http: http.client(), onAskInChat: ask),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'a goal that is not connected shows capabilities and the real next step, with no Try button',
    (tester) async {
      http.on('GET', '/api/v1/apps/goals', _search([_goal('calendar')]));
      await show(tester);

      expect(find.text('Manage my calendar'), findsOneWidget);
      expect(find.text('Reads: events list'), findsOneWidget);
      expect(find.text('Changes: event create'), findsOneWidget);
      expect(find.text('Paste your private link.'), findsOneWidget);
      expect(
        find.byKey(const Key('app-goal-connect-calendar')),
        findsOneWidget,
      );
      expect(find.byKey(const Key('app-goal-try-calendar')), findsNothing);
      expect(find.byKey(const Key('app-goal-pin-calendar')), findsNothing);
    },
  );

  testWidgets(
    'searching sends the words and an unmatched goal is described honestly',
    (tester) async {
      http.on('GET', '/api/v1/apps/goals', _search([_goal('calendar')]));
      await show(tester);

      http.on('GET', '/api/v1/apps/goals', {
        'goals': <Object>[],
        'unmatched': true,
        'suggestion':
            'No app Jarvis knows covers that yet. Nothing has been connected.',
      });
      await tester.enterText(
        find.byKey(const Key('apps-query')),
        'track my parcels',
      );
      await tester.tap(find.byKey(const Key('apps-search')));
      await tester.pumpAndSettle();

      expect(
        http.sent('GET', '/api/v1/apps/goals').last.query['q'],
        'track my parcels',
      );
      expect(find.byKey(const Key('apps-none')), findsOneWidget);
      expect(find.textContaining('Nothing has been connected'), findsOneWidget);
      expect(find.byKey(const Key('app-goal-calendar')), findsNothing);
    },
  );

  testWidgets(
    'trying the calendar shows what is on today, and says so when nothing is',
    (tester) async {
      http
        ..on(
          'GET',
          '/api/v1/apps/goals',
          _search([
            _goal(
              'calendar',
              ready: true,
              tryAction: {
                'label': 'What is on today?',
                'kind': 'calendar_today',
              },
            ),
          ]),
        )
        ..on('POST', '/api/v1/apps/goals/calendar/try', {
          'kind': 'calendar_today',
          'events': [
            {'title': 'Standup'},
          ],
        });
      await show(tester);

      await tester.tap(find.byKey(const Key('app-goal-try-calendar')));
      await tester.pumpAndSettle();
      expect(find.text('· Standup'), findsOneWidget);

      http.on('POST', '/api/v1/apps/goals/calendar/try', {
        'kind': 'calendar_today',
        'events': <Object>[],
      });
      await tester.tap(find.byKey(const Key('app-goal-try-calendar')));
      await tester.pumpAndSettle();
      expect(find.text('Nothing on today.'), findsOneWidget);
    },
  );

  testWidgets('trying mail hands a read-only request to the chat', (
    tester,
  ) async {
    String? asked;
    http
      ..on(
        'GET',
        '/api/v1/apps/goals',
        _search([
          _goal(
            'mail',
            ready: true,
            tryAction: {
              'label': 'Show my latest emails',
              'kind': 'chat_prompt',
            },
          ),
        ]),
      )
      ..on('POST', '/api/v1/apps/goals/mail/try', {
        'kind': 'chat_prompt',
        'events': <Object>[],
        'prompt':
            'Show the sender and subject of my last 5 emails. Only read them.',
      });
    await show(tester, ask: (prompt) => asked = prompt);

    await tester.tap(find.byKey(const Key('app-goal-try-mail')));
    await tester.pumpAndSettle();

    expect(asked, contains('Only read them'));
  });

  testWidgets('pinning and unpinning an action call the pin route', (
    tester,
  ) async {
    http
      ..on(
        'GET',
        '/api/v1/apps/goals',
        _search([
          _goal(
            'mail',
            ready: true,
            tryAction: {'label': 'Try', 'kind': 'chat_prompt'},
          ),
        ]),
      )
      ..on('PUT', '/api/v1/apps/goals/mail/pin', null, status: 204)
      ..on('DELETE', '/api/v1/apps/goals/mail/pin', null, status: 204);
    await show(tester);

    await tester.tap(find.byKey(const Key('app-goal-pin-mail')));
    await tester.pumpAndSettle();
    expect(http.sent('PUT', '/api/v1/apps/goals/mail/pin'), hasLength(1));

    http.on(
      'GET',
      '/api/v1/apps/goals',
      _search([
        _goal(
          'mail',
          ready: true,
          pinned: true,
          tryAction: {'label': 'Try', 'kind': 'chat_prompt'},
        ),
      ]),
    );
    await tester.tap(find.byKey(const Key('app-goal-pin-mail')));
    await tester.pumpAndSettle();
    expect(find.text('Unpin'), findsOneWidget);
  });

  testWidgets('a lookup that fails shows the reason', (tester) async {
    http.on('GET', '/api/v1/apps/goals', {
      'detail': 'Could not reach the catalog.',
    }, status: 500);
    await show(tester);

    expect(find.textContaining('Could not reach the catalog'), findsOneWidget);
  });

  testWidgets(
    'a calendar that cannot be read says what to check instead of showing an empty day',
    (tester) async {
      http
        ..on(
          'GET',
          '/api/v1/apps/goals',
          _search([
            _goal(
              'calendar',
              ready: true,
              tryAction: {
                'label': 'What is on today?',
                'kind': 'calendar_today',
              },
            ),
          ]),
        )
        ..on('POST', '/api/v1/apps/goals/calendar/try', {
          'kind': 'calendar_today',
          'events': <Object>[],
          'problem':
              'Your calendar could not be read just now. Check the link.',
        });
      await show(tester);

      await tester.tap(find.byKey(const Key('app-goal-try-calendar')));
      await tester.pumpAndSettle();

      expect(find.textContaining('could not be read'), findsOneWidget);
      expect(find.text('Nothing on today.'), findsNothing);
    },
  );
}
