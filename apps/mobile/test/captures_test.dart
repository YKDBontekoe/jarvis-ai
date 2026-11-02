import 'dart:convert';
import 'dart:io';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/captures/capture_models.dart';
import 'package:jarvis_mobile/features/captures/capture_store.dart';
import 'package:jarvis_mobile/features/captures/captures_screen.dart';
import 'package:jarvis_mobile/l10n/l10n.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

CaptureItem text(
  String id,
  String body, {
  String account = 'srv',
  DateTime? at,
}) => CaptureItem(
  id: id,
  kind: CaptureKind.text,
  text: body,
  account: account,
  createdAt: at ?? DateTime(2026, 11, 2, 9),
);

CaptureItem link(String id, {String account = 'srv'}) => CaptureItem(
  id: id,
  kind: CaptureKind.link,
  url: 'https://example.com/a',
  text: 'Worth reading',
  account: account,
  createdAt: DateTime(2026, 11, 2, 10),
);

CaptureItem file(String id, String name, int size, {String? path}) =>
    CaptureItem(
      id: id,
      kind: CaptureKind.file,
      fileName: name,
      filePath: path ?? '/tmp/none/$name',
      mimeType: name.endsWith('.png') ? 'image/png' : 'application/pdf',
      sizeBytes: size,
      account: 'srv',
      createdAt: DateTime(2026, 11, 2, 11),
    );

String indexOf(List<CaptureItem> items) =>
    jsonEncode([for (final i in items) i.toJson()]);

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  group('capture rules', () {
    test('photos, PDFs and text are kept; anything else is explained', () {
      expect(captureProblem(kind: CaptureKind.text), isNull);
      expect(captureProblem(kind: CaptureKind.link), isNull);
      expect(
        captureProblem(
          kind: CaptureKind.file,
          fileName: 'a.PDF',
          sizeBytes: 10,
        ),
        isNull,
      );
      expect(
        captureProblem(
          kind: CaptureKind.file,
          fileName: 'a.exe',
          sizeBytes: 10,
        ),
        contains('cannot keep this kind of file'),
      );
      expect(
        captureProblem(kind: CaptureKind.file, fileName: 'a.png', sizeBytes: 0),
        'This file is empty.',
      );
      expect(
        captureProblem(
          kind: CaptureKind.file,
          fileName: 'a.png',
          sizeBytes: maxCaptureBytes + 1,
        ),
        'This file is larger than 20 MB.',
      );
    });

    test('a capture round-trips and an empty one is dropped', () {
      final item = file('f1', 'a.png', 2048);
      expect(CaptureItem.fromJson(item.toJson())?.fileName, 'a.png');
      expect(
        CaptureItem.fromJson({
          'id': 'x',
          'kind': 'text',
          'createdAt': '2026-11-02T09:00:00Z',
          'text': '  ',
        }),
        isNull,
      );
      expect(CaptureItem.fromJson('nope'), isNull);
      expect(CaptureItem.fromJson({'id': 'x', 'kind': 'weird'}), isNull);
    });

    test('titles recognise each kind', () {
      expect(text('1', '\n\nFirst line\nsecond').title, 'First line');
      expect(link('2').title, 'https://example.com/a');
      expect(file('3', 'taxes.pdf', 1).title, 'taxes.pdf');
    });
  });

  group('CaptureStore', () {
    late MemoryCaptureBackend backend;
    late CaptureStore store;

    setUp(() {
      backend = MemoryCaptureBackend();
      store = CaptureStore(backend: backend);
    });

    test('binding an account lets the extension save for it and loads its '
        'captures newest first', () async {
      backend.index = indexOf([
        text('a', 'old', at: DateTime(2026, 11, 1)),
        text('b', 'new', at: DateTime(2026, 11, 2)),
      ]);
      await store.bind('srv');
      expect(backend.account, 'srv');
      expect(store.items.map((c) => c.id), ['b', 'a']);
    });

    test(
      'captures shared for another account are removed, not shown',
      () async {
        backend.index = indexOf([
          text('mine', 'x'),
          file('theirs', 'a.pdf', 5).withAccount('other'),
        ]);
        await store.bind('srv');
        expect(store.items.map((c) => c.id), ['mine']);
        expect(backend.deleted, ['/tmp/none/a.pdf']);
        expect(jsonDecode(backend.index!), hasLength(1));
      },
    );

    test('captures not yet stamped with an account are adopted', () async {
      backend.index = indexOf([text('new', 'x', account: '')]);
      await store.bind('srv');
      expect(store.items.single.account, 'srv');
      expect(jsonDecode(backend.index!).single['account'], 'srv');
    });

    test('removing one deletes its file and keeps the rest', () async {
      backend.index = indexOf([text('a', 'x'), file('b', 'a.pdf', 5)]);
      await store.bind('srv');
      await store.remove('b');
      expect(store.items.map((c) => c.id), ['a']);
      expect(backend.deleted, ['/tmp/none/a.pdf']);
    });

    test('signing out forgets everything and stops new captures', () async {
      backend.index = indexOf([text('a', 'x'), file('b', 'a.pdf', 5)]);
      await store.bind('srv');
      await store.clear();
      expect(store.items, isEmpty);
      expect(backend.account, isNull);
      expect(backend.index, '[]');
      expect(backend.deleted, ['/tmp/none/a.pdf']);
    });

    test('a capture counts as new until it has been presented', () async {
      backend.index = indexOf([text('a', 'x')]);
      await store.bind('srv');
      expect(await store.hasUnpresented(), isTrue);
      await store.markPresented();
      expect(await store.hasUnpresented(), isFalse);

      backend.index = indexOf([
        text('a', 'x'),
        text('b', 'y', at: DateTime(2026, 11, 3)),
      ]);
      await store.refresh();
      expect(await store.hasUnpresented(), isTrue);
    });

    test('a corrupt index is ignored', () async {
      backend.index = '{broken';
      await store.bind('srv');
      expect(store.items, isEmpty);
    });
  });

  group('Captures screen', () {
    late FixtureHttp http;
    late MemoryCaptureBackend backend;
    late CaptureStore store;
    late List<String> opened;
    late List<String> asked;

    setUp(() {
      http = FixtureHttp();
      backend = MemoryCaptureBackend();
      store = CaptureStore(backend: backend);
      opened = [];
      asked = [];
    });

    Future<void> show(
      WidgetTester tester,
      List<CaptureItem> items, {
      Locale? locale,
    }) async {
      backend.index = indexOf(items);
      await tester.runAsync(() => store.bind('srv'));
      tester.view.physicalSize = const Size(800, 2600);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          locale: locale,
          localizationsDelegates: AppLocalizations.localizationsDelegates,
          supportedLocales: AppLocalizations.supportedLocales,
          theme: buildJarvisTheme(),
          home: CapturesScreen(
            http: http.client(),
            store: store,
            onOpenDestination: opened.add,
            onAskInChat: asked.add,
          ),
        ),
      );
      await tester.pumpAndSettle();
    }

    testWidgets('nothing is sent anywhere until a choice is made', (
      tester,
    ) async {
      await show(tester, [text('a', 'Buy milk'), link('b')]);
      expect(find.text('2 WAITING FOR YOU'), findsOneWidget);
      expect(find.text('Buy milk'), findsWidgets);
      expect(http.requests, isEmpty);
      expect(asked, isEmpty);
    });

    testWidgets('an edited note is saved to the Library and the result opens', (
      tester,
    ) async {
      http.on('POST', '/api/v1/library/notes', {'id': 'n1'});
      await show(tester, [text('a', 'Buy milk')]);

      await tester.enterText(
        find.byKey(const Key('capture-edit-a')),
        'Buy oat milk',
      );
      await tester.tap(find.byKey(const Key('capture-save-a')));
      await tester.pumpAndSettle();

      final body =
          http.sent('POST', '/api/v1/library/notes').single.body as Map;
      expect(body['content'], 'Buy oat milk');
      expect(body['title'], 'Buy oat milk');
      expect(find.byKey(const Key('capture-a')), findsNothing);
      expect(find.text('Saved to your Library'), findsOneWidget);
      await tester.tap(find.byKey(const Key('capture-open-result')));
      expect(opened, ['entity:library:n1']);
      expect(store.items, isEmpty);
    });

    testWidgets('a link is clipped with the owner\'s note', (tester) async {
      http.on('POST', '/api/v1/library/clip', {'id': 'l1'});
      await show(tester, [link('b')]);
      await tester.tap(find.byKey(const Key('capture-save-b')));
      await tester.pumpAndSettle();

      final body = http.sent('POST', '/api/v1/library/clip').single.body as Map;
      expect(body['url'], 'https://example.com/a');
      expect(body['note'], 'Worth reading');
    });

    testWidgets('a failed save keeps the capture and says why', (tester) async {
      http.on('POST', '/api/v1/library/notes', {'title': 'No'}, status: 500);
      await show(tester, [text('a', 'Buy milk')]);
      await tester.tap(find.byKey(const Key('capture-save-a')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('captures-error')), findsOneWidget);
      expect(find.byKey(const Key('capture-a')), findsOneWidget);
      expect(store.items, hasLength(1));
    });

    testWidgets('Ask Jarvis sends the reviewed text only on that choice', (
      tester,
    ) async {
      await show(tester, [text('a', 'What is a zebra crossing?')]);
      await tester.tap(find.byKey(const Key('capture-ask-a')));
      await tester.pumpAndSettle();
      expect(asked, ['What is a zebra crossing?']);
    });

    testWidgets('Discard asks first, then removes the capture and its file', (
      tester,
    ) async {
      await show(tester, [file('f', 'photo.png', 2048)]);
      await tester.tap(find.byKey(const Key('capture-discard-f')));
      await tester.pumpAndSettle();
      expect(store.items, hasLength(1));
      await tester.tap(find.text('Discard').last);
      await tester.pumpAndSettle();
      expect(store.items, isEmpty);
      expect(backend.deleted, ['/tmp/none/photo.png']);
    });

    testWidgets('an unsupported or oversized file explains itself and can '
        'only be discarded', (tester) async {
      await show(tester, [
        file('x', 'tool.exe', 100),
        file('y', 'big.png', maxCaptureBytes + 5),
      ]);
      expect(find.byKey(const Key('capture-problem-x')), findsOneWidget);
      expect(
        find.textContaining('cannot keep this kind of file'),
        findsOneWidget,
      );
      expect(find.text('This file is larger than 20 MB.'), findsOneWidget);
      expect(find.byKey(const Key('capture-save-x')), findsNothing);
      expect(find.byKey(const Key('capture-discard-x')), findsOneWidget);
    });

    testWidgets('a file is uploaded to Files only when saved', (tester) async {
      final dir = await tester.runAsync(
        () => Directory.systemTemp.createTemp('captures-test'),
      );
      addTearDown(() => dir!.delete(recursive: true));
      final path = '${dir!.path}/note.pdf';
      await tester.runAsync(() => File(path).writeAsBytes([1, 2, 3]));
      http.on('POST', '/api/v1/files', {'id': 'f1'});
      await show(tester, [file('f', 'note.pdf', 3, path: path)]);

      expect(http.requests, isEmpty);
      await tester.tap(find.byKey(const Key('capture-save-f')));
      // The file is read from disk, which needs real time, not pumped frames.
      for (var i = 0; i < 5; i++) {
        await tester.runAsync(
          () => Future<void>.delayed(const Duration(milliseconds: 100)),
        );
        await tester.pump(const Duration(milliseconds: 50));
      }
      await tester.pumpAndSettle();
      expect(http.sent('POST', '/api/v1/files'), hasLength(1));
      expect(find.text('Saved “note.pdf” to Files'), findsOneWidget);
    });

    testWidgets('an empty inbox explains how captures arrive', (tester) async {
      await show(tester, const []);
      expect(find.text('Nothing shared yet'), findsOneWidget);
    });

    testWidgets('speaks Dutch when the app is Dutch', (tester) async {
      await show(tester, [text('a', 'Melk kopen')], locale: const Locale('nl'));
      expect(find.text('Vastgelegd'), findsOneWidget);
      expect(find.text('Vraag het Jarvis'), findsOneWidget);
      expect(find.text('Weggooien'), findsOneWidget);
      expect(find.text('Ask Jarvis'), findsNothing);
    });
  });
}
