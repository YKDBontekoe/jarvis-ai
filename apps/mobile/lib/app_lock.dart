import 'dart:async';
import 'dart:ui' as ui;

import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:local_auth/local_auth.dart';

import 'theme.dart';
import 'ui/jarvis_ui.dart';
import 'ui/phosphor_icons.dart';

/// How long Jarvis may stay in the background before it locks again.
enum AppLockDelay {
  immediately(Duration.zero, 'Immediately'),
  oneMinute(Duration(minutes: 1), 'After 1 minute'),
  fiveMinutes(Duration(minutes: 5), 'After 5 minutes'),
  fifteenMinutes(Duration(minutes: 15), 'After 15 minutes');

  const AppLockDelay(this.duration, this.label);

  final Duration duration;
  final String label;

  static AppLockDelay parse(String? value) => AppLockDelay.values.firstWhere(
    (delay) => delay.name == value,
    orElse: () => AppLockDelay.oneMinute,
  );
}

/// Face ID, Touch ID, or the device passcode.
abstract class AppLockAuthenticator {
  /// Whether this device can verify its owner at all.
  Future<bool> isAvailable();

  /// Asks the owner to verify; true only when they did.
  Future<bool> authenticate(String reason);
}

class DeviceAppLockAuthenticator implements AppLockAuthenticator {
  DeviceAppLockAuthenticator({LocalAuthentication? auth})
    : _auth = auth ?? LocalAuthentication();

  final LocalAuthentication _auth;

  @override
  Future<bool> isAvailable() async {
    try {
      return await _auth.isDeviceSupported();
    } catch (_) {
      return false;
    }
  }

  @override
  Future<bool> authenticate(String reason) async {
    try {
      // The passcode fallback stays allowed so a failed Face ID never locks
      // the owner out of their own assistant.
      return await _auth.authenticate(
        localizedReason: reason,
        persistAcrossBackgrounding: true,
      );
    } catch (_) {
      return false;
    }
  }
}

abstract class AppLockStore {
  Future<Map<String, String>> read();
  Future<void> write(String key, String value);
}

class MemoryAppLockStore implements AppLockStore {
  MemoryAppLockStore([Map<String, String>? values]) : values = values ?? {};

  final Map<String, String> values;

  @override
  Future<Map<String, String>> read() async => Map.of(values);

  @override
  Future<void> write(String key, String value) async => values[key] = value;
}

/// Keeps the setting in the Keychain, so it cannot be switched off by editing
/// app preferences.
class SecureAppLockStore implements AppLockStore {
  SecureAppLockStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  final FlutterSecureStorage _storage;

  @override
  Future<Map<String, String>> read() async {
    try {
      final values = <String, String>{};
      for (final key in const [
        AppLockController.enabledKey,
        AppLockController.delayKey,
      ]) {
        final value = await _storage.read(key: key);
        if (value != null) values[key] = value;
      }
      return values;
    } catch (_) {
      return const {};
    }
  }

  @override
  Future<void> write(String key, String value) async {
    try {
      await _storage.write(key: key, value: value);
    } catch (_) {}
  }
}

/// Locks Jarvis behind Face ID when it comes back from the background.
class AppLockController extends ChangeNotifier {
  AppLockController({
    AppLockStore? store,
    AppLockAuthenticator? authenticator,
    DateTime Function()? clock,
  }) : _store = store ?? SecureAppLockStore(),
       _authenticator = authenticator ?? DeviceAppLockAuthenticator(),
       _clock = clock ?? DateTime.now;

  static const enabledKey = 'jarvis.appLock.enabled';
  static const delayKey = 'jarvis.appLock.delay';
  static const unlockReason = 'Unlock Jarvis';

  final AppLockStore _store;
  final AppLockAuthenticator _authenticator;
  final DateTime Function() _clock;

  var _enabled = false;
  var _delay = AppLockDelay.oneMinute;
  var _locked = false;
  var _obscured = false;
  var _authenticating = false;
  DateTime? _backgroundedAt;

  bool get enabled => _enabled;
  AppLockDelay get delay => _delay;
  bool get locked => _locked;

  /// True while the app is in the app switcher, so its snapshot shows nothing.
  bool get obscured => _obscured;
  bool get authenticating => _authenticating;

  Future<void> load() async {
    final values = await _store.read();
    _enabled = values[enabledKey] == 'true';
    _delay = AppLockDelay.parse(values[delayKey]);
    _locked = _enabled;
    notifyListeners();
  }

  Future<bool> isAvailable() => _authenticator.isAvailable();

  /// Turning the lock on or off both need the owner to verify first.
  Future<bool> setEnabled(bool value) async {
    if (value == _enabled) return true;
    if (!await _authenticator.isAvailable()) return false;
    if (!await _verify(
      value ? 'Turn on the Jarvis lock' : 'Turn off the Jarvis lock',
    )) {
      return false;
    }
    _enabled = value;
    _locked = false;
    await _store.write(enabledKey, value.toString());
    notifyListeners();
    return true;
  }

  Future<void> setDelay(AppLockDelay value) async {
    if (value == _delay) return;
    _delay = value;
    await _store.write(delayKey, value.name);
    notifyListeners();
  }

  Future<bool> unlock() async {
    if (!_locked) return true;
    if (!await _verify(unlockReason)) return false;
    _locked = false;
    notifyListeners();
    return true;
  }

  Future<bool> _verify(String reason) async {
    if (_authenticating) return false;
    _authenticating = true;
    notifyListeners();
    try {
      return await _authenticator.authenticate(reason);
    } finally {
      _authenticating = false;
      notifyListeners();
    }
  }

  void handleLifecycle(AppLifecycleState state) {
    if (!_enabled) {
      if (_obscured) {
        _obscured = false;
        notifyListeners();
      }
      return;
    }
    switch (state) {
      case AppLifecycleState.inactive:
        // The Face ID sheet itself makes the app inactive; covering the app
        // then would flash the shield behind the system prompt.
        if (!_authenticating && !_obscured) {
          _obscured = true;
          notifyListeners();
        }
      case AppLifecycleState.hidden:
      case AppLifecycleState.paused:
        _backgroundedAt ??= _clock();
        _obscured = true;
        notifyListeners();
      case AppLifecycleState.resumed:
        final since = _backgroundedAt;
        _backgroundedAt = null;
        if (since != null && _clock().difference(since) >= _delay.duration) {
          _locked = true;
        }
        _obscured = false;
        notifyListeners();
      case AppLifecycleState.detached:
        break;
    }
  }
}

class AppLockScope extends InheritedNotifier<AppLockController> {
  const AppLockScope({
    required AppLockController controller,
    required super.child,
    super.key,
  }) : super(notifier: controller);

  static AppLockController? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppLockScope>()?.notifier;
}

/// Shows [child] underneath a lock screen while the app is locked, so chats and
/// drafts keep their state.
class AppLockGate extends StatefulWidget {
  const AppLockGate({required this.controller, required this.child, super.key});

  final AppLockController controller;
  final Widget child;

  @override
  State<AppLockGate> createState() => _AppLockGateState();
}

class _AppLockGateState extends State<AppLockGate> with WidgetsBindingObserver {
  var _wasLocked = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    widget.controller.addListener(_changed);
    _changed();
  }

  @override
  void didUpdateWidget(AppLockGate oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller != widget.controller) {
      oldWidget.controller.removeListener(_changed);
      widget.controller.addListener(_changed);
    }
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    widget.controller.removeListener(_changed);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) =>
      widget.controller.handleLifecycle(state);

  void _changed() {
    final controller = widget.controller;
    final locked = controller.locked;
    // Ask for Face ID right away when the lock appears in the foreground.
    if (locked && !_wasLocked && !controller.obscured) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted && widget.controller.locked) {
          unawaited(widget.controller.unlock());
        }
      });
    }
    _wasLocked = locked;
    if (mounted) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final controller = widget.controller;
    final covered = controller.locked || controller.obscured;
    return Stack(
      fit: StackFit.expand,
      children: [
        ExcludeSemantics(
          excluding: covered,
          child: TickerMode(enabled: !covered, child: widget.child),
        ),
        if (controller.locked)
          _LockScreen(controller: controller)
        else if (controller.obscured)
          const _PrivacyShield(),
      ],
    );
  }
}

class _PrivacyShield extends StatelessWidget {
  const _PrivacyShield();

  @override
  Widget build(BuildContext context) => ClipRect(
    child: BackdropFilter(
      filter: ui.ImageFilter.blur(sigmaX: 24, sigmaY: 24),
      child: ColoredBox(
        color: JarvisColors.of(context).canvas.withValues(alpha: .85),
        child: const Center(
          child: JarvisOrb(size: 72, semanticLabel: 'Jarvis'),
        ),
      ),
    ),
  );
}

class _LockScreen extends StatelessWidget {
  const _LockScreen({required this.controller});

  final AppLockController controller;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Material(
      color: colors.canvas,
      child: SafeArea(
        child: Center(
          child: Padding(
            padding: const EdgeInsets.all(32),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const JarvisOrb(size: 88, semanticLabel: 'Jarvis'),
                const SizedBox(height: 28),
                Text(
                  'Jarvis is locked',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                const SizedBox(height: 8),
                Text(
                  'Unlock with Face ID or your passcode.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: colors.inkSoft),
                ),
                const SizedBox(height: 28),
                FilledButton.icon(
                  onPressed: controller.authenticating
                      ? null
                      : () => unawaited(controller.unlock()),
                  icon: const Icon(PhosphorIconsRegular.lockSimple, size: 18),
                  label: const Text('Unlock'),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
