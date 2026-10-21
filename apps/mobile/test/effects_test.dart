import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/settings/motion_gallery_screen.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

Widget _host(Widget child, {bool reduced = false}) => MaterialApp(
  theme: buildJarvisTheme(),
  builder: (context, child) => MediaQuery(
    data: MediaQuery.of(context).copyWith(disableAnimations: reduced),
    child: child!,
  ),
  home: Scaffold(body: Center(child: child)),
);

void main() {
  test('springs start at zero, finish at one and overshoot on the way', () {
    for (final curve in [JarvisSprings.soft, JarvisSprings.pop]) {
      expect(curve.transform(0), closeTo(0, 1e-9));
      expect(curve.transform(1), closeTo(1, 1e-9));
    }
    final samples = [
      for (var i = 1; i < 100; i++) JarvisSprings.pop.transform(i / 100),
    ];
    expect(samples.any((value) => value > 1), isTrue);
  });

  test('rolling numbers find one number and keep what surrounds it', () {
    expect(RollingNumber.parse('12')!.value, 12);
    expect(RollingNumber.parse('-3')!.value, -3);
    final money = RollingNumber.parse('€1,204.50')!;
    expect(money.value, 1204.5);
    expect(money.format(987.25), '€987.25');
    expect(money.format(1234567), '€1,234,567.00');
    final percent = RollingNumber.parse('82%')!;
    expect(percent.format(41.4), '41%');
    expect(RollingNumber.parse('3 tasks')!.format(1), '1 tasks');
    expect(RollingNumber.parse('3/5'), isNull);
    expect(RollingNumber.parse('no number'), isNull);
    expect(RollingNumber.parse('2 of 3'), isNull);
  });

  testWidgets('a counting number starts at zero the first time', (
    tester,
  ) async {
    await tester.pumpWidget(_host(const RollingNumber('€250', countUp: true)));
    expect(find.text('€0'), findsOneWidget);
    await tester.pumpAndSettle();
    expect(find.text('€250'), findsOneWidget);
  });

  testWidgets('a rolling number lands on its new value', (tester) async {
    final value = ValueNotifier('3');
    addTearDown(value.dispose);
    await tester.pumpWidget(
      _host(
        ValueListenableBuilder<String>(
          valueListenable: value,
          builder: (context, text, _) => RollingNumber(text),
        ),
      ),
    );
    expect(find.text('3'), findsOneWidget);
    value.value = '1,250';
    await tester.pump(const Duration(milliseconds: 100));
    expect(find.text('1,250'), findsNothing);
    await tester.pumpAndSettle();
    expect(find.text('1,250'), findsOneWidget);
    expect(find.bySemanticsLabel('1,250'), findsOneWidget);
  });

  testWidgets('one-shot effects settle and leave the screen idle', (
    tester,
  ) async {
    final done = ValueNotifier(false);
    addTearDown(done.dispose);
    await tester.pumpWidget(
      _host(
        ValueListenableBuilder<bool>(
          valueListenable: done,
          builder: (context, value, _) => Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const PopIn(child: Text('pop')),
              const BlurIn(child: Text('blur')),
              Sheen(trigger: value, child: const Text('sheen')),
              CelebrationBurst(trigger: value, child: const Text('burst')),
              TiltOnPress(child: const Text('tilt')),
            ],
          ),
        ),
      ),
    );
    expect(tester.hasRunningAnimations, isTrue);
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
    done.value = true;
    await tester.pump();
    expect(tester.hasRunningAnimations, isTrue);
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
    final gesture = await tester.startGesture(
      tester.getCenter(find.text('tilt')),
    );
    await tester.pump(const Duration(milliseconds: 50));
    await gesture.up();
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
    for (final label in ['pop', 'blur', 'sheen', 'burst', 'tilt']) {
      expect(find.text(label), findsOneWidget);
    }
  });

  testWidgets('Reduce Motion shows effects in place without animating', (
    tester,
  ) async {
    final done = ValueNotifier(false);
    addTearDown(done.dispose);
    await tester.pumpWidget(
      _host(
        reduced: true,
        ValueListenableBuilder<bool>(
          valueListenable: done,
          builder: (context, value, _) => Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              const PopIn(child: Text('pop')),
              const BlurIn(child: Text('blur')),
              CelebrationBurst(trigger: value, child: const Text('burst')),
              const RollingNumber('7'),
            ],
          ),
        ),
      ),
    );
    expect(tester.hasRunningAnimations, isFalse);
    done.value = true;
    await tester.pump();
    expect(tester.hasRunningAnimations, isFalse);
    expect(find.text('7'), findsOneWidget);
  });

  testWidgets('pages slide toward the side you move to and only the new one '
      'takes taps', (tester) async {
    final page = ValueNotifier(0);
    addTearDown(page.dispose);
    final taps = <int>[];
    await tester.pumpWidget(
      _host(
        ValueListenableBuilder<int>(
          valueListenable: page,
          builder: (context, value, _) => PageSwitcher(
            index: value,
            child: SizedBox(
              key: ValueKey(value),
              width: 200,
              height: 200,
              child: TextButton(
                onPressed: () => taps.add(value),
                child: Text('Page $value'),
              ),
            ),
          ),
        ),
      ),
    );
    page.value = 1;
    await tester.pump(const Duration(milliseconds: 60));
    final entering = tester.getTopLeft(find.text('Page 1')).dx;
    final leaving = tester.getTopLeft(find.text('Page 0')).dx;
    expect(entering, greaterThan(leaving));
    await tester.tap(find.text('Page 1'), warnIfMissed: false);
    await tester.pumpAndSettle();
    expect(find.text('Page 0'), findsNothing);
    await tester.tap(find.text('Page 1'));
    expect(taps, everyElement(1));
    page.value = 0;
    await tester.pump(const Duration(milliseconds: 60));
    expect(
      tester.getTopLeft(find.text('Page 0')).dx,
      lessThan(tester.getTopLeft(find.text('Page 1')).dx),
    );
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
  });

  test('streamed replies are let out a few whole words at a time', () {
    const text = 'One two  three four';
    expect(StreamingMarkdown.advance(text, 0, 1), 4);
    expect(StreamingMarkdown.advance(text, 4, 2), 15);
    expect(StreamingMarkdown.advance(text, 15, 5), text.length);
    expect(StreamingMarkdown.wordsAfter(text, 4), 3);
    expect(StreamingMarkdown.wordsAfter(text, text.length), 0);
  });

  testWidgets('a burst of streamed text flows in and settles complete', (
    tester,
  ) async {
    final text = ValueNotifier('Hello');
    addTearDown(text.dispose);
    await tester.pumpWidget(
      _host(
        ValueListenableBuilder<String>(
          valueListenable: text,
          builder: (context, value, _) => StreamingMarkdown(data: value),
        ),
      ),
    );
    expect(find.textContaining('Hello'), findsWidgets);
    text.value = 'Hello there, this arrived all at once';
    await tester.pump(const Duration(milliseconds: 10));
    // Only part of the burst is out after the first step.
    expect(find.textContaining('all at once'), findsNothing);
    await tester.pumpAndSettle();
    expect(find.textContaining('all at once'), findsOneWidget);
    expect(tester.hasRunningAnimations, isFalse);
  });

  testWidgets('the orb thinks, follows a voice and blooms, then rests', (
    tester,
  ) async {
    final state = ValueNotifier<(bool, int?)>((true, null));
    addTearDown(state.dispose);
    var level = .8;
    await tester.pumpWidget(
      _host(
        ValueListenableBuilder<(bool, int?)>(
          valueListenable: state,
          builder: (context, value, _) => JarvisOrb(
            size: 40,
            thinking: value.$1,
            pulse: value.$2,
            level: value.$1 ? () => level : null,
          ),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 300));
    expect(tester.hasRunningAnimations, isTrue);
    level = 0;
    state.value = (false, 1);
    await tester.pump();
    expect(tester.hasRunningAnimations, isTrue);
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
  });

  testWidgets('with Reduce Motion the thinking orb stays still', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(reduced: true, const JarvisOrb(size: 40, thinking: true)),
    );
    await tester.pump(const Duration(milliseconds: 300));
    expect(tester.hasRunningAnimations, isFalse);
  });

  testWidgets('pulling down shows the orb and refreshes once', (tester) async {
    var refreshes = 0;
    final done = Completer<void>();
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Scaffold(
          body: OrbRefresh(
            onRefresh: () {
              refreshes++;
              return done.future;
            },
            child: ListView(
              key: const Key('list'),
              physics: const AlwaysScrollableScrollPhysics(),
              children: const [SizedBox(height: 2000)],
            ),
          ),
        ),
      ),
    );
    await tester.fling(
      find.byKey(const Key('list')),
      const Offset(0, 400),
      1000,
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 400));
    expect(refreshes, 1);
    expect(find.bySemanticsLabel('Refreshing'), findsOneWidget);
    done.complete();
    await tester.pumpAndSettle();
    expect(find.bySemanticsLabel('Refreshing'), findsNothing);
  });

  testWidgets('loading placeholders take the shape of the page', (
    tester,
  ) async {
    for (final shape in SkeletonShape.values) {
      await tester.pumpWidget(_host(reduced: true, SkeletonList(shape: shape)));
      await tester.pumpAndSettle();
      expect(find.bySemanticsLabel('Loading'), findsOneWidget);
      expect(tester.takeException(), isNull);
    }
  });

  testWidgets('the effects gallery plays every effect', (
    tester,
  ) async {
    tester.view.physicalSize = const Size(800, 3000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(theme: buildJarvisTheme(), home: const MotionGalleryScreen()),
    );
    await tester.pump(const Duration(seconds: 2));
    await tester.tap(find.byKey(const Key('gallery-replay')));
    await tester.tap(find.byKey(const Key('gallery-check')));
    await tester.tap(find.text('Roll'));
    await tester.tap(find.text('Three'));
    await tester.pump(const Duration(seconds: 2));
    expect(find.text('Page 3'), findsOneWidget);
    await tester.tap(find.byKey(const Key('gallery-stream')));
    // Bursts arrive every 260 ms and each group of words takes a step to
    // fade in; give the whole story time to flow out.
    for (var i = 0; i < 80; i++) {
      await tester.pump(const Duration(milliseconds: 150));
    }
    expect(find.textContaining('dentist reminder'), findsOneWidget);
  });
}
