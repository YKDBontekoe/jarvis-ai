import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/api_errors.dart';
import 'package:jarvis_mobile/auth/auth_session.dart';
import 'package:jarvis_mobile/auth/auth_validation.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/devices/device_invoke.dart';
import 'package:jarvis_mobile/features/shell/utility_pages.dart';
import 'package:jarvis_mobile/theme.dart';

DioException _httpError({int? status, Object? data}) {
  final options = RequestOptions(path: '/api/v1/example');
  return DioException(
    requestOptions: options,
    type: status == null
        ? DioExceptionType.connectionError
        : DioExceptionType.badResponse,
    response: status == null
        ? null
        : Response<dynamic>(
            requestOptions: options,
            statusCode: status,
            data: data,
          ),
  );
}

void main() {
  test(
    'sign-in form rejects incomplete credentials before the API is called',
    () {
      expect(
        signInFormError(
          email: 'nope',
          password: 'Secret1',
          creatingAccount: false,
        ),
        'Enter a valid email address.',
      );
      expect(
        signInFormError(email: 'a@b.com', password: '', creatingAccount: false),
        'Enter your password.',
      );
      expect(
        signInFormError(
          email: 'a@b.com',
          password: 'short',
          creatingAccount: true,
        ),
        'Use 8 to 128 characters with an uppercase letter, a lowercase letter, and a number.',
      );
      expect(
        signInFormError(
          email: 'a@b.com',
          password: 'Secret1password',
          creatingAccount: true,
        ),
        isNull,
      );
    },
  );

  test('passwordMeetsPolicy requires mixed case, a number, and length', () {
    expect(passwordMeetsPolicy('Secret1x'), isTrue);
    expect(passwordMeetsPolicy('secret1x'), isFalse);
    expect(passwordMeetsPolicy('SECRET1X'), isFalse);
    expect(passwordMeetsPolicy('Secretxx'), isFalse);
  });

  test('accountErrorMessage prefers API messages over generic HTTP text', () {
    expect(
      accountErrorMessage(
        _httpError(status: 401, data: {'message': 'No match.'}),
      ),
      'No match.',
    );
    expect(
      accountErrorMessage(_httpError(status: 401)),
      'Invalid email or password.',
    );
  });

  test('describeApiError maps status codes and unreachable API failures', () {
    expect(
      describeApiError(_httpError(status: 401)),
      contains('sign-in has expired'),
    );
    expect(
      describeApiError(_httpError(status: 503)),
      contains('temporarily unavailable'),
    );
    expect(
      describeApiError(_httpError(status: 409, data: {'message': 'Busy.'})),
      'Busy.',
    );
    expect(
      describeApiError(_httpError()),
      contains('Could not reach the Jarvis API'),
    );
  });

  test('AuthSession.sessionFrom rejects incomplete JSON', () {
    expect(
      () => AuthSession.sessionFrom({'accessToken': 'a'}),
      throwsStateError,
    );
    final session = AuthSession.sessionFrom({
      'accessToken': 'a',
      'refreshToken': 'r',
      'expiresAt': '2030-01-01T00:00:00.000Z',
    });
    expect(session.accessToken, 'a');
    expect(session.refreshToken, 'r');
  });

  test(
    'utilityPageFor knows settings destinations and ignores unknown ones',
    () {
      final http = Dio();
      expect(utilityPageFor('tasks', http), isNotNull);
      expect(utilityPageFor('agents', http), isNotNull);
      expect(utilityPageFor('sign_out', http), isNull);
      expect(utilityPageFor('nope', http), isNull);
    },
  );

  testWidgets('device invoke rejects non-http URLs without launching', (
    tester,
  ) async {
    await tester.pumpWidget(
      const Directionality(textDirection: TextDirection.ltr, child: SizedBox()),
    );
    final context = tester.element(find.byType(SizedBox));
    final blocked = await performDeviceCapability(
      capability: 'open_url',
      event: {
        'arguments': {'url': 'file:///tmp/secret'},
      },
      mounted: true,
      context: context,
    );
    expect(blocked.error, 'That URL cannot be opened.');
    expect(blocked.result, isNull);

    final missing = await performDeviceCapability(
      capability: 'open_url',
      event: const {},
      mounted: true,
      context: context,
    );
    expect(missing.error, 'No URL was provided.');
  });

  testWidgets('device invoke reports a declined http URL as a result', (
    tester,
  ) async {
    ({String? result, String? error})? outcome;
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) => TextButton(
            onPressed: () async {
              outcome = await performDeviceCapability(
                capability: 'open_url',
                event: {
                  'arguments': {'url': 'https://example.com/docs'},
                },
                mounted: true,
                context: context,
              );
            },
            child: const Text('open'),
          ),
        ),
      ),
    );
    await tester.tap(find.text('open'));
    await tester.pumpAndSettle();
    expect(find.text('Open this link?'), findsOneWidget);
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(outcome?.error, isNull);
    expect(
      outcome?.result,
      'The owner declined to open https://example.com/docs.',
    );
  });

  testWidgets('markdown ignores non-http link taps', (tester) async {
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: const Scaffold(
          body: JarvisMarkdown(
            data: '[secret](file:///tmp/secret) [script](javascript:alert(1))',
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('secret'));
    await tester.pump();
    await tester.tap(find.text('script'));
    await tester.pump();
  });
}
