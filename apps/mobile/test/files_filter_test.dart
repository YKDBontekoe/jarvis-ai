import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/files/files_filter.dart';
import 'package:jarvis_mobile/features/files/files_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, dynamic> file(String id, String name, String status) => {
  'id': id,
  'fileName': name,
  'sizeBytes': 2048,
  'processingStatus': status,
};

final files = [
  file('f1', 'Taxes 2025.pdf', 'indexed'),
  file('f2', 'receipt.jpg', 'indexed'),
  file('f3', 'notes.txt', 'processing'),
  file('f4', 'budget.csv', 'failed'),
  file('f5', 'Café menu.PDF', 'scanning'),
];

List<String> ids(List<Map<String, dynamic>> list) => [
  for (final f in list) f['id'] as String,
];

void main() {
  group('filterFiles', () {
    test('searches the file name, ignoring case and accents', () {
      expect(ids(filterFiles(files, query: 'tax')), ['f1']);
      expect(ids(filterFiles(files, query: 'cafe')), ['f5']);
      expect(ids(filterFiles(files, query: '.PDF')), ['f1', 'f5']);
    });

    test('filters by type and by stage, and both together', () {
      expect(ids(filterFiles(files, kinds: {FileKind.pdf})), ['f1', 'f5']);
      expect(ids(filterFiles(files, kinds: {FileKind.pdf, FileKind.image})), [
        'f1',
        'f2',
        'f5',
      ]);
      expect(ids(filterFiles(files, stage: FileStage.failed)), ['f4']);
      expect(
        ids(filterFiles(files, kinds: {FileKind.pdf}, stage: FileStage.ready)),
        ['f1'],
      );
    });

    test('anything not ready or failed counts as processing', () {
      expect(ids(filterFiles(files, stage: FileStage.processing)), [
        'f3',
        'f5',
      ]);
    });

    test('no filters keeps everything in order', () {
      expect(ids(filterFiles(files)), ['f1', 'f2', 'f3', 'f4', 'f5']);
    });

    test('collection membership is listed per file', () {
      final byFile = collectionNamesByFile([
        {
          'name': 'Taxes',
          'fileIds': ['f1', 'f2'],
        },
        {
          'name': 'Receipts',
          'fileIds': ['f2'],
        },
        {'name': 'Broken', 'fileIds': 'nope'},
      ]);
      expect(byFile['f1'], ['Taxes']);
      expect(byFile['f2'], ['Taxes', 'Receipts']);
      expect(byFile['f3'], isNull);
    });
  });

  group('Files screen', () {
    late FixtureHttp http;

    setUp(() {
      http = FixtureHttp();
      http.on('GET', '/api/v1/files', files);
      http.on('GET', '/api/v1/collections', [
        {
          'id': 'c1',
          'name': 'Taxes',
          'fileIds': ['f1'],
        },
      ]);
    });

    Future<void> show(WidgetTester tester) async {
      tester.view.physicalSize = const Size(900, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          home: FilesScreen(http: http.client()),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('search narrows the list, hides collections and can be '
        'cleared', (tester) async {
      await show(tester);
      expect(find.text('notes.txt'), findsOneWidget);
      expect(find.text('In Taxes'), findsOneWidget);

      await tester.enterText(find.byKey(const Key('files-search')), 'receipt');
      await tester.pumpAndSettle();
      expect(find.text('receipt.jpg'), findsOneWidget);
      expect(find.text('notes.txt'), findsNothing);

      await tester.tap(find.byKey(const Key('files-clear-filters')));
      await tester.pumpAndSettle();
      expect(find.text('notes.txt'), findsOneWidget);
    });

    testWidgets('type and status chips combine', (tester) async {
      await show(tester);
      await tester.tap(find.byKey(const Key('files-kind-pdf')));
      await tester.pumpAndSettle();
      expect(find.text('Taxes 2025.pdf'), findsOneWidget);
      expect(find.text('receipt.jpg'), findsNothing);

      await tester.tap(find.byKey(const Key('files-stage-processing')));
      await tester.pumpAndSettle();
      expect(find.text('Taxes 2025.pdf'), findsNothing);
      expect(find.text('Café menu.PDF'), findsOneWidget);
    });

    testWidgets('no matching files is not the same as no files', (
      tester,
    ) async {
      await show(tester);
      await tester.enterText(find.byKey(const Key('files-search')), 'zzz');
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('files-no-match')), findsOneWidget);
      expect(find.text('No files yet'), findsNothing);
    });

    testWidgets('a library with no files still says No files yet', (
      tester,
    ) async {
      http.on('GET', '/api/v1/files', <Object>[]);
      http.on('GET', '/api/v1/collections', <Object>[]);
      await show(tester);
      expect(find.text('No files yet'), findsOneWidget);
      expect(find.byKey(const Key('files-search')), findsNothing);
    });
  });
}
