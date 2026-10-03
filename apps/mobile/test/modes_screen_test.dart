import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/modes/ambient_screen.dart';
import 'package:jarvis_mobile/features/modes/modes_models.dart';
import 'package:jarvis_mobile/features/modes/modes_screen.dart';

import 'support/fixture_http.dart';

final _now = DateTime(2026, 10, 5, 14, 5);

Map<String, Object?> _mode(String id, String label, String notifications,
        {bool customised = false}) =>
    {
      'id': id,
      'label': label,
      'description': '$label mode.',
      'notifications': notifications,
      'tone': null,
      'customised': customised,
    };

Map<String, Object?> _state({String mode = 'normal', String source = 'default'}) => {
  'mode': mode,
  'label': mode == 'focus' ? 'Focus' : 'Normal',
  'source': source,
  'reason': source == 'manual' ? 'You switched on Focus.' : 'Nothing special is going on.',
  'until': null,
  'notifications': mode == 'focus' ? 'important' : 'all',
  'auto': true,
  'sleepStart': '23:00',
  'sleepEnd': '07:00',
  'modes': [
    _mode('normal', 'Normal', 'all'),
    _mode('focus', 'Focus', 'important'),
    _mode('sleep', 'Sleep', 'none'),
  ],
};

void main() {
  late FixtureHttp http;

  setUp(() {
    http = FixtureHttp();
    http.on('GET', '/api/v1/modes', _state());
  });

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 2400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  test('state parses modes and knows when it was chosen by hand', () {
    final state = ModeStateData.fromJson(_state(mode: 'focus', source: 'manual'))!;
    expect(state.manual, isTrue);
    expect(state.modes, hasLength(3));
    expect(notificationLevelLabel('none'), 'No pushes');
    expect(ModeStateData.fromJson({'mode': 'x'}), isNull);
  });

  testWidgets('shows the current mode and switches for two hours', (tester) async {
    http.on('PUT', '/api/v1/modes/active', _state(mode: 'focus', source: 'manual'));
    await show(tester, ModesScreen(http: http.client(), now: _now));

    expect(find.text('Normal'), findsWidgets);
    expect(find.text('Nothing special is going on.'), findsOneWidget);
    await tester.tap(find.byKey(const Key('mode-switch-focus')));
    await tester.pumpAndSettle();

    final put = http.sent('PUT', '/api/v1/modes/active').single.body as Map;
    expect(put['mode'], 'focus');
    expect(put['minutes'], 120);
    expect(find.text('You switched on Focus.'), findsOneWidget);
    expect(find.byKey(const Key('mode-auto')), findsOneWidget);
  });

  testWidgets('returning to automatic sends auto', (tester) async {
    http.on('GET', '/api/v1/modes', _state(mode: 'focus', source: 'manual'));
    http.on('PUT', '/api/v1/modes/active', _state());
    await show(tester, ModesScreen(http: http.client(), now: _now));

    await tester.tap(find.byKey(const Key('mode-auto')));
    await tester.pumpAndSettle();

    expect((http.sent('PUT', '/api/v1/modes/active').single.body as Map)['mode'], 'auto');
  });

  testWidgets('a mode policy can be changed from its bell', (tester) async {
    http.on('PUT', '/api/v1/modes/focus/policy', _state());
    await show(tester, ModesScreen(http: http.client(), now: _now));

    await tester.tap(find.byKey(const Key('mode-policy-focus')));
    await tester.pumpAndSettle();
    await tester.tap(find.byKey(const Key('policy-none')));
    await tester.pumpAndSettle();

    expect((http.sent('PUT', '/api/v1/modes/focus/policy').single.body as Map)['notifications'], 'none');
  });

  testWidgets('automatic modes can be switched off', (tester) async {
    http.on('PUT', '/api/v1/modes/settings', _state());
    await show(tester, ModesScreen(http: http.client(), now: _now));

    await tester.tap(find.byKey(const Key('modes-auto-switch')));
    await tester.pumpAndSettle();

    expect((http.sent('PUT', '/api/v1/modes/settings').single.body as Map)['auto'], false);
  });

  testWidgets('the ambient display shows the time, mode and what is next', (tester) async {
    http.on('GET', '/api/v1/home', {
      'reminders': [
        {'id': 'r1', 'title': 'Call mum', 'dueAt': '2026-10-05T16:00:00'},
      ],
      'approvals': [
        {'id': 'a1', 'toolName': 'x'},
      ],
      'calendar': {
        'connected': true,
        'events': [
          {'title': 'Dentist', 'startAt': '2026-10-05T15:30:00'},
        ],
      },
      'device': {'batteryPercent': 64},
    });
    await show(tester, AmbientScreen(http: http.client(), now: _now));

    expect(find.text('14:05'), findsOneWidget);
    expect(find.text('Normal'), findsOneWidget);
    expect(find.textContaining('Dentist'), findsOneWidget);
    expect(find.textContaining('Call mum'), findsOneWidget);
    expect(find.text('1 waiting for your approval'), findsOneWidget);
    expect(find.text('Phone battery 64%'), findsOneWidget);
  });
}
