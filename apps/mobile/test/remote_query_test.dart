import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/remote_query.dart';

void main() {
  DioException error({
    DioExceptionType type = DioExceptionType.connectionError,
    int? status,
  }) {
    final options = RequestOptions(path: '/api/v1/conversations/1/messages');
    return DioException(
      requestOptions: options,
      type: type,
      response: status == null
          ? null
          : Response<dynamic>(requestOptions: options, statusCode: status),
    );
  }

  test('closing the phone keeps the query running', () {
    expect(queryContinuesRemotely(error(), stopRequested: false), isTrue);
    expect(
      queryContinuesRemotely(
        error(type: DioExceptionType.receiveTimeout),
        stopRequested: false,
      ),
      isTrue,
    );
  });

  test('a stored reply is visible after the phone opens again', () {
    final messages = [
      {'role': 'user', 'content': 'Weather tomorrow'},
      {'role': 'assistant', 'content': 'Rain in the morning.'},
    ];
    expect(serverStoredReply(messages, 'Weather tomorrow'), isTrue);
    expect(serverStoredReply(messages, 'A different question'), isFalse);
    expect(
      serverStoredReply([
        {'role': 'user', 'content': 'Weather tomorrow'},
      ], 'Weather tomorrow'),
      isFalse,
    );
  });

  test('catch-up retries unreachable API errors with backoff, then stops', () {
    expect(
      catchUpRetryDelay(
        failedAttempts: 1,
        error: error(),
        stopRequested: false,
      ),
      const Duration(seconds: 2),
    );
    expect(
      catchUpRetryDelay(
        failedAttempts: 4,
        error: error(type: DioExceptionType.receiveTimeout),
        stopRequested: false,
      ),
      const Duration(seconds: 16),
    );
    expect(
      catchUpRetryDelay(
        failedAttempts: catchUpMaxFailedAttempts,
        error: error(),
        stopRequested: false,
      ),
      const Duration(seconds: 30),
    );
    expect(
      catchUpRetryDelay(
        failedAttempts: catchUpMaxFailedAttempts + 1,
        error: error(),
        stopRequested: false,
      ),
      isNull,
    );
  });

  test('catch-up does not retry stop, HTTP errors, or a finished response', () {
    expect(
      catchUpRetryDelay(
        failedAttempts: 1,
        error: error(type: DioExceptionType.cancel),
        stopRequested: false,
      ),
      isNull,
    );
    expect(
      catchUpRetryDelay(failedAttempts: 1, error: error(), stopRequested: true),
      isNull,
    );
    expect(
      catchUpRetryDelay(
        failedAttempts: 1,
        error: error(type: DioExceptionType.badResponse, status: 502),
        stopRequested: false,
      ),
      isNull,
    );
    expect(
      catchUpRetryDelay(
        failedAttempts: 0,
        error: error(),
        stopRequested: false,
      ),
      isNull,
    );
  });

  test('stop and a finished server response do not keep waiting', () {
    expect(
      queryContinuesRemotely(
        error(type: DioExceptionType.cancel),
        stopRequested: false,
      ),
      isFalse,
    );
    expect(queryContinuesRemotely(error(), stopRequested: true), isFalse);
    expect(
      queryContinuesRemotely(error(status: 499), stopRequested: false),
      isFalse,
    );
    expect(
      queryContinuesRemotely(
        error(type: DioExceptionType.badResponse, status: 502),
        stopRequested: false,
      ),
      isFalse,
    );
  });
}
