import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:flutter/rendering.dart' show ScrollDirection;
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
  dockTests();
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
      expect(find.bySemanticsLabel(tab.label), findsOneWidget);
      // Only the open tab writes its name; the others are icons.
      expect(
        find.text(tab.label),
        tab == JarvisTab.home ? findsOneWidget : findsNothing,
      );
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

void dockTests() {
  group('the dock', () {
    Future<void> host(
      WidgetTester tester,
      Widget dock, {
      Size size = const Size(390, 800),
      double textScale = 1,
    }) async {
      tester.view.physicalSize = size;
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context)
                .copyWith(textScaler: TextScaler.linear(textScale)),
            child: child!,
          ),
          home: Scaffold(bottomNavigationBar: dock, body: const SizedBox()),
        ),
      );
      await tester.pumpAndSettle();
    }

    JarvisTabBar dock({
      JarvisTab selected = JarvisTab.chats,
      bool compact = false,
      VoidCallback? onUnfold,
      Widget? strip,
    }) => JarvisTabBar(
      selected: selected,
      onSelect: (_) {},
      onJarvis: () {},
      compact: compact,
      onUnfold: onUnfold,
      strip: strip,
    );

    testWidgets('is two pieces: a capsule of tabs and a separate orb', (
      tester,
    ) async {
      await host(tester, dock());
      final capsule = tester.getRect(find.byKey(const Key('dock-capsule')));
      final orb = tester.getRect(find.byKey(const Key('dock-orb')));
      expect(capsule.right, lessThan(orb.left));
      expect(capsule.overlaps(orb), isFalse);
      for (final tab in JarvisTab.values) {
        expect(
          tester.getRect(find.byKey(Key('tab-${tab.name}'))).overlaps(orb),
          isFalse,
        );
      }
      expect(find.byKey(const Key('tab-jarvis')), findsOneWidget);
    });

    testWidgets('the open tab names itself and the others are icons', (
      tester,
    ) async {
      // The test font is wider than the app's, so give the capsule room.
      await host(
        tester,
        dock(selected: JarvisTab.everything),
        size: const Size(520, 800),
      );
      expect(find.text('Everything'), findsOneWidget);
      expect(find.text('Home'), findsNothing);
      expect(find.text('Chats'), findsNothing);
    });

    testWidgets('folds to the open tab and a tap unfolds it', (tester) async {
      var unfolded = 0;
      await host(tester, dock(compact: true, onUnfold: () => unfolded++));
      expect(find.byKey(const Key('tab-chats')), findsOneWidget);
      expect(find.byKey(const Key('tab-home')), findsNothing);
      expect(find.byKey(const Key('tab-you')), findsNothing);
      await tester.tap(find.byKey(const Key('tab-chats')));
      expect(unfolded, 1);
    });

    testWidgets('every control stays at least 44pt, even at 2x text on a '
        '320pt phone', (tester) async {
      await host(
        tester,
        dock(selected: JarvisTab.everything),
        size: const Size(320, 640),
        textScale: 2,
      );
      expect(tester.takeException(), isNull);
      for (final tab in JarvisTab.values) {
        final size = tester.getSize(find.byKey(Key('tab-${tab.name}')));
        expect(size.width, greaterThanOrEqualTo(44), reason: tab.name);
        expect(size.height, greaterThanOrEqualTo(44), reason: tab.name);
      }
      expect(
        tester.getSize(find.byKey(const Key('tab-jarvis'))).shortestSide,
        greaterThanOrEqualTo(44),
      );
      // Nothing is pushed off the screen.
      final orb = tester.getRect(find.byKey(const Key('dock-orb')));
      expect(orb.right, lessThanOrEqualTo(320));
    });

    testWidgets('a contextual strip sits above both pieces', (tester) async {
      await host(
        tester,
        dock(strip: const SizedBox(key: Key('strip'), height: 40)),
      );
      final strip = tester.getRect(find.byKey(const Key('strip')));
      final capsule = tester.getRect(find.byKey(const Key('dock-capsule')));
      expect(strip.bottom, lessThanOrEqualTo(capsule.top));
    });
  });

  group('dockCompactAfterScroll', () {
    test('scrolling down folds, scrolling up or reaching the top unfolds', () {
      bool next(ScrollDirection d, double pixels, bool current) =>
          dockCompactAfterScroll(
            direction: d,
            pixels: pixels,
            current: current,
          );
      expect(next(ScrollDirection.reverse, 300, false), isTrue);
      expect(next(ScrollDirection.reverse, 10, false), isFalse);
      expect(next(ScrollDirection.forward, 300, true), isFalse);
      expect(next(ScrollDirection.idle, 300, true), isTrue);
      expect(next(ScrollDirection.idle, 5, true), isFalse);
      expect(next(ScrollDirection.idle, 300, false), isFalse);
    });
  });
}
