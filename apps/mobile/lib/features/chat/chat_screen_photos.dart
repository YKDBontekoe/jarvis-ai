part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

/// Photos picked in the composer: picked, uploaded to the owner's files, and
/// sent with the next message by file id.
mixin _ChatScreenPhotos on _ChatScreenController {
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
                  'Up to $maxPhotosPerMessage photos per message',
                ),
                onTap: () {
                  Navigator.pop(sheetContext);
                  unawaited(_pickPhotos(ImageSource.gallery));
                },
              ),
              ListTile(
                leading: const IconBadge(
                  icon: PhosphorIconsRegular.clipboardText,
                ),
                title: const Text('Paste a photo'),
                subtitle: const Text('From the clipboard'),
                onTap: () {
                  Navigator.pop(sheetContext);
                  unawaited(_pastePhotosFromClipboard());
                },
              ),
            ],
          ),
        ),
      ),
    );
  }

  Future<void> _pastePhotosFromClipboard() async {
    final incoming = await readClipboardImages();
    if (!mounted) return;
    if (incoming.isEmpty) {
      _composerFocus.value++;
      _showPhotoNotice('Copy a photo, then paste it in the message box.');
      return;
    }
    await _enqueueIncomingPhotos(incoming);
  }

  Future<void> _pickPhotos(ImageSource source) async {
    final room = maxPhotosPerMessage - _pendingPhotos.length;
    if (room <= 0) {
      _showPhotoNotice(
        'You can send up to $maxPhotosPerMessage photos at once.',
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
    final incoming = <IncomingPhoto>[];
    for (final file in picked) {
      incoming.add(
        IncomingPhoto(bytes: await file.readAsBytes(), name: file.name),
      );
      if (!mounted) return;
    }
    await _enqueueIncomingPhotos(incoming);
  }

  Future<void> _enqueueIncomingPhotos(List<IncomingPhoto> incoming) async {
    final result = prepareIncomingPhotos(
      incoming: incoming,
      remainingSlots: maxPhotosPerMessage - _pendingPhotos.length,
    );
    if (result.notice != null) _showPhotoNotice(result.notice!);
    for (final photo in result.photos) {
      if (!mounted) return;
      final pending = PendingPhoto(
        localId: 'photo-${++_photoSerial}',
        bytes: photo.bytes,
        fileName: photo.fileName,
      );
      setState(() => _pendingPhotos = [..._pendingPhotos, pending]);
      unawaited(_uploadPhoto(pending));
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
