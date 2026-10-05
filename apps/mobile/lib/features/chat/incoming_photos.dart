import 'dart:typed_data';

/// JPEG, PNG, and WebP up to this size can go with a chat message.
const maxChatPhotoBytes = 8 * 1024 * 1024;

/// How many photos one message can carry.
const maxPhotosPerMessage = 4;

/// Image bytes from paste, drop, the keyboard, or the picker.
class IncomingPhoto {
  const IncomingPhoto({required this.bytes, this.name, this.mimeType});

  final Uint8List bytes;
  final String? name;
  final String? mimeType;
}

/// A chat-supported image with a file name the upload API accepts.
class PreparedIncomingPhoto {
  const PreparedIncomingPhoto({required this.bytes, required this.fileName});

  final Uint8List bytes;
  final String fileName;
}

class IncomingPhotoResult {
  const IncomingPhotoResult({required this.photos, this.notice});

  final List<PreparedIncomingPhoto> photos;
  final String? notice;
}

/// Keeps JPEG, PNG, and WebP photos that fit the chat limits.
IncomingPhotoResult prepareIncomingPhotos({
  required List<IncomingPhoto> incoming,
  required int remainingSlots,
  int maxBytes = maxChatPhotoBytes,
  int maxPhotos = maxPhotosPerMessage,
}) {
  if (incoming.isEmpty) return const IncomingPhotoResult(photos: []);
  if (remainingSlots <= 0) {
    return IncomingPhotoResult(
      photos: const [],
      notice: 'You can send up to $maxPhotos photos at once.',
    );
  }

  final photos = <PreparedIncomingPhoto>[];
  var skippedLarge = false;
  var skippedType = false;
  var truncated = false;

  for (final item in incoming) {
    if (photos.length >= remainingSlots) {
      truncated = true;
      break;
    }
    if (item.bytes.isEmpty) {
      skippedType = true;
      continue;
    }
    if (item.bytes.length > maxBytes) {
      skippedLarge = true;
      continue;
    }
    final extension = sniffedImageExtension(item.bytes);
    if (extension == null) {
      skippedType = true;
      continue;
    }
    photos.add(
      PreparedIncomingPhoto(
        bytes: item.bytes,
        fileName: incomingPhotoFileName(item.name, extension),
      ),
    );
  }

  String? notice;
  if (truncated) {
    notice = 'Only the first $remainingSlots photos were added.';
  } else if (skippedLarge) {
    notice = 'A photo is larger than 8 MB and was skipped.';
  } else if (skippedType) {
    notice = photos.isEmpty
        ? 'Only JPEG, PNG, and WebP photos can be sent.'
        : 'A photo was skipped because it is not JPEG, PNG, or WebP.';
  }

  return IncomingPhotoResult(photos: photos, notice: notice);
}

/// `.png`, `.jpg`, or `.webp` when [bytes] are a chat-supported image.
String? sniffedImageExtension(Uint8List bytes) {
  if (bytes.length >= 8 &&
      bytes[0] == 0x89 &&
      bytes[1] == 0x50 &&
      bytes[2] == 0x4e &&
      bytes[3] == 0x47 &&
      bytes[4] == 0x0d &&
      bytes[5] == 0x0a &&
      bytes[6] == 0x1a &&
      bytes[7] == 0x0a) {
    return '.png';
  }
  if (bytes.length >= 3 &&
      bytes[0] == 0xff &&
      bytes[1] == 0xd8 &&
      bytes[2] == 0xff) {
    return '.jpg';
  }
  if (bytes.length >= 12 &&
      bytes[0] == 0x52 &&
      bytes[1] == 0x49 &&
      bytes[2] == 0x46 &&
      bytes[3] == 0x46 &&
      bytes[8] == 0x57 &&
      bytes[9] == 0x45 &&
      bytes[10] == 0x42 &&
      bytes[11] == 0x50) {
    return '.webp';
  }
  return null;
}

/// A safe stem plus the sniffed extension, e.g. `leaf.png`.
String incomingPhotoFileName(String? name, String extension) =>
    '${_photoStem(name)}$extension';

String _photoStem(String? name) {
  if (name == null || name.trim().isEmpty) return 'photo';
  var trimmed = name.trim().split('?').first;
  if (trimmed.contains('://')) return 'photo';
  trimmed = trimmed.split('/').last.split('\\').last;
  if (trimmed.isEmpty || trimmed.contains(':')) return 'photo';
  final dot = trimmed.lastIndexOf('.');
  final stem = dot > 0 ? trimmed.substring(0, dot) : trimmed;
  return stem.isEmpty ? 'photo' : stem;
}
