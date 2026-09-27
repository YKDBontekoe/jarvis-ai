import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'file_download_stub.dart'
    if (dart.library.io) 'file_download_io.dart'
    if (dart.library.js_interop) 'file_download_web.dart'
    as file_download;
import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

const _allowedExtensions = [
  'pdf',
  'txt',
  'log',
  'md',
  'markdown',
  'csv',
  'json',
  'jpg',
  'jpeg',
  'png',
  'webp',
];
const _maxFileBytes = 20 * 1024 * 1024;

class FilesScreen extends StatefulWidget {
  const FilesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<FilesScreen> createState() => _FilesScreenState();
}

class _FilesScreenState extends State<FilesScreen> {
  List<Map<String, dynamic>> _files = [];
  bool _loading = true;
  bool _busy = false;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>('/api/v1/files');
      if (mounted && revision == _requestRevision) {
        setState(
          () => _files = jsonMaps(response.data),
        );
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load your files.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _upload() async {
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: _allowedExtensions,
    );
    if (file == null) return;
    final bytes = await file.readAsBytes();
    if (!mounted) return;
    if (bytes.isEmpty || bytes.length > _maxFileBytes) {
      _showError('Choose a non-empty file up to 20 MB.');
      return;
    }

    setState(() => _busy = true);
    try {
      final form = FormData.fromMap({
        'file': MultipartFile.fromBytes(
          bytes,
          filename: file.name,
          contentType: DioMediaType.parse(_contentTypeFor(file.name)),
        ),
      });
      await widget.http.post('/api/v1/files', data: form);
      await _load();
    } on DioException catch (error) {
      final message = error.response?.statusCode == 400
          ? 'That file type is not supported.'
          : 'Jarvis could not upload this file.';
      if (mounted) _showError(message);
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _download(Map<String, dynamic> file) async {
    final id = jsonString(file, 'id');
    if (id == null) return;
    setState(() => _busy = true);
    try {
      await file_download.downloadAndOpen(
        widget.http,
        id,
        asJsonString(file['fileName']) ?? 'jarvis-file',
      );
    } on DioException {
      if (mounted) _showError('Jarvis could not download this file.');
    } catch (error) {
      if (mounted) _showError('Could not open this file: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _retryIndexing(Map<String, dynamic> file) async {
    final id = jsonString(file, 'id');
    if (id == null) return;
    setState(() => _busy = true);
    try {
      await widget.http.post('/api/v1/files/$id/reprocess');
      await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not retry indexing this file.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
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
    if (!confirmed) return;
    if (!mounted) return;
    setState(() => _busy = true);
    try {
      await widget.http.delete('/api/v1/files/$id');
      await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not delete this file.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  String _formatSize(dynamic value) {
    final bytes = asJsonInt(value);
    if (bytes < 1024) return '$bytes B';
    if (bytes < 1024 * 1024) return '${(bytes / 1024).toStringAsFixed(1)} KB';
    return '${(bytes / (1024 * 1024)).toStringAsFixed(1)} MB';
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Files'),
      actions: [
        IconButton(
          onPressed: _load,
          tooltip: 'Refresh',
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'Upload',
          icon: PhosphorIconsRegular.uploadSimple,
          onPressed: _upload,
          busy: _busy,
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _files.isEmpty,
      onRetry: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.folderOpen,
        title: 'No files yet',
        message:
            'Your files will be stored privately with Jarvis. PDFs and text are indexed so Jarvis can search them.',
      ),
      child: ListView.builder(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            itemCount: _files.length,
            itemBuilder: (context, index) {
              final file = _files[index];
              final name = asJsonString(file['fileName']) ?? 'File';
              final icon = _fileIcon(name);
              final status = asJsonString(file['processingStatus']) ?? 'uploaded';
              return ContentWidth(
                child: SurfaceCard(
                  margin: const EdgeInsets.only(bottom: 10),
                  padding: const EdgeInsets.fromLTRB(14, 12, 4, 12),
                  onTap: _busy ? null : () => _download(file),
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
                              style: Theme.of(
                                context,
                              ).textTheme.titleSmall?.copyWith(fontSize: 15),
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
                              ],
                            ),
                          ],
                        ),
                      ),
                      PopupMenuButton<String>(
                        enabled: !_busy,
                        icon: const Icon(PhosphorIconsRegular.dotsThree),
                        onSelected: (action) {
                          if (action == 'open') {
                            _download(file);
                          } else if (action == 'retry') {
                            _retryIndexing(file);
                          } else if (action == 'delete') {
                            _delete(file);
                          }
                        },
                        itemBuilder: (context) => [
                          const PopupMenuItem(
                            value: 'open',
                            child: ListTile(
                              leading: Icon(
                                PhosphorIconsRegular.arrowSquareOut,
                              ),
                              title: Text('Download and open'),
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
                          const PopupMenuItem(
                            value: 'delete',
                            child: ListTile(
                              leading: Icon(
                                PhosphorIconsRegular.trash,
                                color: JarvisColors.danger,
                              ),
                              title: Text(
                                'Delete',
                                style: TextStyle(color: JarvisColors.danger),
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

  IconData _fileIcon(String name) =>
      switch (name.split('.').last.toLowerCase()) {
        'pdf' => PhosphorIconsRegular.filePdf,
        'jpg' || 'jpeg' || 'png' || 'webp' => PhosphorIconsRegular.fileImage,
        'csv' => PhosphorIconsRegular.table,
        'json' => PhosphorIconsRegular.bracketsCurly,
        _ => PhosphorIconsRegular.fileText,
      };
}

String _contentTypeFor(String fileName) {
  final extension = fileName.split('.').last.toLowerCase();
  return switch (extension) {
    'pdf' => 'application/pdf',
    'txt' || 'log' => 'text/plain',
    'md' || 'markdown' => 'text/markdown',
    'csv' => 'text/csv',
    'json' => 'application/json',
    'jpg' || 'jpeg' => 'image/jpeg',
    'png' => 'image/png',
    'webp' => 'image/webp',
    _ => 'application/octet-stream',
  };
}
