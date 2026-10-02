import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/app_lock.dart';
import 'package:jarvis_mobile/features/settings/app_lock_screen.dart';
import 'package:jarvis_mobile/theme.dart';

class _FakeAuthenticator implements AppLockAuthenticator {
  bool available = true;
  var answers = <bool>[];
  final reasons = <String>[];

  @override
  Future<bool> isAvailable() async => available;

  @override
  Future<bool> authenticate(String reason) async {
    reasons.add(reason);
    return answers.isEmpty ? true : answers.removeAt(0);
  }
}

void main() {
  late DateTime now;
  late _FakeAuthenticator auth;
  late MemoryAppLockStore store;

  AppLockController controller() =>
      AppLockController(store: store, authenticator: auth, clock: () => now);

  setUp(() {
    now = DateTime(2030, 1, 1, 12);
    auth = _FakeAuthenticator();
    store = MemoryAppLockStore();
  });

  test('starts unlocked when the lock is off', () async {
    final lock = controller();
    await lock.load();

    expect(lock.enabled, isFalse);
    expect(lock.locked, isFalse);
  });

  test('starts locked when the lock was turned on', () async {
    store.values[AppLockController.enabledKey] = 'true';
    store.values[AppLockController.delayKey] = 'fiveMinutes';
    final lock = controller();
    await lock.load();

    expect(lock.locked, isTrue);
    expect(lock.delay, AppLockDelay.fiveMinutes);
  });

  test('turning the lock on or off needs Face ID', () async {
    final lock = controller();
    await lock.load();

    auth.answers = [false];
    expect(await lock.setEnabled(true), isFalse);
    expect(lock.enabled, isFalse);
    expect(store.values, isEmpty);

    expect(await lock.setEnabled(true), isTrue);
    expect(lock.enabled, isTrue);
    expect(store.values[AppLockController.enabledKey], 'true');

    auth.answers = [false];
    expect(await lock.setEnabled(false), isFalse);
    expect(lock.enabled, isTrue);
    expect(auth.reasons.last, 'Turn off the Jarvis lock');
  });

  test('cannot be turned on without Face ID or a passcode', () async {
    auth.available = false;
    final lock = controller();
    await lock.load();

    expect(await lock.setEnabled(true), isFalse);
    expect(auth.reasons, isEmpty);
  });

  test('locks again only after the chosen delay in the background', () async {
    final lock = controller();
    await lock.load();
    await lock.setEnabled(true);
    await lock.setDelay(AppLockDelay.oneMinute);

    lock.handleLifecycle(AppLifecycleState.inactive);
    expect(lock.obscured, isTrue);
    lock.handleLifecycle(AppLifecycleState.paused);
    now = now.add(const Duration(seconds: 30));
    lock.handleLifecycle(AppLifecycleState.resumed);
    expect(lock.locked, isFalse);
    expect(lock.obscured, isFalse);

    lock.handleLifecycle(AppLifecycleState.paused);
    now = now.add(const Duration(minutes: 2));
    lock.handleLifecycle(AppLifecycleState.resumed);
    expect(lock.locked, isTrue);

    auth.answers = [false];
    expect(await lock.unlock(), isFalse);
    expect(lock.locked, isTrue);
    expect(await lock.unlock(), isTrue);
    expect(lock.locked, isFalse);
  });

  test('a short trip to control center never locks', () async {
    final lock = controller();
    await lock.load();
    await lock.setEnabled(true);
    await lock.setDelay(AppLockDelay.immediately);

    lock.handleLifecycle(AppLifecycleState.inactive);
    now = now.add(const Duration(minutes: 5));
    lock.handleLifecycle(AppLifecycleState.resumed);

    expect(lock.locked, isFalse);
  });

  testWidgets('the gate covers the app until Face ID succeeds', (tester) async {
    store.values[AppLockController.enabledKey] = 'true';
    auth.answers = [false];
    final lock = controller();
    await lock.load();

    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        builder: (context, child) =>
            AppLockGate(controller: lock, child: child!),
        home: const Scaffold(body: Text('Private chat')),
      ),
    );
    await tester.pumpAndSettle();

    // The automatic prompt was declined, so the lock screen stays.
    expect(auth.reasons, [AppLockController.unlockReason]);
    expect(find.text('Jarvis is locked'), findsOneWidget);

    await tester.tap(find.text('Unlock'));
    await tester.pumpAndSettle();

    expect(find.text('Jarvis is locked'), findsNothing);
    expect(find.text('Private chat'), findsOneWidget);
  });

  testWidgets('the settings switch turns the lock on and shows delays', (
    tester,
  ) async {
    final lock = controller();
    await lock.load();

    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: AppLockScope(controller: lock, child: const AppLockScreen()),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('After 5 minutes'), findsNothing);

    await tester.tap(find.byKey(const Key('app-lock-switch')));
    await tester.pumpAndSettle();
    expect(lock.enabled, isTrue);

    await tester.tap(find.byKey(const Key('app-lock-fiveMinutes')));
    await tester.pumpAndSettle();
    expect(lock.delay, AppLockDelay.fiveMinutes);
    expect(store.values[AppLockController.delayKey], 'fiveMinutes');
  });

  testWidgets('says when the device cannot lock', (tester) async {
    auth.available = false;
    final lock = controller();
    await lock.load();

    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: AppLockScope(controller: lock, child: const AppLockScreen()),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.text(
        'Set up Face ID, Touch ID, or a passcode on this device to use the lock.',
      ),
      findsOneWidget,
    );
  });
}
