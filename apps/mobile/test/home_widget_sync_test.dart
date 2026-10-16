import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home/home_context.dart';
import 'package:jarvis_mobile/features/home/home_insights.dart';
import 'package:jarvis_mobile/features/home/home_widget_sync.dart';

void main() {
  final now = DateTime.utc(2026, 10, 16, 7, 14);
  final context = HomeContext(
    dayLine: 'Design review soon, then you are free.',
    hero: HomeHero(
      id: 'soon:d',
      kind: 'soon',
      title: 'Design review',
      detail: 'With the product team',
      reason: 'From your calendar',
      startsAt: now.add(const Duration(minutes: 34)),
    ),
  );
  const needs = [
    NeedsYouItem(
      kind: NeedsYouKind.person,
      id: 'whatsapp:wa:+31633',
      title: 'Rosa',
      detail: 'Je bent de beste · 14 h',
    ),
    NeedsYouItem(
      kind: NeedsYouKind.reminder,
      id: 'r',
      title: 'Bring the charger',
      detail: 'Overdue · 20 min',
    ),
  ];

  test('the payload carries Right now and what needs you', () {
    final values = HomeWidgetPayload.build(
      now: now,
      context: context,
      needsYou: needs,
    ).values;

    expect(values['hero_kind'], 'soon');
    expect(values['hero_title'], 'Design review');
    expect(
      values['hero_starts_at'],
      now.add(const Duration(minutes: 34)).millisecondsSinceEpoch,
    );
    expect(values['needs_count'], 2);
    expect(jsonDecode(values['needs_items']! as String), [
      {'title': 'Rosa', 'detail': 'Je bent de beste · 14 h', 'kind': 'person'},
      {
        'title': 'Bring the charger',
        'detail': 'Overdue · 20 min',
        'kind': 'reminder',
      },
    ]);
  });

  test('without a hero the widget shows a calm day', () {
    final values = HomeWidgetPayload.build(now: now).values;
    expect(values['hero_kind'], 'none');
    expect(values['hero_title'], 'Nothing pressing');
    expect(values.containsKey('hero_starts_at'), isFalse);
    expect(values['needs_count'], 0);
  });

  test('only a change in content counts as a change', () {
    final a = HomeWidgetPayload.build(now: now, context: context);
    final b = HomeWidgetPayload.build(
      now: now.add(const Duration(minutes: 1)),
      context: context,
    );
    expect(a, b);
    expect(a.hashCode, b.hashCode);
    expect(a == HomeWidgetPayload.build(now: now), isFalse);
  });

  test('widget taps map to app destinations', () {
    expect(widgetDestination(Uri.parse('jarvis://ask?homeWidget')), 'ask');
    expect(widgetDestination(Uri.parse('jarvis://voice?homeWidget')), 'voice');
    expect(widgetDestination(Uri.parse('jarvis://settings')), isNull);
    expect(widgetDestination(Uri.parse('https://ask')), isNull);
    expect(widgetDestination(null), isNull);
  });

  testWidgets('on iOS it writes the App Group once per change', (tester) async {
    final calls = <MethodCall>[];
    tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
      const MethodChannel('home_widget'),
      (call) async {
        calls.add(call);
        return true;
      },
    );
    HomeWidgetSync.supported = () => true;
    addTearDown(() {
      HomeWidgetSync.supported = () => false;
      tester.binding.defaultBinaryMessenger.setMockMethodCallHandler(
        const MethodChannel('home_widget'),
        null,
      );
    });

    final payload = HomeWidgetPayload.build(now: now, context: context);
    await HomeWidgetSync.instance.push(payload);
    await HomeWidgetSync.instance.push(
      HomeWidgetPayload.build(
        now: now.add(const Duration(seconds: 30)),
        context: context,
      ),
    );

    expect(calls.first.method, 'setAppGroupId');
    expect(calls.first.arguments, {'groupId': HomeWidgetSync.appGroup});
    expect(calls.where((c) => c.method == 'updateWidget'), hasLength(1));
    expect(
      calls.where((c) => c.method == 'updateWidget').single.arguments['ios'],
      'JarvisWidget',
    );
    expect(
      calls
          .where((c) => c.method == 'saveWidgetData')
          .map((c) => c.arguments['id']),
      containsAll(['hero_title', 'needs_count', 'needs_items']),
    );
  });
}
