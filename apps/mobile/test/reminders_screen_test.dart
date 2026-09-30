import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/reminders_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RemindersScreen(http: http.client()),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('weekday recurrence is posted with the selected time', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    http.on('GET', '/api/v1/briefings/daily', {
      'enabled': true,
      'localTime': '08:00:00',
      'timeZoneId': 'Europe/Amsterdam',
    });
    http.on('POST', '/api/v1/reminders', {
      'id': 'r1',
      'title': 'Take out the trash',
      'dueAt': '2030-01-16T06:30:00Z',
      'status': 'pending',
      'recurrence': 'weekdays',
      'weekdays': 31,
      'timeZoneId': 'Europe/Amsterdam',
      'createdAt': '2030-01-15T12:00:00Z',
    });
    await show(tester);

    await tester.tap(find.text('New'));
    await tester.pumpAndSettle();
    await tester.enterText(find.byType(TextFormField), 'Take out the trash');
    await tester.tap(find.byKey(const Key('recurrence-weekdays')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/reminders').single.body as Map<String, dynamic>;
    expect(body['title'], 'Take out the trash');
    expect(body['recurrence'], 'weekdays');
    expect(body['timeZoneId'], 'Europe/Amsterdam');
    expect(body['localTime'], '09:00:00');
  });

  testWidgets('list shows the recurrence rule beside the next fire', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Take out the trash',
        'dueAt': DateTime.now().add(const Duration(days: 1)).toUtc().toIso8601String(),
        'status': 'pending',
        'recurrence': 'weekdays',
        'createdAt': DateTime.now().toUtc().toIso8601String(),
      },
    ]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    await show(tester);

    expect(find.text('Take out the trash'), findsOneWidget);
    expect(find.textContaining('Weekdays · next'), findsOneWidget);
  });

  testWidgets('open chat uses the linked conversation', (tester) async {
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Stretch',
        'dueAt': DateTime.now()
            .add(const Duration(hours: 1))
            .toUtc()
            .toIso8601String(),
        'status': 'pending',
        'recurrence': 'none',
        'conversationId': 'c1',
        'createdAt': DateTime.now().toUtc().toIso8601String(),
      },
    ]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    String? opened;
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RemindersScreen(
          http: http.client(),
          onOpenConversation: (id) async => opened = id,
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Open chat'));
    await tester.pumpAndSettle();
    expect(opened, 'c1');
  });

  testWidgets('reminders group into overdue, upcoming and finished', (
    tester,
  ) async {
    final now = DateTime.now().toUtc();
    String at(Duration offset) => now.add(offset).toIso8601String();
    http.on('GET', '/api/v1/reminders', [
      {'id': 'a', 'title': 'Late thing', 'dueAt': at(const Duration(hours: -2)), 'status': 'pending'},
      {'id': 'b', 'title': 'Next week thing', 'dueAt': at(const Duration(days: 8)), 'status': 'pending'},
      {'id': 'c', 'title': 'Old thing', 'dueAt': at(const Duration(days: -3)), 'status': 'delivered'},
    ]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    await show(tester);

    for (final heading in ['Overdue', 'Upcoming', 'Finished']) {
      expect(find.text(heading), findsOneWidget);
    }
    expect(
      tester.getTopLeft(find.text('Late thing')).dy,
      lessThan(tester.getTopLeft(find.text('Next week thing')).dy),
    );
    expect(
      tester.getTopLeft(find.text('Next week thing')).dy,
      lessThan(tester.getTopLeft(find.text('Old thing')).dy),
    );
  });

  testWidgets('notifications open on request and can all be marked read', (
    tester,
  ) async {
    final now = DateTime.now().toUtc().toIso8601String();
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', [
      {'id': 'n1', 'type': 'task.completed', 'title': 'Task finished', 'body': 'One', 'createdAt': now, 'readAt': null},
      {'id': 'n2', 'type': 'task.completed', 'title': 'Another finished', 'body': 'Two', 'createdAt': now, 'readAt': null},
    ]);
    http.on('POST', '/api/v1/notifications/n1/read', <String, Object>{});
    http.on('POST', '/api/v1/notifications/n2/read', <String, Object>{});
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RemindersScreen(
          http: http.client(),
          initialTab: RemindersTab.notifications,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Today'), findsOneWidget);
    expect(find.text('Task finished'), findsOneWidget);
    await tester.tap(find.byTooltip('Mark all as read'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/notifications/n1/read'), hasLength(1));
    expect(http.sent('POST', '/api/v1/notifications/n2/read'), hasLength(1));
  });

  testWidgets('an approval notification can be approved in place', (tester) async {
    final now = DateTime.now().toUtc().toIso8601String();
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', [
      {'id': 'n1', 'type': 'approval.required', 'title': 'Approval needed', 'body': 'Jarvis is waiting for approval to run InvokeMcpTool.', 'sourceId': 'a1', 'createdAt': now, 'readAt': null},
    ]);
    http.on('GET', '/api/v1/approvals', [
      {'id': 'a1', 'toolName': 'InvokeMcpTool', 'status': 'pending'},
    ]);
    http.on('POST', '/api/v1/approvals/a1/decision', {'id': 'm1', 'role': 'assistant', 'content': 'Done.'});
    tester.view.physicalSize = const Size(900, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: RemindersScreen(
          http: http.client(),
          initialTab: RemindersTab.notifications,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.textContaining('Wants to:'), findsOneWidget);
    await tester.tap(find.text('Approve'));
    await tester.pumpAndSettle();
    final sent = http.sent('POST', '/api/v1/approvals/a1/decision').single.body as Map;
    expect(sent['approved'], true);
  });
}
