import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/appearance.dart';
import 'package:jarvis_mobile/features/home/next_up.dart';
import 'package:jarvis_mobile/features/settings/appearance_screen.dart';
import 'package:jarvis_mobile/features/shell/jarvis_tab_bar.dart';
import 'package:jarvis_mobile/l10n/l10n.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/offline_banner.dart';

void main() {
  final dutch = lookupAppLocalizations(const Locale('nl'));

  test('English and Dutch are supported and every key is translated', () {
    expect(
      AppLocalizations.supportedLocales.map((locale) => locale.languageCode),
      containsAll(['en', 'nl']),
    );
    expect(dutch.greetingMorning, 'Goedemorgen');
    expect(dutch.tabEverything, 'Alles');
    expect(englishStrings.tabEverything, 'Everything');
  });

  test('formatters follow the language they are given', () {
    final now = DateTime(2026, 10, 15, 12);
    expect(greetingFor(DateTime(2026, 1, 1, 8), dutch), 'Goedemorgen');
    expect(greetingFor(DateTime(2026, 1, 1, 21)), 'Good evening');
    expect(
      offlineAge(now.subtract(const Duration(hours: 3)), now, dutch),
      '3 uur geleden',
    );
    expect(dutch.turnQualityTurns(1, 0, 4), startsWith('1 beurt mislukt'));
    expect(dutch.turnQualityTurns(2, 0, 4), startsWith('2 beurten mislukt'));
    expect(JarvisTab.you.localizedLabel(dutch), 'Jij');
  });

  test('the language choice is stored and restored', () async {
    final store = MemoryAppearanceStore();
    final controller = AppearanceController(store: store);
    await controller.load();
    expect(controller.language.locale, isNull);

    await controller.setLanguage(LanguagePreference.dutch);
    expect(store.language, 'dutch');

    final restored = AppearanceController(store: store);
    await restored.load();
    expect(restored.language, LanguagePreference.dutch);
    expect(restored.language.locale, const Locale('nl'));
  });

  testWidgets('choosing Nederlands switches the app to Dutch', (tester) async {
    final controller = AppearanceController(store: MemoryAppearanceStore());
    await controller.load();
    await tester.pumpWidget(
      AppearanceScope(
        controller: controller,
        child: ListenableBuilder(
          listenable: controller,
          builder: (context, _) => MaterialApp(
            theme: buildJarvisTheme(),
            locale: controller.language.locale ?? const Locale('en'),
            supportedLocales: AppLocalizations.supportedLocales,
            localizationsDelegates: AppLocalizations.localizationsDelegates,
            home: const AppearanceScreen(),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Appearance'), findsOneWidget);

    await tester.ensureVisible(find.byKey(const Key('language-dutch')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('language-dutch')));
    await tester.pumpAndSettle();

    expect(controller.language, LanguagePreference.dutch);
    expect(find.text('Weergave'), findsOneWidget);
    expect(find.text('Taal'), findsOneWidget);
  });
}
