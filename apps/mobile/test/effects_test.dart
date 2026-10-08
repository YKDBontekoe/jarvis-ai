import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/theme.dart';
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

  test('rolling numbers only roll plain whole numbers', () {
    expect(RollingNumber.parse('12'), 12);
    expect(RollingNumber.parse('-3'), -3);
    expect(RollingNumber.parse('1,204'), 1204);
    expect(RollingNumber.parse('3/5'), isNull);
    expect(RollingNumber.parse('€12'), isNull);
    expect(RollingNumber.parse('1.5'), isNull);
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
}
