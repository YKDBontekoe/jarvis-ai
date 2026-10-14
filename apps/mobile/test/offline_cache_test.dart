import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/offline_cache.dart';
import 'package:jarvis_mobile/ui/offline_banner.dart';

/// Answers with canned JSON until [offline] is set, then fails like a dropped connection.
class _FlakyAdapter implements HttpClientAdapter {
  bool offline = false;
  int status = 200;
  Object body = {'items': []};
  int calls = 0;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    calls++;
    if (offline) {
      throw DioException.connectionError(
        requestOptions: options,
        reason: 'Network is unreachable',
      );
    }
    return ResponseBody.fromString(
      jsonEncode(body),
      status,
      headers: {
        Headers.contentTypeHeader: ['application/json'],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}

void main() {
  late _FlakyAdapter adapter;
  late Dio http;
  late OfflineCache cache;
  var now = DateTime.utc(2026, 10, 14, 9);

  setUp(() {
    now = DateTime.utc(2026, 10, 14, 9);
    adapter = _FlakyAdapter();
    http = Dio(BaseOptions(baseUrl: 'https://jarvis.test'))
      ..httpClientAdapter = adapter;
    cache = OfflineCache(store: MemoryOfflineCacheStore(), clock: () => now);
    cache.attach(http);
  });

  test('serves the last good answer when the connection drops', () async {
    adapter.body = {
      'items': ['Buy milk'],
    };
    await http.get<Object?>(
      '/api/v1/tasks',
      queryParameters: {'state': 'open'},
    );
    await pumpEventQueue();
    adapter.offline = true;
    now = now.add(const Duration(hours: 2));

    final response = await http.get<Object?>(
      '/api/v1/tasks',
      queryParameters: {'state': 'open'},
    );

    expect(response.data, {
      'items': ['Buy milk'],
    });
    expect(isCachedResponse(response), isTrue);
    expect(cache.isOffline, isTrue);
    expect(cache.state.value!.savedAt, DateTime.utc(2026, 10, 14, 9).toLocal());
  });

  test('comes back online on the next answer from the server', () async {
    await http.get<Object?>('/api/v1/tasks');
    await pumpEventQueue();
    adapter.offline = true;
    await http.get<Object?>('/api/v1/tasks');
    expect(cache.isOffline, isTrue);

    adapter.offline = false;
    expect(await cache.retry(), isTrue);

    expect(cache.isOffline, isFalse);
  });

  test('server errors and writes never fall back to saved data', () async {
    await http.get<Object?>('/api/v1/tasks');
    await pumpEventQueue();
    adapter.status = 500;

    await expectLater(
      http.get<Object?>('/api/v1/tasks'),
      throwsA(isA<DioException>()),
    );
    adapter.offline = true;
    await expectLater(
      http.post<Object?>('/api/v1/tasks', data: {'title': 'x'}),
      throwsA(isA<DioException>()),
    );
    expect(cache.state.value?.savedAt, isNull);
  });

  test('sign-in and secret routes are never saved', () async {
    await http.get<Object?>('/api/v1/auth/session');
    await http.get<Object?>('/api/v1/integrations/github/credentials');
    await pumpEventQueue();
    adapter.offline = true;

    await expectLater(
      http.get<Object?>('/api/v1/auth/session'),
      throwsA(isA<DioException>()),
    );
    await expectLater(
      http.get<Object?>('/api/v1/integrations/github/credentials'),
      throwsA(isA<DioException>()),
    );
  });

  test('old saved answers expire and sign-out clears everything', () async {
    await http.get<Object?>('/api/v1/tasks');
    await http.get<Object?>('/api/v1/memories');
    await pumpEventQueue();
    adapter.offline = true;
    now = now.add(OfflineCache.maxAge + const Duration(minutes: 1));
    await expectLater(
      http.get<Object?>('/api/v1/tasks'),
      throwsA(isA<DioException>()),
    );

    now = DateTime.utc(2026, 10, 14, 10);
    await cache.clear();
    await expectLater(
      http.get<Object?>('/api/v1/memories'),
      throwsA(isA<DioException>()),
    );
  });

  test('query parameter order does not change the cache key', () {
    final a = RequestOptions(
      path: '/api/v1/x',
      baseUrl: 'https://jarvis.test',
      queryParameters: {'a': 1, 'b': 2},
    );
    final b = RequestOptions(
      path: '/api/v1/x',
      baseUrl: 'https://jarvis.test',
      queryParameters: {'b': 2, 'a': 1},
    );
    expect(OfflineCache.keyFor(a), OfflineCache.keyFor(b));
  });

  test('offline age reads naturally', () {
    final now = DateTime(2026, 10, 14, 12);
    expect(offlineAge(now, now), 'just now');
    expect(
      offlineAge(now.subtract(const Duration(minutes: 5)), now),
      '5 min ago',
    );
    expect(offlineAge(now.subtract(const Duration(hours: 3)), now), '3 h ago');
    expect(offlineAge(now.subtract(const Duration(days: 1)), now), 'yesterday');
    expect(
      offlineAge(now.subtract(const Duration(days: 4)), now),
      '4 days ago',
    );
  });

  testWidgets('the banner shows while offline and retry clears it', (
    tester,
  ) async {
    await tester.runAsync(() async {
      await http.get<Object?>('/api/v1/tasks');
      await pumpEventQueue();
    });
    await tester.pumpWidget(
      MaterialApp(
        builder: (context, child) => OfflineBanner(cache: cache, child: child!),
        home: const Scaffold(body: Text('Tasks')),
      ),
    );
    expect(find.byKey(const ValueKey('offline-banner')), findsNothing);

    adapter.offline = true;
    await tester.runAsync(() => http.get<Object?>('/api/v1/tasks'));
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('offline-banner')), findsOneWidget);
    expect(find.textContaining('Offline · showing data from'), findsOneWidget);

    adapter.offline = false;
    await tester.tap(find.text('Retry'));
    await tester.runAsync(() => pumpEventQueue());
    await tester.pumpAndSettle();
    expect(find.byKey(const ValueKey('offline-banner')), findsNothing);
    expect(find.text('Tasks'), findsOneWidget);
  });
}
