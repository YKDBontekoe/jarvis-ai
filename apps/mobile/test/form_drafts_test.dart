import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/journal/journal_editor_screen.dart';
import 'package:jarvis_mobile/storage/draft_store.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    SharedPreferences.setMockInitialValues({});
  });

  Future<void> openEditor(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: TextButton(
              onPressed: () => Navigator.of(context).push<void>(
                MaterialPageRoute(
                  builder: (_) => JournalEditorScreen(http: http.client()),
                ),
              ),
              child: const Text('open'),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  testWidgets('typing saves a draft that can be restored after reopening', (
    tester,
  ) async {
    await openEditor(tester);
    await tester.enterText(
      find.byKey(const Key('journal-content')),
      'A long day',
    );
    await tester.pump(const Duration(seconds: 1));
    await tester.pump();

    // The app is closed without saving.
    await tester.pumpWidget(const SizedBox());
    await tester.pump();

    await openEditor(tester);
    expect(find.byKey(const Key('draft-banner')), findsOneWidget);
    expect(find.text('A long day'), findsNothing);
    await tester.tap(find.byKey(const Key('draft-restore')));
    await tester.pumpAndSettle();
    expect(find.text('A long day'), findsOneWidget);
    expect(find.byKey(const Key('draft-banner')), findsNothing);
  });

  testWidgets('Discard on the banner forgets the draft', (tester) async {
    await openEditor(tester);
    await tester.enterText(find.byKey(const Key('journal-content')), 'Oops');
    await tester.pump(const Duration(seconds: 1));
    await tester.pumpWidget(const SizedBox());
    await tester.pump();

    await openEditor(tester);
    await tester.tap(find.byKey(const Key('draft-discard')));
    await tester.pumpAndSettle();
    await tester.pumpWidget(const SizedBox());
    await tester.pump();

    final store = await DraftStore.open();
    expect(
      store.read(
        DraftStore.keyFor(http.client().options.baseUrl, 'journal:new'),
      ),
      isNull,
    );
  });

  testWidgets('a successful save removes the draft', (tester) async {
    http.on('POST', '/api/v1/journal', {'id': 'j1'});
    await openEditor(tester);
    await tester.enterText(find.byKey(const Key('journal-content')), 'Saved');
    await tester.pump(const Duration(seconds: 1));
    await tester.tap(find.byKey(const Key('journal-save')));
    await tester.pumpAndSettle();

    final store = await DraftStore.open();
    expect(
      store.read(
        DraftStore.keyFor(http.client().options.baseUrl, 'journal:new'),
      ),
      isNull,
    );
    expect(http.sent('POST', '/api/v1/journal'), hasLength(1));
  });

  test('drafts expire after 30 days and clearAll removes them', () async {
    final store = await DraftStore.open();
    store.write('k', {
      'a': 'b',
    }, now: DateTime.now().subtract(const Duration(days: 31)));
    await store.flush();
    expect((await DraftStore.open()).read('k'), isNull);

    store.write('k', {'a': 'b'});
    await store.flush();
    expect((await DraftStore.open()).read('k'), {'a': 'b'});
    await DraftStore.clearAll();
    expect((await DraftStore.open()).read('k'), isNull);
  });
}
