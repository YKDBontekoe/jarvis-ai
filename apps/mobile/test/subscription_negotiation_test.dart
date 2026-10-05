import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/finance/finance_models.dart';
import 'package:jarvis_mobile/features/finance/finance_screen.dart';
import 'package:jarvis_mobile/features/tasks/task_details_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 15, 12);

Map<String, Object?> _subscription({
  String id = 's1',
  String status = 'active',
  String? cancelUrl,
  String? taskId,
  String? goal,
  String? started,
}) => {
  'id': id,
  'merchant': 'Netflix',
  'amount': 15.99,
  'currency': 'EUR',
  'cadence': 'monthly',
  'lastChargedOn': '2026-10-03',
  'nextDueOn': '2026-11-03',
  'chargeCount': 5,
  'previousAmount': 13.99,
  'status': status,
  'remindDaysBefore': null,
  'perMonth': 15.99,
  'cancelUrl': cancelUrl,
  'negotiationTaskId': taskId,
  'negotiationGoal': goal,
  'negotiationStartedAt': started,
};

Map<String, Object?> _overview(List<Map<String, Object?>> subscriptions) => {
  'year': 2026,
  'month': 10,
  'currency': 'EUR',
  'total': 265,
  'budgets': <Object>[],
  'forecast': {
    'year': 2026,
    'month': 10,
    'currency': 'EUR',
    'spentSoFar': 265,
    'expectedRecurring': 10,
    'projectedOther': 256,
    'projectedTotal': 531,
    'previousMonthTotal': 400,
    'daysLeft': 16,
    'upcoming': <Object>[],
  },
  'subscriptions': subscriptions,
  'subscriptionsPerMonth': 15.99,
  'alerts': <Object>[],
};

void main() {
  late FixtureHttp http;
  late List<String> asked;

  setUp(() {
    http = FixtureHttp();
    asked = [];
  });

  /// Opens the finance screen from a first page, so popping it (as the browser route does) has somewhere to go.
  Future<void> show(WidgetTester tester, {bool withChat = true}) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        theme: buildJarvisTheme(),
        home: Builder(
          builder: (context) => Scaffold(
            body: Center(
              child: ElevatedButton(
                key: const Key('open-finance'),
                onPressed: () => Navigator.of(context).push<void>(
                  MaterialPageRoute(
                    builder: (_) => FinanceScreen(
                      http: http.client(),
                      now: _now,
                      onAskInChat: withChat ? asked.add : null,
                    ),
                  ),
                ),
                child: const Text('open'),
              ),
            ),
          ),
        ),
      ),
    );
    await tester.tap(find.byKey(const Key('open-finance')));
    await tester.pumpAndSettle();
  }

  Future<void> openSheet(WidgetTester tester) async {
    await tester.tap(find.byKey(const Key('subscription-negotiate-s1')));
    await tester.pumpAndSettle();
  }

  test(
    'the negotiation state parses, and is empty when the server sends none',
    () {
      final data = SubscriptionData.fromJson(
        _subscription(
          cancelUrl: 'https://www.netflix.com/cancelplan',
          taskId: 't1',
          goal: 'cancel',
          started: '2026-10-14T09:00:00Z',
        ),
      )!;
      expect(data.cancelUrl, 'https://www.netflix.com/cancelplan');
      expect(data.negotiationTaskId, 't1');
      expect(data.negotiationGoal, 'cancel');
      expect(data.negotiationStartedAt, isNotNull);

      final plain = SubscriptionData.fromJson(_subscription())!;
      expect(plain.cancelUrl, isNull);
      expect(plain.negotiationTaskId, isNull);
      expect(plain.negotiationStartedAt, isNull);
    },
  );

  testWidgets(
    'only a subscription you still pay for offers to cancel or negotiate',
    (tester) async {
      http.on(
        'GET',
        '/api/v1/finance/overview',
        _overview([
          _subscription(),
          _subscription(id: 's2', status: 'cancelled'),
          _subscription(id: 's3', status: 'dismissed'),
        ]),
      );
      await show(tester);

      expect(
        find.byKey(const Key('subscription-negotiate-s1')),
        findsOneWidget,
      );
      expect(find.byKey(const Key('subscription-negotiate-s2')), findsNothing);
      expect(find.byKey(const Key('subscription-negotiate-s3')), findsNothing);
    },
  );

  testWidgets('drafting a message starts a task and says where to find it', (
    tester,
  ) async {
    http.on('GET', '/api/v1/finance/overview', _overview([_subscription()]));
    http.on('POST', '/api/v1/finance/subscriptions/s1/negotiate', {
      'mode': 'draft',
      'merchant': 'Netflix',
      'taskId': 't1',
      'prompt': null,
    });
    await show(tester);

    await openSheet(tester);
    expect(find.text('Draft the message'), findsOneWidget);
    await tester.tap(find.byKey(const Key('negotiate-start')));
    await tester.pumpAndSettle();

    final body =
        http
                .sent('POST', '/api/v1/finance/subscriptions/s1/negotiate')
                .single
                .body
            as Map;
    expect(body, {'goal': 'cancel', 'mode': 'draft', 'cancelUrl': ''});
    expect(
      find.text('Jarvis is drafting the message. You will find it in Tasks.'),
      findsOneWidget,
    );
    expect(asked, isEmpty);
    // The overview is loaded again so the card can show the drafting task.
    expect(http.sent('GET', '/api/v1/finance/overview'), hasLength(2));
  });

  testWidgets(
    'a lower price in the browser opens a chat with the prompt and leaves the screen',
    (tester) async {
      http.on('GET', '/api/v1/finance/overview', _overview([_subscription()]));
      http.on('POST', '/api/v1/finance/subscriptions/s1/negotiate', {
        'mode': 'browser',
        'merchant': 'Netflix',
        'taskId': null,
        'prompt': 'Help me get a lower price...',
      });
      await show(tester);

      await openSheet(tester);
      await tester.tap(find.byKey(const Key('negotiate-goal-lower')));
      await tester.tap(find.byKey(const Key('negotiate-mode-browser')));
      await tester.enterText(
        find.byKey(const Key('negotiate-url')),
        '  https://www.netflix.com/cancelplan ',
      );
      await tester.pump();
      expect(find.text('Open the chat'), findsOneWidget);
      await tester.tap(find.byKey(const Key('negotiate-start')));
      await tester.pumpAndSettle();

      final body =
          http
                  .sent('POST', '/api/v1/finance/subscriptions/s1/negotiate')
                  .single
                  .body
              as Map;
      expect(body, {
        'goal': 'lower_price',
        'mode': 'browser',
        'cancelUrl': 'https://www.netflix.com/cancelplan',
      });
      expect(asked, ['Help me get a lower price...']);
      expect(find.byType(FinanceScreen), findsNothing);
    },
  );

  testWidgets(
    'the browser route needs a chat and cannot be picked without one',
    (tester) async {
      http.on('GET', '/api/v1/finance/overview', _overview([_subscription()]));
      await show(tester, withChat: false);

      await openSheet(tester);
      expect(find.textContaining('Needs the chat'), findsOneWidget);
      await tester.tap(find.byKey(const Key('negotiate-mode-browser')));
      await tester.pump();

      expect(find.text('Draft the message'), findsOneWidget);
      expect(find.text('Open the chat'), findsNothing);
    },
  );

  testWidgets('the sheet is closed without sending anything when dismissed', (
    tester,
  ) async {
    http.on('GET', '/api/v1/finance/overview', _overview([_subscription()]));
    await show(tester);

    await openSheet(tester);
    await tester.tapAt(const Offset(10, 10));
    await tester.pumpAndSettle();

    expect(
      http.sent('POST', '/api/v1/finance/subscriptions/s1/negotiate'),
      isEmpty,
    );
    expect(find.byKey(const Key('negotiate-start')), findsNothing);
  });

  testWidgets('a rejected request shows the reason and starts nothing', (
    tester,
  ) async {
    http.on('GET', '/api/v1/finance/overview', _overview([_subscription()]));
    http.on('POST', '/api/v1/finance/subscriptions/s1/negotiate', {
      'title': 'Validation failed',
      'errors': {
        'cancelUrl': ['Use a full https address for the cancel page.'],
      },
    }, status: 400);
    await show(tester);

    await openSheet(tester);
    await tester.enterText(
      find.byKey(const Key('negotiate-url')),
      'http://nope.example',
    );
    await tester.tap(find.byKey(const Key('negotiate-start')));
    await tester.pumpAndSettle();

    expect(find.textContaining('https address'), findsOneWidget);
    expect(asked, isEmpty);
    expect(find.byType(FinanceScreen), findsOneWidget);
  });

  testWidgets(
    'a saved cancel page is shown on the card and prefilled in the sheet',
    (tester) async {
      http.on(
        'GET',
        '/api/v1/finance/overview',
        _overview([
          _subscription(cancelUrl: 'https://www.netflix.com/cancelplan'),
        ]),
      );
      await show(tester);

      expect(find.text('Cancel page: www.netflix.com'), findsOneWidget);
      await openSheet(tester);

      expect(
        tester
            .widget<TextField>(find.byKey(const Key('negotiate-url')))
            .controller!
            .text,
        'https://www.netflix.com/cancelplan',
      );
    },
  );

  testWidgets('a drafting task is shown with a way to open it', (tester) async {
    http.on(
      'GET',
      '/api/v1/finance/overview',
      _overview([
        _subscription(
          taskId: 't1',
          goal: 'lower_price',
          started: '2026-10-14T09:00:00Z',
        ),
      ]),
    );
    await show(tester);

    expect(
      find.byKey(const Key('subscription-negotiation-s1')),
      findsOneWidget,
    );
    expect(find.textContaining('Price message asked'), findsOneWidget);
    await tester.tap(find.byKey(const Key('subscription-task-s1')));
    await tester.pumpAndSettle();

    expect(find.byType(TaskDetailsScreen), findsOneWidget);
  });

  testWidgets('a subscription with nothing negotiated shows no task line', (
    tester,
  ) async {
    http.on('GET', '/api/v1/finance/overview', _overview([_subscription()]));
    await show(tester);

    expect(find.byKey(const Key('subscription-negotiation-s1')), findsNothing);
    expect(find.byKey(const Key('subscription-cancel-url-s1')), findsNothing);
  });
}
