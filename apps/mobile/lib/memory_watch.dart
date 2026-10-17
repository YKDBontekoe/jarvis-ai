import 'package:flutter/widgets.dart';
import 'package:sentry_flutter/sentry_flutter.dart';

import 'error_reporting.dart';
import 'memory_rss_stub.dart' if (dart.library.io) 'memory_rss_io.dart';

/// Frees decoded images when the OS warns about memory, and leaves a memory
/// breadcrumb on each lifecycle change so a watchdog kill shows how big the
/// app had grown before it.
class MemoryWatch with WidgetsBindingObserver {
  @visibleForTesting
  MemoryWatch();

  static MemoryWatch? _installed;

  static void install() {
    if (_installed != null) return;
    WidgetsBinding.instance.addObserver(_installed = MemoryWatch());
  }

  @override
  void didHaveMemoryPressure() {
    final cache = PaintingBinding.instance.imageCache;
    final freed = cache.currentSizeBytes;
    cache
      ..clear()
      ..clearLiveImages();
    _breadcrumb('memory pressure', {'imageCacheFreedBytes': freed});
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed ||
        state == AppLifecycleState.paused) {
      _breadcrumb(state.name, {
        'imageCacheBytes': PaintingBinding.instance.imageCache.currentSizeBytes,
      });
    }
  }

  static void _breadcrumb(String message, Map<String, Object> data) {
    if (!sentryTracingEnabled) return;
    Sentry.addBreadcrumb(
      Breadcrumb(
        category: 'device.memory',
        message: message,
        level: SentryLevel.info,
        data: {...data, ...processMemory()},
      ),
    );
  }
}
