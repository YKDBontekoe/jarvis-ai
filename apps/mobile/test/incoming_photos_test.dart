import 'dart:typed_data';

import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/incoming_photos.dart';

// A 1x1 transparent PNG.
final _png = Uint8List.fromList([
  0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, //
  0x49, 0x48, 0x44, 0x52, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01,
  0x08, 0x06, 0x00, 0x00, 0x00, 0x1F, 0x15, 0xC4, 0x89, 0x00, 0x00, 0x00,
  0x0D, 0x49, 0x44, 0x41, 0x54, 0x78, 0x9C, 0x63, 0x00, 0x01, 0x00, 0x00,
  0x05, 0x00, 0x01, 0x0D, 0x0A, 0x2D, 0xB4, 0x00, 0x00, 0x00, 0x00, 0x49,
  0x45, 0x4E, 0x44, 0xAE, 0x42, 0x60, 0x82,
]);

final _jpeg = Uint8List.fromList([0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10]);
final _webp = Uint8List.fromList([
  0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00, 0x57, 0x45, 0x42, 0x50,
]);
final _gif = Uint8List.fromList([0x47, 0x49, 0x46, 0x38, 0x39, 0x61]);

void main() {
  test('sniffs PNG, JPEG, and WebP and rejects GIF', () {
    expect(sniffedImageExtension(_png), '.png');
    expect(sniffedImageExtension(_jpeg), '.jpg');
    expect(sniffedImageExtension(_webp), '.webp');
    expect(sniffedImageExtension(_gif), isNull);
    expect(sniffedImageExtension(Uint8List(0)), isNull);
  });

  test('names a pasted photo from a path and ignores content URIs', () {
    expect(incomingPhotoFileName('leaf.HEIC', '.jpg'), 'leaf.jpg');
    expect(incomingPhotoFileName('C:\\Temp\\shot.png', '.png'), 'shot.png');
    expect(
      incomingPhotoFileName('content://media/1', '.png'),
      'photo.png',
    );
    expect(incomingPhotoFileName(null, '.webp'), 'photo.webp');
  });

  test('keeps JPEG, PNG, and WebP that fit the chat limits', () {
    final result = prepareIncomingPhotos(
      incoming: [
        IncomingPhoto(bytes: _png, name: 'plant.png'),
        IncomingPhoto(bytes: _jpeg, name: 'receipt.jpg'),
        IncomingPhoto(bytes: _webp, name: 'sticker.webp'),
      ],
      remainingSlots: 4,
    );

    expect(result.photos.map((photo) => photo.fileName), [
      'plant.png',
      'receipt.jpg',
      'sticker.webp',
    ]);
    expect(result.notice, isNull);
  });

  test('skips GIF and oversized photos with a notice', () {
    final huge = Uint8List(maxChatPhotoBytes + 1)..setAll(0, _jpeg);
    final skippedType = prepareIncomingPhotos(
      incoming: [IncomingPhoto(bytes: _gif, name: 'loop.gif')],
      remainingSlots: 4,
    );
    final skippedLarge = prepareIncomingPhotos(
      incoming: [IncomingPhoto(bytes: huge, name: 'huge.jpg')],
      remainingSlots: 4,
    );

    expect(skippedType.photos, isEmpty);
    expect(skippedType.notice, 'Only JPEG, PNG, and WebP photos can be sent.');
    expect(skippedLarge.photos, isEmpty);
    expect(
      skippedLarge.notice,
      'A photo is larger than 8 MB and was skipped.',
    );
  });

  test('caps a paste at the remaining slots', () {
    final result = prepareIncomingPhotos(
      incoming: [
        IncomingPhoto(bytes: _png, name: 'a.png'),
        IncomingPhoto(bytes: _png, name: 'b.png'),
        IncomingPhoto(bytes: _png, name: 'c.png'),
      ],
      remainingSlots: 2,
    );

    expect(result.photos.map((photo) => photo.fileName), ['a.png', 'b.png']);
    expect(result.notice, 'Only the first 2 photos were added.');
  });

  test('explains when the composer is already full', () {
    final result = prepareIncomingPhotos(
      incoming: [IncomingPhoto(bytes: _png, name: 'a.png')],
      remainingSlots: 0,
    );

    expect(result.photos, isEmpty);
    expect(result.notice, 'You can send up to 4 photos at once.');
  });
}
