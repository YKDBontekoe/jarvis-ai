import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/main.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/jarvis_ui.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  darkTheme: buildJarvisTheme(brightness: Brightness.dark),
  home: Scaffold(body: child),
);

void main() {
  testWidgets('rapid content changes leave only the latest interactive page', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    final page = ValueNotifier(0);
    addTearDown(page.dispose);
    final opened = <int>[];
    await tester.pumpWidget(
      _host(
        ValueListenableBuilder<int>(
          valueListenable: page,
          builder: (context, value, _) => MotionSwitcher(
            child: SizedBox(
              key: ValueKey(value),
              width: 200,
              height: 200,
              child: TextButton(
                onPressed: () => opened.add(value),
                child: Text('Page $value'),
              ),
            ),
          ),
        ),
      ),
    );
    page.value = 1;
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 40));
    expect(find.bySemanticsLabel('Page 0'), findsNothing);
    page.value = 2;
    await tester.pump();
    await tester.tapAt(tester.getCenter(find.text('Page 2')));
    expect(opened, [2]);
    await tester.pumpAndSettle();
    expect(find.text('Page 0'), findsNothing);
    expect(find.text('Page 1'), findsNothing);
    expect(find.text('Page 2'), findsOneWidget);
    expect(tester.hasRunningAnimations, isFalse);
    semantics.dispose();
  });

  testWidgets('cancelled presses release feedback without activating', (
    tester,
  ) async {
    var activations = 0;
    await tester.pumpWidget(
      _host(
        Center(
          child: PressFeedback(
            builder: (context, highlight) => InkWell(
              onTap: () => activations++,
              onHighlightChanged: highlight,
              child: const SizedBox(
                width: 150,
                height: 50,
                child: Text('Press'),
              ),
            ),
          ),
        ),
      ),
    );
    final gesture = await tester.startGesture(
      tester.getCenter(find.text('Press')),
    );
    await tester.pump(const Duration(milliseconds: 200));
    final scale = tester.widget<AnimatedScale>(find.byType(AnimatedScale));
    expect(scale.scale, lessThan(1));
    await gesture.cancel();
    await tester.pumpAndSettle();
    expect(activations, 0);
    expect(tester.widget<AnimatedScale>(find.byType(AnimatedScale)).scale, 1);
  });

  testWidgets('Reduce Motion keeps pressed surfaces still and functional', (
    tester,
  ) async {
    var activations = 0;
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        builder: (context, child) => MediaQuery(
          data: MediaQuery.of(context).copyWith(disableAnimations: true),
          child: child!,
        ),
        home: Scaffold(
          body: Center(
            child: SurfaceCard(
              onTap: () => activations++,
              child: const Text('Open'),
            ),
          ),
        ),
      ),
    );
    final gesture = await tester.startGesture(
      tester.getCenter(find.text('Open')),
    );
    await tester.pump(const Duration(milliseconds: 200));
    expect(tester.widget<AnimatedScale>(find.byType(AnimatedScale)).scale, 1);
    await gesture.up();
    await tester.pumpAndSettle();
    expect(activations, 1);
    expect(tester.hasRunningAnimations, isFalse);
  });

  test('status styles share labels and colors across screens', () {
    expect(statusStyle('needs_approval').label, 'Needs approval');
    expect(statusStyle('needs_approval').color, JarvisColors.light.warning);
    expect(statusStyle('running').label, 'In progress');
    expect(statusStyle('completed').color, JarvisColors.light.success);
    expect(statusStyle('ready').label, 'Ready');
    expect(statusStyle('ready').color, JarvisColors.light.success);
    expect(statusStyle('failed').color, JarvisColors.light.danger);
    expect(statusStyle('some_new_state').label, 'Some new state');
    expect(statusStyle('').label, 'Unknown');
  });

  testWidgets('status pill renders the mapped label', (tester) async {
    await tester.pumpWidget(_host(StatusPill.forStatus('cancelled')));
    expect(find.text('Cancelled'), findsOneWidget);
  });

  testWidgets('FadeSlideIn eases rows in and skips motion when reduced', (
    tester,
  ) async {
    await tester.pumpWidget(_host(const FadeSlideIn(child: Text('row'))));
    expect(tester.widget<Opacity>(find.byType(Opacity)).opacity, 0);
    await tester.pumpAndSettle();
    expect(tester.widget<Opacity>(find.byType(Opacity)).opacity, 1);

    await tester.pumpWidget(
      MediaQuery(
        data: const MediaQueryData(disableAnimations: true),
        child: _host(const FadeSlideIn(child: Text('row'))),
      ),
    );
    expect(find.byType(Opacity), findsNothing);
    expect(find.text('row'), findsOneWidget);
  });

  testWidgets('FadeSlideIn starts in place for rows already on screen', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(const FadeSlideIn(animate: false, child: Text('row'))),
    );
    expect(tester.widget<Opacity>(find.byType(Opacity)).opacity, 1);
  });

  testWidgets('loading lists show placeholder rows instead of a spinner', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        ListScreenBody(
          loading: true,
          error: null,
          isEmpty: true,
          empty: const Text('empty'),
          onRetry: () {},
          child: const SizedBox(),
        ),
      ),
    );
    await tester.pump(const Duration(milliseconds: 500));
    expect(find.byType(SkeletonList), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsNothing);
    expect(find.bySemanticsLabel('Loading'), findsOneWidget);
  });

  testWidgets('pushed pages fade and grow in with the shared motion', (
    tester,
  ) async {
    final navigator = GlobalKey<NavigatorState>();
    await tester.pumpWidget(
      MaterialApp(
        navigatorKey: navigator,
        theme: buildJarvisTheme().copyWith(platform: TargetPlatform.android),
        home: const Text('first'),
      ),
    );
    navigator.currentState!.push(
      MaterialPageRoute<void>(builder: (_) => const Text('second')),
    );
    await tester.pump();
    await tester.pump(const Duration(milliseconds: 100));
    final scale = tester.widget<ScaleTransition>(
      find
          .ancestor(
            of: find.text('second'),
            matching: find.byType(ScaleTransition),
          )
          .first,
    );
    expect(scale.scale.value, inExclusiveRange(JarvisMotion.startScale, 1));
    await tester.pumpAndSettle();
    expect(find.text('second'), findsOneWidget);
  });

  testWidgets('header actions drop their label on narrow screens', (
    tester,
  ) async {
    Future<void> show(double width) async {
      tester.view.physicalSize = Size(width, 800);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        _host(
          HeaderAction(
            label: 'Collection',
            icon: Icons.add,
            onPressed: () {},
            collapsesWhenNarrow: true,
          ),
        ),
      );
    }

    await show(390);
    expect(find.text('Collection'), findsNothing);
    expect(find.byTooltip('Collection'), findsOneWidget);
    await show(800);
    expect(find.text('Collection'), findsOneWidget);
  });

  testWidgets('the orb is idle and decorative unless asked otherwise', (
    tester,
  ) async {
    final semantics = tester.ensureSemantics();
    await tester.pumpWidget(_host(const JarvisOrb(size: 64)));
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
    expect(find.bySemanticsLabel('Jarvis'), findsNothing);

    await tester.pumpWidget(
      _host(const JarvisOrb(size: 64, semanticLabel: 'Jarvis')),
    );
    expect(find.bySemanticsLabel('Jarvis'), findsOneWidget);
    semantics.dispose();
  });

  testWidgets('the listening orb animates until it stops listening', (
    tester,
  ) async {
    await tester.pumpWidget(_host(const JarvisOrb(size: 64, listening: true)));
    await tester.pump(const Duration(milliseconds: 500));
    expect(tester.hasRunningAnimations, isTrue);

    await tester.pumpWidget(_host(const JarvisOrb(size: 64)));
    await tester.pumpAndSettle();
    expect(tester.hasRunningAnimations, isFalse);
  });

  testWidgets(
    'list screen body keeps rows and shows a retry banner after a refresh error',
    (tester) async {
      var retried = false;
      await tester.pumpWidget(
        _host(
          ListScreenBody(
            loading: false,
            error: 'Could not refresh.',
            isEmpty: false,
            empty: const Text('empty'),
            onRetry: () => retried = true,
            child: const Text('existing-row'),
          ),
        ),
      );
      expect(find.text('existing-row'), findsOneWidget);
      expect(find.text('Could not refresh.'), findsOneWidget);
      await tester.tap(find.text('Retry'));
      expect(retried, isTrue);
    },
  );

  testWidgets('error state offers a retry action', (tester) async {
    var retried = false;
    await tester.pumpWidget(
      _host(ErrorState(message: 'Offline', onRetry: () => retried = true)),
    );
    expect(find.text('Offline'), findsOneWidget);
    await tester.tap(find.text('Retry'));
    expect(retried, isTrue);
  });

  testWidgets('confirm dialog resolves to the chosen action', (tester) async {
    bool? result;
    await tester.pumpWidget(
      _host(
        Builder(
          builder: (context) => TextButton(
            onPressed: () async => result = await showJarvisConfirm(
              context,
              title: 'Delete memory?',
              message: 'Jarvis will forget this.',
              confirmLabel: 'Delete',
              destructive: true,
              icon: Icons.delete_outline_rounded,
            ),
            child: const Text('Open'),
          ),
        ),
      ),
    );

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    expect(find.text('Delete memory?'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(result, isFalse);

    await tester.tap(find.text('Open'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Delete'));
    await tester.pumpAndSettle();
    expect(result, isTrue);
  });

  testWidgets('the tab bar moves between Home, Chats, Everything and You', (
    tester,
  ) async {
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-clock')), findsOneWidget);

    await tester.tap(find.byKey(const Key('tab-chats')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('chats-filter-field')), findsOneWidget);

    await tester.tap(find.byKey(const Key('tab-everything')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('everything-search')), findsOneWidget);

    await tester.tap(find.byKey(const Key('tab-you')));
    await tester.pumpAndSettle();
    expect(find.text('Your assistant'), findsOneWidget);
    expect(find.text('Appearance'), findsOneWidget);
    expect(find.text('Connected apps'), findsOneWidget);

    await tester.tap(find.byKey(const Key('tab-home')));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-clock')), findsOneWidget);
  });

  testWidgets('the orb opens a conversation and Back returns to the tab', (
    tester,
  ) async {
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('tab-jarvis')));
    await tester.pumpAndSettle();
    expect(find.text('Ask Jarvis anything'), findsOneWidget);
    expect(find.byKey(const Key('tab-home')), findsNothing);

    await tester.tap(find.byTooltip('Back'));
    await tester.pumpAndSettle();
    expect(find.byKey(const Key('home-clock')), findsOneWidget);
    expect(find.byKey(const Key('tab-home')), findsOneWidget);
  });
}
