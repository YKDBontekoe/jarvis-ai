import 'package:dio/dio.dart';

import '../json_maps.dart';
import 'api_config.dart';

String describeApiError(DioException error) {
  final status = error.response?.statusCode;
  if (status == 401) {
    return 'Your sign-in has expired. Sign in again to continue.';
  }
  if (status == 502) {
    return 'Jarvis could not complete this request. Please try again.';
  }
  if (status == 503) {
    return 'A Jarvis service is temporarily unavailable. Please try again.';
  }
  if (status == 409) {
    final message = asJsonString(jsonObject(error.response?.data)?['message']);
    return message ?? 'Jarvis is busy with this conversation.';
  }
  if (status != null) return 'Jarvis returned HTTP $status.';
  return 'Could not reach the Jarvis API at $apiBaseUrl.';
}

bool isAuthExpired(DioException error, {required bool authEnabled}) =>
    authEnabled && error.response?.statusCode == 401;
