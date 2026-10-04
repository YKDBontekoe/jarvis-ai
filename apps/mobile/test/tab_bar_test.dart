import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/jarvis_tab_bar.dart';
import 'package:jarvis_mobile/features/shell/tab_icons.dart';
import 'package:jarvis_mobile/theme.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: Align(alignment: Alignment.bottomCenter, child: child)),
);

void main() {
  testWidgets('the bar shows four tabs and the orb', (tester) async {
    await tester.pumpWidget(
      _host(
        JarvisTabBar(
          selected: JarvisTab.home,
          onSelect: (_) {},
          onJarvis: () {},
        ),
      ),
    );
    for (final tab in JarvisTab.values) {
      expect(find.byKey(Key('tab-${tab.name}')), findsOneWidget);
      expect(find.text(tab.label), findsOneWidget);
    }
    expect(find.byKey(const Key('tab-jarvis')), findsOneWidget);
    expect(find.byKey(const Key('tab-attention')), findsNothing);
  });

  testWidgets('tapping reports the tab, and the orb is not a tab', (
    tester,
  ) async {
    final chosen = <JarvisTab>[];
    var orb = 0;
    await tester.pumpWidget(
      _host(
        JarvisTabBar(
          selected: JarvisTab.home,
          onSelect: chosen.add,
          onJarvis: () => orb++,
        ),
      ),
    );
    await tester.tap(find.byKey(const Key('tab-chats')));
    await tester.tap(find.byKey(const Key('tab-everything')));
    await tester.tap(find.byKey(const Key('tab-you')));
    await tester.tap(find.byKey(const Key('tab-jarvis')));
    expect(chosen, [JarvisTab.chats, JarvisTab.everything, JarvisTab.you]);
    expect(orb, 1);
  });

  testWidgets('Chats shows a dot when something is unread', (tester) async {
    await tester.pumpWidget(
      _host(
        JarvisTabBar(
          selected: JarvisTab.home,
          onSelect: (_) {},
          onJarvis: () {},
          chatsAttention: true,
        ),
      ),
    );
    expect(find.byKey(const Key('tab-attention')), findsOneWidget);
    expect(find.bySemanticsLabel('Chats, unread'), findsOneWidget);
  });

  testWidgets('the selected tab is exposed to accessibility', (tester) async {
    final handle = tester.ensureSemantics();
    await tester.pumpWidget(
      _host(
        JarvisTabBar(
          selected: JarvisTab.everything,
          onSelect: (_) {},
          onJarvis: () {},
        ),
      ),
    );
    expect(
      tester.getSemantics(find.byKey(const Key('tab-everything'))),
      matchesSemantics(
        label: 'Everything',
        isButton: true,
        isSelected: true,
        hasSelectedState: true,
        hasTapAction: true,
      ),
    );
    handle.dispose();
  });

  testWidgets('the rail offers the same destinations', (tester) async {
    final chosen = <JarvisTab>[];
    await tester.pumpWidget(
      _host(
        SizedBox(
          height: 600,
          child: JarvisNavRail(
            selected: null,
            onSelect: chosen.add,
            onJarvis: () {},
          ),
        ),
      ),
    );
    for (final tab in JarvisTab.values) {
      expect(find.byKey(Key('tab-${tab.name}')), findsOneWidget);
    }
    await tester.tap(find.byKey(const Key('tab-you')));
    expect(chosen, [JarvisTab.you]);
  });

  group('icons', () {
    testWidgets('every glyph paints, open or filled', (tester) async {
      await tester.pumpWidget(
        _host(
          Row(
            children: [
              for (final glyph in TabGlyph.values) ...[
                TabIcon(glyph: glyph, color: Colors.black),
                TabIcon(glyph: glyph, color: Colors.black, fill: Colors.grey),
              ],
            ],
          ),
        ),
      );
      expect(tester.takeException(), isNull);
      expect(find.byType(TabIcon), findsNWidgets(TabGlyph.values.length * 2));
    });

    test('path data stays inside the 24 unit grid', () {
      for (final data in [
        'M4.5 10.2 12 4.2l7.5 6V19a1.3 1.3 0 0 1-1.3 1.3H15v-5.2a1 1 0 0 0-1-1h-4a1 1 0 0 0-1 1v5.2H5.8A1.3 1.3 0 0 1 4.5 19z',
        'M5.2 19.6c1.1-3.3 3.7-5.1 6.8-5.1s5.7 1.8 6.8 5.1',
      ]) {
        final bounds = parseSvgPath(data).getBounds();
        expect(bounds.left, greaterThanOrEqualTo(0));
        expect(bounds.top, greaterThanOrEqualTo(0));
        expect(bounds.right, lessThanOrEqualTo(24));
        expect(bounds.bottom, lessThanOrEqualTo(24));
        expect(bounds.width, greaterThan(5));
      }
    });

    test('the parser follows absolute and relative commands', () {
      final square = parseSvgPath('M2 2H8V8H2z').getBounds();
      expect(square, const Rect.fromLTRB(2, 2, 8, 8));
      final relative = parseSvgPath('m2 2h6v6h-6z').getBounds();
      expect(relative, square);
      final implicitLines = parseSvgPath('M0 0 4 0 4 4').getBounds();
      expect(implicitLines, const Rect.fromLTRB(0, 0, 4, 4));
    });

    test('an unsupported command is an error, not a silent gap', () {
      expect(() => parseSvgPath('M0 0Q1 1 2 2'), throwsFormatException);
    });
  });
}
