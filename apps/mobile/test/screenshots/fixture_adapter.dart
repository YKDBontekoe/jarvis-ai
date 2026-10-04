import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

/// Answers by "METHOD /path" (exact, then by longest matching prefix) and
/// logs everything it could not answer so fixtures are easy to fill in.
class ScreenshotHttp implements HttpClientAdapter {
  ScreenshotHttp(this.routes);

  final Map<String, Object?> routes;
  final Set<String> missing = {};

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    final key = '${options.method} ${options.path}';
    Object? body;
    var found = false;
    if (routes.containsKey(key)) {
      body = routes[key];
      found = true;
    } else {
      final prefixes = routes.keys.where(
        (route) =>
            route.endsWith('*') &&
            key.startsWith(route.substring(0, route.length - 1)),
      );
      if (prefixes.isNotEmpty) {
        final best = prefixes.reduce((a, b) => a.length >= b.length ? a : b);
        body = routes[best];
        found = true;
      }
    }
    if (!found) missing.add(key);
    return ResponseBody.fromString(
      jsonEncode(
        body ?? (options.method == 'GET' ? <Object>[] : <String, Object>{}),
      ),
      found ? 200 : (options.method == 'GET' ? 404 : 200),
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
