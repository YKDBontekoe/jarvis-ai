import 'dart:convert';
import 'dart:typed_data';

import 'package:dio/dio.dart';

typedef RecordedRequest = ({String method, String path, Object? body});

/// Serves canned JSON by "METHOD /path" and records every request with its decoded body.
class FixtureHttp implements HttpClientAdapter {
  final Map<String, Object?> responses = {};
  final Map<String, int> statuses = {};
  final List<RecordedRequest> requests = [];

  Dio client() =>
      Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
        ..httpClientAdapter = this;

  void on(String method, String path, Object? body, {int status = 200}) {
    responses['$method $path'] = body;
    statuses['$method $path'] = status;
  }

  Iterable<RecordedRequest> sent(String method, String path) =>
      requests.where((r) => r.method == method && r.path == path);

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    Object? body = options.data;
    if (body is String && body.isNotEmpty) body = jsonDecode(body);
    requests.add((method: options.method, path: options.path, body: body));
    final key = '${options.method} ${options.path}';
    final status = statuses[key] ?? (responses.containsKey(key) ? 200 : 404);
    return ResponseBody.fromString(
      jsonEncode(responses[key] ?? const <String, Object>{}),
      status,
      headers: {
        Headers.contentTypeHeader: [Headers.jsonContentType],
      },
    );
  }

  @override
  void close({bool force = false}) {}
}
