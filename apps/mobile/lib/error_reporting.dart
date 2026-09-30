import 'dart:developer' as developer;

import 'package:flutter/foundation.dart';

/// Receives errors that would otherwise vanish. Plug a crash service in here;
/// the default only logs.
typedef ErrorSink =
    void Function(Object error, StackTrace? stack, String? context);

ErrorSink? errorSink;

/// Logs an unexpected error without its message, which can carry request
/// details, and hands it to [errorSink] when one is set.
void reportError(Object error, StackTrace? stack, {String? context}) {
  developer.log(
    'Unexpected ${error.runtimeType}${context == null ? '' : ' in $context'}',
    name: 'jarvis',
    error: kDebugMode ? error : null,
    stackTrace: stack,
  );
  try {
    errorSink?.call(error, stack, context);
  } catch (_) {}
}

/// Routes framework and platform errors through [reportError].
void installErrorReporting() {
  final previous = FlutterError.onError;
  FlutterError.onError = (details) {
    reportError(details.exception, details.stack, context: 'flutter');
    previous?.call(details);
  };
  PlatformDispatcher.instance.onError = (error, stack) {
    reportError(error, stack, context: 'platform');
    return true;
  };
}
