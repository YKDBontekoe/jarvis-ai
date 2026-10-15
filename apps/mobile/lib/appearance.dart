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

/// How much the app moves: follow the device's Reduce Motion setting, or
/// choose for Jarvis alone.
enum MotionPreference {
  system,
  full,
  reduced;

  static MotionPreference parse(String? value) => switch (value) {
    'full' => MotionPreference.full,
    'reduced' => MotionPreference.reduced,
    _ => MotionPreference.system,
  };
}

/// Which language Jarvis's screens use: the device's, or one chosen here.
/// Only languages with translations are offered.
enum LanguagePreference {
  system,
  english,
  dutch;

  /// Null follows the device.
  Locale? get locale => switch (this) {
    LanguagePreference.system => null,
    LanguagePreference.english => const Locale('en'),
    LanguagePreference.dutch => const Locale('nl'),
  };

  static LanguagePreference parse(String? value) => switch (value) {
    'english' => LanguagePreference.english,
    'dutch' => LanguagePreference.dutch,
    _ => LanguagePreference.system,
  };
}

abstract class AppearanceStore {
  Future<String?> read();
  Future<void> write(String value);
  Future<String?> readMotion();
  Future<void> writeMotion(String value);
  Future<String?> readLanguage();
  Future<void> writeLanguage(String value);
}

/// In-memory store for tests and when OS storage is unavailable.
class MemoryAppearanceStore implements AppearanceStore {
  MemoryAppearanceStore([this.value, this.motion, this.language]);

  String? value;
  String? motion;
  String? language;

  @override
  Future<String?> read() async => value;

  @override
  Future<void> write(String next) async => value = next;

  @override
  Future<String?> readMotion() async => motion;

  @override
  Future<void> writeMotion(String next) async => motion = next;

  @override
  Future<String?> readLanguage() async => language;

  @override
  Future<void> writeLanguage(String next) async => language = next;
}

class SecureAppearanceStore implements AppearanceStore {
  SecureAppearanceStore({FlutterSecureStorage? storage})
    : _storage = storage ?? const FlutterSecureStorage();

  static const key = 'jarvis.appearance';
  static const motionKey = 'jarvis.motion';
  static const languageKey = 'jarvis.language';
  final FlutterSecureStorage _storage;

  @override
  Future<String?> read() => _read(key);

  @override
  Future<void> write(String value) => _write(key, value);

  @override
  Future<String?> readMotion() => _read(motionKey);

  @override
  Future<void> writeMotion(String value) => _write(motionKey, value);

  @override
  Future<String?> readLanguage() => _read(languageKey);

  @override
  Future<void> writeLanguage(String value) => _write(languageKey, value);

  Future<String?> _read(String name) async {
    try {
      return await _storage.read(key: name);
    } catch (_) {
      return null;
    }
  }

  Future<void> _write(String name, String value) async {
    try {
      await _storage.write(key: name, value: value);
    } catch (_) {}
  }
}

class AppearanceController extends ChangeNotifier {
  AppearanceController({AppearanceStore? store})
    : _store = store ?? SecureAppearanceStore();

  final AppearanceStore _store;
  AppearancePreference preference = AppearancePreference.system;
  MotionPreference motion = MotionPreference.system;
  LanguagePreference language = LanguagePreference.system;
  var _loaded = false;

  bool get loaded => _loaded;
  ThemeMode get themeMode => preference.themeMode;

  Future<void> load() async {
    preference = AppearancePreference.parse(await _store.read());
    motion = MotionPreference.parse(await _store.readMotion());
    language = LanguagePreference.parse(await _store.readLanguage());
    _loaded = true;
    notifyListeners();
  }

  Future<void> setMotion(MotionPreference value) async {
    if (motion == value) return;
    motion = value;
    notifyListeners();
    await _store.writeMotion(value.name);
  }

  Future<void> setLanguage(LanguagePreference value) async {
    if (language == value) return;
    language = value;
    notifyListeners();
    await _store.writeLanguage(value.name);
  }

  /// Applies [motion] on top of the device setting that [data] carries.
  MediaQueryData applyMotion(MediaQueryData data) => switch (motion) {
    MotionPreference.system => data,
    MotionPreference.full => data.copyWith(disableAnimations: false),
    MotionPreference.reduced => data.copyWith(disableAnimations: true),
  };

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
