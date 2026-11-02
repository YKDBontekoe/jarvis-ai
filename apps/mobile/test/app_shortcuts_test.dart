import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/app_shortcuts.dart';
import 'package:jarvis_mobile/features/shell/incoming_routes.dart';
import 'package:quick_actions/quick_actions.dart';

void main() {
  setUp(() => AppShortcuts.supported = () => true);
  tearDown(() => AppShortcuts.supported = () => false);

  test('the app-icon menu offers the four flows', () {
    expect(
      [for (final s in appShortcuts) s.type],
      ['new_chat', 'talk', 'new_reminder', 'captures'],
    );
    expect(shortcutDestination('new_chat'), 'ask');
    expect(shortcutDestination('talk'), 'voice');
    expect(shortcutDestination('new_reminder'), 'reminders/new');
    expect(shortcutDestination('captures'), 'captures');
    expect(shortcutDestination('unknown'), isNull);
  });

  test('titles follow the language', () {
    expect(appShortcuts[1].title('en'), 'Talk to Jarvis');
    expect(appShortcuts[1].title('nl'), 'Praat met Jarvis');
    expect(appShortcuts[0].title('de'), 'New chat');
  });

  test(
    'installing registers the items and a choice waits in the queue',
    () async {
      final routes = IncomingRoutes();
      void Function(String)? handler;
      List<ShortcutItem>? items;
      await AppShortcuts.install(
        languageCode: 'nl',
        routes: routes,
        initialize: (h) async => handler = h,
        setItems: (list) async => items = list,
      );

      expect(items!.map((i) => i.localizedTitle), [
        'Nieuwe chat',
        'Praat met Jarvis',
        'Nieuwe herinnering',
        'Opgeslagen items',
      ]);
      // Chosen while the app was closed: held until the app can show it.
      handler!('new_reminder');
      expect(routes.pending?.destination, 'reminders/new');
      expect(routes.pending?.source, 'shortcut');
      // An unknown type is ignored.
      routes.take();
      handler!('nope');
      expect(routes.hasPending, isFalse);
    },
  );

  test('a platform without the menu is left alone', () async {
    AppShortcuts.supported = () => false;
    var called = false;
    await AppShortcuts.install(initialize: (_) async => called = true);
    expect(called, isFalse);
  });

  test('a failing platform call never breaks the app', () async {
    await AppShortcuts.install(initialize: (_) async => throw StateError('no'));
  });
}
