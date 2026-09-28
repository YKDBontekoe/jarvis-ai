import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/remote_query.dart';

void main() {
  DioException error({DioExceptionType type = DioExceptionType.connectionError, int? status}) {
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
    expect(
      queryContinuesRemotely(error(), stopRequested: false),
      isTrue,
    );
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

  test('stop and a finished server response do not keep waiting', () {
    expect(
      queryContinuesRemotely(
        error(type: DioExceptionType.cancel),
        stopRequested: false,
      ),
      isFalse,
    );
    expect(
      queryContinuesRemotely(error(), stopRequested: true),
      isFalse,
    );
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
