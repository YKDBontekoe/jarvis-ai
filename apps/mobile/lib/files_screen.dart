import 'package:dio/dio.dart';
import 'package:file_picker/file_picker.dart';
import 'package:flutter/material.dart';

import 'file_download_stub.dart'
    if (dart.library.io) 'file_download_io.dart'
    as file_download;

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>('/api/v1/files');
      if (mounted) {
        setState(
          () => _files = (response.data ?? []).cast<Map<String, dynamic>>(),
        );
      }
    } on DioException {
      if (mounted) setState(() => _error = 'Jarvis could not load your files.');
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _upload() async {
    final file = await FilePicker.pickFile(
      type: FileType.custom,
      allowedExtensions: _allowedExtensions,
    );
    if (file == null) return;
    final bytes = await file.readAsBytes();
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
    setState(() => _busy = true);
    try {
      await file_download.downloadAndOpen(
        widget.http,
        file['id'] as String,
        file['fileName'] as String? ?? 'jarvis-file',
      );
    } on DioException {
      if (mounted) _showError('Jarvis could not download this file.');
    } catch (error) {
      if (mounted) _showError('Could not open this file: $error');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _delete(Map<String, dynamic> file) async {
    final confirmed = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete file?'),
        content: Text('“${file['fileName']}” will be removed from Jarvis.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (confirmed != true) return;
    setState(() => _busy = true);
    try {
      await widget.http.delete('/api/v1/files/${file['id']}');
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
    final bytes = (value as num?)?.toInt() ?? 0;
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
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: _busy ? null : _upload,
      icon: _busy
          ? const SizedBox.square(
              dimension: 18,
              child: CircularProgressIndicator(strokeWidth: 2),
            )
          : const Icon(Icons.upload_file_outlined),
      label: const Text('Upload file'),
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!),
                TextButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : _files.isEmpty
        ? const Center(
            child: Padding(
              padding: EdgeInsets.all(28),
              child: Text('Your files will be stored privately with Jarvis.'),
            ),
          )
        : ListView.builder(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
            itemCount: _files.length,
            itemBuilder: (context, index) {
              final file = _files[index];
              return Card(
                margin: const EdgeInsets.only(bottom: 10),
                child: ListTile(
                  leading: const Icon(Icons.insert_drive_file_outlined),
                  title: Text(file['fileName'] as String? ?? 'File'),
                  subtitle: Text(
                    '${_formatSize(file['sizeBytes'])} · ${file['processingStatus'] ?? 'uploaded'}',
                  ),
                  trailing: PopupMenuButton<String>(
                    enabled: !_busy,
                    onSelected: (action) =>
                        action == 'open' ? _download(file) : _delete(file),
                    itemBuilder: (context) => const [
                      PopupMenuItem(
                        value: 'open',
                        child: Text('Download and open'),
                      ),
                      PopupMenuItem(value: 'delete', child: Text('Delete')),
                    ],
                  ),
                  onTap: _busy ? null : () => _download(file),
                ),
              );
            },
          ),
  );
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
