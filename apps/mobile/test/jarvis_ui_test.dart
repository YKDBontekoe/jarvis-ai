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

  testWidgets('menu opens the sidebar with destinations and recents', (
    tester,
  ) async {
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await tester.pumpAndSettle();
    expect(find.text('Recents'), findsNothing);
    await tester.tap(find.byTooltip('Menu'));
    await tester.pumpAndSettle();
    for (final label in ['Voice', 'Tasks', 'Memory', 'Recents', 'Settings']) {
      expect(find.text(label), findsOneWidget);
    }
  });

  testWidgets('settings opens from the sidebar', (tester) async {
    await tester.pumpWidget(const JarvisApp(skipAuthentication: true));
    await tester.pumpAndSettle();
    await tester.tap(find.byTooltip('Menu'));
    await tester.pumpAndSettle();
    await tester.tap(find.text('Settings'));
    await tester.pumpAndSettle();
    expect(find.text('Your assistant'), findsOneWidget);
    expect(find.text('Appearance'), findsOneWidget);
    expect(find.text('Integrations'), findsOneWidget);
  });
}
