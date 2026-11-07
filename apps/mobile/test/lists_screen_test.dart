import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/lists/lists_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _item(String id, String text, {bool done = false}) => {
  'id': id,
  'text': text,
  'done': done,
};

Map<String, Object?> _list(String id, String name, List<Map<String, Object?>> items) =>
    {'id': id, 'name': name, 'items': items};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester) async {
    tester.view.physicalSize = const Size(900, 1800);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: ListsScreen(http: http.client())));
    await tester.pumpAndSettle();
  }

  testWidgets('shows each list with open counts and ticked items', (tester) async {
    http.on('GET', '/api/v1/lists', [
      _list('1', 'Shopping', [_item('a', 'Milk'), _item('b', 'Eggs', done: true)]),
      _list('2', 'Packing', [_item('c', 'Passport')]),
    ]);
    await show(tester);

    expect(find.text('Shopping'), findsOneWidget);
    expect(find.text('1 open'), findsNWidgets(2));
    expect(find.text('Milk'), findsOneWidget);
    expect(find.text('Clear 1 ticked'), findsOneWidget);
  });

  testWidgets('ticking an item sends the list name and item text', (tester) async {
    http.on('GET', '/api/v1/lists', [
      _list('1', 'Shopping', [_item('a', 'Milk')]),
    ]);
    http.on('POST', '/api/v1/lists/done', _list('1', 'Shopping', [_item('a', 'Milk', done: true)]));
    await show(tester);

    await tester.tap(find.byType(Checkbox));
    await tester.pumpAndSettle();

    final body = http.sent('POST', '/api/v1/lists/done').single.body as Map;
    expect(body['list'], 'Shopping');
    expect(body['items'], ['Milk']);
    expect(body['done'], true);
  });

  testWidgets('adding an item posts it and an empty entry is ignored', (tester) async {
    http.on('GET', '/api/v1/lists', [
      _list('1', 'Shopping', [_item('a', 'Milk')]),
    ]);
    http.on('POST', '/api/v1/lists/items', _list('1', 'Shopping', [_item('a', 'Milk')]));
    await show(tester);

    await tester.tap(find.byTooltip('Add to Shopping'));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/lists/items'), isEmpty);

    await tester.enterText(find.byType(TextField), 'Bread');
    await tester.tap(find.byTooltip('Add to Shopping'));
    await tester.pumpAndSettle();
    final body = http.sent('POST', '/api/v1/lists/items').single.body as Map;
    expect(body['items'], ['Bread']);
  });

  testWidgets('an empty state offers to start a list', (tester) async {
    http.on('GET', '/api/v1/lists', <Object?>[]);
    await show(tester);

    expect(find.text('No lists yet'), findsOneWidget);
    expect(find.byKey(const Key('lists-new')), findsOneWidget);
  });
}
