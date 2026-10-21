import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_errors.dart';
import 'package:jarvis_mobile/api/api_problem.dart';

DioException _problemError(Map<String, dynamic> body, {int status = 400}) {
  final options = RequestOptions(path: '/api/v1/example');
  return DioException(
    requestOptions: options,
    type: DioExceptionType.badResponse,
    response: Response<dynamic>(
      requestOptions: options,
      statusCode: status,
      data: body,
    ),
  );
}

void main() {
  test('parseJarvisApiProblem reads contract extensions', () {
    final problem = parseJarvisApiProblem({
      'type': 'https://jarvis.dev/problems/v1/conflict',
      'title': 'Conflict',
      'status': 409,
      'detail': 'Busy.',
      'code': 'conflict',
      'traceId': 'trace-1',
      'apiErrorVersion': 1,
    });
    expect(problem?.code, 'conflict');
    expect(problem?.traceId, 'trace-1');
    expect(problem?.apiErrorVersion, 1);
    expect(problem?.detail, 'Busy.');
  });

  test('describeApiError prefers stable codes over HTTP status', () {
    expect(
      describeApiError(
        _problemError(
          {
            'code': 'dependency_unavailable',
            'detail': 'Voice service is temporarily unavailable.',
            'status': 503,
          },
          status: 503,
        ),
      ),
      'Voice service is temporarily unavailable.',
    );
    expect(
      describeApiError(
        _problemError(
          {
            'code': 'authentication_required',
            'detail': 'Authentication is required.',
            'status': 401,
          },
          status: 401,
        ),
      ),
      contains('sign-in has expired'),
    );
    expect(
      describeApiError(
        _problemError(
          {
            'code': 'conflict',
            'detail': 'Busy.',
            'status': 409,
          },
          status: 409,
        ),
      ),
      'Busy.',
    );
  });

  test('isAuthExpired recognizes authentication_required code', () {
    final error = _problemError(
      {
        'code': JarvisApiErrorCodes.authenticationRequired,
        'status': 401,
      },
      status: 401,
    );
    expect(isAuthExpired(error, authEnabled: true), isTrue);
    expect(isAuthExpired(error, authEnabled: false), isFalse);
  });
}
