import 'package:dio/dio.dart';

const apiBaseUrl = String.fromEnvironment(
  'JARVIS_API_URL',
  defaultValue: 'http://localhost:5082',
);

const apiConnectTimeout = Duration(seconds: 10);
const apiReceiveTimeout = Duration(seconds: 20);
const longRunningReceiveTimeout = Duration(minutes: 20);

Dio createJarvisHttp() => Dio(
  BaseOptions(
    baseUrl: apiBaseUrl,
    connectTimeout: apiConnectTimeout,
    receiveTimeout: apiReceiveTimeout,
  ),
);

Options longRunningOptions() =>
    Options(receiveTimeout: longRunningReceiveTimeout);
