import 'dart:io';

import 'package:dio/dio.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

Future<void> downloadAndOpen(Dio http, String id, String fileName) async {
  final response = await http.get<List<int>>(
    '/api/v1/files/$id/content',
    options: Options(
      responseType: ResponseType.bytes,
      receiveTimeout: const Duration(minutes: 2),
    ),
  );
  final directory = await getTemporaryDirectory();
  final safeName = fileName.replaceAll(RegExp(r'[^A-Za-z0-9._-]'), '_');
  final path = '${directory.path}/$id-$safeName';
  await File(path).writeAsBytes(response.data ?? const <int>[], flush: true);
  final result = await OpenFilex.open(path);
  if (result.type != ResultType.done) {
    throw StateError(result.message);
  }
}
