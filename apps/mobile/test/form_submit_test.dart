import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/people/people_screen.dart';
import 'package:jarvis_mobile/features/reminders/reminders_screen.dart';
import 'package:jarvis_mobile/features/tasks/tasks_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';

import 'support/fixture_http.dart';

/// Creation editors save inside themselves: a failure keeps them open with
/// what was typed, and a repeated tap sends a single request.
void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => 'Europe/Lisbon';
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  testWidgets('a failed task start keeps the dialog and the typed text', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks', <Object>[]);
    http.on('POST', '/api/v1/tasks', {'title': 'x'}, status: 500);
    await show(tester, TasksScreen(http: http.client()));

    await tester.tap(find.text('New task'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField).at(0), 'Buy milk');
    await tester.enterText(find.byType(TextFormField).at(1), 'Get 2% milk');
    await tester.tap(find.text('Start task'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('submit-error')), findsOneWidget);
    expect(find.text('Buy milk'), findsOneWidget);
    expect(find.text('Get 2% milk'), findsOneWidget);

    http.on('POST', '/api/v1/tasks', {'id': 't1'});
    await tester.tap(find.text('Start task'));
    await tester.pumpAndSettle();
    expect(find.text('Start task'), findsNothing);
    expect(http.sent('POST', '/api/v1/tasks'), hasLength(2));
  });

  testWidgets('tapping Start task twice sends one request', (tester) async {
    final gate = Completer<void>();
    http.beforeRespond = (o) async {
      if (o.method == 'POST') await gate.future;
    };
    http.on('GET', '/api/v1/tasks', <Object>[]);
    http.on('POST', '/api/v1/tasks', {'id': 't1'});
    await show(tester, TasksScreen(http: http.client()));

    await tester.tap(find.text('New task'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField).at(0), 'Buy milk');
    await tester.enterText(find.byType(TextFormField).at(1), 'Get 2% milk');
    await tester.tap(find.text('Start task'));
    await tester.pump();
    await tester.tap(find.byType(FilledButton).last, warnIfMissed: false);
    await tester.pump();
    gate.complete();
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/tasks'), hasLength(1));
  });

  testWidgets('a failed reminder save keeps the editor open', (tester) async {
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    http.on('GET', '/api/v1/briefings/daily', {
      'enabled': false,
      'localTime': '08:00:00',
      'timeZoneId': 'UTC',
    });
    http.on('POST', '/api/v1/reminders', <String, Object>{}, status: 500);
    await show(tester, RemindersScreen(http: http.client()));

    await tester.tap(find.text('New'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), 'Water plants');
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('submit-error')), findsOneWidget);
    expect(find.text('Water plants'), findsOneWidget);
    expect(find.text('Create a reminder'), findsOneWidget);
  });

  testWidgets('a failed person save keeps the sheet and the name', (
    tester,
  ) async {
    http.on('GET', '/api/v1/people', <Object>[]);
    http.on('GET', '/api/v1/people/suggestions', <Object>[]);
    http.on('POST', '/api/v1/people', {'title': 'Nope'}, status: 400);
    await show(tester, PeopleScreen(http: http.client()));

    await tester.tap(find.byKey(const Key('people-add-first')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('person-name')), 'Anna');
    await tester.ensureVisible(find.byKey(const Key('person-save')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('person-save')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('submit-error')), findsOneWidget);
    expect(find.text('Anna'), findsOneWidget);
    expect(find.byKey(const Key('person-save')), findsOneWidget);
    expect(http.sent('POST', '/api/v1/people'), hasLength(1));
  });
}
