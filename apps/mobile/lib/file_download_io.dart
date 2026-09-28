import 'dart:io';

import 'package:dio/dio.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

Future<void> downloadAndOpen(Dio http, String id, String fileName) async {
  final response = await http.get<dynamic>(
    '/api/v1/files/$id/content',
    options: Options(
      responseType: ResponseType.bytes,
      receiveTimeout: const Duration(minutes: 2),
    ),
  );
  final raw = response.data;
  if (raw is! List<int>) {
    throw StateError('Jarvis returned an invalid file.');
  }
  final directory = await getTemporaryDirectory();
  final safeName = fileName.replaceAll(RegExp(r'[^A-Za-z0-9._-]'), '_');
  final path = '${directory.path}/$id-$safeName';
  await File(path).writeAsBytes(raw, flush: true);
  final result = await OpenFilex.open(path);
  if (result.type != ResultType.done) {
    throw StateError(result.message);
  }
}
