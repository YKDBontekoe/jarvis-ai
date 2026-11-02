import 'dart:async';
import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';
import 'package:home_widget/home_widget.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../home/home_widget_sync.dart';
import 'capture_models.dart';

/// Where captures are staged. On iPhone this is the App Group shared with the
/// Share Extension; tests use [MemoryCaptureBackend].
abstract class CaptureBackend {
  Future<String?> readIndex();
  Future<void> writeIndex(String json);

  /// The account the app is signed in to, so the extension can stamp what it
  /// saves. Null when signed out, which makes the extension refuse new captures.
  Future<void> writeAccount(String? account);
  Future<void> deleteFile(String path);
}

class MemoryCaptureBackend implements CaptureBackend {
  String? index;
  String? account;
  final List<String> deleted = [];

  @override
  Future<String?> readIndex() async => index;

  @override
  Future<void> writeIndex(String json) async => index = json;

  @override
  Future<void> writeAccount(String? value) async => account = value;

  @override
  Future<void> deleteFile(String path) async => deleted.add(path);
}

/// The App Group shared with the Share Extension (see `ios/ShareExtension`).
class SharedContainerCaptureBackend implements CaptureBackend {
  static const indexKey = 'captures_index';
  static const accountKey = 'capture_account';
  bool _ready = false;

  Future<void> _prepare() async {
    if (_ready) return;
    await HomeWidget.setAppGroupId(HomeWidgetSync.appGroup);
    _ready = true;
  }

  @override
  Future<String?> readIndex() async {
    await _prepare();
    return HomeWidget.getWidgetData<String>(indexKey);
  }

  @override
  Future<void> writeIndex(String json) async {
    await _prepare();
    await HomeWidget.saveWidgetData<String>(indexKey, json);
  }

  @override
  Future<void> writeAccount(String? account) async {
    await _prepare();
    await HomeWidget.saveWidgetData<String>(accountKey, account ?? '');
  }

  @override
  Future<void> deleteFile(String path) async {
    try {
      final file = File(path);
      if (await file.exists()) await file.delete();
    } catch (_) {
      // A file that cannot be removed is cleaned up with its folder later.
    }
  }
}

/// Captures waiting for review: read from the shared container, bound to the
/// account the app is signed in to, and removed (with their files) when handled.
class CaptureStore extends ChangeNotifier {
  CaptureStore({CaptureBackend? backend})
    : _backend = backend ?? SharedContainerCaptureBackend();

  static final instance = CaptureStore();
  static const _presentedKey = 'jarvis.captures.presented.v1';
  static const maxItems = 50;

  CaptureBackend _backend;
  String? _account;
  List<CaptureItem> _items = const [];

  /// Replaces where captures live; for tests.
  @visibleForTesting
  set backend(CaptureBackend value) => _backend = value;

  List<CaptureItem> get items => _items;
  String? get account => _account;

  /// Newest first.
  CaptureItem? get newest => _items.isEmpty ? null : _items.first;

  /// Starts a session for [account]: the extension may now save for it, anything
  /// saved for another account is discarded, and unclaimed captures are adopted.
  Future<void> bind(String account) async {
    _account = account;
    try {
      await _backend.writeAccount(account);
    } catch (_) {
      // No shared container on this device; captures just stay empty.
    }
    await refresh();
  }

  /// Re-reads the shared container.
  Future<void> refresh() async {
    final account = _account;
    if (account == null) return;
    String? raw;
    try {
      raw = await _backend.readIndex();
    } catch (_) {
      return;
    }
    final all = _decode(raw);
    final mine = <CaptureItem>[];
    var changed = false;
    for (final item in all) {
      if (item.account.isEmpty) {
        mine.add(item.withAccount(account));
        changed = true;
      } else if (item.account == account) {
        mine.add(item);
      } else {
        // Shared for someone else: never shown here, and removed.
        unawaited(_discardFile(item));
        changed = true;
      }
    }
    mine.sort((a, b) => b.createdAt.compareTo(a.createdAt));
    final kept = mine.take(maxItems).toList();
    if (kept.length != mine.length) changed = true;
    if (changed) await _save(kept);
    _items = kept;
    notifyListeners();
  }

  List<CaptureItem> _decode(String? raw) {
    if (raw == null || raw.isEmpty) return const [];
    try {
      final decoded = jsonDecode(raw);
      if (decoded is! List) return const [];
      return [for (final entry in decoded) ?CaptureItem.fromJson(entry)];
    } catch (_) {
      return const [];
    }
  }

  Future<void> _save(List<CaptureItem> items) async {
    try {
      await _backend.writeIndex(
        jsonEncode([for (final item in items) item.toJson()]),
      );
    } catch (_) {
      // Kept in memory for this session.
    }
  }

  Future<void> _discardFile(CaptureItem item) async {
    final path = item.filePath;
    if (path != null && path.isNotEmpty) await _backend.deleteFile(path);
  }

  /// Removes a capture once it has been handled or discarded, with its file.
  Future<void> remove(String id) async {
    final item = _items.where((c) => c.id == id).firstOrNull;
    if (item == null) return;
    _items = [
      for (final other in _items)
        if (other.id != id) other,
    ];
    notifyListeners();
    await _discardFile(item);
    await _save(_items);
  }

  /// Signing out: forget every capture, delete their files and stop the
  /// extension from saving for an account that is gone.
  Future<void> clear() async {
    final old = _items;
    _items = const [];
    _account = null;
    notifyListeners();
    for (final item in old) {
      await _discardFile(item);
    }
    try {
      await _backend.writeIndex('[]');
      await _backend.writeAccount(null);
    } catch (_) {
      // Nothing was shared on this device.
    }
    try {
      await (await SharedPreferences.getInstance()).remove(_presentedKey);
    } catch (_) {
      // Nothing was stored.
    }
  }

  /// True when something arrived that the owner has not been shown yet.
  Future<bool> hasUnpresented() async {
    final newest = this.newest;
    if (newest == null) return false;
    try {
      final seen = (await SharedPreferences.getInstance()).getString(
        _presentedKey,
      );
      return seen != newest.id;
    } catch (_) {
      return true;
    }
  }

  /// Remembers that the newest capture was shown.
  Future<void> markPresented() async {
    final newest = this.newest;
    if (newest == null) return;
    try {
      await (await SharedPreferences.getInstance()).setString(
        _presentedKey,
        newest.id,
      );
    } catch (_) {
      // It may be shown once more.
    }
  }
}
