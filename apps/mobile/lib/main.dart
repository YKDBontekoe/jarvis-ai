import 'dart:async';

import 'package:flutter/material.dart';

import 'app_lock.dart';
import 'appearance.dart';
import 'error_reporting.dart';
import 'features/chat/chat_screen.dart';
import 'push/firebase_bootstrap.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  installErrorReporting();
  await initializeFirebase();
  final appearance = AppearanceController();
  await appearance.load();
  final appLock = AppLockController();
  await appLock.load();
  runApp(JarvisApp(appearance: appearance, appLock: appLock));
}

class JarvisApp extends StatefulWidget {
  const JarvisApp({
    this.skipAuthentication = false,
    this.appearance,
    this.appLock,
    super.key,
  });

  /// Widget tests render the assistant shell without an account session.
  final bool skipAuthentication;

  /// When omitted, the app loads the saved preference (default: match device).
  final AppearanceController? appearance;

  /// Face ID lock; tests leave it out and get an unlocked app without a lock.
  final AppLockController? appLock;

  @override
  State<JarvisApp> createState() => _JarvisAppState();
}

class _JarvisAppState extends State<JarvisApp> {
  late final AppearanceController _appearance =
      widget.appearance ?? AppearanceController();
  var _ownsAppearance = false;

  @override
  void initState() {
    super.initState();
    _appearance.addListener(_onAppearance);
    if (widget.appearance == null) {
      _ownsAppearance = true;
      unawaited(_appearance.load());
    }
  }

  @override
  void dispose() {
    _appearance.removeListener(_onAppearance);
    if (_ownsAppearance) _appearance.dispose();
    super.dispose();
  }

  void _onAppearance() {
    if (mounted) setState(() {});
  }

  @override
  Widget build(BuildContext context) {
    final appLock = widget.appLock;
    final app = AppearanceScope(
      controller: _appearance,
      child: MaterialApp(
        title: 'Jarvis',
        debugShowCheckedModeBanner: false,
        theme: buildJarvisTheme(),
        darkTheme: buildJarvisTheme(brightness: Brightness.dark),
        themeMode: _appearance.themeMode,
        builder: appLock == null
            ? null
            : (context, child) => AppLockGate(
                controller: appLock,
                child: child ?? const SizedBox.shrink(),
              ),
        home: ChatScreen(skipAuthentication: widget.skipAuthentication),
      ),
    );
    return appLock == null
        ? app
        : AppLockScope(controller: appLock, child: app);
  }
}
