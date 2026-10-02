import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:geolocator/geolocator.dart';
import 'package:jarvis_mobile/reminders_screen.dart';
import 'package:jarvis_mobile/schedule_format.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    deviceTimeZoneLookup = () async => null;
  });

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
        http.sent('POST', '/api/v1/reminders').single.body
            as Map<String, dynamic>;
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
        'dueAt': DateTime.now()
            .add(const Duration(days: 1))
            .toUtc()
            .toIso8601String(),
        'status': 'pending',
        'recurrence': 'weekdays',
        'createdAt': DateTime.now().toUtc().toIso8601String(),
      },
    ]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    await show(tester);

    expect(find.text('Take out the trash'), findsOneWidget);
    expect(
      find.textContaining('Every weekday · next Tomorrow'),
      findsOneWidget,
    );
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
    await tester.tap(find.byTooltip('Reminder actions'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Open chat'));
    await tester.pumpAndSettle();
    expect(opened, 'c1');
  });

  testWidgets('reminders group into overdue, upcoming and finished', (
    tester,
  ) async {
    final now = DateTime.now().toUtc();
    String at(Duration offset) => now.add(offset).toIso8601String();
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'a',
        'title': 'Late thing',
        'dueAt': at(const Duration(hours: -2)),
        'status': 'pending',
      },
      {
        'id': 'b',
        'title': 'Next week thing',
        'dueAt': at(const Duration(days: 8)),
        'status': 'pending',
      },
      {
        'id': 'c',
        'title': 'Old thing',
        'dueAt': at(const Duration(days: -3)),
        'status': 'delivered',
      },
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
      {
        'id': 'n1',
        'type': 'task.completed',
        'title': 'Task finished',
        'body': 'One',
        'createdAt': now,
        'readAt': null,
      },
      {
        'id': 'n2',
        'type': 'task.completed',
        'title': 'Another finished',
        'body': 'Two',
        'createdAt': now,
        'readAt': null,
      },
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

  testWidgets('an approval notification can be approved in place', (
    tester,
  ) async {
    final now = DateTime.now().toUtc().toIso8601String();
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', [
      {
        'id': 'n1',
        'type': 'approval.required',
        'title': 'Approval needed',
        'body': 'Jarvis is waiting for approval to run InvokeMcpTool.',
        'sourceId': 'a1',
        'createdAt': now,
        'readAt': null,
      },
    ]);
    http.on('GET', '/api/v1/approvals', [
      {'id': 'a1', 'toolName': 'InvokeMcpTool', 'status': 'pending'},
    ]);
    http.on('POST', '/api/v1/approvals/a1/decision', {
      'id': 'm1',
      'role': 'assistant',
      'content': 'Done.',
    });
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
    final sent =
        http.sent('POST', '/api/v1/approvals/a1/decision').single.body as Map;
    expect(sent['approved'], true);
  });

  testWidgets('a delivered reminder says when it fired and can be snoozed', (
    tester,
  ) async {
    final fired = DateTime.now().subtract(const Duration(minutes: 3)).toUtc();
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Call mom',
        'dueAt': fired.toIso8601String(),
        'status': 'completed',
        'recurrence': 'none',
        'lastDeliveredAt': fired.toIso8601String(),
        'completedAt': fired.toIso8601String(),
      },
    ]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    http.on('POST', '/api/v1/reminders/r1/snooze', {
      'id': 'r1',
      'title': 'Call mom',
      'status': 'pending',
      'dueAt': DateTime.now()
          .add(const Duration(minutes: 10))
          .toUtc()
          .toIso8601String(),
    });
    await show(tester);

    expect(find.textContaining('Reminded you Today'), findsOneWidget);
    await tester.tap(find.byTooltip('Reminder actions'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Remind me again in 10 min'));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/reminders/r1/snooze').single.body as Map;
    expect(body['minutes'], 10);
    expect(find.textContaining('Snoozed until Today'), findsOneWidget);
  });

  testWidgets('an upcoming one-time reminder can be marked done', (
    tester,
  ) async {
    http.on('GET', '/api/v1/reminders', [
      {
        'id': 'r1',
        'title': 'Pay rent',
        'dueAt': DateTime.now()
            .add(const Duration(hours: 3))
            .toUtc()
            .toIso8601String(),
        'status': 'pending',
        'recurrence': 'none',
      },
    ]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    http.on('POST', '/api/v1/reminders/r1/complete', {
      'id': 'r1',
      'status': 'completed',
    });
    await show(tester);

    expect(find.textContaining('in 3 h'), findsOneWidget);
    await tester.tap(find.byTooltip('Reminder actions'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Mark as done'));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/reminders/r1/complete'), hasLength(1));
  });

  testWidgets('a due reminder notification offers snooze in place', (
    tester,
  ) async {
    final now = DateTime.now().toUtc().toIso8601String();
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', [
      {
        'id': 'n1',
        'type': 'reminder.due',
        'title': 'Stretch',
        'body': 'Reminder · due now',
        'sourceId': 'r1',
        'createdAt': now,
        'readAt': null,
      },
    ]);
    http.on('POST', '/api/v1/reminders/r1/snooze', {
      'id': 'r1',
      'status': 'pending',
    });
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

    await tester.tap(find.text('1 hour'));
    await tester.pumpAndSettle();
    final body =
        http.sent('POST', '/api/v1/reminders/r1/snooze').single.body as Map;
    expect(body['minutes'], 60);
  });

  testWidgets('new reminders use the device time zone and save it once', (
    tester,
  ) async {
    deviceTimeZoneLookup = () async => 'Europe/Lisbon';
    http.on('GET', '/api/v1/reminders', <Object>[]);
    http.on('GET', '/api/v1/notifications', <Object>[]);
    http.on('GET', '/api/v1/briefings/daily', {
      'enabled': false,
      'localTime': '08:00:00',
      'timeZoneId': 'UTC',
      'workflowId': '',
    });
    http.on('PUT', '/api/v1/briefings/daily', <String, Object>{});
    http.on('POST', '/api/v1/reminders', {'id': 'r1', 'status': 'pending'});
    await show(tester);

    await tester.tap(find.text('New'));
    await tester.pumpAndSettle();
    expect(find.text('Time zone: Europe/Lisbon'), findsOneWidget);
    await tester.enterText(find.byType(TextFormField), 'Water plants');
    await tester.tap(find.byKey(const Key('recurrence-daily')));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Save'));
    await tester.pumpAndSettle();

    final body =
        http.sent('POST', '/api/v1/reminders').single.body
            as Map<String, dynamic>;
    expect(body['timeZoneId'], 'Europe/Lisbon');
    final saved =
        http.sent('PUT', '/api/v1/briefings/daily').single.body
            as Map<String, dynamic>;
    expect(saved['timeZoneId'], 'Europe/Lisbon');
    expect(saved['enabled'], false);
  });

  group('place reminders', () {
    Future<void> showWithLocation(
      WidgetTester tester, {
      LocationPermission access = LocationPermission.always,
      List<String>? opened,
    }) async {
      tester.view.physicalSize = const Size(900, 1600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: RemindersScreen(
            http: http.client(),
            locate: ({bool requestPermission = true}) async =>
                (latitude: 52.3702, longitude: 4.8952, accuracy: 30.0),
            locationAccess: () async => access,
            openLocationSettings: () async {
              opened?.add('settings');
              return true;
            },
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    Map<String, dynamic> placeReminder({
      String title = 'Buy milk',
      String status = 'pending',
      bool repeats = false,
      String trigger = 'arrive',
    }) => {
      'id': 'p-$title',
      'title': title,
      'dueAt': '2026-10-01T10:00:00Z',
      'status': status,
      'recurrence': 'none',
      'weekdays': 0,
      'timeZoneId': 'UTC',
      'createdAt': '2026-10-01T10:00:00Z',
      'place': {
        'name': 'Supermarket',
        'latitude': 52.1,
        'longitude': 5.1,
        'radiusMeters': 300,
        'trigger': trigger,
        'repeats': repeats,
      },
    };

    testWidgets('a place reminder posts where you are and the place', (
      tester,
    ) async {
      http.on('GET', '/api/v1/reminders', <Object>[]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      http.on('GET', '/api/v1/briefings/daily', {
        'enabled': false,
        'localTime': '08:00:00',
        'timeZoneId': 'Europe/Amsterdam',
        'workflowId': 'wf',
      });
      http.on('POST', '/api/v1/devices/telemetry', <String, Object>{});
      http.on('POST', '/api/v1/reminders', placeReminder());
      await showWithLocation(tester);

      await tester.tap(find.text('New'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextFormField).first, 'Water plants');
      await tester.tap(find.text('At a place'));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('place-fields')), findsOneWidget);

      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();
      expect(find.text('Name the place.'), findsOneWidget);
      expect(http.sent('POST', '/api/v1/reminders'), isEmpty);

      await tester.enterText(find.byKey(const Key('place-name')), 'Home');
      await tester.tap(find.byKey(const Key('trigger-leave')));
      await tester.tap(find.byKey(const Key('use-current-location')));
      await tester.pumpAndSettle();
      expect(find.text('Using where you are now'), findsOneWidget);
      await tester.tap(find.byKey(const Key('every-visit')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();

      final body =
          http.sent('POST', '/api/v1/reminders').single.body
              as Map<String, dynamic>;
      expect(body['title'], 'Water plants');
      expect(body.containsKey('dueAt'), isFalse);
      expect(body['place'], {
        'name': 'Home',
        'latitude': 52.3702,
        'longitude': 4.8952,
        'radiusMeters': 150.0,
        'trigger': 'leave',
        'repeats': true,
      });
      expect(http.sent('POST', '/api/v1/devices/telemetry'), hasLength(1));
    });

    testWidgets('earlier places can be picked again', (tester) async {
      http.on('GET', '/api/v1/reminders', [placeReminder(status: 'completed')]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      http.on('GET', '/api/v1/briefings/daily', <String, Object>{});
      http.on('POST', '/api/v1/devices/telemetry', <String, Object>{});
      http.on('POST', '/api/v1/reminders', placeReminder());
      await showWithLocation(tester);

      await tester.tap(find.text('New'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextFormField).first, 'Buy eggs');
      await tester.tap(find.text('At a place'));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('saved-place-Supermarket')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Save'));
      await tester.pumpAndSettle();

      final place =
          (http.sent('POST', '/api/v1/reminders').single.body
                  as Map<String, dynamic>)['place']
              as Map<String, dynamic>;
      expect(place['name'], 'Supermarket');
      expect(place['latitude'], 52.1);
      expect(place['radiusMeters'], 300.0);
    });

    testWidgets('waiting place reminders group by place without snooze', (
      tester,
    ) async {
      http.on('GET', '/api/v1/reminders', [
        placeReminder(repeats: true),
        placeReminder(title: 'Call mum', trigger: 'leave'),
      ]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      await showWithLocation(tester);

      expect(find.text('At a place'), findsOneWidget);
      expect(
        find.text('When you arrive at Supermarket · every visit'),
        findsOneWidget,
      );
      expect(find.text('When you leave Supermarket'), findsOneWidget);
      expect(find.byKey(const Key('place-access-hint')), findsNothing);

      await tester.tap(find.byTooltip('Reminder actions').first);
      await tester.pumpAndSettle();
      expect(find.text('Remind me in 10 min'), findsNothing);
      expect(find.text('Stop repeating'), findsOneWidget);
    });

    testWidgets('without Always access the list explains and opens Settings', (
      tester,
    ) async {
      final opened = <String>[];
      http.on('GET', '/api/v1/reminders', [placeReminder()]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      await showWithLocation(
        tester,
        access: LocationPermission.whileInUse,
        opened: opened,
      );

      expect(find.text('Only while Jarvis is open'), findsOneWidget);
      await tester.tap(find.text('Open Settings'));
      expect(opened, ['settings']);
    });
  });
}
