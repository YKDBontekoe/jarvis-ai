import 'incoming_photos.dart';

/// Listens for pasted or dropped images. No-op off the web.
class ImagePasteListener {
  ImagePasteListener({
    required this.shouldHandlePaste,
    required this.shouldHandleDrop,
    required this.onImages,
  });

  final bool Function() shouldHandlePaste;
  final bool Function() shouldHandleDrop;
  final void Function(List<IncomingPhoto> images) onImages;

  void attach() {}

  void detach() {}
}

/// Images currently on the clipboard, if this platform can read them.
Future<List<IncomingPhoto>> readClipboardImages() async => const [];
