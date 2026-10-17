import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/people/people_models.dart';
import 'package:jarvis_mobile/features/people/person_detail_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'harness.dart';

/// The person page with its follow-through card, quiet and active, on a phone.
/// (The page's own stat tiles already overflow at the largest text size on a
/// small phone, with or without this card.) Run:
/// flutter test test/screenshots/person_follow_through.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);

  for (final (name, size, scale) in [('phone', const Size(393, 852), 1.0)]) {
    for (final quiet in [false, true]) {
      screenshotTest('follow-through ${quiet ? 'quiet' : 'active'} on $name', (
        tester,
      ) async {
        usePhone(tester, size: size);
        final person = {
          'id': 'p1',
          'name': 'Rosa Visser',
          'relationship': 'Friend',
          'contactEveryDays': 7,
          'daysSinceContact': 12,
          'contactDue': true,
          'daysUntilBirthday': 3,
        };
        final http = ScreenshotHttp({
          'GET /api/v1/people/p1': {'person': person, 'facts': <Object?>[]},
          'GET /api/v1/people/p1/follow-through': {
            'hasData': true,
            'cadence': {
              'status': 'due',
              'reason': 'You last talked 12 days ago and wanted to stay in touch every week.',
            },
            'birthday': {'daysUntil': 3},
            'promises': [
              {'direction': 'i_owe', 'description': 'Send the kitchen deck'},
              {'direction': 'owed_to_me', 'description': 'Return the drill'},
            ],
            'reasons': ['You last talked 12 days ago.'],
            'nudge': {
              'mode': quiet ? 'snoozed' : 'on',
              'quiet': quiet,
              'until': '2026-11-16T00:00:00',
              'notUsefulCount': quiet ? 1 : 0,
            },
          },
        });
        final dio = Dio(BaseOptions(baseUrl: 'https://fixture.invalid'))
          ..httpClientAdapter = http;
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
              theme: buildJarvisTheme(),
              home: PersonDetailScreen(
                http: dio,
                person: PersonData.fromJson(person)!,
              ),
            ),
          ),
        );
        for (var i = 0; i < 20; i++) {
          await tester.pump(const Duration(milliseconds: 100));
        }
        await tester.scrollUntilVisible(
          find.byKey(const Key('person-follow-through')),
          200,
          scrollable: find.byType(Scrollable).first,
        );
        await tester.pump(const Duration(milliseconds: 400));
        await capture(
          tester,
          'follow-through-${quiet ? 'quiet' : 'active'}-$name',
        );
      });
    }
  }
}
