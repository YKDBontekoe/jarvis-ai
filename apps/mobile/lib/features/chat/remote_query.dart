import 'package:dio/dio.dart';

/// A query keeps running on the server when the phone closes or the connection
/// drops. Stop, and any response the server already returned, end it here.
bool queryContinuesRemotely(
  DioException error, {
  required bool stopRequested,
}) {
  if (stopRequested || error.type == DioExceptionType.cancel) return false;
  if (error.response?.statusCode == 499) return false;
  return error.response == null;
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
