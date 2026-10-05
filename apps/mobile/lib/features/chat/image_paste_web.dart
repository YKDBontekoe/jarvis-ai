import 'dart:async';
import 'dart:js_interop';
import 'dart:typed_data';

import 'package:web/web.dart' as web;

import 'incoming_photos.dart';

/// Listens for pasted or dropped images in the hosted web client.
class ImagePasteListener {
  ImagePasteListener({
    required this.shouldHandlePaste,
    required this.shouldHandleDrop,
    required this.onImages,
  });

  final bool Function() shouldHandlePaste;
  final bool Function() shouldHandleDrop;
  final void Function(List<IncomingPhoto> images) onImages;

  bool _attached = false;
  late final JSFunction _pasteJs = _onPaste.toJS;
  late final JSFunction _dropJs = _onDrop.toJS;
  late final JSFunction _dragOverJs = _onDragOver.toJS;

  void attach() {
    if (_attached) return;
    _attached = true;
    web.window.addEventListener('paste', _pasteJs, true.toJS);
    web.window.addEventListener('drop', _dropJs, true.toJS);
    web.window.addEventListener('dragover', _dragOverJs, true.toJS);
    web.window.addEventListener('dragenter', _dragOverJs, true.toJS);
  }

  void detach() {
    if (!_attached) return;
    _attached = false;
    web.window.removeEventListener('paste', _pasteJs, true.toJS);
    web.window.removeEventListener('drop', _dropJs, true.toJS);
    web.window.removeEventListener('dragover', _dragOverJs, true.toJS);
    web.window.removeEventListener('dragenter', _dragOverJs, true.toJS);
  }

  void _onPaste(web.Event event) {
    if (!shouldHandlePaste()) return;
    final files = _imageFiles((event as web.ClipboardEvent).clipboardData);
    if (files.isEmpty) return;
    event.preventDefault();
    event.stopPropagation();
    unawaited(_emit(files));
  }

  void _onDrop(web.Event event) {
    if (!shouldHandleDrop()) return;
    final files = _imageFiles((event as web.DragEvent).dataTransfer);
    if (files.isEmpty) return;
    event.preventDefault();
    event.stopPropagation();
    unawaited(_emit(files));
  }

  void _onDragOver(web.Event event) {
    if (!shouldHandleDrop()) return;
    final drag = event as web.DragEvent;
    if (!_hasFiles(drag.dataTransfer)) return;
    event.preventDefault();
    drag.dataTransfer?.dropEffect = 'copy';
  }

  Future<void> _emit(List<web.File> files) async {
    final incoming = <IncomingPhoto>[];
    for (final file in files) {
      final bytes = await _bytesOf(file);
      if (bytes.isEmpty) continue;
      incoming.add(
        IncomingPhoto(bytes: bytes, name: file.name, mimeType: file.type),
      );
    }
    if (incoming.isEmpty) return;
    onImages(incoming);
  }
}

Future<List<IncomingPhoto>> readClipboardImages() async {
  try {
    final jsItems = await web.window.navigator.clipboard.read().toDart;
    final incoming = <IncomingPhoto>[];
    for (final item in jsItems.toDart) {
      final imageType = _imageClipboardType(item);
      if (imageType == null) continue;
      final blob = await item.getType(imageType).toDart;
      final bytes = await _bytesOfBlob(blob);
      if (bytes.isEmpty) continue;
      incoming.add(IncomingPhoto(bytes: bytes, mimeType: imageType));
    }
    return incoming;
  } catch (_) {
    return const [];
  }
}

String? _imageClipboardType(web.ClipboardItem item) {
  for (final type in item.types.toDart) {
    final mime = type.toDart;
    if (mime.startsWith('image/')) return mime;
  }
  return null;
}

List<web.File> _imageFiles(web.DataTransfer? data) {
  if (data == null) return const [];
  final files = <web.File>[];
  final seen = <int>{};
  final items = data.items;
  for (var i = 0; i < items.length; i++) {
    final item = items[i];
    if (item.kind != 'file') continue;
    final file = item.getAsFile();
    if (file == null || !_looksLikeImage(file.type, file.name)) continue;
    if (!seen.add(identityHashCode(file))) continue;
    files.add(file);
  }
  if (files.isNotEmpty) return files;
  final list = data.files;
  for (var i = 0; i < list.length; i++) {
    final file = list.item(i);
    if (file == null || !_looksLikeImage(file.type, file.name)) continue;
    files.add(file);
  }
  return files;
}

bool _hasFiles(web.DataTransfer? data) {
  if (data == null) return false;
  if (data.files.length > 0) return true;
  for (final type in data.types.toDart) {
    if (type.toDart == 'Files') return true;
  }
  return false;
}

bool _looksLikeImage(String type, String name) {
  final mime = type.toLowerCase();
  if (mime.startsWith('image/')) return true;
  if (mime.isNotEmpty) return false;
  if (name.isEmpty) return true;
  final lower = name.toLowerCase();
  return lower.endsWith('.png') ||
      lower.endsWith('.jpg') ||
      lower.endsWith('.jpeg') ||
      lower.endsWith('.webp') ||
      lower.endsWith('.gif') ||
      lower.endsWith('.heic') ||
      lower.endsWith('.heif');
}

Future<Uint8List> _bytesOf(web.File file) => _bytesOfBlob(file);

Future<Uint8List> _bytesOfBlob(web.Blob blob) async {
  final buffer = await blob.arrayBuffer().toDart;
  return buffer.toDart.asUint8List();
}
