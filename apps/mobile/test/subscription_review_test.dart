import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/finance/subscription_review_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

const _id = 's1';
const _path = '/api/v1/finance/subscriptions/$_id/review';

const _paths = [
  {'id': 'keep', 'label': 'Keep it', 'nextAction': 'Nothing to do.'},
  {
    'id': 'investigate',
    'label': 'Look at alternatives',
    'nextAction': 'Start a comparison.',
  },
  {
    'id': 'negotiate',
    'label': 'Ask for a lower price',
    'nextAction': 'Draft a message.',
  },
  {
    'id': 'cancel',
    'label': 'Cancel it',
    'nextAction': 'Only cancelled with confirmation.',
  },
  {'id': 'remind', 'label': 'Remind me', 'nextAction': 'Get a reminder.'},
  {
    'id': 'archive',
    'label': 'Not a subscription',
    'nextAction': 'Stop tracking it.',
  },
];

Map<String, Object?> _view({
  String status = 'active',
  Map<String, Object?> review = const {},
  Map<String, Object?>? saving,
  bool quiet = false,
}) => {
  'subscription': {'id': _id, 'merchant': 'Netflix', 'status': status},
  'review': review,
  'quiet': quiet,
  'facts': ['12.99 EUR monthly.', 'The next charge date is an estimate.'],
  'paths': _paths,
  'saving': saving,
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> open(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 4200);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      _host(SubscriptionReviewScreen(http: http.client(), subscriptionId: _id)),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'shows what the records show and every path with its next action',
    (tester) async {
      http.on('GET', _path, _view());
      await open(tester);

      expect(find.text('12.99 EUR monthly.'), findsOneWidget);
      expect(find.textContaining('estimate'), findsOneWidget);
      for (final path in _paths) {
        expect(find.byKey(Key('review-path-${path['id']}')), findsOneWidget);
        expect(find.text(path['nextAction']!), findsOneWidget);
      }
    },
  );

  testWidgets('keep posts the choice and says it is quiet', (tester) async {
    http
      ..on('GET', _path, _view())
      ..on(
        'POST',
        '$_path/choice',
        _view(review: {'choice': 'keep'}, quiet: true),
      );
    await open(tester);

    await tester.tap(find.byKey(const Key('review-path-keep')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/choice').single.body, {'choice': 'keep'});
    expect(find.byKey(const Key('review-quiet')), findsOneWidget);
    expect(find.text('YOUR CHOICE'), findsOneWidget);
  });

  testWidgets('a pack is saved to the Library and nothing is sent', (
    tester,
  ) async {
    http
      ..on('GET', _path, _view())
      ..on('POST', '$_path/pack', {'libraryItemId': 'l1', 'markdown': '# x'});
    await open(tester);

    await tester.tap(find.byKey(const Key('review-pack-cancel')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/pack').single.body, {'goal': 'cancel'});
    expect(find.textContaining('Nothing was sent'), findsOneWidget);
  });

  testWidgets('tracking their reply posts once and the button says so', (
    tester,
  ) async {
    http
      ..on('GET', _path, _view())
      ..on('POST', '$_path/follow-up', {'commitmentId': 'c1', 'created': true});
    await open(tester);
    http.on('GET', _path, _view(review: {'followUpCommitmentId': 'c1'}));

    await tester.tap(find.byKey(const Key('review-follow-up')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/follow-up'), hasLength(1));
    expect(find.text('Their reply is tracked'), findsOneWidget);
  });

  testWidgets('a confirmed lower price is posted and explained as a saving', (
    tester,
  ) async {
    http
      ..on('GET', _path, _view())
      ..on(
        'POST',
        '$_path/outcome',
        _view(saving: {'basis': 'You confirmed 9.00 EUR: 3.00 a month.'}),
      );
    await open(tester);
    expect(find.byKey(const Key('review-saving')), findsNothing);

    await tester.tap(find.byKey(const Key('review-confirm-price')));
    await tester.pumpAndSettle();
    await tester.enterText(find.byKey(const Key('review-field')), '9,00');
    await tester.tap(find.byKey(const Key('review-field-ok')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '$_path/outcome').single.body, {
      'confirmedAmount': 9.0,
    });
    expect(find.textContaining('3.00 a month'), findsOneWidget);
  });

  testWidgets(
    'a refused cancellation shows why and the subscription stays active',
    (tester) async {
      http
        ..on('GET', _path, _view())
        ..on('POST', '$_path/cancellation', {
          'errors': {
            'evidence': ['Write down the merchant\'s confirmation.'],
          },
          'detail': 'Write down the merchant\'s confirmation.',
        }, status: 400);
      await open(tester);

      await tester.tap(find.byKey(const Key('review-confirm-cancel')));
      await tester.pumpAndSettle();
      await tester.enterText(find.byKey(const Key('review-field')), 'done');
      await tester.tap(find.byKey(const Key('review-field-ok')));
      await tester.pumpAndSettle();

      expect(find.textContaining("merchant's confirmation"), findsOneWidget);
      expect(find.byKey(const Key('review-status')), findsNothing);
    },
  );

  testWidgets(
    'a cancelled subscription shows its evidence and offers no actions',
    (tester) async {
      http.on(
        'GET',
        _path,
        _view(status: 'cancelled', review: {'cancelEvidence': 'Ref NF-1'}),
      );
      await open(tester);

      expect(find.textContaining('Ref NF-1'), findsOneWidget);
      expect(find.byKey(const Key('review-path-keep')), findsNothing);
    },
  );

  testWidgets('a missing subscription shows an error with retry', (
    tester,
  ) async {
    http.on('GET', _path, null, status: 404);
    await open(tester);

    expect(find.text('Could not load this review.'), findsOneWidget);
  });
}
