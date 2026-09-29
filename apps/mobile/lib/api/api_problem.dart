import '../json_maps.dart';

/// Stable Jarvis API error codes (problem details contract v1).
abstract final class JarvisApiErrorCodes {
  static const validationFailed = 'validation_failed';
  static const authenticationRequired = 'authentication_required';
  static const authorizationForbidden = 'authorization_forbidden';
  static const resourceNotFound = 'resource_not_found';
  static const conflict = 'conflict';
  static const rateLimited = 'rate_limited';
  static const dependencyUnavailable = 'dependency_unavailable';
  static const timeout = 'timeout';
  static const requestCancelled = 'request_cancelled';
  static const malwareRejected = 'malware_rejected';
  static const internalError = 'internal_error';
}

class JarvisApiProblem {
  const JarvisApiProblem({
    this.code,
    this.traceId,
    this.apiErrorVersion,
    this.detail,
    this.title,
    this.status,
    this.errors,
  });

  final String? code;
  final String? traceId;
  final int? apiErrorVersion;
  final String? detail;
  final String? title;
  final int? status;
  final Map<String, List<String>>? errors;

  String? firstFieldMessage() {
    final fieldErrors = errors;
    if (fieldErrors == null) return null;
    for (final messages in fieldErrors.values) {
      if (messages.isNotEmpty && messages.first.isNotEmpty) return messages.first;
    }
    return null;
  }
}

JarvisApiProblem? parseJarvisApiProblem(dynamic data) {
  final map = jsonObject(data);
  if (map == null) return null;
  final code = asJsonString(map['code']);
  final traceId = asJsonString(map['traceId']);
  final apiErrorVersion = map['apiErrorVersion'] is int
      ? map['apiErrorVersion'] as int
      : asJsonInt(map['apiErrorVersion'], -1);
  final errorsRaw = map['errors'];
  Map<String, List<String>>? errors;
  if (errorsRaw is Map) {
    errors = {};
    for (final entry in errorsRaw.entries) {
      if (entry.key is! String) continue;
      final messages = jsonStrings(entry.value);
      if (messages.isNotEmpty) errors![entry.key as String] = messages;
    }
    if (errors!.isEmpty) errors = null;
  }

  return JarvisApiProblem(
    code: code,
    traceId: traceId,
    apiErrorVersion: apiErrorVersion >= 0 ? apiErrorVersion : null,
    detail: asJsonString(map['detail']),
    title: asJsonString(map['title']),
    status: map['status'] is int ? map['status'] as int : asJsonInt(map['status'], -1),
    errors: errors,
  );
}
