import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/command_palette.dart';
import 'package:jarvis_mobile/features/search/context_commands.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

class _Subject extends StatefulWidget {
  const _Subject({required this.context, required this.open});

  final String context;
  final void Function(BuildContext context) open;

  @override
  State<_Subject> createState() => _SubjectState();
}

class _SubjectState extends State<_Subject> with PaletteContextMixin<_Subject> {
  @override
  String get paletteContextValue => widget.context;

  @override
  Widget build(BuildContext context) => Scaffold(
    body: Center(
      child: ElevatedButton(
        key: const Key('open-palette'),
        onPressed: () => widget.open(context),
        child: const Text('Open'),
      ),
    ),
  );
}

void main() {
  late FixtureHttp http;
  final opened = <String>[];

  setUp(() {
    SharedPreferences.setMockInitialValues({});
    http = FixtureHttp();
    opened.clear();
  });

  Future<void> openPalette(WidgetTester tester, String contextValue) async {
    tester.view.physicalSize = const Size(800, 1600);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: _Subject(
          context: contextValue,
          open: (context) => showJarvisCommandPalette(
            context,
            http: http.client(),
            onConversation: (_) async {},
            onUtility: opened.add,
          ),
        ),
      ),
    );
    await tester.tap(find.byKey(const Key('open-palette')));
    await tester.pumpAndSettle();
  }

  test(
    'each kind of subject has its own commands and unknown ones have none',
    () {
      expect(
        contextCommandsFor('project:p1').single.label,
        'Draft a project update',
      );
      expect(
        contextCommandsFor('meeting:m1').single.label,
        'Prepare this meeting',
      );
      expect(
        contextCommandsFor('goal:g1').single.label,
        "Plan this week's steps",
      );
      expect(
        contextCommandsFor('document:d1').single.label,
        'Save this draft to the Library',
      );
      expect(
        contextCommandsFor('subscription:s1').single.label,
        'Prepare a price pack',
      );
      expect(contextCommandsFor(null), isEmpty);
      expect(contextCommandsFor('nonsense'), isEmpty);
      expect(contextCommandsFor('project:'), isEmpty);
      expect(contextCommandsFor('file:f1'), isEmpty);
    },
  );

  test(
    'typing matches English and Dutch words, and too little matches nothing',
    () {
      expect(matchContextCommands('project:p1', ''), hasLength(1));
      expect(matchContextCommands('project:p1', 'upd'), hasLength(1));
      expect(matchContextCommands('project:p1', 'voortgang'), hasLength(1));
      expect(matchContextCommands('project:p1', 'u'), isEmpty);
      expect(matchContextCommands('project:p1', 'banana'), isEmpty);
    },
  );

  testWidgets(
    'the palette opened on a project offers its action first and shows the result it created',
    (tester) async {
      http.on('POST', '/api/v1/documents', {
        'draft': {'id': 'd9'},
      }, status: 201);
      await openPalette(tester, 'project:p1');

      expect(find.text('FOR THIS PROJECT'), findsOneWidget);
      await tester.tap(find.byKey(const Key('context-command-0')));
      await tester.pumpAndSettle();

      expect(http.sent('POST', '/api/v1/documents').single.body, {
        'kind': 'project_update',
        'projectId': 'p1',
      });
      expect(opened, ['document:d9']); // the owner lands on the linked result
    },
  );

  testWidgets(
    'a failed command claims nothing and says why, and the palette stays open',
    (tester) async {
      http.on('POST', '/api/v1/meetings/m1/prep', {
        'detail': 'That meeting was not found.',
      }, status: 400);
      await openPalette(tester, 'meeting:m1');

      await tester.tap(find.byKey(const Key('context-command-0')));
      await tester.pumpAndSettle();

      expect(
        find.textContaining('Prepare this meeting did not work'),
        findsOneWidget,
      );
      expect(find.textContaining('not found'), findsOneWidget);
      expect(opened, isEmpty);
      expect(find.byKey(const Key('context-command-0')), findsOneWidget);
    },
  );

  testWidgets(
    'typing narrows the contextual commands and a subject-less palette shows none',
    (tester) async {
      await openPalette(tester, 'goal:g1');
      await tester.enterText(find.byType(TextField).first, 'plan');
      await tester.pump();
      expect(find.byKey(const Key('context-command-0')), findsOneWidget);
      await tester.enterText(find.byType(TextField).first, 'zzz');
      await tester.pump();
      expect(find.byKey(const Key('context-command-0')), findsNothing);
    },
  );

  testWidgets('leaving a screen removes its subject from the palette', (
    tester,
  ) async {
    await tester.pumpWidget(
      MaterialApp(
        home: _Subject(context: 'project:p1', open: (_) {}),
      ),
    );
    expect(currentPaletteContext, 'project:p1');

    await tester.pumpWidget(const MaterialApp(home: SizedBox()));

    expect(currentPaletteContext, isNull);
  });
}
