import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/conversation_summary.dart';
import 'package:jarvis_mobile/theme.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: child),
);

void main() {
  test('reads the summary response and skips blank items', () {
    final summary = ConversationSummaryData.fromJson({
      'summary': ' We planned the trip. ',
      'keyPoints': ['Train at 9', '  ', 3],
      'actionItems': ['Book the hotel'],
      'messageCount': 8,
    });

    expect(summary.summary, 'We planned the trip.');
    expect(summary.keyPoints, ['Train at 9']);
    expect(summary.actionItems, ['Book the hotel']);
    expect(summary.messageCount, 8);
    expect(
      summary.toMarkdown(),
      'We planned the trip.\n\n**Key points**\n\n- Train at 9'
      '\n\n**To do**\n\n- [ ] Book the hotel',
    );
    expect(ConversationSummaryData.fromJson(null).summary, isEmpty);
  });

  testWidgets('shows the summary and sets a reminder per action item', (
    tester,
  ) async {
    final reminded = <String>[];
    await tester.pumpWidget(
      _host(
        ConversationSummarySheet(
          load: () async => const ConversationSummaryData(
            summary: 'We planned the trip.',
            keyPoints: ['Train at 9'],
            actionItems: ['Book the hotel', 'Pack the charger'],
          ),
          onRemind: (item) async {
            reminded.add(item);
            return true;
          },
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('We planned the trip.'), findsOneWidget);
    expect(find.text('Train at 9'), findsOneWidget);
    expect(find.text('Book the hotel'), findsOneWidget);
    expect(find.byTooltip('Remind me'), findsNWidgets(2));

    await tester.tap(find.byTooltip('Remind me').first);
    await tester.pumpAndSettle();

    expect(reminded, ['Book the hotel']);
    expect(find.byTooltip('Reminder set'), findsOneWidget);
    expect(find.byTooltip('Remind me'), findsOneWidget);
  });

  testWidgets('says when there is nothing to do', (tester) async {
    await tester.pumpWidget(
      _host(
        ConversationSummarySheet(
          load: () async => const ConversationSummaryData(summary: 'Hi.'),
          onRemind: (_) async => true,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.text('Nothing left to do from this chat.'), findsOneWidget);
  });

  testWidgets('shows the error and retries', (tester) async {
    var calls = 0;
    await tester.pumpWidget(
      _host(
        ConversationSummarySheet(
          load: () async {
            calls++;
            if (calls == 1) {
              throw const ConversationSummaryException(
                'This conversation is too short to summarize.',
              );
            }
            return const ConversationSummaryData(summary: 'Second try.');
          },
          onRemind: (_) async => true,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(
      find.text('This conversation is too short to summarize.'),
      findsOneWidget,
    );
    await tester.tap(find.text('Retry'));
    await tester.pumpAndSettle();

    expect(find.text('Second try.'), findsOneWidget);
    expect(calls, 2);
  });
}
