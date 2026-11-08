import 'dart:async';

import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';

import '../../ui/phosphor_icons.dart';

import 'file_download_stub.dart'
    if (dart.library.io) 'file_download_io.dart'
    if (dart.library.js_interop) 'file_download_web.dart'
    as file_download;
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';
import 'files_filter.dart';

const uploadableFileExtensions = [
  'pdf',
  'txt',
  'log',
  'srt',
  'vtt',
  'md',
  'markdown',
  'csv',
  'tsv',
  'ics',
  'json',
  'yaml',
  'yml',
  'docx',
  'pptx',
  'jpg',
  'jpeg',
  'png',
  'webp',
];
const maxUploadBytes = 20 * 1024 * 1024;

/// A file the owner picked to upload: its name and contents.
typedef PickedUpload = ({String name, Uint8List bytes});

enum UploadState { uploading, failed, cancelled }

/// One upload in progress (or stopped), shown at the top of the list with
/// its own progress, Cancel and Retry. Stays until it succeeds or is dismissed.
class UploadJob {
  UploadJob({required this.id, required this.fileName, required this.bytes});

  final int id;
  final String fileName;
  final Uint8List bytes;
  int sent = 0;
  UploadState state = UploadState.uploading;
  String? message;
  CancelToken cancel = CancelToken();

  int get total => bytes.length;
  bool get sentEverything => sent >= total;
}

/// A download in progress for one file.
class _DownloadJob {
  _DownloadJob();

  int received = 0;
  int total = -1;
  final CancelToken cancel = CancelToken();
}

class FilesScreen extends StatefulWidget {
  const FilesScreen({
    required this.http,
    this.onOpenConversation,
    this.pickUpload,
    this.download,
    super.key,
  });

  final Dio http;

  /// Opens a chat. Without it a file cannot be asked about from here.
  final Future<void> Function(String conversationId)? onOpenConversation;

  /// Lets the owner choose a file to upload; defaults to the system picker.
  final Future<PickedUpload?> Function()? pickUpload;

  /// Fetches and opens a file; defaults to the platform implementation.
  final Future<bool> Function(
    Dio http,
    String id,
    String fileName, {
    int? expectedBytes,
    void Function(int received, int total)? onProgress,
    CancelToken? cancelToken,
  })?
  download;

  @override
  State<FilesScreen> createState() => _FilesScreenState();
}

class _FilesScreenState extends State<FilesScreen> {
  List<Map<String, dynamic>> _files = [];
  List<Map<String, dynamic>> _collections = [];
  bool _loading = true;

  /// Items with a request running, by file id, `collection:<id>` or
  /// `new-collection`. Only that item is blocked; the rest of the list stays usable.
  final Set<String> _busy = {};
  final List<UploadJob> _uploads = [];
  final _search = TextEditingController();
  final Set<FileKind> _kinds = {};
  FileStage? _stage;

  bool get _filtering =>
      _search.text.trim().isNotEmpty || _kinds.isNotEmpty || _stage != null;

  void _clearFilters() => setState(() {
    _search.clear();
    _kinds.clear();
    _stage = null;
  });

  final Map<String, _DownloadJob> _downloads = {};
  int _uploadSerial = 0;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _search.dispose();
    for (final job in _uploads) {
      job.cancel.cancel();
    }
    for (final job in _downloads.values) {
      job.cancel.cancel();
    }
    super.dispose();
  }

  bool _isBusy(String key) => _busy.contains(key);

  /// Runs [action] for the item [key] unless that item is already busy.
  Future<void> _run(String key, Future<void> Function() action) async {
    if (!_busy.add(key)) return;
    setState(() {});
    try {
      await action();
    } finally {
      if (mounted) setState(() => _busy.remove(key));
    }
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/files');
      var collections = <Map<String, dynamic>>[];
      try {
        final collectionsResponse = await widget.http.get<dynamic>(
          '/api/v1/collections',
        );
        collections = jsonMaps(collectionsResponse.data);
      } on DioException {
        // Collections are optional; keep the files list usable without them.
      }
      if (mounted && revision == _requestRevision) {
        setState(() {
          _files = jsonMaps(response.data);
          _collections = collections;
        });
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load your files.');
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load your files.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<PickedUpload?> _pick() async {
    final custom = widget.pickUpload;
    if (custom != null) return custom();
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: uploadableFileExtensions,
    );
    if (file == null) return null;
    return (name: file.name, bytes: await file.readAsBytes());
  }

  Future<void> _upload() async {
    final picked = await _pick();
    if (picked == null || !mounted) return;
    final bytes = picked.bytes;
    if (bytes.isEmpty || bytes.length > maxUploadBytes) {
      _showError('Choose a non-empty file up to 20 MB.');
      return;
    }
    final job = UploadJob(
      id: ++_uploadSerial,
      fileName: picked.name,
      bytes: bytes,
    );
    setState(() => _uploads.add(job));
    await _sendUpload(job);
  }

  Future<void> _sendUpload(UploadJob job) async {
    setState(() {
      job.state = UploadState.uploading;
      job.message = null;
      job.sent = 0;
      job.cancel = CancelToken();
    });
    try {
      final form = FormData.fromMap({
        'file': MultipartFile.fromBytes(
          job.bytes,
          filename: job.fileName,
          contentType: DioMediaType.parse(uploadContentTypeFor(job.fileName)),
        ),
      });
      await widget.http.post(
        '/api/v1/files',
        data: form,
        cancelToken: job.cancel,
        onSendProgress: (sent, total) {
          if (!mounted) return;
          setState(() => job.sent = sent < job.total ? sent : job.total);
        },
        options: Options(
          sendTimeout: const Duration(minutes: 2),
          receiveTimeout: const Duration(minutes: 2),
        ),
      );
      if (!mounted) return;
      // The file is saved; scanning and indexing continue on the server and
      // show as the file's own status in the list.
      setState(() => _uploads.remove(job));
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        if (CancelToken.isCancel(error)) {
          job.state = UploadState.cancelled;
          job.message = 'Upload cancelled. Nothing was added.';
        } else {
          job.state = UploadState.failed;
          job.message = error.response?.statusCode == 400
              ? 'That file type is not supported.'
              : 'Jarvis could not upload this file.';
        }
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        job.state = UploadState.failed;
        job.message = 'Jarvis could not upload this file.';
      });
    }
  }

  void _cancelUpload(UploadJob job) => job.cancel.cancel();

  void _dismissUpload(UploadJob job) => setState(() => _uploads.remove(job));

  Future<void> _download(Map<String, dynamic> file) async {
    final id = jsonString(file, 'id');
    if (id == null || _downloads.containsKey(id) || _isBusy(id)) return;
    final job = _DownloadJob();
    setState(() => _downloads[id] = job);
    final download = widget.download ?? file_download.downloadAndOpen;
    try {
      await download(
        widget.http,
        id,
        asJsonString(file['fileName']) ?? 'jarvis-file',
        expectedBytes: file['sizeBytes'] is int
            ? file['sizeBytes'] as int
            : null,
        cancelToken: job.cancel,
        onProgress: (received, total) {
          if (!mounted) return;
          setState(() {
            job.received = received;
            job.total = total;
          });
        },
      );
    } on DioException catch (error) {
      if (mounted && !CancelToken.isCancel(error)) {
        _showError('Jarvis could not download this file.');
      } else if (mounted) {
        _showError('Download cancelled.');
      }
    } catch (_) {
      if (mounted) {
        _showError(
          'Could not open this file. Try again, or download it to open it '
          'in another app.',
        );
      }
    } finally {
      if (mounted) setState(() => _downloads.remove(id));
    }
  }

  /// Starts a chat about one file, with that file attached as its only source.
  Future<void> _ask(Map<String, dynamic> file) async {
    final id = jsonString(file, 'id');
    final open = widget.onOpenConversation;
    if (id == null || open == null) return;
    String? conversationId;
    await _run(id, () async {
      try {
        final response = await widget.http.post<dynamic>(
          '/api/v1/files/$id/ask',
        );
        final body = jsonObject(response.data);
        conversationId = asJsonString(body?['conversationId']);
        final status = asJsonString(body?['status']);
        if (mounted &&
            status != null &&
            status != 'ready' &&
            status != 'indexed') {
          ScaffoldMessenger.maybeOf(context)
            ?..hideCurrentSnackBar()
            ..showSnackBar(
              const SnackBar(
                content: Text(
                  'Jarvis is still reading this file. Answers may be partial.',
                ),
              ),
            );
        }
      } on DioException catch (error) {
        if (mounted) {
          _showError(
            error.response?.statusCode == 404
                ? 'That file is gone.'
                : (firstProblemMessage(error.response?.data) ??
                      'Jarvis could not start a chat about this file.'),
          );
        }
      } catch (_) {
        if (mounted) {
          _showError('Jarvis could not start a chat about this file.');
        }
      }
    });
    if (conversationId != null && mounted) await open(conversationId!);
  }

  Future<void> _retryIndexing(Map<String, dynamic> file) async {
    final id = jsonString(file, 'id');
    if (id == null) return;
    await _run(id, () async {
      try {
        await widget.http.post('/api/v1/files/$id/reprocess');
        await _load();
      } on DioException {
        if (mounted) _showError('Jarvis could not retry indexing this file.');
      } catch (_) {
        if (mounted) _showError('Jarvis could not retry indexing this file.');
      }
    });
  }

  Future<void> _delete(Map<String, dynamic> file) async {
    final id = jsonString(file, 'id');
    if (id == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete file?',
      message: '“${file['fileName']}” will be removed from Jarvis.',
      cancelLabel: 'Keep',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    await _run(id, () async {
      try {
        await widget.http.delete('/api/v1/files/$id');
        await _load();
      } on DioException {
        if (mounted) _showError('Jarvis could not delete this file.');
      } catch (_) {
        if (mounted) _showError('Jarvis could not delete this file.');
      }
    });
  }

  Future<void> _createCollection() async {
    final name = await showJarvisDialog<String>(
      context: context,
      builder: (_) => const _NewCollectionDialog(),
    );
    if (name == null || name.isEmpty || !mounted) return;
    await _run('new-collection', () async {
      try {
        await widget.http.post('/api/v1/collections', data: {'name': name});
        await _load();
      } on DioException {
        if (mounted) _showError('Could not create that collection.');
      } catch (_) {
        if (mounted) _showError('Could not create that collection.');
      }
    });
  }

  Future<void> _deleteCollection(Map<String, dynamic> collection) async {
    final id = jsonString(collection, 'id');
    if (id == null) return;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete collection?',
      message: 'Files stay in your library; only the grouping is removed.',
      cancelLabel: 'Keep',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    await _run('collection:$id', () async {
      try {
        await widget.http.delete('/api/v1/collections/$id');
        await _load();
      } on DioException {
        if (mounted) _showError('Could not delete this collection.');
      } catch (_) {
        if (mounted) _showError('Could not delete this collection.');
      }
    });
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _formatSize(dynamic value) => formatFileSize(asJsonInt(value));

  @override
  Widget build(BuildContext context) {
    final shown = filterFiles(
      _files,
      query: _search.text,
      kinds: _kinds,
      stage: _stage,
    );
    final membership = collectionNamesByFile(_collections);
    // Collections are hidden while searching so the matches stand alone.
    final visibleCollections = _filtering
        ? const <Map<String, dynamic>>[]
        : _collections;
    return _scaffold(context, shown, membership, visibleCollections);
  }

  Widget _scaffold(
    BuildContext context,
    List<Map<String, dynamic>> shown,
    Map<String, List<String>> membership,
    List<Map<String, dynamic>> visibleCollections,
  ) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Files'),
      actions: [
        IconButton(
          onPressed: _load,
          tooltip: 'Refresh',
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'Collection',
          icon: PhosphorIconsRegular.folders,
          onPressed: _createCollection,
          busy: _isBusy('new-collection'),
          collapsesWhenNarrow: true,
        ),
        HeaderAction(
          label: 'Upload',
          icon: PhosphorIconsRegular.uploadSimple,
          onPressed: _upload,
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _files.isEmpty && _collections.isEmpty && _uploads.isEmpty,
      onRetry: _load,
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.folderOpen,
        title: 'No files yet',
        message: 'Your files will be stored privately with Jarvis. PDFs and text are indexed so Jarvis can search them.',
      ),
      child: ListView.builder(
        keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        itemCount:
            (_files.isEmpty ? 0 : 1) +
            _uploads.length +
            visibleCollections.length +
            (_filtering && shown.isEmpty ? 1 : shown.length),
        itemBuilder: (context, index) {
          if (_files.isNotEmpty) {
            if (index == 0) return ContentWidth(child: _filterBar(context));
            index -= 1;
          }
          if (index < _uploads.length) {
            return ContentWidth(child: _uploadCard(_uploads[index]));
          }
          index -= _uploads.length;
          if (index < visibleCollections.length) {
            final collection = visibleCollections[index];
            final name = asJsonString(collection['name']) ?? 'Collection';
            final fileIds = collection['fileIds'];
            final count = fileIds is List ? fileIds.length : 0;
            return ContentWidth(
              child: SurfaceCard(
                margin: const EdgeInsets.only(bottom: 10),
                padding: const EdgeInsets.fromLTRB(14, 12, 4, 12),
                child: Row(
                  children: [
                    const IconBadge(
                      icon: PhosphorIconsRegular.folders,
                      size: 44,
                    ),
                    const SizedBox(width: 14),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            name,
                            style: const TextStyle(fontWeight: FontWeight.w600),
                          ),
                          Text(
                            '$count files',
                            style: TextStyle(
                              color: JarvisColors.of(context).muted,
                              fontSize: 13,
                            ),
                          ),
                        ],
                      ),
                    ),
                    IconButton(
                      tooltip: 'Delete collection',
                      onPressed:
                          _isBusy('collection:${jsonString(collection, 'id')}')
                          ? null
                          : () => _deleteCollection(collection),
                      icon: const Icon(PhosphorIconsRegular.trash),
                    ),
                  ],
                ),
              ),
            );
          }
          index -= visibleCollections.length;
          if (shown.isEmpty) {
            return ContentWidth(child: _noMatches(context));
          }
          final file = shown[index];
          final name = asJsonString(file['fileName']) ?? 'File';
          final inCollections = membership[jsonString(file, 'id')] ?? const [];
          final icon = _fileIcon(name);
          final status = asJsonString(file['processingStatus']) ?? 'uploaded';
          final fileId = jsonString(file, 'id') ?? '';
          final busy = _isBusy(fileId);
          final download = _downloads[fileId];
          return ContentWidth(
            child: SurfaceCard(
              margin: const EdgeInsets.only(bottom: 10),
              padding: const EdgeInsets.fromLTRB(14, 12, 4, 12),
              onTap: busy || download != null ? null : () => _download(file),
              child: Row(
                children: [
                  IconBadge(icon: icon, size: 44),
                  const SizedBox(width: 14),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          name,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: Theme.of(context).textTheme.titleSmall
                              ?.copyWith(fontSize: 15),
                        ),
                        const SizedBox(height: 6),
                        Wrap(
                          spacing: 8,
                          runSpacing: 6,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: [
                            Text(
                              _formatSize(file['sizeBytes']),
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                            StatusPill.forStatus(status),
                            if (file['partiallyIndexed'] == true)
                              Tooltip(
                                message: 'Only the first part of this file is searchable.',
                                child: StatusPill(
                                  label: 'Partly indexed',
                                  color: JarvisColors.of(context).warning,
                                ),
                              ),
                          ],
                        ),
                        if (inCollections.isNotEmpty)
                          Padding(
                            padding: const EdgeInsets.only(top: 6),
                            child: Text(
                              key: Key('file-collections-$fileId'),
                              'In ${inCollections.join(', ')}',
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: Theme.of(context).textTheme.bodySmall
                                  ?.copyWith(
                                    color: JarvisColors.of(context).inkSoft,
                                  ),
                            ),
                          ),
                        if (download != null)
                          _downloadProgress(download, fileId),
                      ],
                    ),
                  ),
                  if (busy)
                    const Padding(
                      padding: EdgeInsets.all(12),
                      child: SizedBox.square(
                        dimension: 20,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      ),
                    )
                  else
                    PopupMenuButton<String>(
                      enabled: download == null,
                      icon: const Icon(PhosphorIconsRegular.dotsThree),
                      onSelected: (action) {
                        if (action == 'open') {
                          _download(file);
                        } else if (action == 'ask') {
                          _ask(file);
                        } else if (action == 'retry') {
                          _retryIndexing(file);
                        } else if (action == 'delete') {
                          _delete(file);
                        }
                      },
                      itemBuilder: (context) => [
                        PopupMenuItem(
                          value: 'open',
                          child: ListTile(
                            leading: const Icon(
                              PhosphorIconsRegular.arrowSquareOut,
                            ),
                            title: Text(
                              kIsWeb ? 'Download' : 'Download and open',
                            ),
                            contentPadding: EdgeInsets.zero,
                          ),
                        ),
                        if (widget.onOpenConversation != null &&
                            status != 'failed' &&
                            status != 'deleting')
                          const PopupMenuItem(
                            value: 'ask',
                            child: ListTile(
                              leading: Icon(PhosphorIconsRegular.chatCircle),
                              title: Text('Ask Jarvis about this'),
                              contentPadding: EdgeInsets.zero,
                            ),
                          ),
                        if (status == 'failed')
                          const PopupMenuItem(
                            value: 'retry',
                            child: ListTile(
                              leading: Icon(
                                PhosphorIconsRegular.arrowsClockwise,
                              ),
                              title: Text('Retry indexing'),
                              contentPadding: EdgeInsets.zero,
                            ),
                          ),
                        PopupMenuItem(
                          value: 'delete',
                          child: ListTile(
                            leading: Icon(
                              PhosphorIconsRegular.trash,
                              color: JarvisColors.of(context).danger,
                            ),
                            title: Text(
                              'Delete',
                              style: TextStyle(
                                color: JarvisColors.of(context).danger,
                              ),
                            ),
                            contentPadding: EdgeInsets.zero,
                          ),
                        ),
                      ],
                    ),
                ],
              ),
            ),
          );
        },
      ),
    ),
  );

  /// Filename search plus type and status chips, shown above the list.
  Widget _filterBar(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          TextField(
            key: const Key('files-search'),
            controller: _search,
            onChanged: (_) => setState(() {}),
            textInputAction: TextInputAction.search,
            decoration: InputDecoration(
              hintText: 'Search by file name',
              prefixIcon: Icon(
                PhosphorIconsRegular.magnifyingGlass,
                size: 18,
                color: colors.muted,
              ),
              suffixIcon: SearchClearButton(
                controller: _search,
                buttonKey: const Key('files-search-clear'),
                tooltip: 'Clear search',
                onCleared: () => setState(() {}),
              ),
            ),
          ),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.only(top: 8),
            child: Row(
              children: [
                for (final kind in FileKind.values)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: FilterChip(
                      key: Key('files-kind-${kind.name}'),
                      label: Text(kind.label),
                      selected: _kinds.contains(kind),
                      onSelected: (on) => setState(
                        () => on ? _kinds.add(kind) : _kinds.remove(kind),
                      ),
                    ),
                  ),
                for (final stage in FileStage.values)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: ChoiceChip(
                      key: Key('files-stage-${stage.name}'),
                      label: Text(stage.label),
                      selected: _stage == stage,
                      onSelected: (on) =>
                          setState(() => _stage = on ? stage : null),
                    ),
                  ),
              ],
            ),
          ),
          if (_filtering)
            Align(
              alignment: Alignment.centerLeft,
              child: TextButton.icon(
                key: const Key('files-clear-filters'),
                onPressed: _clearFilters,
                icon: const Icon(PhosphorIconsRegular.x, size: 14),
                label: const Text('Clear filters'),
              ),
            ),
        ],
      ),
    );
  }

  /// Files exist, but none fit the search: say so instead of showing "No files yet".
  Widget _noMatches(BuildContext context) => Padding(
    padding: const EdgeInsets.symmetric(vertical: 32),
    child: Center(
      child: Text(
        'No files match. You have ${_files.length} '
        '${_files.length == 1 ? 'file' : 'files'} in all.',
        key: const Key('files-no-match'),
        textAlign: TextAlign.center,
        style: TextStyle(color: JarvisColors.of(context).muted),
      ),
    ),
  );

  Widget _uploadCard(UploadJob job) {
    final colors = JarvisColors.of(context);
    final uploading = job.state == UploadState.uploading;
    final progress = job.total == 0 ? 0.0 : job.sent / job.total;
    final status = switch (job.state) {
      UploadState.uploading when job.sentEverything =>
        'Sent. Jarvis is saving it…',
      UploadState.uploading =>
        '${formatFileSize(job.sent)} of ${formatFileSize(job.total)}',
      _ => job.message ?? '',
    };
    return SurfaceCard(
      key: Key('upload-${job.id}'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(14, 12, 8, 12),
      child: Row(
        children: [
          IconBadge(icon: _fileIcon(job.fileName), size: 44),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  job.fileName,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: Theme.of(context).textTheme.titleSmall
                      ?.copyWith(fontSize: 15),
                ),
                const SizedBox(height: 6),
                if (uploading)
                  LinearProgressIndicator(
                    key: Key('upload-progress-${job.id}'),
                    value: job.sentEverything ? null : progress,
                    minHeight: 4,
                    borderRadius: BorderRadius.circular(2),
                  ),
                const SizedBox(height: 6),
                Text(
                  status,
                  key: Key('upload-status-${job.id}'),
                  style: TextStyle(
                    fontSize: 13,
                    color: job.state == UploadState.failed
                        ? colors.danger
                        : colors.muted,
                  ),
                ),
              ],
            ),
          ),
          if (uploading && !job.sentEverything)
            TextButton(
              key: Key('upload-cancel-${job.id}'),
              onPressed: () => _cancelUpload(job),
              child: const Text('Cancel'),
            )
          else if (!uploading) ...[
            TextButton(
              key: Key('upload-retry-${job.id}'),
              onPressed: () => unawaited(_sendUpload(job)),
              child: const Text('Retry'),
            ),
            IconButton(
              key: Key('upload-dismiss-${job.id}'),
              tooltip: 'Dismiss',
              onPressed: () => _dismissUpload(job),
              icon: const Icon(PhosphorIconsRegular.x, size: 18),
            ),
          ],
        ],
      ),
    );
  }

  Widget _downloadProgress(_DownloadJob job, String fileId) {
    final known = job.total > 0;
    return Padding(
      padding: const EdgeInsets.only(top: 8, right: 10),
      child: Row(
        children: [
          Expanded(
            child: LinearProgressIndicator(
              key: Key('download-progress-$fileId'),
              value: known ? job.received / job.total : null,
              minHeight: 4,
              borderRadius: BorderRadius.circular(2),
            ),
          ),
          TextButton(
            key: Key('download-cancel-$fileId'),
            onPressed: job.cancel.cancel,
            style: TextButton.styleFrom(
              visualDensity: VisualDensity.compact,
              minimumSize: const Size(0, 32),
            ),
            child: const Text('Cancel'),
          ),
        ],
      ),
    );
  }

  IconData _fileIcon(String name) =>
      switch (name.split('.').last.toLowerCase()) {
        'pdf' => PhosphorIconsRegular.filePdf,
        'jpg' || 'jpeg' || 'png' || 'webp' => PhosphorIconsRegular.fileImage,
        'csv' => PhosphorIconsRegular.table,
        'json' => PhosphorIconsRegular.bracketsCurly,
        _ => PhosphorIconsRegular.fileText,
      };
}

/// "512 B", "3.4 KB", "1.2 MB".
String formatFileSize(int bytes) {
  if (bytes < 1024) return '$bytes B';
  if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
  return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
}

String uploadContentTypeFor(String fileName) {
  final extension = fileName.split('.').last.toLowerCase();
  return switch (extension) {
    'pdf' => 'application/pdf',
    'txt' || 'log' || 'srt' || 'vtt' => 'text/plain',
    'md' || 'markdown' => 'text/markdown',
    'csv' => 'text/csv',
    'tsv' => 'text/tab-separated-values',
    'ics' => 'text/calendar',
    'json' => 'application/json',
    'yaml' || 'yml' => 'application/yaml',
    'docx' =>
      'application/vnd.openxmlformats-officedocument.wordprocessingml.document',
    'pptx' => 'application/vnd.openxmlformats-officedocument.presentationml.presentation',
    'jpg' || 'jpeg' => 'image/jpeg',
    'png' => 'image/png',
    'webp' => 'image/webp',
    _ => 'application/octet-stream',
  };
}

class _NewCollectionDialog extends StatefulWidget {
  const _NewCollectionDialog();

  @override
  State<_NewCollectionDialog> createState() => _NewCollectionDialogState();
}

class _NewCollectionDialogState extends State<_NewCollectionDialog> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  String get _name => _controller.text.trim();

  void _create() {
    if (_name.isEmpty) return;
    Navigator.pop(context, _name);
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('New collection'),
    content: TextField(
      textCapitalization: TextCapitalization.words,
      controller: _controller,
      autofocus: true,
      maxLength: 80,
      buildCounter: quietCounter,
      textInputAction: TextInputAction.done,
      onChanged: (_) => setState(() {}),
      onSubmitted: (_) => _create(),
      decoration: const InputDecoration(hintText: 'Collection name'),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: _name.isEmpty ? null : _create,
        child: const Text('Create'),
      ),
    ],
  );
}
