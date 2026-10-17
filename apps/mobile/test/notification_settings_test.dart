import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/notifications/notification_settings_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> category(
  String id, {
  bool locked = false,
  String setting = 'on',
  String outcome = 'push',
  List<String> held = const [],
}) => {
  'id': id,
  'title': 'Title $id',
  'description': 'About $id',
  'locked': locked,
  'setting': setting,
  'now': {'outcome': outcome, 'reason': 'x'},
  'modes': [
    for (final m in held)
      {'mode': m.toLowerCase(), 'label': m, 'outcome': 'held'},
  ],
};

Map<String, Object?> view({String updates = 'on'}) => {
  'currentMode': 'normal',
  'currentModeLabel': 'Normal',
  'summary': updates == 'on'
      ? 'Everything reaches your phone right away.'
      : '1 kind of notification stays in the app only, by your choice.',
  'categories': [
    category('approvals', locked: true),
    category(
      'updates',
      setting: updates,
      outcome: updates == 'on' ? 'push' : 'app_only',
      held: ['Sleep'],
    ),
  ],
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/notifications/preferences', view());
  });

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(800, 2000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(home: NotificationSettingsScreen(http: http.client())),
    );
    await tester.pumpAndSettle();
  }

  testWidgets(
    'locked kinds show a lock instead of a switch and modes that hold a kind are named',
    (tester) async {
      await show(tester);

      expect(
        find.text('Everything reaches your phone right away.'),
        findsOneWidget,
      );
      expect(find.byKey(const Key('notif-switch-approvals')), findsNothing);
      expect(find.byKey(const Key('notif-locked')), findsOneWidget);
      expect(find.text('Held in Sleep.'), findsOneWidget);
      expect(find.textContaining('whether or not it buzzes'), findsOneWidget);
    },
  );

  testWidgets('turning a kind off sends quiet and shows the new summary', (
    tester,
  ) async {
    http.on(
      'PUT',
      '/api/v1/notifications/preferences/updates',
      view(updates: 'quiet'),
    );
    await show(tester);

    await tester.tap(find.byKey(const Key('notif-switch-updates')));
    await tester.pumpAndSettle();

    expect(
      (http.sent('PUT', '/api/v1/notifications/preferences/updates').single.body
          as Map)['push'],
      'quiet',
    );
    expect(
      find.text(
        '1 kind of notification stays in the app only, by your choice.',
      ),
      findsOneWidget,
    );
    expect(find.text('IN THE APP ONLY'), findsOneWidget);
  });

  testWidgets('a refused change says why and keeps the screen as it was', (
    tester,
  ) async {
    http.on('PUT', '/api/v1/notifications/preferences/updates', {
      'detail': 'Nope.',
    }, status: 400);
    await show(tester);

    await tester.tap(find.byKey(const Key('notif-switch-updates')));
    await tester.pumpAndSettle();

    expect(find.text('Nope.'), findsOneWidget);
    expect(
      find.text('Everything reaches your phone right away.'),
      findsOneWidget,
    );
  });
}
