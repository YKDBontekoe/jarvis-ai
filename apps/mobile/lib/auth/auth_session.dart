import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter_secure_storage/flutter_secure_storage.dart';

import '../api/api_config.dart';
import '../json_maps.dart';

class AuthSession {
  AuthSession({this.enabled = true});

  final bool enabled;
  final _storage = const FlutterSecureStorage();
  final _authHttp = attachSentryHttp(
    Dio(
      BaseOptions(
        baseUrl: apiBaseUrl,
        connectTimeout: apiConnectTimeout,
        receiveTimeout: apiReceiveTimeout,
      ),
    ),
  );
  Future<String?>? _accessTokenInFlight;
  var _generation = 0;
  Future<void> _sessionLock = Future.value();

  Future<T> _serialized<T>(Future<T> Function() action) {
    final previous = _sessionLock;
    final released = Completer<void>();
    _sessionLock = released.future;
    return previous
        .catchError((_) {})
        .then((_) => action())
        .whenComplete(released.complete);
  }

  Future<String?> accessToken({bool forceRefresh = false}) {
    final existing = _accessTokenInFlight;
    if (existing != null && !forceRefresh) return existing;
    late final Future<String?> pending;
    pending =
        _serialized(
          () => _readOrRefreshAccessToken(forceRefresh: forceRefresh),
        ).whenComplete(() {
          if (identical(_accessTokenInFlight, pending)) {
            _accessTokenInFlight = null;
          }
        });
    _accessTokenInFlight = pending;
    return pending;
  }

  Future<String?> _readOrRefreshAccessToken({bool forceRefresh = false}) async {
    if (!enabled) return null;
    final generation = _generation;
    final expiration = int.tryParse(
      await _storage.read(key: 'token_expiration') ?? '',
    );
    final accessToken = await _storage.read(key: 'access_token');
    if (generation != _generation) return null;
    if (!forceRefresh &&
        accessToken != null &&
        expiration != null &&
        expiration > DateTime.now().millisecondsSinceEpoch + 30000) {
      return accessToken;
    }

    final refreshToken = await _storage.read(key: 'refresh_token');
    if (refreshToken == null) return null;
    try {
      final response = await _authHttp.post<Object?>(
        '/api/v1/auth/refresh',
        data: {'refreshToken': refreshToken},
      );
      if (generation != _generation) return null;
      final session = sessionFrom(response.data);
      await _save(session.accessToken, session.refreshToken, session.expiresAt);
      if (generation != _generation) return null;
      return session.accessToken;
    } on DioException catch (error) {
      if (error.response?.statusCode == 401) {
        _generation++;
        _accessTokenInFlight = null;
        await _storage.deleteAll();
        return null;
      }
      rethrow;
    }
  }

  Future<void> signIn(String email, String password) =>
      _exchange('/api/v1/auth/login', email, password);

  Future<void> register(String email, String password) =>
      _exchange('/api/v1/auth/register', email, password);

  Future<void> _exchange(String path, String email, String password) async {
    await _serialized(() async {
      final response = await _authHttp.post<Object?>(
        path,
        data: {'email': email, 'password': password},
      );
      _generation++;
      _accessTokenInFlight = null;
      final session = sessionFrom(response.data);
      await _save(session.accessToken, session.refreshToken, session.expiresAt);
    });
  }

  Future<void> signOut() async {
    await _serialized(() async {
      _generation++;
      _accessTokenInFlight = null;
      final refreshToken = await _storage.read(key: 'refresh_token');
      if (refreshToken != null) {
        try {
          await _authHttp.post<void>(
            '/api/v1/auth/logout',
            data: {'refreshToken': refreshToken},
          );
        } catch (_) {
          // Local sign-out still clears the session when the API is unreachable.
        }
      }
      await _storage.deleteAll();
    });
  }

  static ({String accessToken, String refreshToken, DateTime expiresAt})
  sessionFrom(Object? data) {
    final body = jsonObject(data);
    final accessToken = asJsonString(body?['accessToken']);
    final refreshToken = asJsonString(body?['refreshToken']);
    final expiresAt = jsonDate(body?['expiresAt']);
    if (accessToken == null ||
        accessToken.isEmpty ||
        refreshToken == null ||
        refreshToken.isEmpty ||
        expiresAt == null) {
      throw StateError('Jarvis returned an incomplete sign-in response.');
    }
    return (
      accessToken: accessToken,
      refreshToken: refreshToken,
      expiresAt: expiresAt,
    );
  }

  Future<void> _save(
    String accessToken,
    String refreshToken,
    DateTime expiresAt,
  ) async {
    await _storage.write(key: 'access_token', value: accessToken);
    await _storage.write(key: 'refresh_token', value: refreshToken);
    await _storage.write(
      key: 'token_expiration',
      value: expiresAt.millisecondsSinceEpoch.toString(),
    );
  }
}
