import 'package:flutter/foundation.dart';

/// What a capture holds: some text, a web link, or a file (photo or document).
enum CaptureKind { text, link, file }

/// Photos and documents Jarvis can keep, and the size limit. The same rules
/// the Files page uses, so a capture is never accepted that Files would refuse.
const capturableFileExtensions = [
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
const maxCaptureBytes = 20 * 1024 * 1024;

/// Why a capture cannot be kept, as one plain sentence, or null when it can.
String? captureProblem({
  required CaptureKind kind,
  String? fileName,
  int sizeBytes = 0,
}) {
  if (kind != CaptureKind.file) return null;
  final extension = (fileName ?? '').split('.').last.toLowerCase();
  if (!capturableFileExtensions.contains(extension)) {
    return 'Jarvis cannot keep this kind of file. It can keep photos, PDFs and text files.';
  }
  if (sizeBytes <= 0) return 'This file is empty.';
  if (sizeBytes > maxCaptureBytes) return 'This file is larger than 20 MB.';
  return null;
}

/// One thing shared into Jarvis from another app and waiting for review. It is
/// kept on this device, tied to the account it was shared for, and never sent
/// to the assistant until the owner chooses what to do with it.
@immutable
class CaptureItem {
  const CaptureItem({
    required this.id,
    required this.kind,
    required this.createdAt,
    this.account = '',
    this.text,
    this.url,
    this.fileName,
    this.filePath,
    this.mimeType,
    this.sizeBytes = 0,
  });

  /// Stable across restarts; also the staging folder name.
  final String id;
  final CaptureKind kind;
  final DateTime createdAt;

  /// The account (server) it was shared for; empty until the app claims it.
  final String account;
  final String? text;
  final String? url;
  final String? fileName;

  /// Where the staged copy of a file is, inside the app's own storage.
  final String? filePath;
  final String? mimeType;
  final int sizeBytes;

  bool get isImage => (mimeType ?? '').startsWith('image/');

  String? get problem =>
      captureProblem(kind: kind, fileName: fileName, sizeBytes: sizeBytes);

  /// A line to recognise it by.
  String get title {
    switch (kind) {
      case CaptureKind.file:
        return fileName ?? 'File';
      case CaptureKind.link:
        return url ?? text ?? 'Link';
      case CaptureKind.text:
        final line = (text ?? '')
            .split('\n')
            .map((l) => l.trim())
            .firstWhere((l) => l.isNotEmpty, orElse: () => 'Note');
        return line.length > 80 ? '${line.substring(0, 80)}…' : line;
    }
  }

  CaptureItem withAccount(String value) => CaptureItem(
    id: id,
    kind: kind,
    createdAt: createdAt,
    account: value,
    text: text,
    url: url,
    fileName: fileName,
    filePath: filePath,
    mimeType: mimeType,
    sizeBytes: sizeBytes,
  );

  Map<String, Object?> toJson() => {
    'id': id,
    'kind': kind.name,
    'createdAt': createdAt.toUtc().toIso8601String(),
    'account': account,
    'text': text,
    'url': url,
    'fileName': fileName,
    'filePath': filePath,
    'mimeType': mimeType,
    'sizeBytes': sizeBytes,
  };

  static CaptureItem? fromJson(Object? raw) {
    if (raw is! Map) return null;
    final id = raw['id'];
    final createdAt = DateTime.tryParse('${raw['createdAt']}');
    final kind = CaptureKind.values
        .where((k) => k.name == raw['kind'])
        .firstOrNull;
    if (id is! String || id.isEmpty || createdAt == null || kind == null) {
      return null;
    }
    String? string(String key) =>
        raw[key] is String ? raw[key] as String : null;
    final item = CaptureItem(
      id: id,
      kind: kind,
      createdAt: createdAt.toLocal(),
      account: string('account') ?? '',
      text: string('text'),
      url: string('url'),
      fileName: string('fileName'),
      filePath: string('filePath'),
      mimeType: string('mimeType'),
      sizeBytes: raw['sizeBytes'] is int ? raw['sizeBytes'] as int : 0,
    );
    // A capture with nothing in it is not worth keeping.
    final empty =
        (kind == CaptureKind.file && (item.filePath ?? '').isEmpty) ||
        (kind == CaptureKind.link && (item.url ?? '').isEmpty) ||
        (kind == CaptureKind.text && (item.text ?? '').trim().isEmpty);
    return empty ? null : item;
  }
}
