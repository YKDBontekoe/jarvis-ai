import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/jarvis_tab_bar.dart';
import 'package:jarvis_mobile/features/shell/tab_icons.dart';
import 'package:jarvis_mobile/theme.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(
    body: Align(alignment: Alignment.bottomCenter, child: child),
  ),
);

void main() {
  testWidgets(
    'the busy orb stops moving with Reduce Motion and still opens chat',
    (tester) async {
      final reduced = ValueNotifier(false);
      addTearDown(reduced.dispose);
      var opened = 0;
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          builder: (context, child) => ValueListenableBuilder<bool>(
            valueListenable: reduced,
            builder: (context, value, _) => MediaQuery(
              data: MediaQuery.of(context).copyWith(disableAnimations: value),
              child: child!,
            ),
          ),
          home: Scaffold(
            body: JarvisTabBar(
              selected: JarvisTab.home,
              jarvisBusy: true,
              onSelect: (_) {},
              onJarvis: () => opened++,
            ),
          ),
        ),
      );
      await tester.pump(const Duration(milliseconds: 500));
      expect(tester.hasRunningAnimations, isTrue);
      expect(
        find.bySemanticsLabel('Jarvis is replying. Open chat'),
        findsOneWidget,
      );
      reduced.value = true;
      await tester.pumpAndSettle();
      expect(tester.hasRunningAnimations, isFalse);
      await tester.tap(find.byKey(const Key('tab-jarvis')));
      await tester.pumpAndSettle();
      expect(opened, 1);
      expect(tester.hasRunningAnimations, isFalse);
    },
  );

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
      expect(find.text(tab.label), findsNothing);
      expect(find.bySemanticsLabel(tab.label), findsOneWidget);
    }
    expect(find.byKey(const Key('tab-jarvis')), findsOneWidget);
    expect(find.text('Jarvis'), findsNothing);
    expect(find.bySemanticsLabel('Ask Jarvis'), findsOneWidget);
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
                TabIcon(glyph: glyph, color: Colors.black, selected: true),
              ],
            ],
          ),
        ),
      );
      expect(tester.takeException(), isNull);
      expect(find.byType(TabIcon), findsNWidgets(TabGlyph.values.length * 2));
    });
  });
}
