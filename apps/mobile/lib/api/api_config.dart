import 'package:dio/dio.dart';
import 'package:sentry_dio/sentry_dio.dart';

import '../error_reporting.dart';

const apiBaseUrl = String.fromEnvironment(
  'JARVIS_API_URL',
  defaultValue: 'http://localhost:5082',
);

const apiConnectTimeout = Duration(seconds: 10);
const apiReceiveTimeout = Duration(seconds: 20);
const longRunningReceiveTimeout = Duration(minutes: 20);

Dio createJarvisHttp() {
  final dio = Dio(
    BaseOptions(
      baseUrl: apiBaseUrl,
      connectTimeout: apiConnectTimeout,
      receiveTimeout: apiReceiveTimeout,
    ),
  );
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
