import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/finance/subscription_review_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'harness.dart';

/// The renewal review of a subscription on a phone, a small phone and at the
/// largest text size. Run: flutter test test/screenshots/subscription_review.dart
void main() {
  setUpAll(loadAppFonts);
  setUp(mockPlatformChannels);

  for (final (name, size, scale) in [
    ('phone', const Size(393, 852), 1.0),
    ('small', const Size(320, 640), 1.0),
    ('large-text', const Size(320, 640), 3.0),
  ]) {
    screenshotTest('subscription review on $name', (tester) async {
      usePhone(tester, size: size);
      final http = ScreenshotHttp({
        'GET /api/v1/finance/subscriptions/s1/review': {
          'subscription': {
            'id': 's1',
            'merchant': 'Netflix',
            'status': 'active',
          },
          'review': {
            'choice': 'negotiate',
            'libraryItemId': 'l1',
            'followUpCommitmentId': 'c1',
          },
          'quiet': false,
          'facts': [
            '15.99 EUR monthly, about 15.99 a month.',
            'Charged 8 times in your bank data, last on 27 Sep 2026; next expected 27 Oct 2026 (10 days).',
            'The price went up from 12.99 to 15.99.',
            'The next charge date is an estimate from the pattern in your charges, not a date the merchant gave you.',
          ],
          'saving': {
            'basis': 'You confirmed 12.99 EUR monthly on 16 Oct 2026, against 15.99 before: 3.00 a month. Over a year that is 36.00 if the price holds.',
          },
          'paths': [
            {
              'id': 'keep',
              'label': 'Keep it',
              'nextAction': 'Nothing to do. Jarvis stays quiet about it until 15 Jan 2027.',
            },
            {
              'id': 'investigate',
              'label': 'Look at alternatives',
              'nextAction':
                  'Start a comparison with this plan as the first option.',
            },
            {
              'id': 'negotiate',
              'label': 'Ask for a lower price',
              'nextAction': 'Draft a message or work through it live with Jarvis; you decide on every offer.',
            },
            {
              'id': 'cancel',
              'label': 'Cancel it',
              'nextAction': 'It counts as cancelled only when you have the merchant\'s confirmation.',
            },
            {
              'id': 'remind',
              'label': 'Remind me',
              'nextAction': 'Get a reminder before the next charge.',
            },
            {
              'id': 'archive',
              'label': 'Not a subscription',
              'nextAction': 'Stop tracking it.',
            },
          ],
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
            home: SubscriptionReviewScreen(http: dio, subscriptionId: 's1'),
          ),
        ),
      );
      for (var i = 0; i < 20; i++) {
        await tester.pump(const Duration(milliseconds: 100));
      }
      await capture(tester, 'subscription-review-$name');
    });
  }
}
