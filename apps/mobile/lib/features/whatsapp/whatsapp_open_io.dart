import 'dart:io';
import 'dart:typed_data';

import 'package:open_filex/open_filex.dart';
import 'package:path_provider/path_provider.dart';

Future<void> openWhatsAppBytes(Uint8List bytes, String fileName) async {
  final directory = await getTemporaryDirectory();
  final safe = fileName.replaceAll(RegExp(r'[^A-Za-z0-9._-]'), '_');
  final path = '${directory.path}/wa-${DateTime.now().microsecondsSinceEpoch}-$safe';
  await File(path).writeAsBytes(bytes, flush: true);
  final result = await OpenFilex.open(path);
  if (result.type != ResultType.done) {
    throw StateError(result.message);
  }
}
