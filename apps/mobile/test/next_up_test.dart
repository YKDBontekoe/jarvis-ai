import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home/next_up.dart';

String _iso(DateTime time) => time.toUtc().toIso8601String();

void main() {
  final now = DateTime(2026, 10, 3, 18, 16);

  group('upcomingItems', () {
    test('merges events and reminders, soonest first', () {
      final items = upcomingItems({
        'calendar': {
          'events': [
            {'title': 'Dinner', 'startAt': _iso(DateTime(2026, 10, 3, 19, 45))},
          ],
        },
        'reminders': [
          {'title': 'Call mum', 'dueAt': _iso(DateTime(2026, 10, 3, 19, 30))},
        ],
      }, now);
      expect(items.map((i) => i.title), ['Call mum', 'Dinner']);
      expect(items.first.reminder, isTrue);
      expect(items.last.reminder, isFalse);
    });

    test('skips what is over but keeps an event that is still going', () {
      final items = upcomingItems({
        'calendar': {
          'events': [
            {
              'title': 'Over',
              'startAt': _iso(DateTime(2026, 10, 3, 9)),
              'endAt': _iso(DateTime(2026, 10, 3, 10)),
            },
            {
              'title': 'Going',
              'startAt': _iso(DateTime(2026, 10, 3, 18)),
              'endAt': _iso(DateTime(2026, 10, 3, 19)),
            },
          ],
        },
        'reminders': [
          {'title': 'Late', 'dueAt': _iso(DateTime(2026, 10, 3, 8))},
        ],
      }, now);
      expect(items.map((i) => i.title), ['Going']);
    });

    test('keeps the location of an event as its detail', () {
      final items = upcomingItems({
        'calendar': {
          'events': [
            {
              'title': 'Flight',
              'startAt': _iso(DateTime(2026, 10, 3, 21)),
              'location': 'Schiphol',
            },
          ],
        },
      }, now);
      expect(items.single.detail, 'Schiphol');
    });

    test('junk and missing data give an empty list', () {
      expect(upcomingItems(null, now), isEmpty);
      expect(upcomingItems(const {}, now), isEmpty);
      expect(
        upcomingItems({
          'calendar': {
            'events': [
              {'title': '', 'startAt': _iso(DateTime(2026, 10, 3, 20))},
              {'title': 'No time'},
              'text',
            ],
          },
          'reminders': 'nope',
        }, now),
        isEmpty,
      );
    });
  });

  group('formatting', () {
    test('clockTime pads hours and minutes', () {
      expect(clockTime(DateTime(2026, 1, 1, 7, 5)), '07:05');
      expect(clockTime(DateTime(2026, 1, 1, 19, 45)), '19:45');
    });

    test('countdownLabel reads naturally', () {
      String at(Duration gap) => countdownLabel(now.add(gap), now);
      expect(at(Duration.zero), 'now');
      expect(at(const Duration(minutes: -5)), 'now');
      expect(at(const Duration(minutes: 25)), 'in 25 min');
      expect(at(const Duration(hours: 1, minutes: 29)), 'in 1 h 29 min');
      expect(at(const Duration(hours: 3)), 'in 3 h');
      expect(at(const Duration(hours: 24, minutes: 30)), 'tomorrow');
      expect(at(const Duration(days: 3)), 'Tuesday');
    });

    test('longDate and greeting', () {
      expect(longDate(now), 'Saturday 3 October');
      expect(greetingFor(DateTime(2026, 1, 1, 8)), 'Good morning');
      expect(greetingFor(DateTime(2026, 1, 1, 14)), 'Good afternoon');
      expect(greetingFor(DateTime(2026, 1, 1, 21)), 'Good evening');
    });
  });
}
