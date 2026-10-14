import 'dart:io';

import 'package:path_provider/path_provider.dart';

import 'offline_cache.dart';

OfflineCacheStore createPlatformOfflineCacheStore() => FileOfflineCacheStore();

/// One file per request in the app's private support directory, named by a
/// hash of the request so paths and ids never appear in file names.
class FileOfflineCacheStore implements OfflineCacheStore {
  FileOfflineCacheStore({Future<Directory> Function()? directory})
    : _directory = directory ?? _defaultDirectory;

  /// The oldest files are removed past this many entries.
  static const maxEntries = 400;

  final Future<Directory> Function() _directory;
  Directory? _resolved;
  final _memory = MemoryOfflineCacheStore();
  bool _unavailable = false;
  int _writes = 0;

  static Future<Directory> _defaultDirectory() async => Directory(
    '${(await getApplicationSupportDirectory()).path}/offline_cache',
  );

  Future<Directory?> _dir() async {
    if (_unavailable) return null;
    try {
      final dir = _resolved ??= await _directory();
      if (!await dir.exists()) await dir.create(recursive: true);
      return dir;
    } catch (_) {
      _unavailable = true;
      return null;
    }
  }

  File _file(Directory dir, String key) =>
      File('${dir.path}/${_hash(key)}.json');

  @override
  Future<String?> read(String key) async {
    final dir = await _dir();
    if (dir == null) return _memory.read(key);
    final file = _file(dir, key);
    if (!await file.exists()) return null;
    return file.readAsString();
  }

  @override
  Future<void> write(String key, String value) async {
    final dir = await _dir();
    if (dir == null) return _memory.write(key, value);
    await _file(dir, key).writeAsString(value, flush: false);
    if (++_writes % 25 == 0) await _prune(dir);
  }

  @override
  Future<void> clear() async {
    await _memory.clear();
    final dir = await _dir();
    if (dir != null && await dir.exists()) await dir.delete(recursive: true);
  }

  Future<void> _prune(Directory dir) async {
    final files = await dir
        .list()
        .where((entity) => entity is File)
        .cast<File>()
        .toList();
    if (files.length <= maxEntries) return;
    final dated = [for (final file in files) (file, await file.lastModified())]
      ..sort((a, b) => a.$2.compareTo(b.$2));
    for (final (file, _) in dated.take(files.length - maxEntries)) {
      try {
        await file.delete();
      } catch (_) {}
    }
  }

  /// 64-bit FNV-1a, printed as hex.
  static String _hash(String value) {
    var hash = 0xcbf29ce484222325;
    for (final unit in value.codeUnits) {
      hash ^= unit;
      hash *= 0x100000001b3; // Wraps at 64 bits on native platforms.
    }
    String half(int value) => value.toRadixString(16).padLeft(8, '0');
    return half((hash >>> 32) & 0xFFFFFFFF) + half(hash & 0xFFFFFFFF);
  }
}
