import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/theme.dart';

ToolStep done(String tool, {List<String> refs = const []}) =>
    ToolStep(tool, ToolStepStatus.completed, refs: refs);

void main() {
  group('foldToolSteps', () {
    test('two finished steps stay as they are', () {
      final segments = foldToolSteps([done('SearchMemory'), done('ListFiles')]);
      expect(segments.map((s) => s.folded), [false, false]);
      expect(segments.map((s) => s.start), [0, 1]);
    });

    test('three or more finished steps in a row fold together', () {
      final segments = foldToolSteps([
        done('SearchMemory'),
        done('ListFiles'),
        done('CreateReminder'),
        done('GetTime'),
      ]);
      expect(segments, hasLength(1));
      expect(segments.single.folded, isTrue);
      expect(segments.single.steps, hasLength(4));
    });

    test('running and failed steps break a run and stay visible', () {
      final segments = foldToolSteps([
        done('A'),
        done('B'),
        done('C'),
        const ToolStep('D', ToolStepStatus.failed),
        done('E'),
        done('F'),
        const ToolStep('G', ToolStepStatus.running),
      ]);
      expect(segments.map((s) => (s.start, s.folded, s.steps.length)), [
        (0, true, 3),
        (3, false, 1),
        (4, false, 1),
        (5, false, 1),
        (6, false, 1),
      ]);
    });

    test('the summary gives the count and what was made', () {
      expect(
        foldedStepsSummary([done('A'), done('B'), done('C')]),
        '3 steps done',
      );
      expect(
        foldedStepsSummary([
          done('A', refs: ['reminder:1']),
          done('B', refs: ['task:2', 'task:3']),
          done('C'),
        ]),
        '3 steps done · made 3 things',
      );
      expect(
        foldedStepsSummary([
          done('A', refs: ['reminder:1']),
          done('B'),
          done('C'),
        ]),
        '3 steps done · made 1 thing',
      );
    });
  });

  group('ToolRunView', () {
    Future<void> show(WidgetTester tester, ToolRunEntry run) =>
        tester.pumpWidget(
          MaterialApp(
            theme: buildJarvisTheme(),
            home: Scaffold(body: ToolRunView(run: run)),
          ),
        );

    testWidgets('a long finished run folds, and every detail is one tap away', (
      tester,
    ) async {
      await show(
        tester,
        ToolRunEntry([
          done('SearchMemory'),
          done('ListFiles'),
          done('CreateReminder'),
          const ToolStep('ListFiles', ToolStepStatus.failed),
          const ToolStep('CreateReminder', ToolStepStatus.running),
        ]),
      );

      expect(find.text('3 steps done'), findsOneWidget);
      expect(find.text('Searched memory'), findsNothing);
      // Failures and running steps are never hidden.
      expect(find.text('Checking your files failed'), findsOneWidget);
      expect(find.text('Scheduling a reminder'), findsOneWidget);

      await tester.tap(find.byKey(const Key('tool-fold-expand')));
      await tester.pump();
      expect(find.text('Searched memory'), findsOneWidget);
      expect(find.text('3 steps done'), findsNothing);

      await tester.tap(find.byKey(const Key('tool-fold-collapse')));
      await tester.pump();
      expect(find.text('3 steps done'), findsOneWidget);
    });

    testWidgets('a short run is shown as it always was', (tester) async {
      await show(
        tester,
        ToolRunEntry([done('SearchMemory'), done('ListFiles')]),
      );
      expect(find.text('Searched memory'), findsOneWidget);
      expect(find.byKey(const Key('tool-fold-expand')), findsNothing);
    });
  });
}
