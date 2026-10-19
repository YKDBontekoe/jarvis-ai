import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/mutation_queue.dart';

import 'support/fixture_http.dart';

const server = 'https://fixture.invalid';

/// FixtureHttp that can lose the connection.
class _Network implements HttpClientAdapter {
  _Network(this.inner);

  final FixtureHttp inner;
  bool offline = false;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) {
    if (offline) {
      throw DioException(
        requestOptions: options,
        type: DioExceptionType.connectionError,
        error: 'offline',
      );
    }
    return inner.fetch(options, requestStream, cancelFuture);
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  late MemoryMutationStore store;
  late DateTime now;
  late MutationQueue queue;
  late FixtureHttp http;
  late _Network network;
  late Dio dio;

  MutationQueue fresh() => MutationQueue(store: store, clock: () => now);

  setUp(() async {
    store = MemoryMutationStore();
    now = DateTime(2026, 10, 18, 9);
    queue = fresh();
    await queue.bind(server);
    http = FixtureHttp();
    network = _Network(http);
    dio = Dio(BaseOptions(baseUrl: server))..httpClientAdapter = network;
  });

  Future<void> complete(String id) => queue.enqueue(
    kind: MutationKinds.reminderComplete,
    targetId: id,
    label: 'Mark “$id” as done',
    group: 'reminder:$id',
  );

  test(
    'changes made offline survive a restart and each ends in one outcome',
    () async {
      await complete('r1');
      await queue.enqueue(
        kind: MutationKinds.habitCheckIn,
        targetId: 'h1',
        label: 'Check in “Walk”',
        group: 'habit:h1:2026-10-18',
        payload: {'date': '2026-10-18', 'done': true},
      );

      final restarted = fresh();
      await restarted.bind(server);
      expect(restarted.items.value.map((m) => m.label), [
        'Mark “r1” as done',
        'Check in “Walk”',
      ]);

      http.on('POST', '/api/v1/reminders/r1/complete', {'id': 'r1'});
      http.on('POST', '/api/v1/habits/h1/check-ins', {'id': 'h1'});
      final result = await restarted.drain(dio);

      expect(result.done, 2);
      expect(restarted.items.value, isEmpty);
      expect(http.sent('POST', '/api/v1/habits/h1/check-ins').single.body, {
        'date': '2026-10-18',
        'done': true,
      });
      final again = fresh();
      await again.bind(server);
      expect(again.items.value, isEmpty);
    },
  );

  test('the same thing twice is one change and the newest wins', () async {
    await complete('r1');
    await complete('r1');
    await queue.enqueue(
      kind: MutationKinds.habitCheckIn,
      targetId: 'h1',
      label: 'Check in',
      group: 'habit:h1:d',
      payload: {'done': true},
    );
    await queue.enqueue(
      kind: MutationKinds.habitCheckIn,
      targetId: 'h1',
      label: 'Undo check-in',
      group: 'habit:h1:d',
      payload: {'done': false},
    );

    expect(queue.items.value, hasLength(2));
    expect(queue.items.value.last.payload['done'], false);
    expect(queue.items.value.last.label, 'Undo check-in');
  });

  test(
    'a lost connection stops the run, keeps the change queued and backs off',
    () async {
      await complete('r1');
      await complete('r2');
      network.offline = true;

      final result = await queue.drain(dio);

      expect(result.stopped, 'offline');
      expect(
        queue.items.value.every((m) => m.status == MutationStatus.queued),
        isTrue,
      );
      expect(queue.items.value.first.attempts, 1);
      expect(
        queue.items.value.last.attempts,
        0,
        reason: 'it did not cycle through every item',
      );
      expect(queue.items.value.first.nextAttemptAt!.isAfter(now), isTrue);

      network.offline = false;
      http.on('POST', '/api/v1/reminders/r1/complete', {});
      http.on('POST', '/api/v1/reminders/r2/complete', {});
      expect(
        (await queue.drain(dio)).done,
        1,
        reason: 'r1 is still backing off',
      );
      now = now.add(const Duration(minutes: 1));
      expect((await queue.drain(dio)).done, 1);
      expect(queue.items.value, isEmpty);
    },
  );

  test('after several failed tries it waits for the owner instead of retrying forever', () async {
    await complete('r1');
    network.offline = true;
    for (var i = 0; i < MutationQueue.maxAttempts; i++) {
      now = now.add(const Duration(hours: 1));
      await queue.drain(dio);
    }

    expect(queue.items.value.single.status, MutationStatus.failed);
    expect(
      queue.items.value.single.message,
      contains('Retry it when you are back online'),
    );
    await queue.retry(queue.items.value.single.id);
    expect(queue.items.value.single.status, MutationStatus.queued);
    expect(queue.items.value.single.attempts, 0);
  });

  test(
    'an expired sign-in stops everything without using up attempts',
    () async {
      await complete('r1');
      await complete('r2');
      http.on('POST', '/api/v1/reminders/r1/complete', {}, status: 401);

      final result = await queue.drain(dio);

      expect(result.stopped, 'signed_out');
      expect(queue.items.value.map((m) => m.attempts), [0, 0]);
      expect(http.sent('POST', '/api/v1/reminders/r2/complete'), isEmpty);
    },
  );

  test('a deleted target is an explained conflict and a refusal is a failure, neither is called done', () async {
    await complete('gone');
    await complete('bad');
    http.on('POST', '/api/v1/reminders/gone/complete', {}, status: 404);
    http.on('POST', '/api/v1/reminders/bad/complete', {}, status: 400);

    final result = await queue.drain(dio);

    expect(result.done, 0);
    expect(queue.items.value[0].status, MutationStatus.conflict);
    expect(queue.items.value[0].message, contains('no longer exists'));
    expect(queue.items.value[1].status, MutationStatus.failed);
    await queue.discard(queue.items.value[0].id);
    expect(queue.items.value, hasLength(1));
  });

  test('an old change expires visibly and is not applied unasked', () async {
    await complete('r1');
    now = now.add(MutationQueue.maxAge + const Duration(hours: 1));
    http.on('POST', '/api/v1/reminders/r1/complete', {});

    final result = await queue.drain(dio);

    expect(result.done, 0);
    expect(queue.items.value.single.status, MutationStatus.expired);
    expect(http.sent('POST', '/api/v1/reminders/r1/complete'), isEmpty);
  });

  test('the queue is bounded and says why', () async {
    for (var i = 0; i < MutationQueue.maxItems; i++) {
      await complete('r$i');
    }
    final over = await queue.enqueue(
      kind: MutationKinds.reminderComplete,
      targetId: 'x',
      label: 'x',
      group: 'reminder:x',
    );

    expect(over.mutation, isNull);
    expect(over.problem, contains('Too many changes'));
  });

  test(
    'another server or another account never sees or sends the changes',
    () async {
      await complete('r1');

      final other = fresh();
      await other.bind('https://other.invalid');
      expect(other.items.value, isEmpty);
      final back = fresh();
      await back.bind(server);
      expect(
        back.items.value,
        isEmpty,
        reason: 'binding to another server removed them',
      );

      await complete('r2');
      await queue.clear();
      final after = fresh();
      await after.bind(server);
      expect(after.items.value, isEmpty);

      await complete('r3');
      final wrongDio = Dio(BaseOptions(baseUrl: 'https://other.invalid'))
        ..httpClientAdapter = network;
      expect((await queue.drain(wrongDio)).done, 0);
      expect(http.requests, isEmpty);
    },
  );
}
