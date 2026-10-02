import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/devices/place_reminder_tracker.dart';

void main() {
  test('only pending reminders with a place need tracking', () {
    expect(hasPendingPlaceReminder([]), isFalse);
    expect(
      hasPendingPlaceReminder([
        {'status': 'pending', 'place': null},
        {
          'status': 'completed',
          'place': {'name': 'Home'},
        },
      ]),
      isFalse,
    );
    expect(
      hasPendingPlaceReminder([
        {
          'status': 'pending',
          'place': {'name': 'Home'},
        },
      ]),
      isTrue,
    );
  });

  test('reports when moved far enough or after a quiet spell', () {
    final start = DateTime(2026, 10, 1, 18);
    final last = (latitude: 52.0, longitude: 5.0, at: start);
    bool report(double latitude, Duration after) => shouldReportPosition(
      last: last,
      latitude: latitude,
      longitude: 5.0,
      at: start.add(after),
    );

    expect(
      shouldReportPosition(last: null, latitude: 1, longitude: 1, at: start),
      isTrue,
    );
    expect(report(52.01, const Duration(seconds: 5)), isFalse);
    expect(report(52.0001, const Duration(minutes: 1)), isFalse);
    expect(report(52.01, const Duration(minutes: 1)), isTrue);
    expect(report(52.0, const Duration(minutes: 6)), isTrue);
  });
}
