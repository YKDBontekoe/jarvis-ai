import 'package:flutter/material.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

/// How Jarvis should pick light vs dark: follow the device, or lock one look.
enum AppearancePreference {
  system,
  light,
  dark;

  ThemeMode get themeMode => switch (this) {
    AppearancePreference.system => ThemeMode.system,
    AppearancePreference.light => ThemeMode.light,
    AppearancePreference.dark => ThemeMode.dark,
  };

  static AppearancePreference parse(String? value) => switch (value) {
    'light' => AppearancePreference.light,
    'dark' => AppearancePreference.dark,
    _ => AppearancePreference.system,
  };
}

abstract class AppearanceStore {
  Future<String?> read();
  Future<void> write(String value);
}

/// In-memory store for tests and when OS storage is unavailable.
class MemoryAppearanceStore implements AppearanceStore {
  MemoryAppearanceStore([this.value]);

  String? value;

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String next) async => value = next;
}

class SecureAppearanceStore implements AppearanceStore {
  SecureAppearanceStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  static const key = 'jarvis.appearance';
  final FlutterSecureStorage _storage;

  @override
  Future<String?> read() async {
    try {
      return await _storage.read(key: key);
    } catch (_) {
      return null;
    }
  }

  @override
  Future<void> write(String value) async {
    try {
      await _storage.write(key: key, value: value);
    } catch (_) {}
  }
}

class AppearanceController extends ChangeNotifier {
  AppearanceController({AppearanceStore? store})
    : _store = store ?? SecureAppearanceStore();

  final AppearanceStore _store;
  AppearancePreference preference = AppearancePreference.system;
  var _loaded = false;

  bool get loaded => _loaded;
  ThemeMode get themeMode => preference.themeMode;

  Future<void> load() async {
    preference = AppearancePreference.parse(await _store.read());
    _loaded = true;
    notifyListeners();
  }

  Future<void> setPreference(AppearancePreference value) async {
    if (preference == value) return;
    preference = value;
    notifyListeners();
    await _store.write(value.name);
  }
}

class AppearanceScope extends InheritedNotifier<AppearanceController> {
  const AppearanceScope({
    required AppearanceController controller,
    required super.child,
    super.key,
  }) : super(notifier: controller);

  static AppearanceController of(BuildContext context) {
    final scope = context.dependOnInheritedWidgetOfExactType<AppearanceScope>();
    assert(scope != null, 'AppearanceScope is missing.');
    return scope!.notifier!;
  }

  static AppearanceController? maybeOf(BuildContext context) =>
      context.dependOnInheritedWidgetOfExactType<AppearanceScope>()?.notifier;
}
