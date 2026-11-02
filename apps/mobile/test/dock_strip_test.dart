import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/dock_strip.dart';
import 'package:jarvis_mobile/features/shell/jarvis_tab_bar.dart';
import 'package:jarvis_mobile/theme.dart';

DockStripData strip(
  DockStripKind kind,
  String key, {
  VoidCallback? onTap,
  VoidCallback? onDismiss,
}) => DockStripData(
  kind: kind,
  key: key,
  title: key,
  onTap: onTap ?? () {},
  onDismiss: onDismiss,
);

void main() {
  group('chooseDockStrip', () {
    final writing = strip(DockStripKind.writing, 'writing');
    final replied = strip(DockStripKind.replied, 'reply');
    final needs = strip(DockStripKind.needsYou, 'needs');

    test('Jarvis writing beats a finished reply, which beats Needs you', () {
      expect(
        chooseDockStrip(writing: writing, replied: replied, needsYou: needs),
        writing,
      );
      expect(chooseDockStrip(replied: replied, needsYou: needs), replied);
      expect(chooseDockStrip(needsYou: needs), needs);
    });

    test('there is no strip when nothing is going on', () {
      expect(chooseDockStrip(), isNull);
    });

    test('a hidden strip steps aside so the next one can show', () {
      expect(
        chooseDockStrip(
          replied: replied,
          needsYou: needs,
          dismissed: {'reply'},
        ),
        needs,
      );
      expect(chooseDockStrip(needsYou: needs, dismissed: {'needs'}), isNull);
    });

    test('a hidden strip comes back when something newer replaces it', () {
      final newer = strip(DockStripKind.needsYou, 'needs:newer');
      expect(chooseDockStrip(needsYou: newer, dismissed: {'needs'}), newer);
    });
  });

  group('DockStrip', () {
    Future<void> show(WidgetTester tester, DockStripData data) =>
        tester.pumpWidget(
          MaterialApp(
            theme: buildJarvisTheme(),
            home: Scaffold(
              bottomNavigationBar: JarvisTabBar(
                selected: JarvisTab.chats,
                onSelect: (_) {},
                onJarvis: () {},
                strip: DockStrip(data: data),
              ),
            ),
          ),
        );

    testWidgets('shows the title and opens its destination when tapped', (
      tester,
    ) async {
      var opened = 0;
      await show(
        tester,
        strip(DockStripKind.replied, 'Done', onTap: () => opened++),
      );
      expect(find.byKey(const Key('dock-strip-title')), findsOneWidget);
      await tester.tap(find.byKey(const Key('dock-strip-open')));
      expect(opened, 1);
    });

    testWidgets('can be hidden with the cross, when it allows that', (
      tester,
    ) async {
      var hidden = 0;
      await show(
        tester,
        strip(DockStripKind.needsYou, 'Rosa', onDismiss: () => hidden++),
      );
      await tester.tap(find.byKey(const Key('dock-strip-dismiss')));
      expect(hidden, 1);

      await show(tester, strip(DockStripKind.writing, 'writing'));
      await tester.pump();
      expect(find.byKey(const Key('dock-strip-dismiss')), findsNothing);
    });

    testWidgets('is announced as one thing to screen readers', (tester) async {
      final handle = tester.ensureSemantics();
      await show(
        tester,
        const DockStripData(
          kind: DockStripKind.needsYou,
          key: 'k',
          title: 'Rosa is waiting',
          detail: '14 h',
          action: 'Reply',
          onTap: _noop,
        ),
      );
      expect(
        find.bySemanticsLabel('Rosa is waiting, 14 h, Reply'),
        findsOneWidget,
      );
      handle.dispose();
    });
  });
}

void _noop() {}
