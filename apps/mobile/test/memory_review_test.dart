import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/memory/memory_review_screen.dart';
import 'package:jarvis_mobile/theme.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _entry(
  String id,
  String content, {
  List<String> reasons = const ['recent'],
  bool pinned = false,
  int corrections = 0,
  String? chat,
  String? profile,
}) => {
  'id': id,
  'kind': 'fact',
  'content': content,
  'confidence': .9,
  'isPinned': pinned,
  'createdAt': '2026-10-15T10:00:00Z',
  'validUntil': null,
  'reasons': reasons,
  'source': {
    'type': chat == null ? 'user' : 'conversation',
    'label': chat == null ? 'You added this' : 'From the chat “Planning”',
    'conversationId': chat,
    'messageId': chat == null ? null : 'm1',
  },
  'profileId': profile == null ? null : 'p1',
  'profileName': profile,
  'corrections': corrections,
};

Widget _host(Widget child) =>
    MaterialApp(theme: buildJarvisTheme(), home: child);

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      _host(
        MemoryReviewScreen(
          http: http.client(),
          onOpenConversation: (_) async {},
        ),
      ),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'shows why each memory is here, where it came from and its scope',
    (tester) async {
      http.on('GET', '/api/v1/memory/review', [
        _entry('a', 'Rosa prefers Fridays', chat: 'c1', profile: 'Work'),
        _entry(
          'b',
          'Maybe vegetarian',
          reasons: ['low_confidence', 'expiring'],
          corrections: 2,
        ),
      ]);
      await show(tester);

      expect(find.text('Rosa prefers Fridays'), findsOneWidget);
      expect(find.text('Learned lately'), findsOneWidget);
      expect(find.text('Only for Work'), findsOneWidget);
      expect(find.text('Jarvis was unsure'), findsOneWidget);
      expect(find.text('Expires soon'), findsOneWidget);
      expect(find.text('All profiles'), findsOneWidget);
      expect(find.textContaining('From the chat “Planning”'), findsOneWidget);
      expect(find.text('Corrected 2 times'), findsOneWidget);
      // Only a memory with a real source chat offers "See where".
      expect(find.byKey(const Key('memory-source-a')), findsOneWidget);
      expect(find.byKey(const Key('memory-source-b')), findsNothing);
    },
  );

  testWidgets('a correction posts the new wording and reloads', (tester) async {
    http
      ..on('GET', '/api/v1/memory/review', [
        _entry('a', 'Rosa prefers Wednesdays'),
      ])
      ..on('POST', '/api/v1/memory/a/correct', {'id': 'a'});
    await show(tester);

    await tester.tap(find.byKey(const Key('memory-correct-a')));
    await tester.pumpAndSettle();
    // Unchanged text cannot be saved.
    expect(
      tester
          .widget<FilledButton>(find.byKey(const Key('memory-correction-save')))
          .onPressed,
      isNull,
    );
    await tester.enterText(
      find.byKey(const Key('memory-correction-field')),
      '  Rosa prefers Thursdays now ',
    );
    await tester.pump();
    http.on('GET', '/api/v1/memory/review', <Object>[]);
    await tester.tap(find.byKey(const Key('memory-correction-save')));
    await tester.pumpAndSettle();

    final sent =
        http.sent('POST', '/api/v1/memory/a/correct').single.body as Map;
    expect(sent['content'], 'Rosa prefers Thursdays now');
    expect(find.text('Nothing to review'), findsOneWidget);
  });

  testWidgets('"No longer true" asks first, then ends the memory', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/memory/review', [
        _entry('a', 'Travelling until Friday'),
      ])
      ..on('POST', '/api/v1/memory/a/expire', {'id': 'a'});
    await show(tester);

    await tester.tap(find.byKey(const Key('memory-expire-a')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/memory/a/expire'), isEmpty);
    await tester.tap(find.text('Stop using it'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/memory/a/expire'), hasLength(1));
  });

  testWidgets('a pinned memory explains itself and offers no replace or end', (
    tester,
  ) async {
    http.on('GET', '/api/v1/memory/review', [
      _entry('p', 'Always answer in Dutch', pinned: true),
    ]);
    await show(tester);

    expect(find.text('Pinned'), findsOneWidget);
    expect(find.textContaining('Edit or unpin this one'), findsOneWidget);
    expect(find.byKey(const Key('memory-correct-p')), findsNothing);
    expect(find.byKey(const Key('memory-expire-p')), findsNothing);
  });

  testWidgets('the server refusing a replacement is shown, not swallowed', (
    tester,
  ) async {
    http
      ..on('GET', '/api/v1/memory/review', [_entry('a', 'Old wording here')])
      ..on('POST', '/api/v1/memory/a/correct', {
        'message': 'That memory is no longer current.',
      }, status: 409);
    await show(tester);

    await tester.tap(find.byKey(const Key('memory-correct-a')));
    await tester.pumpAndSettle();
    await tester.enterText(
      find.byKey(const Key('memory-correction-field')),
      'New wording here',
    );
    await tester.pump();
    await tester.tap(find.byKey(const Key('memory-correction-save')));
    await tester.pumpAndSettle();

    expect(find.byType(SnackBar), findsOneWidget);
    expect(find.text('Old wording here'), findsOneWidget);
  });

  testWidgets('history lists the replaced wordings', (tester) async {
    http
      ..on('GET', '/api/v1/memory/review', [
        _entry('a', 'Rosa prefers Thursdays now', corrections: 1),
      ])
      ..on('GET', '/api/v1/memory/a/history', [
        {
          'id': 'old',
          'content': 'Rosa prefers Wednesdays',
          'createdAt': '2026-10-01T10:00:00Z',
          'replacedAt': '2026-10-16T10:00:00Z',
        },
      ]);
    await show(tester);

    await tester.tap(find.byKey(const Key('memory-history-a')));
    await tester.pumpAndSettle();
    expect(find.text('Earlier versions'), findsOneWidget);
    expect(find.text('Rosa prefers Wednesdays'), findsOneWidget);
  });

  testWidgets('an empty review says nothing is waiting', (tester) async {
    http.on('GET', '/api/v1/memory/review', <Object>[]);
    await show(tester);
    expect(find.text('Nothing to review'), findsOneWidget);
  });
}
