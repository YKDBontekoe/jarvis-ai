import 'dart:typed_data';

Future<void> openWhatsAppBytes(Uint8List bytes, String fileName) =>
    Future.error(
      UnsupportedError('Opening a file is only available in the app.'),
    );
