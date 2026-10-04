import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/appearance.dart';
import 'package:jarvis_mobile/features/settings/appearance_screen.dart';
import 'package:jarvis_mobile/features/settings/settings_view.dart';
import 'package:jarvis_mobile/main.dart';
import 'package:jarvis_mobile/theme.dart';

void main() {
  const secureStorage = MethodChannel(
    'plugins.it_nomads.com/flutter_secure_storage',
  );

  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(secureStorage, (call) async => null);
  });

  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(secureStorage, null);
    TestWidgetsFlutterBinding.instance.platformDispatcher
        .clearPlatformBrightnessTestValue();
  });

  test('appearance preference maps to theme mode and round-trips storage', () {
    expect(AppearancePreference.parse(null), AppearancePreference.system);
    expect(AppearancePreference.parse('nope'), AppearancePreference.system);
    expect(AppearancePreference.parse('light').themeMode, ThemeMode.light);
    expect(AppearancePreference.parse('dark').themeMode, ThemeMode.dark);
    expect(AppearancePreference.system.themeMode, ThemeMode.system);
  });

  test('appearance controller persists the chosen preference', () async {
    final store = MemoryAppearanceStore();
    final controller = AppearanceController(store: store);
    await controller.load();
    expect(controller.preference, AppearancePreference.system);
    await controller.setPreference(AppearancePreference.dark);
    expect(store.value, 'dark');

    final restored = AppearanceController(store: store);
    await restored.load();
    expect(restored.preference, AppearancePreference.dark);
    expect(restored.themeMode, ThemeMode.dark);
  });

  test('dark theme uses the dark palette on surfaces and ink', () {
    final theme = buildJarvisTheme(brightness: Brightness.dark);
    final colors = theme.extension<JarvisColors>();
    expect(colors, JarvisColors.dark);
    expect(theme.brightness, Brightness.dark);
    expect(theme.scaffoldBackgroundColor, JarvisColors.dark.canvas);
    expect(theme.colorScheme.onSurface, JarvisColors.dark.ink);
  });

  testWidgets('settings lists appearance', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        darkTheme: buildJarvisTheme(brightness: Brightness.dark),
        home: Scaffold(body: SettingsView(connected: true, onOpen: (_) {})),
      ),
    );
    expect(find.byKey(const Key('settings-appearance')), findsOneWidget);
    expect(find.text('Appearance'), findsOneWidget);
    expect(find.text('Light, dark, or match this device'), findsOneWidget);
  });

  testWidgets('appearance screen can lock light, dark, or the device', (
    tester,
  ) async {
    final controller = AppearanceController(
      store: MemoryAppearanceStore('system'),
    );
    await controller.load();
    await tester.pumpWidget(
      AppearanceScope(
        controller: controller,
        child: MaterialApp(
          theme: buildJarvisTheme(),
          darkTheme: buildJarvisTheme(brightness: Brightness.dark),
          themeMode: controller.themeMode,
          home: const AppearanceScreen(),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Match device'), findsOneWidget);
    expect(find.text('Light'), findsOneWidget);
    expect(find.text('Dark'), findsOneWidget);

    await tester.tap(find.byKey(const Key('appearance-dark')));
    await tester.pumpAndSettle();
    expect(controller.preference, AppearancePreference.dark);

    await tester.tap(find.byKey(const Key('appearance-light')));
    await tester.pumpAndSettle();
    expect(controller.preference, AppearancePreference.light);

    await tester.tap(find.byKey(const Key('appearance-system')));
    await tester.pumpAndSettle();
    expect(controller.preference, AppearancePreference.system);
  });

  testWidgets('locked dark mode stays dark when the device is light', (
    tester,
  ) async {
    tester.platformDispatcher.platformBrightnessTestValue = Brightness.light;
    final appearance = AppearanceController(
      store: MemoryAppearanceStore('dark'),
    );
    await appearance.load();
    await tester.pumpWidget(
      JarvisApp(skipAuthentication: true, appearance: appearance),
    );
    await tester.pumpAndSettle();
    final context = tester.element(find.byKey(const Key('home-clock')));
    expect(Theme.of(context).brightness, Brightness.dark);
    expect(JarvisColors.of(context).canvas, JarvisColors.dark.canvas);
    expect(JarvisColors.of(context).ink, JarvisColors.dark.ink);
  });

  testWidgets('match device follows the platform brightness', (tester) async {
    tester.platformDispatcher.platformBrightnessTestValue = Brightness.dark;
    final appearance = AppearanceController(
      store: MemoryAppearanceStore('system'),
    );
    await appearance.load();
    await tester.pumpWidget(
      JarvisApp(skipAuthentication: true, appearance: appearance),
    );
    await tester.pumpAndSettle();
    var context = tester.element(find.byKey(const Key('home-clock')));
    expect(appearance.themeMode, ThemeMode.system);
    expect(Theme.of(context).brightness, Brightness.dark);
    expect(JarvisColors.of(context).isDark, isTrue);

    tester.platformDispatcher.platformBrightnessTestValue = Brightness.light;
    await tester.pumpAndSettle();
    context = tester.element(find.byKey(const Key('home-clock')));
    expect(Theme.of(context).brightness, Brightness.light);
    expect(JarvisColors.of(context).canvas, JarvisColors.light.canvas);
  });

  testWidgets(
    'settings opens appearance and applying dark restyles the shell',
    (tester) async {
      final appearance = AppearanceController(store: MemoryAppearanceStore());
      await appearance.load();
      await tester.pumpWidget(
        JarvisApp(skipAuthentication: true, appearance: appearance),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('tab-you')));
      await tester.pumpAndSettle();
      await tester.tap(find.text('Appearance'));
      await tester.pumpAndSettle();
      expect(find.text('Match device'), findsOneWidget);
      await tester.tap(find.text('Dark'));
      await tester.pumpAndSettle();
      expect(appearance.preference, AppearancePreference.dark);
      await tester.pageBack();
      await tester.pumpAndSettle();
      final context = tester.element(find.text('Your assistant'));
      expect(Theme.of(context).brightness, Brightness.dark);
      expect(JarvisColors.of(context).canvas, JarvisColors.dark.canvas);
    },
  );
}
