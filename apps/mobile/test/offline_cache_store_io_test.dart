import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/offline_cache_store_io.dart';

void main() {
  late Directory root;

  setUp(
    () async => root = await Directory.systemTemp.createTemp('jarvis-cache'),
  );
  tearDown(() async {
    if (await root.exists()) await root.delete(recursive: true);
  });

  test('saves, reads back and clears entries on disk', () async {
    final store = FileOfflineCacheStore(
      directory: () async => Directory('${root.path}/offline_cache'),
    );

    await store.write('https://jarvis.test/api/v1/tasks?', '{"a":1}');

    expect(await store.read('https://jarvis.test/api/v1/tasks?'), '{"a":1}');
    expect(await store.read('https://jarvis.test/api/v1/other?'), isNull);
    final names = Directory('${root.path}/offline_cache')
        .listSync()
        .map((entity) => entity.uri.pathSegments.last);
    expect(names.single, matches(RegExp(r'^[0-9a-f]{16}\.json$')));

    await store.clear();
    expect(await store.read('https://jarvis.test/api/v1/tasks?'), isNull);
  });

  test('keeps working in memory when the directory is unavailable', () async {
    final store = FileOfflineCacheStore(
      directory: () async => throw const FileSystemException('denied'),
    );

    await store.write('k', 'v');

    expect(await store.read('k'), 'v');
  });
}
