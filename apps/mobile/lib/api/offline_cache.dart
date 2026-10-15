import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';

import 'offline_cache_store_stub.dart'
    if (dart.library.io) 'offline_cache_store_io.dart';

/// Where cached responses live. Native builds use files in the app's private
/// support directory; the web build keeps them in memory for the session.
abstract class OfflineCacheStore {
  Future<String?> read(String key);
  Future<void> write(String key, String value);
  Future<void> clear();
}

class MemoryOfflineCacheStore implements OfflineCacheStore {
  final _values = <String, String>{};

  @override
  Future<String?> read(String key) async => _values[key];

  @override
  Future<void> write(String key, String value) async => _values[key] = value;

  @override
  Future<void> clear() async => _values.clear();
}

/// The app is showing saved data because the server could not be reached.
@immutable
class OfflineState {
  const OfflineState({this.savedAt});

  /// When the newest response shown from the cache was saved, if any was.
  final DateTime? savedAt;
}

/// Keeps the last good answer to each JSON GET and serves it when the network
/// fails, so screens open with saved data instead of an error while offline.
///
/// Only connection failures fall back: a server error or a 4xx still reaches
/// the screen. Writes never fall back (the chat outbox queues messages). The
/// cache is cleared on sign-out, so another account never sees it.
class OfflineCache {
  OfflineCache({OfflineCacheStore? store, DateTime Function()? clock})
    : _store = store ?? createPlatformOfflineCacheStore(),
      _clock = clock ?? DateTime.now;

  /// App-wide instance used by the shared HTTP client and the offline banner.
  static final instance = OfflineCache();

  /// Saved answers older than this are not shown.
  static const maxAge = Duration(days: 14);

  /// Larger bodies (big exports, long histories) are not saved.
  static const maxEntryChars = 768 * 1024;

  /// Marks a response that came from the cache.
  static const cachedAtExtra = 'jarvisCachedAt';

  /// Opt a request out of the cache.
  static const skipExtra = 'jarvisSkipOfflineCache';

  // Never cached: sign-in, secrets, binary or streaming content, and the
  // health probe itself.
  static final _skipPath = RegExp(
    r'/(auth|credentials?|secrets?|tokens?|download|content|raw|export|stream|thumbnail|avatar|photo)(/|$|\?)',
    caseSensitive: false,
  );

  final OfflineCacheStore _store;
  final DateTime Function() _clock;
  final state = ValueNotifier<OfflineState?>(null);
  Dio? _http;

  bool get isOffline => state.value != null;

  void attach(Dio http) {
    _http = http;
    http.interceptors.add(
      InterceptorsWrapper(
        onResponse: (response, handler) {
          if (response.requestOptions.extra[cachedAtExtra] == null) {
            _markOnline();
          }
          if (_cacheable(response.requestOptions) &&
              response.statusCode == 200 &&
              (response.data is Map || response.data is List)) {
            unawaited(_save(response.requestOptions, response.data));
          }
          handler.next(response);
        },
        onError: (error, handler) async {
          if (error.response != null) _markOnline();
          if (!isConnectionFailure(error)) {
            handler.next(error);
            return;
          }
          if (!_cacheable(error.requestOptions)) {
            _markOffline(null);
            handler.next(error);
            return;
          }
          final saved = await _load(error.requestOptions);
          if (saved == null) {
            _markOffline(null);
            handler.next(error);
            return;
          }
          _markOffline(saved.savedAt);
          error.requestOptions.extra[cachedAtExtra] = saved.savedAt;
          handler.resolve(
            Response<dynamic>(
              requestOptions: error.requestOptions,
              data: saved.data,
              statusCode: 200,
              extra: {cachedAtExtra: saved.savedAt},
            ),
          );
        },
      ),
    );
  }

  /// Checks whether the server answers again; clears the offline state if so.
  Future<bool> retry() async {
    final http = _http;
    if (http == null) return false;
    try {
      await http.get<void>(
        '/alive',
        options: Options(extra: {skipExtra: true}),
      );
      _markOnline();
      return true;
    } on DioException catch (error) {
      if (error.response != null) {
        _markOnline();
        return true;
      }
      return false;
    }
  }

  Future<void> clear() async {
    state.value = null;
    try {
      await _store.clear();
    } catch (_) {
      // Storage that cannot be cleared also cannot be read back.
    }
  }

  static bool isConnectionFailure(DioException error) {
    if (error.response != null) return false;
    return switch (error.type) {
      DioExceptionType.connectionError ||
      DioExceptionType.connectionTimeout ||
      DioExceptionType.sendTimeout ||
      DioExceptionType.receiveTimeout => true,
      DioExceptionType.unknown => error.error is! StateError,
      _ => false,
    };
  }

  bool _cacheable(RequestOptions options) =>
      options.method.toUpperCase() == 'GET' &&
      options.extra[skipExtra] != true &&
      options.responseType == ResponseType.json &&
      options.uri.path.contains('/api/') &&
      !_skipPath.hasMatch(options.uri.path);

  static String keyFor(RequestOptions options) {
    final uri = options.uri;
    final query = uri.queryParametersAll.entries.toList()
      ..sort((a, b) => a.key.compareTo(b.key));
    final normalized = query
        .map((entry) => '${entry.key}=${entry.value.join(',')}')
        .join('&');
    return '${uri.scheme}://${uri.authority}${uri.path}?$normalized';
  }

  Future<void> _save(RequestOptions options, Object? data) async {
    try {
      final body = jsonEncode({
        'savedAt': _clock().toUtc().toIso8601String(),
        'data': data,
      });
      if (body.length > maxEntryChars) return;
      await _store.write(keyFor(options), body);
    } catch (_) {
      // A full or unavailable disk only loses the offline copy.
    }
  }

  Future<({Object? data, DateTime savedAt})?> _load(
    RequestOptions options,
  ) async {
    try {
      final raw = await _store.read(keyFor(options));
      if (raw == null) return null;
      final decoded = jsonDecode(raw);
      if (decoded is! Map) return null;
      final savedAt = DateTime.tryParse('${decoded['savedAt']}');
      if (savedAt == null || _clock().difference(savedAt) > maxAge) {
        return null;
      }
      return (data: decoded['data'], savedAt: savedAt.toLocal());
    } catch (_) {
      return null;
    }
  }

  void _markOnline() {
    if (state.value != null) state.value = null;
  }

  void _markOffline(DateTime? savedAt) {
    final current = state.value?.savedAt;
    // Show the oldest saved time on screen so the banner never overstates freshness.
    final shown = current == null || savedAt == null
        ? (savedAt ?? current)
        : (savedAt.isBefore(current) ? savedAt : current);
    state.value = OfflineState(savedAt: shown);
  }
}

/// True when a response was served from the offline cache.
bool isCachedResponse(Response<dynamic> response) =>
    response.extra[OfflineCache.cachedAtExtra] != null;
