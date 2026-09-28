import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import '../auth/auth_session.dart';
import 'api_errors.dart';

/// Attaches the owner bearer token and refreshes once on 401.
void attachJarvisAuthInterceptor({
  required Dio http,
  required AuthSession auth,
  required bool Function() isSessionInactive,
  required VoidCallback onAuthLost,
}) {
  http.interceptors.add(
    InterceptorsWrapper(
      onRequest: (options, handler) async {
        try {
          final token = await auth.accessToken();
          if (auth.enabled && (token == null || token.isEmpty)) {
            if (!isSessionInactive()) onAuthLost();
            handler.reject(
              DioException(
                requestOptions: options,
                type: DioExceptionType.cancel,
                error: StateError('Missing access token.'),
              ),
            );
            return;
          }
          if (token != null) {
            options.headers['Authorization'] = 'Bearer $token';
          }
          handler.next(options);
        } catch (error) {
          handler.reject(DioException(requestOptions: options, error: error));
        }
      },
      onError: (error, handler) async {
        if (isSessionInactive() ||
            !isAuthExpired(error, authEnabled: auth.enabled) ||
            error.requestOptions.extra['jarvisRetriedAuth'] == true) {
          if (!isSessionInactive() &&
              isAuthExpired(error, authEnabled: auth.enabled)) {
            onAuthLost();
          }
          handler.next(error);
          return;
        }
        try {
          final token = await auth.accessToken(forceRefresh: true);
          if (token == null || token.isEmpty) {
            if (!isSessionInactive()) onAuthLost();
            handler.next(error);
            return;
          }
          final request = error.requestOptions;
          request.headers['Authorization'] = 'Bearer $token';
          request.extra['jarvisRetriedAuth'] = true;
          final data = request.data;
          if (data is FormData) {
            request.data = data.clone();
          }
          handler.resolve(await http.fetch(request));
        } on DioException catch (retryError) {
          handler.next(retryError);
        } catch (_) {
          handler.next(error);
        }
      },
    ),
  );
}
