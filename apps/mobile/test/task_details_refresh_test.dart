import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/tasks/task_details_screen.dart';
import 'package:jarvis_mobile/features/tasks/task_refresh_bus.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _task(String status) => {
  'id': 't1',
  'title': 'Research flights',
  'status': status,
  'conversationId': 'c1',
  'createdAt': '2026-11-02T08:00:00Z',
};

List<Object> _messages(int count) => [
  for (var i = 0; i < count; i++)
    {'role': i.isEven ? 'user' : 'assistant', 'content': 'Message number $i'},
];

void main() {
  late FixtureHttp http;
  late TaskRefreshBus bus;

  setUp(() {
    http = FixtureHttp();
    bus = TaskRefreshBus();
    http.on('GET', '/api/v1/tasks/t1', _task('running'));
    http.on('GET', '/api/v1/tasks/t1/messages', _messages(2));
    http.on('GET', '/api/v1/entities/task:t1/related', <String, Object>{});
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: TaskDetailsScreen(
          http: http.client(),
          taskId: 't1',
          pollEvery: const Duration(seconds: 2),
          bus: bus,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  int taskReads() => http.sent('GET', '/api/v1/tasks/t1').length;

  testWidgets('a running task is polled and picks up new activity', (
    tester,
  ) async {
    await show(tester);
    expect(find.text('Message number 2'), findsNothing);

    http.on('GET', '/api/v1/tasks/t1/messages', _messages(3));
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();

    expect(find.text('Message number 2'), findsOneWidget);
    expect(taskReads(), 2);
  });

  testWidgets('polling stops once the task is finished', (tester) async {
    await show(tester);
    http.on('GET', '/api/v1/tasks/t1', _task('completed'));
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();
    final reads = taskReads();

    await tester.pump(const Duration(seconds: 10));
    await tester.pumpAndSettle();
    expect(taskReads(), reads);
    expect(find.text('Completed'), findsWidgets);
  });

  testWidgets('nothing is fetched while the app is in the background', (
    tester,
  ) async {
    await show(tester);
    for (final state in [
      AppLifecycleState.inactive,
      AppLifecycleState.hidden,
      AppLifecycleState.paused,
    ]) {
      tester.binding.handleAppLifecycleStateChanged(state);
    }
    await tester.pump(const Duration(seconds: 10));
    expect(taskReads(), 1);

    for (final state in [
      AppLifecycleState.hidden,
      AppLifecycleState.inactive,
      AppLifecycleState.resumed,
    ]) {
      tester.binding.handleAppLifecycleStateChanged(state);
    }
    await tester.pumpAndSettle();
    expect(taskReads(), 2);
  });

  testWidgets('a page covered by another one is not polled', (tester) async {
    await show(tester);
    final navigator = tester.state<NavigatorState>(find.byType(Navigator));
    navigator.push(
      MaterialPageRoute<void>(builder: (_) => const Scaffold(body: Text('Up'))),
    );
    await tester.pumpAndSettle();

    await tester.pump(const Duration(seconds: 10));
    expect(taskReads(), 1);
  });

  testWidgets('an event for the task conversation refreshes right away', (
    tester,
  ) async {
    await show(tester);
    http.on('GET', '/api/v1/tasks/t1/messages', _messages(3));

    bus.bump('someone-else');
    await tester.pump();
    expect(taskReads(), 1);

    bus.bump('c1');
    await tester.pumpAndSettle();
    expect(taskReads(), 2);
    expect(find.text('Message number 2'), findsOneWidget);
  });

  testWidgets('a failed background refresh keeps what is on screen', (
    tester,
  ) async {
    await show(tester);
    http.on('GET', '/api/v1/tasks/t1', <String, Object>{}, status: 500);
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();

    expect(find.text('Message number 1'), findsOneWidget);
    expect(find.textContaining('could not load'), findsNothing);
  });

  testWidgets('the transcript keeps its scroll position across a refresh', (
    tester,
  ) async {
    http.on('GET', '/api/v1/tasks/t1/messages', _messages(14));
    await show(tester);
    final list = find.byType(Scrollable).first;
    await tester.drag(list, const Offset(0, -500));
    await tester.pumpAndSettle();
    final before = tester.state<ScrollableState>(list).position.pixels;
    expect(before, greaterThan(0));

    http.on('GET', '/api/v1/tasks/t1/messages', _messages(15));
    await tester.pump(const Duration(seconds: 2));
    await tester.pumpAndSettle();

    expect(tester.state<ScrollableState>(list).position.pixels, before);
  });
}
