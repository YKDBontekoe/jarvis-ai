import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/schedule_format.dart';

void main() {
  Future<BuildContext> contextFor(WidgetTester tester) async {
    late BuildContext captured;
    await tester.pumpWidget(
      MaterialApp(
        home: Builder(
          builder: (context) {
            captured = context;
            return const SizedBox();
          },
        ),
      ),
    );
    return captured;
  }

  testWidgets('times read on the 24-hour clock everywhere', (tester) async {
    final context = await contextFor(tester);
    expect(clockOf(context, DateTime(2026, 11, 8, 18, 5)), '18:05');
    expect(clockOf(context, DateTime(2026, 11, 8, 8, 49)), '08:49');
  });

  testWidgets('friendly days are capitalised alone and lowercase in a '
      'sentence', (tester) async {
    final context = await contextFor(tester);
    final now = DateTime(2026, 11, 8, 12);
    expect(
      friendlyWhen(context, DateTime(2026, 11, 8, 18), now: now),
      'Today 18:00',
    );
    expect(
      friendlyWhen(
        context,
        DateTime(2026, 11, 7, 20),
        now: now,
        inSentence: true,
      ),
      'yesterday 20:00',
    );
    expect(
      friendlyWhen(
        context,
        DateTime(2026, 11, 9, 7, 30),
        now: now,
        inSentence: true,
      ),
      'tomorrow 07:30',
    );
  });
}
