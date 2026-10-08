import 'dart:async';

import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

import 'app_lock.dart';
import 'appearance.dart';
import 'error_reporting.dart';
import 'features/chat/chat_screen.dart';
import 'push/firebase_bootstrap.dart';
import 'theme.dart';

Future<void> main() async {
  if (sentryDsn.isEmpty || !(kReleaseMode || sentryEnabledOverride)) {
    await startJarvis();
    return;
  }

  await SentryFlutter.init((options) {
    options.dsn = sentryDsn;
    options.sendDefaultPii = false;
    options.tracesSampleRate = kReleaseMode ? 0.2 : 1.0;
    options.enableLogs = true;
    options.attachScreenshot = false;
    // attachViewHierarchy stays at the SDK default (off). A view dump would include chat text.
    options.replay.sessionSampleRate = 0;
    options.replay.onErrorSampleRate = 1;
    options.privacy.maskAllText = true;
    options.privacy.maskAllImages = true;
    options.beforeSendLog = filterSentryLog;
  }, appRunner: () => startJarvis(wrapWithSentry: true));
}

Future<void> startJarvis({bool wrapWithSentry = false}) async {
  WidgetsFlutterBinding.ensureInitialized();
  if (wrapWithSentry) sentryTracingEnabled = true;
  installErrorReporting();
  await initializeFirebase();
  final appearance = AppearanceController();
  await appearance.load();
  final appLock = AppLockController();
  await appLock.load();
  final app = JarvisApp(appearance: appearance, appLock: appLock);
  runApp(wrapWithSentry ? SentryWidget(child: app) : app);
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
        builder: (context, child) {
          final content = child ?? const SizedBox.shrink();
          final media = MediaQuery.of(context);
          final theme = Theme.of(context);
          // On very narrow phones titles sit closer to the back button.
          final narrow = media.size.width < 360;
          return MediaQuery(
            data: _appearance.applyMotion(media),
            child: Theme(
              data: narrow
                  ? theme.copyWith(
                      appBarTheme: theme.appBarTheme.copyWith(titleSpacing: 6),
                    )
                  : theme,
              child: appLock == null
                  ? content
                  : AppLockGate(controller: appLock, child: content),
            ),
          );
        },
        home: ChatScreen(skipAuthentication: widget.skipAuthentication),
      ),
    );
    return appLock == null
        ? app
        : AppLockScope(controller: appLock, child: app);
  }
}
