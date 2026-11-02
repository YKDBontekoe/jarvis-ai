import 'dart:js_interop';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:web/web.dart' as web;

Future<bool> downloadAndOpen(
  Dio http,
  String id,
  String fileName, {
  int? expectedBytes,
  void Function(int received, int total)? onProgress,
  CancelToken? cancelToken,
}) async {
  final response = await http.get<dynamic>(
    '/api/v1/files/$id/content',
    onReceiveProgress: onProgress,
    cancelToken: cancelToken,
    options: Options(
      responseType: ResponseType.bytes,
      receiveTimeout: const Duration(minutes: 2),
    ),
  );
  final raw = response.data;
  if (raw is! List<int>) {
    throw StateError('Jarvis returned an invalid file.');
  }
  final bytes = Uint8List.fromList(raw);
  final blob = web.Blob([bytes.toJS].toJS);
  final url = web.URL.createObjectURL(blob);
  final anchor = web.document.createElement('a') as web.HTMLAnchorElement;
  anchor.href = url;
  anchor.download = fileName;
  web.document.body?.appendChild(anchor);
  anchor.click();
  anchor.remove();
  web.URL.revokeObjectURL(url);
  return false;
}
