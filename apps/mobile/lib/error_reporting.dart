import 'dart:developer' as developer;

import 'package:flutter/foundation.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

/// Receives errors that would otherwise vanish. Plug a crash service in here;
/// the default only logs.
typedef ErrorSink =
    void Function(Object error, StackTrace? stack, String? context);

ErrorSink? errorSink;

/// True after [SentryFlutter.init] so HTTP clients can attach trace headers.
bool sentryTracingEnabled = false;

const sentryDsn = String.fromEnvironment(
  'JARVIS_SENTRY_DSN',
  defaultValue:
      'https://0b6943d19bd2891c9bc52565264bc631@o4512185266339840.ingest.de.sentry.io/4512185389482064',
);

/// Release builds report to Sentry. Debug and tests stay quiet unless
/// `--dart-define=JARVIS_SENTRY_ENABLE=true` is set.
const sentryEnabledOverride = bool.fromEnvironment('JARVIS_SENTRY_ENABLE');

/// Logs an unexpected error without its message, which can carry request
/// details, and hands it to [errorSink] when one is set.
void reportError(
  Object error,
  StackTrace? stack, {
  String? context,
  bool capture = true,
}) {
  developer.log(
    'Unexpected ${error.runtimeType}${context == null ? '' : ' in $context'}',
    name: 'jarvis',
    error: kDebugMode ? error : null,
    stackTrace: stack,
  );
  try {
    errorSink?.call(error, stack, context);
  } catch (_) {}
  if (!capture || !sentryTracingEnabled) return;
  Sentry.captureException(
    error,
    stackTrace: stack,
    withScope: (scope) {
      if (context != null) scope.setTag('context', context);
    },
  );
}

/// Routes framework and platform errors through [reportError].
///
/// Framework errors are not captured here when Sentry already installed its
/// own handler; that handler is chained as [previous].
void installErrorReporting() {
  final previous = FlutterError.onError;
  FlutterError.onError = (details) {
    reportError(
      details.exception,
      details.stack,
      context: 'flutter',
      capture: false,
    );
    previous?.call(details);
  };
  PlatformDispatcher.instance.onError = (error, stack) {
    reportError(error, stack, context: 'platform', capture: false);
    return true;
  };
}

/// Drops logs below warning before they leave the device.
SentryLog? filterSentryLog(SentryLog log) {
  if (log.level == SentryLogLevel.trace ||
      log.level == SentryLogLevel.debug ||
      log.level == SentryLogLevel.info) {
    return null;
  }
  return log;
}
