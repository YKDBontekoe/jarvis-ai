import 'dart:js_interop';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:web/web.dart' as web;

Future<void> downloadAndOpen(Dio http, String id, String fileName) async {
  final response = await http.get<List<int>>(
    '/api/v1/files/$id/content',
    options: Options(
      responseType: ResponseType.bytes,
      receiveTimeout: const Duration(minutes: 2),
    ),
  );
  final bytes = Uint8List.fromList(response.data ?? const <int>[]);
  final blob = web.Blob([bytes.toJS].toJS);
  final url = web.URL.createObjectURL(blob);
  final anchor = web.document.createElement('a') as web.HTMLAnchorElement;
  anchor.href = url;
  anchor.download = fileName;
  web.document.body?.appendChild(anchor);
  anchor.click();
  anchor.remove();
  web.URL.revokeObjectURL(url);
}
