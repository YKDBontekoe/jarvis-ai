import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/decisions/decision_editor_screen.dart';
import 'package:jarvis_mobile/features/journal/journal_editor_screen.dart';
import 'package:jarvis_mobile/features/projects/project_editor.dart';

import 'support/fixture_http.dart';

/// Editors ask before dropping edits, and only when something changed.
void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> open(
    WidgetTester tester,
    Widget Function(FixtureHttp) editor,
  ) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: TextButton(
                onPressed: () => Navigator.of(
                  context,
                ).push<void>(MaterialPageRoute(builder: (_) => editor(http))),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
  }

  final editors =
      <String, ({Widget Function(FixtureHttp) build, Finder field})>{
        'journal': (
          build: (h) => JournalEditorScreen(http: h.client()),
          field: find.byType(TextField).first,
        ),
        'project': (
          build: (h) => ProjectEditorScreen(http: h.client()),
          field: find.byType(TextFormField).first,
        ),
        'decision': (
          build: (h) => DecisionEditorScreen(http: h.client()),
          field: find.byType(TextField).first,
        ),
      };

  for (final entry in editors.entries) {
    group(entry.key, () {
      testWidgets('closes straight away when nothing changed', (tester) async {
        await open(tester, entry.value.build);
        await tester.pageBack();
        await tester.pumpAndSettle();
        expect(find.text('Discard your changes?'), findsNothing);
        expect(find.text('open'), findsOneWidget);
      });

      testWidgets('asks after an edit; Keep editing stays, Discard leaves', (
        tester,
      ) async {
        await open(tester, entry.value.build);
        await tester.enterText(entry.value.field, 'Something new');
        await tester.pump();
        await tester.pageBack();
        await tester.pumpAndSettle();
        expect(find.text('Discard your changes?'), findsOneWidget);

        await tester.tap(find.text('Keep editing'));
        await tester.pumpAndSettle();
        expect(find.text('Something new'), findsOneWidget);
        expect(find.text('open'), findsNothing);

        await tester.pageBack();
        await tester.pumpAndSettle();
        await tester.tap(find.text('Discard'));
        await tester.pumpAndSettle();
        expect(find.text('open'), findsOneWidget);
      });

      testWidgets('reverting the edit closes without asking', (tester) async {
        await open(tester, entry.value.build);
        await tester.enterText(entry.value.field, 'x');
        await tester.pump();
        await tester.enterText(entry.value.field, '');
        await tester.pump();
        await tester.pageBack();
        await tester.pumpAndSettle();
        expect(find.text('Discard your changes?'), findsNothing);
        expect(find.text('open'), findsOneWidget);
      });
    });
  }
}
