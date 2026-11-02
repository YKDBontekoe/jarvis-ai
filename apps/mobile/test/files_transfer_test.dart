import 'dart:async';
import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/files/file_download_io.dart'
    as io_download;
import 'package:jarvis_mobile/features/files/files_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _file(String id, String name, {int size = 2048}) => {
  'id': id,
  'fileName': name,
  'sizeBytes': size,
  'processingStatus': 'indexed',
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/files', [
      _file('f1', 'taxes.pdf'),
      _file('f2', 'notes.txt'),
    ]);
    http.on('GET', '/api/v1/collections', <Object>[]);
  });

  Future<void> show(
    WidgetTester tester, {
    Future<PickedUpload?> Function()? pick,
    Future<bool> Function(
      Dio,
      String,
      String, {
      int? expectedBytes,
      void Function(int, int)? onProgress,
      CancelToken? cancelToken,
    })?
    download,
    Future<void> Function(String)? onOpen,
  }) async {
    tester.view.physicalSize = const Size(900, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: FilesScreen(
          http: http.client(),
          pickUpload: pick,
          download: download,
          onOpenConversation: onOpen,
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  PickedUpload picked() => (name: 'scan.pdf', bytes: Uint8List(400 * 1024));

  group('uploads', () {
    testWidgets('show byte progress and can be cancelled without success', (
      tester,
    ) async {
      final gate = Completer<void>();
      http.beforeRespond = (options) async {
        if (options.method == 'POST') {
          options.onSendProgress?.call(200 * 1024, 400 * 1024);
          await gate.future;
        }
      };
      http.on('POST', '/api/v1/files', {'id': 'f3'});
      await show(tester, pick: () async => picked());

      await tester.tap(find.text('Upload'));
      for (var i = 0; i < 5; i++) {
        await tester.pump(const Duration(milliseconds: 50));
      }

      expect(find.text('scan.pdf'), findsOneWidget);
      expect(find.text('200.0 KB of 400.0 KB'), findsOneWidget);
      expect(find.byKey(const Key('upload-progress-1')), findsOneWidget);

      await tester.tap(find.byKey(const Key('upload-cancel-1')));
      await tester.pumpAndSettle();

      expect(find.text('Upload cancelled. Nothing was added.'), findsOneWidget);
      // Cancelling is not success: the list was not reloaded.
      expect(http.sent('GET', '/api/v1/files'), hasLength(1));
    });

    testWidgets('a failed upload can be retried and then disappears', (
      tester,
    ) async {
      http.on('POST', '/api/v1/files', {'title': 'No'}, status: 500);
      await show(tester, pick: () async => picked());

      await tester.tap(find.text('Upload'));
      await tester.pumpAndSettle();
      expect(find.text('Jarvis could not upload this file.'), findsOneWidget);

      http.on('POST', '/api/v1/files', {'id': 'f3'});
      await tester.tap(find.byKey(const Key('upload-retry-1')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('upload-1')), findsNothing);
      expect(http.sent('POST', '/api/v1/files'), hasLength(2));
      // The list reloaded, so the new file's own status takes over.
      expect(http.sent('GET', '/api/v1/files'), hasLength(2));
    });

    testWidgets('an oversized file is refused before any request', (
      tester,
    ) async {
      await show(
        tester,
        pick: () async =>
            (name: 'big.pdf', bytes: Uint8List(maxUploadBytes + 1)),
      );
      await tester.tap(find.text('Upload'));
      await tester.pumpAndSettle();

      expect(find.text('Choose a non-empty file up to 20 MB.'), findsOneWidget);
      expect(http.sent('POST', '/api/v1/files'), isEmpty);
    });
  });

  group('per-item busy', () {
    testWidgets('asking about one file leaves the other row usable', (
      tester,
    ) async {
      final gate = Completer<void>();
      http.beforeRespond = (options) async {
        if (options.path.endsWith('/ask')) await gate.future;
      };
      http.on('POST', '/api/v1/files/f1/ask', {
        'conversationId': 'c1',
        'status': 'indexed',
      });
      final opened = <String>[];
      await show(tester, onOpen: (id) async => opened.add(id));

      await tester.tap(find.byType(PopupMenuButton<String>).first);
      await tester.pumpAndSettle();
      await tester.tap(find.text('Ask Jarvis about this'));
      await tester.pump();

      // Row f1 shows a spinner; row f2 still has its menu.
      expect(find.byType(CircularProgressIndicator), findsOneWidget);
      expect(find.byType(PopupMenuButton<String>), findsOneWidget);
      await tester.tap(find.byType(PopupMenuButton<String>));
      await tester.pump(const Duration(milliseconds: 400));
      expect(find.text('Download and open'), findsOneWidget);
      await tester.tapAt(const Offset(5, 5));
      await tester.pump(const Duration(milliseconds: 400));

      gate.complete();
      await tester.pumpAndSettle();
      expect(opened, ['c1']);
    });
  });

  group('downloads', () {
    testWidgets('show progress, can be cancelled and leave other rows alone', (
      tester,
    ) async {
      CancelToken? token;
      final gate = Completer<void>();
      await show(
        tester,
        download:
            (http, id, name, {expectedBytes, onProgress, cancelToken}) async {
              token = cancelToken;
              onProgress?.call(512, 2048);
              await gate.future;
              return false;
            },
      );

      await tester.tap(find.text('taxes.pdf'));
      await tester.pump();
      await tester.pump();

      expect(find.byKey(const Key('download-progress-f1')), findsOneWidget);
      expect(find.byKey(const Key('download-progress-f2')), findsNothing);
      final bar = tester.widget<LinearProgressIndicator>(
        find.byKey(const Key('download-progress-f1')),
      );
      expect(bar.value, .25);

      await tester.tap(find.byKey(const Key('download-cancel-f1')));
      expect(token!.isCancelled, isTrue);
      gate.complete();
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('download-progress-f1')), findsNothing);
    });
  });

  group('download storage', () {
    late Directory dir;

    setUp(() async {
      dir = await Directory.systemTemp.createTemp('jarvis-files-test');
    });
    tearDown(() async => dir.delete(recursive: true));

    test('a completed download is opened again without downloading', () async {
      http.on('GET', '/api/v1/files/f1/content', 'x' * 100);
      final opened = <String>[];
      final client = http.client();

      final first = await io_download.downloadAndOpen(
        client,
        'f1',
        'taxes.pdf',
        directory: dir,
        open: (path) async => opened.add(path),
      );
      expect(first, isFalse);
      final stored = File(io_download.downloadPathFor(dir, 'f1', 'taxes.pdf'));
      expect(stored.existsSync(), isTrue);
      final size = stored.lengthSync();

      final second = await io_download.downloadAndOpen(
        client,
        'f1',
        'taxes.pdf',
        expectedBytes: size,
        directory: dir,
        open: (path) async => opened.add(path),
      );
      expect(second, isTrue);
      expect(http.sent('GET', '/api/v1/files/f1/content'), hasLength(1));
      expect(opened, hasLength(2));
    });

    test('a copy with the wrong size is fetched again', () async {
      http.on('GET', '/api/v1/files/f1/content', 'y' * 50);
      final stored = File(io_download.downloadPathFor(dir, 'f1', 'taxes.pdf'))
        ..writeAsStringSync('short');

      final reused = await io_download.downloadAndOpen(
        http.client(),
        'f1',
        'taxes.pdf',
        expectedBytes: 9999,
        directory: dir,
        open: (_) async {},
      );
      expect(reused, isFalse);
      expect(http.sent('GET', '/api/v1/files/f1/content'), hasLength(1));
      expect(stored.lengthSync(), isNot(5));
    });

    test('a failed download leaves no partial file behind', () async {
      http.on('GET', '/api/v1/files/f1/content', 'nope', status: 500);
      await expectLater(
        io_download.downloadAndOpen(
          http.client(),
          'f1',
          'taxes.pdf',
          directory: dir,
          open: (_) async {},
        ),
        throwsA(isA<DioException>()),
      );
      expect(dir.listSync(), isEmpty);
    });
  });
}
