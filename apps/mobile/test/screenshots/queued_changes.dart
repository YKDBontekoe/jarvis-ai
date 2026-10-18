import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/api/mutation_queue.dart';
import 'package:jarvis_mobile/features/settings/queued_changes_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'harness.dart';

/// The Waiting to sync screen with one change in each state.
/// Run: flutter test test/screenshots/queued_changes.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);

  for (final (name, size, scale, dark) in [
    ('phone', const Size(393, 852), 1.0, false),
    ('dark', const Size(393, 852), 1.0, true),
    ('large-text', const Size(320, 640), 3.0, false),
  ]) {
    screenshotTest('queued changes on $name', (tester) async {
      usePhone(tester, size: size);
      final now = DateTime(2026, 10, 18, 9);
      final queue = MutationQueue(
        store: MemoryMutationStore(),
        clock: () => now.subtract(const Duration(hours: 3)),
      );
      await queue.bind('https://fixture.invalid');
      await queue.enqueue(
        kind: MutationKinds.reminderComplete,
        targetId: 'r1',
        label: 'Mark “Call the plumber” as done',
        group: 'reminder:r1',
      );
      await queue.enqueue(
        kind: MutationKinds.habitCheckIn,
        targetId: 'h1',
        label: 'Check in “Walk” for 2026-10-18',
        group: 'habit:h1:d',
        payload: {'date': '2026-10-18', 'done': true},
      );
      await queue.enqueue(
        kind: MutationKinds.reminderComplete,
        targetId: 'r2',
        label: 'Mark “Send the deck” as done',
        group: 'reminder:r2',
      );
      await queue.enqueue(
        kind: MutationKinds.reminderComplete,
        targetId: 'r3',
        label: 'Mark “Old thing” as done',
        group: 'reminder:r3',
      );
      final all = queue.items.value;
      queue.items.value = [
        all[0],
        all[1],
        all[2].copyWith(
          status: MutationStatus.conflict,
          message: 'It no longer exists, so this change was not applied.',
        ),
        all[3].copyWith(
          status: MutationStatus.failed,
          message: 'Jarvis could not apply this. Retry it, or discard it.',
        ),
      ];
      await tester.pumpWidget(
        RepaintBoundary(
          key: screenshotKey,
          child: MaterialApp(
            debugShowCheckedModeBanner: false,
            builder: (context, child) => MediaQuery(
              data: MediaQuery.of(context)
                  .copyWith(textScaler: TextScaler.linear(scale)),
              child: child!,
            ),
            theme: buildJarvisTheme(
              brightness: dark ? Brightness.dark : Brightness.light,
            ),
            home: QueuedChangesScreen(
              http: Dio(BaseOptions(baseUrl: 'https://fixture.invalid')),
              queue: queue,
              clock: () => now,
            ),
          ),
        ),
      );
      for (var i = 0; i < 8; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, 'queued-changes-$name');
    });
  }
}
