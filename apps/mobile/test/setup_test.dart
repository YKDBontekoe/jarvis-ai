import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/setup/setup_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> goal(
  String id, {
  bool done = false,
  bool declined = false,
  String? destination,
  String? blocker,
}) => {
  'id': id,
  'title': 'Title $id',
  'why': 'Why $id',
  'done': done,
  'declined': declined,
  'action': 'Do $id',
  'destination': destination,
  'blocker': blocker,
};

Map<String, Object?> view({bool skipped = false}) => {
  'summary': '1 of 3 done.',
  'done': 1,
  'total': 3,
  'nextGoalId': 'calendar',
  'goals': [
    goal('talk', done: true),
    goal('calendar', declined: skipped, destination: 'find-app'),
    goal('reminder', destination: 'reminders', blocker: 'Needs sign-in'),
  ],
  'capabilities': [
    {
      'id': 'model',
      'title': 'Brain',
      'status': 'needs_reauth',
      'detail': 'Sign in again.',
      'destination': 'models',
    },
    {
      'id': 'push',
      'title': 'Push',
      'status': 'unsupported',
      'detail': 'Cannot check.',
      'destination': null,
    },
  ],
};

void main() {
  late FixtureHttp http;
  final opened = <String>[];

  setUp(() {
    http = FixtureHttp();
    opened.clear();
    http.on('GET', '/api/v1/setup', view());
  });

  Future<void> show(WidgetTester tester, {ValueChanged<String>? ask}) async {
    tester.view.physicalSize = const Size(800, 2200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: SetupScreen(
          http: http.client(),
          onAskInChat: ask,
          pageFor: (destination) {
            opened.add(destination);
            return Scaffold(body: Text('page:$destination'));
          },
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'shows what is done, what blocks a step and what is not connected',
    (tester) async {
      await show(tester);

      expect(find.text('1 OF 3 DONE'), findsOneWidget);
      expect(find.byKey(const Key('setup-done-mark')), findsOneWidget);
      expect(find.text('Title talk'), findsOneWidget);
      expect(find.byKey(const Key('setup-act-talk')), findsNothing);
      expect(find.textContaining('Needs sign-in'), findsOneWidget);
      expect(find.text('Sign in again'), findsOneWidget);
      expect(find.text('Not checked'), findsOneWidget);
      expect(find.byKey(const Key('setup-fix-push')), findsNothing);
    },
  );

  testWidgets(
    'a step opens its page and an optional one can be skipped and brought back',
    (tester) async {
      http.on(
        'POST',
        '/api/v1/setup/goals/calendar/decline',
        view(skipped: true),
      );
      http.on('POST', '/api/v1/setup/goals/calendar/reopen', view());
      await show(tester);

      await tester.tap(find.byKey(const Key('setup-act-calendar')));
      await tester.pumpAndSettle();
      expect(find.text('page:find-app'), findsOneWidget);
      Navigator.of(tester.element(find.text('page:find-app'))).pop();
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('setup-skip-reminder')), findsNothing);
      await tester.tap(find.byKey(const Key('setup-skip-calendar')));
      await tester.pumpAndSettle();
      expect(find.text('SKIPPED'), findsOneWidget);
      expect(find.byKey(const Key('setup-act-calendar')), findsNothing);
      await tester.tap(find.byKey(const Key('setup-reopen-calendar')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('setup-act-calendar')), findsOneWidget);
    },
  );

  testWidgets('the support report is shown before anything is copied', (
    tester,
  ) async {
    http.on('GET', '/api/v1/setup/support-bundle', {
      'version': '1.0',
      'note': 'versions and states only',
    });
    await show(tester);

    await tester.tap(find.byKey(const Key('setup-support')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('setup-bundle-text')), findsOneWidget);
    expect(find.textContaining('versions and states only'), findsOneWidget);
  });

  testWidgets('talking to Jarvis starts a chat instead of opening a page', (
    tester,
  ) async {
    http.on('GET', '/api/v1/setup', {
      'summary': 'x',
      'goals': [goal('talk')],
      'capabilities': <Object>[],
    });
    String? asked;
    await show(tester, ask: (text) => asked = text);

    await tester.tap(find.byKey(const Key('setup-act-talk')));
    await tester.pumpAndSettle();

    expect(asked, contains('Jarvis'));
    expect(opened, isEmpty);
  });
}
