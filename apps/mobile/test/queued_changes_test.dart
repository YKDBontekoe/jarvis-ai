import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/mutation_queue.dart';
import 'package:jarvis_mobile/features/reminders/reminders_screen.dart';
import 'package:jarvis_mobile/features/settings/queued_changes_screen.dart';

import 'package:jarvis_mobile/schedule_format.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

const server = 'https://fixture.invalid';

void main() {
  late MutationQueue queue;
  late FixtureHttp http;
  final now = DateTime(2026, 10, 18, 9);

  setUp(() async {
    queue = MutationQueue(store: MemoryMutationStore(), clock: () => now);
    await queue.bind(server);
    http = FixtureHttp();
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: QueuedChangesScreen(
          http: http.client(),
          queue: queue,
          clock: () => now,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets('an empty queue says nothing is waiting', (tester) async {
    await show(tester);

    expect(find.text('Nothing is waiting'), findsOneWidget);
    expect(find.byKey(const Key('queue-sync')), findsNothing);
  });

  testWidgets(
    'waiting changes are named, aged and not called done; sending confirms them',
    (tester) async {
      await queue.enqueue(
        kind: MutationKinds.reminderComplete,
        targetId: 'r1',
        label: 'Mark “Call the plumber” as done',
        group: 'reminder:r1',
      );
      http.on('POST', '/api/v1/reminders/r1/complete', {'id': 'r1'});
      await show(tester);

      expect(find.text('1 waiting'), findsOneWidget);
      expect(find.text('Mark “Call the plumber” as done'), findsOneWidget);
      expect(find.text('WAITING · JUST NOW'), findsOneWidget);
      expect(
        find.textContaining('not done until Jarvis confirms'),
        findsOneWidget,
      );

      await tester.tap(find.byKey(const Key('queue-sync')));
      await tester.pumpAndSettle();

      expect(find.text('1 sent and confirmed.'), findsOneWidget);
      expect(find.text('Nothing is waiting'), findsOneWidget);
    },
  );

  testWidgets('a refused change needs you and can be retried or discarded', (
    tester,
  ) async {
    await queue.enqueue(
      kind: MutationKinds.reminderComplete,
      targetId: 'bad',
      label: 'Mark “Bad” as done',
      group: 'reminder:bad',
    );
    http.on('POST', '/api/v1/reminders/bad/complete', {}, status: 400);
    await show(tester);
    await tester.tap(find.byKey(const Key('queue-sync')));
    await tester.pumpAndSettle();

    expect(find.text('NEEDS YOU'), findsOneWidget);
    expect(find.textContaining('Retry it, or discard it'), findsOneWidget);
    await tester.tap(find.textContaining('Try again'));
    await tester.pumpAndSettle();
    expect(queue.items.value.single.status, MutationStatus.queued);

    await tester.tap(find.textContaining('Discard'));
    await tester.pumpAndSettle();
    expect(queue.items.value, isEmpty);
  });

  testWidgets('sending while still offline keeps everything and says so', (
    tester,
  ) async {
    await queue.enqueue(
      kind: MutationKinds.reminderComplete,
      targetId: 'r1',
      label: 'Mark as done',
      group: 'reminder:r1',
    );
    final offline = Dio(BaseOptions(baseUrl: server))
      ..httpClientAdapter = _Down();
    await tester.pumpWidget(
      MaterialApp(
        home: QueuedChangesScreen(
          http: offline,
          queue: queue,
          clock: () => now,
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.tap(find.byKey(const Key('queue-sync')));
    await tester.pumpAndSettle();

    expect(find.textContaining('still cannot be reached'), findsOneWidget);
    expect(queue.items.value, hasLength(1));
  });

  testWidgets(
    'marking a reminder done while offline queues it and does not claim it is done',
    (tester) async {
      SharedPreferences.setMockInitialValues({});
      deviceTimeZoneLookup = () async => null;
      await MutationQueue.instance.clear();
      await MutationQueue.instance.bind(server);
      http.on('GET', '/api/v1/reminders', [
        {
          'id': 'r1',
          'title': 'Call mum',
          'dueAt': '2030-01-16T18:00:00Z',
          'status': 'pending',
          'createdAt': '2030-01-15T12:00:00Z',
        },
      ]);
      http.on('GET', '/api/v1/notifications', <Object>[]);
      final flaky = _Flaky(http);
      final dio = Dio(BaseOptions(baseUrl: server))..httpClientAdapter = flaky;
      tester.view.physicalSize = const Size(900, 1600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(MaterialApp(home: RemindersScreen(http: dio)));
      await tester.pumpAndSettle();

      flaky.offline = true;
      await tester.timedDrag(
        find.text('Call mum'),
        const Offset(500, 0),
        const Duration(milliseconds: 400),
      );
      await tester.pump(const Duration(seconds: 1));
      await tester.pumpAndSettle();

      final queued = MutationQueue.instance.items.value;
      expect(queued, hasLength(1));
      expect(queued.single.kind, MutationKinds.reminderComplete);
      expect(queued.single.targetId, 'r1');
      expect(find.textContaining('It is not done yet'), findsOneWidget);
      expect(find.text('Marked as done.'), findsNothing);
      await MutationQueue.instance.clear();
    },
  );
}

class _Down implements HttpClientAdapter {
  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<List<int>>? requestStream,
    Future<void>? cancelFuture,
  ) => throw DioException(
    requestOptions: options,
    type: DioExceptionType.connectionError,
    error: 'down',
  );

  @override
  void close({bool force = false}) {}
}

/// Serves the fixtures, and loses the connection for writes when told to.
class _Flaky implements HttpClientAdapter {
  _Flaky(this.inner);

  final FixtureHttp inner;
  bool offline = false;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<List<int>>? requestStream,
    Future<void>? cancelFuture,
  ) {
    if (offline && options.method == 'POST') {
      throw DioException(
        requestOptions: options,
        type: DioExceptionType.connectionError,
        error: 'down',
      );
    }
    return inner.fetch(options, null, cancelFuture);
  }

  @override
  void close({bool force = false}) {}
}
