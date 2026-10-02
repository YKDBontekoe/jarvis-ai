import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:sentry_dio/sentry_dio.dart';

import '../error_reporting.dart';

const _configuredApiBaseUrl = String.fromEnvironment('JARVIS_API_URL');

// Hosted browsers use the same origin for HTTP and SignalR. Native clients
// retain the local development default unless their build supplies an API URL.
final apiBaseUrl = _configuredApiBaseUrl.isNotEmpty
    ? _configuredApiBaseUrl
    : kIsWeb
    ? Uri.base.origin
    : 'http://localhost:5082';

const apiConnectTimeout = Duration(seconds: 10);
const apiReceiveTimeout = Duration(seconds: 20);
const longRunningReceiveTimeout = Duration(minutes: 20);

/// Serves the whole app from canned responses in widget tests and the
/// screenshot harness (`test/screenshots`).
@visibleForTesting
HttpClientAdapter? debugJarvisHttpAdapter;

Dio createJarvisHttp() {
  final dio = Dio(
    BaseOptions(
      baseUrl: apiBaseUrl,
      connectTimeout: apiConnectTimeout,
      receiveTimeout: apiReceiveTimeout,
    ),
  );
  if (debugJarvisHttpAdapter case final adapter?) {
    dio.httpClientAdapter = adapter;
  }
  attachSentryHttp(dio);
  return dio;
}

/// Adds Sentry trace headers once the SDK is initialized.
Dio attachSentryHttp(Dio dio) {
  if (sentryTracingEnabled) dio.addSentry();
  return dio;
}

Options longRunningOptions() =>
    Options(receiveTimeout: longRunningReceiveTimeout);
