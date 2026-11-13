import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home/home_layout_cards.dart';
import 'package:jarvis_mobile/features/tiles/home_layout_sync.dart';
import 'package:jarvis_mobile/theme.dart';

void main() {
  for (final dark in [false, true]) {
    testWidgets('the Home layout cards fit 320pt at 3x text, '
        '${dark ? 'dark' : 'light'}', (tester) async {
      tester.view.physicalSize = const Size(320, 2400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.reset);
      final hidden = <String>{};
      var kept = 0;
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(
            brightness: dark ? Brightness.dark : Brightness.light,
          ),
          home: MediaQuery(
            data: const MediaQueryData(
              size: Size(320, 2400),
              textScaler: TextScaler.linear(3),
            ),
            child: Scaffold(
              body: SingleChildScrollView(
                padding: const EdgeInsets.all(16),
                child: StatefulBuilder(
                  builder: (context, setState) => Column(
                    children: [
                      JarvisHomeChangeCard(
                        change: const JarvisHomeChange(
                          revision: 2,
                          note:
                              'Habits and journal up front, finance moved off',
                        ),
                        onKeep: () => kept++,
                        onUndo: () {},
                      ),
                      HomeCardsEditor(
                        isShown: (card) => !hidden.contains(card),
                        onChanged: (card, visible) => setState(
                          () =>
                              visible ? hidden.remove(card) : hidden.add(card),
                        ),
                        onAskJarvis: () {},
                      ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(tester.takeException(), isNull);
      expect(find.bySemanticsLabel(RegExp('Keep it')), findsWidgets);
      await tester.tap(find.byKey(const Key('home-jarvis-change-keep')));
      expect(kept, 1);
      await tester.ensureVisible(find.byKey(const Key('home-card-brief')));
      await tester.tap(find.byKey(const Key('home-card-brief')));
      await tester.pumpAndSettle();
      expect(hidden, {HomeCardIds.brief});
    });
  }
}
