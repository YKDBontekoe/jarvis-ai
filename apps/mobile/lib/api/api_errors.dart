import 'package:dio/dio.dart';

import '../json_maps.dart';
import 'api_config.dart';
import 'api_problem.dart';

String describeApiError(DioException error) {
  final response = error.response;
  final status = response?.statusCode;
  final problem = parseJarvisApiProblem(response?.data);
  final legacyMessage = asJsonString(jsonObject(response?.data)?['message']);

  if (problem?.code != null) {
    final fromCode = _messageForCode(problem!.code!, problem, legacyMessage);
    if (fromCode != null) return fromCode;
  }

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
    return legacyMessage ?? problem?.detail ?? 'Jarvis is busy with this conversation.';
  }
  final problemText = firstProblemMessage(response?.data);
  if (problemText != null && problemText.isNotEmpty) return problemText;
  if (status != null) return 'Jarvis returned HTTP $status.';
  return 'Could not reach the Jarvis API at $apiBaseUrl.';
}

String? _messageForCode(
  String code,
  JarvisApiProblem problem,
  String? legacyMessage,
) {
  switch (code) {
    case JarvisApiErrorCodes.authenticationRequired:
      return legacyMessage ??
          problem.detail ??
          'Your sign-in has expired. Sign in again to continue.';
    case JarvisApiErrorCodes.authorizationForbidden:
      return legacyMessage ??
          problem.detail ??
          'You do not have permission to do that.';
    case JarvisApiErrorCodes.validationFailed:
      return problem.firstFieldMessage() ??
          problem.detail ??
          legacyMessage ??
          'Check the highlighted fields and try again.';
    case JarvisApiErrorCodes.resourceNotFound:
      return problem.detail ?? legacyMessage ?? 'That item could not be found.';
    case JarvisApiErrorCodes.conflict:
      return legacyMessage ?? problem.detail ?? 'Jarvis is busy with this conversation.';
    case JarvisApiErrorCodes.rateLimited:
      return problem.detail ?? 'Too many requests. Wait a moment and try again.';
    case JarvisApiErrorCodes.dependencyUnavailable:
      return problem.detail ??
          'A Jarvis service is temporarily unavailable. Please try again.';
    case JarvisApiErrorCodes.timeout:
      return problem.detail ?? 'This request timed out. Please try again.';
    case JarvisApiErrorCodes.requestCancelled:
      return problem.detail;
    case JarvisApiErrorCodes.malwareRejected:
      return problem.detail ??
          'The uploaded file was rejected by malware scanning.';
    case JarvisApiErrorCodes.internalError:
      return 'Jarvis could not complete this request. Please try again.';
  }
  return problem.detail ?? legacyMessage;
}

bool isAuthExpired(DioException error, {required bool authEnabled}) {
  if (!authEnabled) return false;
  final problem = parseJarvisApiProblem(error.response?.data);
  if (problem?.code == JarvisApiErrorCodes.authenticationRequired) return true;
  return error.response?.statusCode == 401;
}
