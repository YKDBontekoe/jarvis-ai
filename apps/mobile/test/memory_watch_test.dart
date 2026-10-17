import 'dart:ui' as ui;

import 'package:flutter/painting.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/memory_watch.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();

  test('memory pressure empties the decoded image cache', () async {
    final recorder = ui.PictureRecorder();
    ui.Canvas(recorder)
        .drawRect(const ui.Rect.fromLTWH(0, 0, 8, 8), ui.Paint());
    final image = await recorder.endRecording().toImage(8, 8);
    final cache = PaintingBinding.instance.imageCache;
    cache.putIfAbsent(
      'photo',
      () => OneFrameImageStreamCompleter(Future.value(ImageInfo(image: image))),
    );
    await Future<void>.delayed(Duration.zero);
    expect(cache.currentSizeBytes, greaterThan(0));

    MemoryWatch().didHaveMemoryPressure();

    expect(cache.currentSizeBytes, 0);
    expect(cache.currentSize, 0);
  });
}
