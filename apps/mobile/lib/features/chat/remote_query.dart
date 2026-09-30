import 'package:dio/dio.dart';

/// Give up catch-up after this many consecutive unreachable-API failures.
const catchUpMaxFailedAttempts = 12;

/// A query keeps running on the server when the phone closes or the connection
/// drops. Stop, and any response the server already returned, end it here.
bool queryContinuesRemotely(DioException error, {required bool stopRequested}) {
  if (stopRequested || error.type == DioExceptionType.cancel) return false;
  if (error.response?.statusCode == 499) return false;
  return error.response == null;
}

/// Delay before retrying a failed catch-up poll, or `null` when polling should stop.
Duration? catchUpRetryDelay({
  required int failedAttempts,
  required DioException error,
  required bool stopRequested,
}) {
  if (failedAttempts < 1 || failedAttempts > catchUpMaxFailedAttempts) {
    return null;
  }
  if (!queryContinuesRemotely(error, stopRequested: stopRequested)) {
    return null;
  }
  final seconds = switch (failedAttempts) {
    1 => 2,
    2 => 4,
    3 => 8,
    4 => 16,
    _ => 30,
  };
  return Duration(seconds: seconds);
}

/// True when [messages] already contains the assistant reply for this send.
bool serverStoredReply(
  List<Map<String, dynamic>> messages,
  String? pendingUserText,
) {
  if (pendingUserText == null) {
    return messages.isNotEmpty && messages.last['role'] == 'assistant';
  }
  var seenUser = false;
  for (final message in messages) {
    if (!seenUser) {
      if (message['role'] == 'user' && message['content'] == pendingUserText) {
        seenUser = true;
      }
      continue;
    }
    final content = message['content'];
    if (message['role'] == 'assistant' &&
        content is String &&
        content.trim().isNotEmpty) {
      return true;
    }
  }
  return false;
}

/// Backoff for realtime reconnects: starts fast, caps at 30 s and never gives up.
Duration realtimeReconnectDelay(int previousRetryCount) {
  const seconds = [0, 1, 2, 5, 10, 20];
  final base = previousRetryCount < seconds.length
      ? seconds[previousRetryCount]
      : 30;
  return Duration(seconds: base);
}
