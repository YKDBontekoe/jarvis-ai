part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

/// Photos picked in the composer: picked, uploaded to the owner's files, and
/// sent with the next message by file id.
mixin _ChatScreenPhotos on _ChatScreenController {
  static const _maxPhotoBytes = 8 * 1024 * 1024;
  int _photoSerial = 0;

  void _showPhotoSources() {
    _dismissKeyboard();
    unawaited(
      showModalBottomSheet<void>(
        context: context,
        showDragHandle: true,
        builder: (sheetContext) => SafeArea(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                leading: const IconBadge(icon: PhosphorIconsRegular.camera),
                title: const Text('Take a photo'),
                onTap: () {
                  Navigator.pop(sheetContext);
                  unawaited(_pickPhotos(ImageSource.camera));
                },
              ),
              ListTile(
                leading: const IconBadge(icon: PhosphorIconsRegular.image),
                title: const Text('Choose from library'),
                subtitle: Text(
                  'Up to $_maxPhotosPerMessage photos per message',
                ),
                onTap: () {
                  Navigator.pop(sheetContext);
                  unawaited(_pickPhotos(ImageSource.gallery));
                },
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _pickPhotos(ImageSource source) async {
    final room = _maxPhotosPerMessage - _pendingPhotos.length;
    if (room <= 0) {
      _showPhotoNotice(
        'You can send up to $_maxPhotosPerMessage photos at once.',
      );
      return;
    }
    final picker = ImagePicker();
    List<XFile> picked;
    try {
      if (source == ImageSource.camera) {
        final photo = await picker.pickImage(
          source: ImageSource.camera,
          maxWidth: 2048,
          maxHeight: 2048,
          imageQuality: 85,
        );
        picked = photo == null ? const [] : [photo];
      } else {
        picked = await picker.pickMultiImage(
          maxWidth: 2048,
          maxHeight: 2048,
          imageQuality: 85,
          limit: room > 1 ? room : null,
        );
      }
    } catch (_) {
      _showPhotoNotice(
        source == ImageSource.camera
            ? 'Jarvis could not open the camera. Check camera access in Settings.'
            : 'Jarvis could not open your photos. Check photo access in Settings.',
      );
      return;
    }
    if (!mounted || picked.isEmpty) return;
    if (picked.length > room) {
      _showPhotoNotice('Only the first $room photos were added.');
      picked = picked.take(room).toList();
    }
    for (final file in picked) {
      final bytes = await file.readAsBytes();
      if (!mounted) return;
      if (bytes.isEmpty || bytes.length > _maxPhotoBytes) {
        _showPhotoNotice('A photo is larger than 8 MB and was skipped.');
        continue;
      }
      final photo = PendingPhoto(
        localId: 'photo-${++_photoSerial}',
        bytes: bytes,
        fileName: photoFileName(file.name),
      );
      setState(() => _pendingPhotos = [..._pendingPhotos, photo]);
      unawaited(_uploadPhoto(photo));
    }
  }

  Future<void> _uploadPhoto(PendingPhoto photo) async {
    void replace(PendingPhoto next) {
      if (!mounted) return;
      setState(
        () => _pendingPhotos = [
          for (final item in _pendingPhotos)
            item.localId == photo.localId ? next : item,
        ],
      );
    }

    try {
      final response = await _http.post<dynamic>(
        '/api/v1/files',
        data: FormData.fromMap({
          'file': MultipartFile.fromBytes(
            photo.bytes,
            filename: photo.fileName,
            contentType: DioMediaType.parse(photoContentType(photo.fileName)),
          ),
        }),
        options: Options(
          sendTimeout: const Duration(minutes: 2),
          receiveTimeout: const Duration(minutes: 2),
        ),
      );
      final id = asJsonString(jsonObject(response.data)?['id']);
      if (id == null) throw const FormatException('Missing file id.');
      _photoBytes[id] = Future.value(photo.bytes);
      replace(photo.copyWith(fileId: id));
    } catch (_) {
      replace(photo.copyWith(failed: true));
      _showPhotoNotice(
        'A photo could not be uploaded. Remove it and try again.',
      );
    }
  }

  void _removePendingPhoto(PendingPhoto photo) => setState(
    () => _pendingPhotos = _pendingPhotos
        .where((item) => item.localId != photo.localId)
        .toList(),
  );

  /// Bytes of a sent photo, fetched once per session from the owner's files.
  Future<Uint8List?> _loadPhoto(String fileId) =>
      _photoBytes.putIfAbsent(fileId, () async {
        try {
          final response = await _http.get<List<int>>(
            '/api/v1/files/$fileId/content',
            options: Options(responseType: ResponseType.bytes),
          );
          final data = response.data;
          return data == null ? null : Uint8List.fromList(data);
        } catch (_) {
          _photoBytes.remove(fileId);
          return null;
        }
      });

  void _showPhotoNotice(String message) {
    if (!mounted) return;
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }
}

const _maxPhotosPerMessage = 4;

/// Keeps a picked photo's name with an extension the server accepts.
String photoFileName(String name) {
  final lower = name.toLowerCase();
  if (lower.endsWith('.jpg') ||
      lower.endsWith('.jpeg') ||
      lower.endsWith('.png') ||
      lower.endsWith('.webp')) {
    return name;
  }
  final dot = name.lastIndexOf('.');
  return '${dot > 0 ? name.substring(0, dot) : 'photo'}.jpg';
}

String photoContentType(String name) {
  final lower = name.toLowerCase();
  if (lower.endsWith('.png')) return 'image/png';
  if (lower.endsWith('.webp')) return 'image/webp';
  return 'image/jpeg';
}
