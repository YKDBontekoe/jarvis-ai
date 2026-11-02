import 'dart:io';

import 'package:dio/dio.dart';
import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

/// Where a downloaded file is kept while the app can reuse it.
String downloadPathFor(Directory directory, String id, String fileName) {
  final safeName = fileName.replaceAll(RegExp(r'[^A-Za-z0-9._-]'), '_');
  return '${directory.path}/$id-$safeName';
}

/// Downloads a file once and opens it. A completed copy still in the temporary
/// folder (and the expected size, when known) is opened again without another
/// download; a partial download is never left behind or reused.
///
/// Returns true when the stored copy was reused.
Future<bool> downloadAndOpen(
  Dio http,
  String id,
  String fileName, {
  int? expectedBytes,
  void Function(int received, int total)? onProgress,
  CancelToken? cancelToken,
  Directory? directory,
  Future<void> Function(String path)? open,
}) async {
  final dir = directory ?? await getTemporaryDirectory();
  final path = downloadPathFor(dir, id, fileName);
  final stored = File(path);
  var reused = false;
  if (await stored.exists() &&
      (expectedBytes == null || await stored.length() == expectedBytes)) {
    reused = true;
  } else {
    final part = File('$path.part');
    try {
      await http.download(
        '/api/v1/files/$id/content',
        part.path,
        onReceiveProgress: onProgress,
        cancelToken: cancelToken,
        options: Options(receiveTimeout: const Duration(minutes: 2)),
      );
      await part.rename(path);
    } catch (_) {
      if (await part.exists()) await part.delete();
      rethrow;
    }
  }
  await (open ?? _openFile)(path);
  return reused;
}

Future<void> _openFile(String path) async {
  final result = await OpenFilex.open(path);
  if (result.type != ResultType.done) {
    throw StateError(result.message);
  }
}
