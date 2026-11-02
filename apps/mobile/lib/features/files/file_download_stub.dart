import 'package:dio/dio.dart';

Future<bool> downloadAndOpen(
  Dio http,
  String id,
  String fileName, {
  int? expectedBytes,
  void Function(int received, int total)? onProgress,
  CancelToken? cancelToken,
}) => Future.error(
  UnsupportedError('Download and open is only available in the app.'),
);
