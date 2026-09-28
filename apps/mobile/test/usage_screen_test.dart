import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/usage/usage_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _usage() => {
  'period': '7d',
  'timeZoneId': 'Europe/Amsterdam',
  'personalization': {
    'score': 58,
    'band': 'Familiar',
    'summary': 'Jarvis has a working picture of you, from 12 active memories.',
    'activeMemories': 12,
    'pinnedMemories': 2,
    'supersededMemories': 1,
    'memoryKinds': 4,
    'personaTraits': 3,
    'hasCustomInstructions': true,
    'hasPreferredName': false,
    'feedbackRatings': 5,
  },
  'activity': {
    'messagesSent': {'inPeriod': 18, 'total': 40},
    'assistantReplies': {'inPeriod': 17, 'total': 39},
    'conversations': {'inPeriod': 3, 'total': 8},
    'dreams': {'inPeriod': 2, 'total': 7},
    'memories': {'inPeriod': 3, 'total': 13},
    'skills': {'inPeriod': 1, 'total': 4},
    'files': {'inPeriod': 0, 'total': 2},
    'graphEntities': {'inPeriod': 2, 'total': 9},
    'graphRelations': {'inPeriod': 1, 'total': 6},
    'tasksCompleted': {'inPeriod': 1, 'total': 5},
    'reminders': {'inPeriod': 0, 'total': 2},
    'approvals': {'inPeriod': 1, 'total': 3},
    'webSearches': {'inPeriod': 4, 'total': 11},
    'channelMessages': {'inPeriod': 0, 'total': 0},
    'feedbackRatings': {'inPeriod': 1, 'total': 5},
    'browserSessions': {'inPeriod': 0, 'total': 1},
    'lastDreamAt': '2026-09-27T03:00:00Z',
    'diaryEntries': 3,
  },
  'codex': {
    'calls': 6,
    'completedCalls': 6,
    'failedCalls': 0,
    'inputTokens': 12000,
    'outputTokens': 3400,
    'cachedInputTokens': 800,
    'reasoningOutputTokens': 200,
    'totalTokens': 15400,
    'estimatedCostUsd': null,
    'unpricedCalls': 0,
    'webSearchActions': 4,
    'averageDurationMs': 1800,
    'costNote': 'Included with your ChatGPT subscription. Codex does not bill per token.',
    'lifetimeCalls': 20,
    'lifetimeTotalTokens': 90000,
    'lifetimeEstimatedCostUsd': null,
    'purposes': [
      {'purpose': 'chat', 'calls': 4},
      {'purpose': 'background', 'calls': 2},
    ],
  },
  'openRouter': {
    'calls': 3,
    'completedCalls': 3,
    'failedCalls': 0,
    'inputTokens': 2000000,
    'outputTokens': 1000,
    'cachedInputTokens': 0,
    'reasoningOutputTokens': 0,
    'totalTokens': 2001000,
    'estimatedCostUsd': 6.03,
    'unpricedCalls': 0,
    'webSearchActions': 0,
    'averageDurationMs': 900,
    'costNote': 'Estimated from OpenRouter prices.',
    'lifetimeCalls': 3,
    'lifetimeTotalTokens': 2001000,
    'lifetimeEstimatedCostUsd': 6.03,
    'purposes': [
      {'purpose': 'embedding', 'calls': 3},
    ],
  },
  'other': null,
  'daily': [
    {'day': '2026-09-27', 'codexTokens': 1000, 'openRouterTokens': 0, 'openRouterCostUsd': 0},
    {'day': '2026-09-28', 'codexTokens': 14400, 'openRouterTokens': 2001000, 'openRouterCostUsd': 6.03},
  ],
  'chartNote': null,
  'models': [
    {
      'provider': 'openrouter',
      'model': 'openai/text-embedding-3-small',
      'purpose': 'embedding',
      'calls': 3,
      'totalTokens': 2001000,
      'estimatedCostUsd': 6.03,
    },
    {
      'provider': 'codex',
      'model': 'gpt-5.4',
      'purpose': 'chat',
      'calls': 4,
      'totalTokens': 14000,
      'estimatedCostUsd': null,
    },
  ],
};

void main() {
  test('token and cost formatters stay compact', () {
    expect(formatTokenCount(860), '860');
    expect(formatTokenCount(15400), '15k');
    expect(formatTokenCount(2001000), '2.0M');
    expect(formatUsd(6.03), '\$6.03');
    expect(formatUsd(0.004), '<\$0.01');
    expect(formatUsd(null), '—');
  });

  testWidgets('usage dashboard shows tokens, cost, memories, and dreams', (
    tester,
  ) async {
    final http = FixtureHttp()..on('GET', '/api/v1/usage', _usage());
    tester.view.physicalSize = const Size(900, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: UsageScreen(http: http.client())),
    );
    await tester.pumpAndSettle();

    expect(find.text('Familiar'), findsOneWidget);
    expect(find.textContaining('12 active memories'), findsOneWidget);
    expect(find.text('\$6.03'), findsWidgets);
    expect(find.text('18'), findsOneWidget);
    expect(find.text('12'), findsWidgets);
    expect(find.textContaining('3 diary entries'), findsOneWidget);
    expect(find.text('gpt-5.4'), findsOneWidget);
    expect(find.text('ChatGPT subscription'), findsOneWidget);
    expect(find.text('Codex CLI'), findsOneWidget);

    await tester.tap(find.byKey(const Key('usage-period-30d')));
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('usage-period-30d')), findsOneWidget);
    expect(http.sent('GET', '/api/v1/usage').length, 2);
  });
}
