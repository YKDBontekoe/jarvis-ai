import 'dart:convert';

import 'package:file_picker/file_picker.dart';

/// Lets the owner choose a bank CSV and returns its text; null when they cancel.
Future<String?> pickBankStatement() async {
  final file = await FilePicker.pickFile(
    type: FileType.custom,
    allowedExtensions: const ['csv', 'txt'],
  );
  if (file == null) return null;
  return utf8.decode(await file.readAsBytes(), allowMalformed: true);
}
