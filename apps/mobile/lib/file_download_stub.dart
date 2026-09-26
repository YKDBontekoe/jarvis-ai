import 'package:dio/dio.dart';

Future<void> downloadAndOpen(Dio http, String id, String fileName) =>
    Future.error(
      UnsupportedError('Download and open is only available in the app.'),
    );
