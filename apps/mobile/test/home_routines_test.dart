import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home_routines/home_routines_screen.dart';
import 'package:jarvis_mobile/features/home_routines/routine_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> step(
  String id,
  String label,
  String status, {
  String? device,
  bool sensitive = false,
}) => {
  'step': {
    'id': id,
    'label': label,
    'readOnly': false,
    'sensitive': sensitive,
    'reversible': !sensitive,
  },
  'device': device == null ? null : {'name': device},
  'status': status,
  'reason': status == 'unavailable' ? 'Not reachable.' : 'Would run.',
};

Map<String, Object?> plan({bool removed = false}) => {
  'template': {
    'id': 'leave',
    'title': 'Leave home',
    'description': 'Switch things off.',
  },
  'summary': removed ? '1 of 2 would run.' : '2 of 3 would run.',
  'steps': [
    step(
      'l-living',
      'Turn off the living room light',
      'ready',
      device: 'Living room light',
    ),
    step(
      'l-eco',
      'Set the hall thermostat to eco',
      removed ? 'removed' : 'ready',
      device: 'Hall thermostat',
    ),
    step(
      'l-lock',
      'Lock the front door',
      'needs_approval',
      device: 'Front door lock',
      sensitive: true,
    ),
  ],
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets('the list says the devices are examples before anything else', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home-routines', {
      'routines': [
        {
          'id': 'leave',
          'title': 'Leave home',
          'description': 'Switch things off.',
          'steps': [{}, {}],
        },
      ],
      'devices': [
        {
          'id': 'd1',
          'name': 'Bedroom lamp',
          'room': 'Bedroom',
          'present': false,
        },
      ],
      'notice':
          'These are example devices. Jarvis is not connected to a real home.',
    });
    await show(tester, HomeRoutinesScreen(http: http.client()));

    expect(find.textContaining('example devices'), findsOneWidget);
    expect(find.byKey(const Key('routine-leave')), findsOneWidget);
    expect(find.text('Not reachable'), findsOneWidget);
    expect(find.text('Example'), findsOneWidget);
  });

  testWidgets(
    'a security action is shown as needing approval, never as ready',
    (tester) async {
      http.on('GET', '/api/v1/home-routines/leave/plan', plan());
      await show(
        tester,
        RoutineScreen(http: http.client(), routineId: 'leave'),
      );

      expect(find.text('Needs approval'), findsOneWidget);
      expect(find.text('Ready'), findsNWidgets(2));
    },
  );

  testWidgets('taking an action out sends the remaining choice', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home-routines/leave/plan', plan());
    http.on('PUT', '/api/v1/home-routines/leave/choices', plan(removed: true));
    await show(tester, RoutineScreen(http: http.client(), routineId: 'leave'));

    await tester.tap(find.byKey(const Key('step-toggle-l-eco')));
    await tester.pumpAndSettle();

    final body =
        http.sent('PUT', '/api/v1/home-routines/leave/choices').single.body
            as Map;
    expect(body['removed'], ['l-eco']);
    expect(find.text('Put back'), findsOneWidget);
    expect(find.text('1 of 2 would run.'), findsOneWidget);
  });

  testWidgets('a simulated run lists each outcome honestly and can be undone', (
    tester,
  ) async {
    http.on('GET', '/api/v1/home-routines/leave/plan', plan());
    http.on('POST', '/api/v1/home-routines/leave/run', {
      'id': 'r1',
      'summary': '2 of 3 would run, 1 needs your approval. Nothing changed in your home.',
      'steps': [
        {
          'stepId': 'l-living',
          'label': 'Turn off the living room light',
          'outcome': 'simulated',
          'detail': 'Simulated. Nothing changed.',
          'reversible': true,
        },
        {
          'stepId': 'l-lock',
          'label': 'Lock the front door',
          'outcome': 'not_run',
          'detail': 'You would approve this one yourself.',
          'reversible': false,
        },
      ],
    });
    http.on('POST', '/api/v1/home-routines/runs/r1/reverse', {
      'id': 'r1',
      'summary': 'Undone.',
      'steps': [
        {
          'stepId': 'l-living',
          'label': 'Turn off the living room light',
          'outcome': 'reversed',
          'detail': 'Undone in the simulation.',
          'reversible': false,
        },
      ],
    });
    await show(tester, RoutineScreen(http: http.client(), routineId: 'leave'));

    await tester.tap(find.byKey(const Key('routine-run')));
    await tester.pumpAndSettle();
    expect(find.textContaining('Nothing changed in your home'), findsOneWidget);
    expect(find.text('NOT RUN'), findsOneWidget);

    await tester.tap(find.byKey(const Key('routine-undo')));
    await tester.pumpAndSettle();
    expect(
      http.sent('POST', '/api/v1/home-routines/runs/r1/reverse'),
      hasLength(1),
    );
    expect(find.text('UNDONE'), findsOneWidget);
    expect(find.byKey(const Key('routine-undo')), findsNothing);
  });
}
