import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home/home_widget_sync.dart';
import 'package:jarvis_mobile/features/settings/widget_privacy_screen.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:shared_preferences/shared_preferences.dart';

class _FakeSync implements HomeWidgetSync {
  int cleared = 0;

  @override
  Future<void> clear() async => cleared++;

  @override
  dynamic noSuchMethod(Invocation invocation) => null;
}

void main() {
  setUp(() => SharedPreferences.setMockInitialValues({}));

  Future<_FakeSync> show(WidgetTester tester) async {
    final sync = _FakeSync();
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: WidgetPrivacyScreen(sync: sync),
      ),
    );
    await tester.pumpAndSettle();
    return sync;
  }

  testWidgets('details are shown by default and the choice is explained', (
    tester,
  ) async {
    await show(tester);
    expect(
      tester
          .widget<SwitchListTile>(find.byKey(const Key('widget-hide-details')))
          .value,
      isFalse,
    );
    expect(find.byKey(const Key('widget-privacy-note')), findsOneWidget);
  });

  testWidgets('hiding details is remembered and empties the widget at once', (
    tester,
  ) async {
    final sync = await show(tester);
    await tester.tap(find.byKey(const Key('widget-hide-details')));
    await tester.pumpAndSettle();

    expect(sync.cleared, 1);
    expect(await WidgetPrivacy.hideDetails(), isTrue);
  });

  testWidgets('showing details again does not clear anything', (tester) async {
    SharedPreferences.setMockInitialValues({
      WidgetPrivacy.hideDetailsKey: true,
    });
    final sync = await show(tester);
    await tester.tap(find.byKey(const Key('widget-hide-details')));
    await tester.pumpAndSettle();
    expect(sync.cleared, 0);
    expect(await WidgetPrivacy.hideDetails(), isFalse);
  });
}
