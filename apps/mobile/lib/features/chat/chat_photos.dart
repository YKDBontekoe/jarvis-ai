part of 'chat_widgets.dart';

/// Loads a sent photo's bytes by file id; null when it cannot be shown.
typedef PhotoLoader = Future<Uint8List?> Function(String fileId);

/// A photo picked for the next message, uploading or ready to send.
class PendingPhoto {
  const PendingPhoto({
    required this.localId,
    required this.bytes,
    required this.fileName,
    this.fileId,
    this.failed = false,
  });

  final String localId;
  final Uint8List bytes;
  final String fileName;

  /// Set once the upload finished; the message refers to the photo by it.
  final String? fileId;
  final bool failed;

  bool get uploading => fileId == null && !failed;

  PendingPhoto copyWith({String? fileId, bool? failed}) => PendingPhoto(
    localId: localId,
    bytes: bytes,
    fileName: fileName,
    fileId: fileId ?? this.fileId,
    failed: failed ?? this.failed,
  );
}

/// Thumbnails of photos waiting to be sent, shown inside the composer.
class ComposerPhotoStrip extends StatelessWidget {
  const ComposerPhotoStrip({
    required this.photos,
    required this.onRemove,
    super.key,
  });

  final List<PendingPhoto> photos;
  final ValueChanged<PendingPhoto> onRemove;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SizedBox(
      height: 76,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        padding: const EdgeInsets.fromLTRB(8, 8, 8, 0),
        itemCount: photos.length,
        separatorBuilder: (_, _) => const SizedBox(width: 8),
        itemBuilder: (context, index) {
          final photo = photos[index];
          return Semantics(
            label: photo.failed
                ? 'Photo did not upload'
                : photo.uploading
                ? 'Photo uploading'
                : 'Photo ready to send',
            image: true,
            child: Stack(
              clipBehavior: Clip.none,
              children: [
                ClipRRect(
                  borderRadius: BorderRadius.circular(12),
                  child: SizedBox.square(
                    dimension: 64,
                    child: Stack(
                      fit: StackFit.expand,
                      children: [
                        Image.memory(
                          photo.bytes,
                          fit: BoxFit.cover,
                          cacheWidth: 192,
                          gaplessPlayback: true,
                          errorBuilder: (_, _, _) => ColoredBox(
                            color: colors.surfaceRaised,
                            child: const Icon(PhosphorIconsRegular.image),
                          ),
                        ),
                        if (photo.uploading || photo.failed)
                          ColoredBox(
                            color: Colors.black.withValues(alpha: .35),
                            child: Center(
                              child: photo.failed
                                  ? const Icon(
                                      PhosphorIconsRegular.warningCircle,
                                      color: Colors.white,
                                    )
                                  : const SizedBox.square(
                                      dimension: 18,
                                      child: CircularProgressIndicator(
                                        strokeWidth: 2,
                                        color: Colors.white,
                                      ),
                                    ),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
                Positioned(
                  top: -6,
                  right: -6,
                  child: SizedBox.square(
                    dimension: 24,
                    child: IconButton(
                      tooltip: 'Remove photo',
                      padding: EdgeInsets.zero,
                      onPressed: () => onRemove(photo),
                      icon: const Icon(PhosphorIconsRegular.x, size: 12),
                      style: IconButton.styleFrom(
                        backgroundColor: colors.ink,
                        foregroundColor: colors.onInk,
                        minimumSize: const Size.square(24),
                      ),
                    ),
                  ),
                ),
              ],
            ),
          );
        },
      ),
    );
  }
}

/// Photos sent with a message, as rounded tiles that open full screen.
class MessagePhotoGrid extends StatelessWidget {
  const MessagePhotoGrid({
    required this.photos,
    required this.loader,
    super.key,
  });

  final List<MessagePhoto> photos;
  final PhotoLoader? loader;

  @override
  Widget build(BuildContext context) {
    final single = photos.length == 1;
    final size = single ? 220.0 : 108.0;
    return Wrap(
      alignment: WrapAlignment.end,
      spacing: 6,
      runSpacing: 6,
      children: [
        for (final photo in photos)
          _PhotoTile(photo: photo, loader: loader, size: size),
      ],
    );
  }
}

class _PhotoTile extends StatefulWidget {
  const _PhotoTile({
    required this.photo,
    required this.loader,
    required this.size,
  });

  final MessagePhoto photo;
  final PhotoLoader? loader;
  final double size;

  @override
  State<_PhotoTile> createState() => _PhotoTileState();
}

class _PhotoTileState extends State<_PhotoTile> {
  late Future<Uint8List?> _bytes = _load();

  Future<Uint8List?> _load() async {
    final local = widget.photo.bytes;
    if (local != null) return local;
    final loader = widget.loader;
    if (loader == null) return null;
    try {
      return await loader(widget.photo.fileId);
    } catch (_) {
      return null;
    }
  }

  @override
  void didUpdateWidget(_PhotoTile oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.photo.fileId != widget.photo.fileId) _bytes = _load();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return FutureBuilder<Uint8List?>(
      future: _bytes,
      builder: (context, snapshot) {
        final bytes = snapshot.data;
        final Widget child;
        if (bytes != null) {
          child = Image.memory(
            bytes,
            fit: BoxFit.cover,
            cacheWidth: (widget.size * 3).round(),
            gaplessPlayback: true,
            errorBuilder: (_, _, _) => _placeholder(colors, failed: true),
          );
        } else {
          child = _placeholder(
            colors,
            failed: snapshot.connectionState == ConnectionState.done,
          );
        }
        return Semantics(
          button: bytes != null,
          image: true,
          label: 'Photo ${widget.photo.fileName}',
          child: GestureDetector(
            onTap: bytes == null
                ? null
                : () => Navigator.of(context).push(
                    MaterialPageRoute<void>(
                      fullscreenDialog: true,
                      builder: (_) => PhotoViewerPage(
                        bytes: bytes,
                        title: widget.photo.fileName,
                      ),
                    ),
                  ),
            child: ClipRRect(
              borderRadius: BorderRadius.circular(16),
              child: SizedBox.square(dimension: widget.size, child: child),
            ),
          ),
        );
      },
    );
  }

  Widget _placeholder(JarvisColors colors, {required bool failed}) =>
      ColoredBox(
        color: colors.surfaceRaised,
        child: Center(
          child: failed
              ? Icon(PhosphorIconsRegular.image, color: colors.muted)
              : const SizedBox.square(
                  dimension: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
        ),
      );
}

/// A photo on black, pinch to zoom.
class PhotoViewerPage extends StatelessWidget {
  const PhotoViewerPage({required this.bytes, required this.title, super.key});

  final Uint8List bytes;
  final String title;

  @override
  Widget build(BuildContext context) => Scaffold(
    backgroundColor: Colors.black,
    appBar: AppBar(
      backgroundColor: Colors.black,
      foregroundColor: Colors.white,
      title: Text(title, maxLines: 1, overflow: TextOverflow.ellipsis),
    ),
    body: InteractiveViewer(
      maxScale: 5,
      child: Center(child: Image.memory(bytes, fit: BoxFit.contain)),
    ),
  );
}
