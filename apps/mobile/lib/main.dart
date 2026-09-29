import 'dart:async';

import 'package:flutter/material.dart';

import 'appearance.dart';
import 'features/chat/chat_screen.dart';
import 'push/firebase_bootstrap.dart';
import 'theme.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();
  await initializeFirebase();
  final appearance = AppearanceController();
  await appearance.load();
  runApp(JarvisApp(appearance: appearance));
}

class JarvisApp extends StatefulWidget {
  const JarvisApp({
    this.skipAuthentication = false,
    this.appearance,
    super.key,
  });

  /// Widget tests render the assistant shell without an account session.
  final bool skipAuthentication;

  /// When omitted, the app loads the saved preference (default: match device).
  final AppearanceController? appearance;

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
  Widget build(BuildContext context) => AppearanceScope(
    controller: _appearance,
    child: MaterialApp(
      title: 'Jarvis',
      debugShowCheckedModeBanner: false,
      theme: buildJarvisTheme(),
      darkTheme: buildJarvisTheme(brightness: Brightness.dark),
      themeMode: _appearance.themeMode,
      home: ChatScreen(skipAuthentication: widget.skipAuthentication),
    ),
  );
}
