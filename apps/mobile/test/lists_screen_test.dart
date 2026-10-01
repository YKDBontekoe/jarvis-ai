import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/home/home_overview.dart';
import 'package:jarvis_mobile/features/lists/list_detail_screen.dart';
import 'package:jarvis_mobile/features/lists/list_models.dart';
import 'package:jarvis_mobile/features/lists/lists_screen.dart';

import 'support/fixture_http.dart';

Map<String, Object?> _item(String id, String text, {bool done = false}) => {
  'id': id,
  'listId': 'l1',
  'text': text,
  'done': done,
  'doneAt': done ? '2026-10-01T18:00:00Z' : null,
  'createdAt': '2026-10-01T17:00:00Z',
  'updatedAt': '2026-10-01T17:00:00Z',
};

Map<String, Object?> _list(
  String id,
  String name,
  String kind,
  List<Map<String, Object?>> items,
) => {
  'id': id,
  'name': name,
  'kind': kind,
  'openCount': items.where((x) => x['done'] != true).length,
  'doneCount': items.where((x) => x['done'] == true).length,
  'items': items,
  'createdAt': '2026-10-01T17:00:00Z',
  'updatedAt': '2026-10-01T18:00:00Z',
};

void main() {
  late FixtureHttp http;

  setUp(() => http = FixtureHttp());

  Future<void> show(WidgetTester tester, Widget child) async {
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(MaterialApp(home: child));
    await tester.pumpAndSettle();
  }

  test('lists parse from the API and summarise progress', () {
    final list = PersonalListData.fromJson(
      _list('l1', 'Boodschappen', 'shopping', [
        _item('a', 'melk'),
        _item('b', 'brood', done: true),
      ]),
    )!;
    expect(list.open.map((x) => x.text), ['melk']);
    expect(list.done.map((x) => x.text), ['brood']);
    expect(listProgressLabel(list), '1 to go');
    expect(listKindLabel('todo'), 'To-do');
    expect(PersonalListData.fromJson({'name': 'no id'}), isNull);
  });

  testWidgets('overview shows each list with progress and open items', (
    tester,
  ) async {
    http.on('GET', '/api/v1/lists', [
      _list('l1', 'Boodschappen', 'shopping', [
        _item('a', 'melk'),
        _item('b', 'brood', done: true),
      ]),
      _list('l2', 'To-do', 'todo', []),
    ]);
    await show(tester, ListsScreen(http: http.client()));

    expect(find.text('Boodschappen'), findsOneWidget);
    expect(find.text('1 to go · 1 of 2 done'), findsOneWidget);
    expect(find.text('melk'), findsOneWidget);
    expect(find.text('To-do'), findsOneWidget);
    expect(find.text('Empty'), findsOneWidget);
  });

  testWidgets('empty overview offers to start a grocery list', (tester) async {
    http.on('GET', '/api/v1/lists', <Object>[]);
    http.on('POST', '/api/v1/lists', _list('l1', 'Groceries', 'shopping', []));
    http.on(
      'GET',
      '/api/v1/lists/l1',
      _list('l1', 'Groceries', 'shopping', []),
    );
    await show(tester, ListsScreen(http: http.client()));

    expect(find.text('No lists yet'), findsOneWidget);
    await tester.tap(find.byKey(const Key('lists-start-shopping')));
    await tester.pumpAndSettle();

    expect(http.sent('POST', '/api/v1/lists').single.body, {
      'name': 'Groceries',
      'kind': 'shopping',
    });
    expect(find.text('Nothing here yet'), findsOneWidget);
  });

  testWidgets('detail adds items and ticks them off', (tester) async {
    final initial = _list('l1', 'Boodschappen', 'shopping', [
      _item('a', 'melk'),
    ]);
    http.on('GET', '/api/v1/lists/l1', initial);
    http.on(
      'POST',
      '/api/v1/lists/l1/items',
      _list('l1', 'Boodschappen', 'shopping', [
        _item('a', 'melk'),
        _item('c', 'kaas'),
      ]),
    );
    http.on(
      'PATCH',
      '/api/v1/lists/l1/items/a',
      _item('a', 'melk', done: true),
    );
    await show(
      tester,
      ListDetailScreen(
        http: http.client(),
        list: PersonalListData.fromJson(initial)!,
      ),
    );

    await tester.enterText(find.byKey(const Key('list-add-field')), 'kaas');
    await tester.pump();
    await tester.tap(find.byKey(const Key('list-add')));
    await tester.pumpAndSettle();
    expect(http.sent('POST', '/api/v1/lists/l1/items').single.body, {
      'items': ['kaas'],
    });
    expect(find.text('kaas'), findsOneWidget);
    expect(find.text('0 of 2 done'), findsOneWidget);

    await tester.tap(find.text('melk'));
    await tester.pumpAndSettle();
    expect(http.sent('PATCH', '/api/v1/lists/l1/items/a').single.body, {
      'done': true,
    });
    expect(find.text('1 of 2 done'), findsOneWidget);
    expect(find.text('Checked off · 1'), findsOneWidget);
  });

  testWidgets('a failed tick is rolled back', (tester) async {
    final initial = _list('l1', 'To-do', 'todo', [
      _item('a', 'bel loodgieter'),
    ]);
    http.on('GET', '/api/v1/lists/l1', initial);
    http.on(
      'PATCH',
      '/api/v1/lists/l1/items/a',
      <String, Object>{},
      status: 500,
    );
    await show(
      tester,
      ListDetailScreen(
        http: http.client(),
        list: PersonalListData.fromJson(initial)!,
      ),
    );

    await tester.tap(find.text('bel loodgieter'));
    await tester.pumpAndSettle();

    expect(find.text('0 of 1 done'), findsOneWidget);
    expect(find.text('Could not update that item.'), findsOneWidget);
  });

  testWidgets('home shows a lists card with open items', (tester) async {
    http.on('GET', '/api/v1/lists', [
      _list('l1', 'Boodschappen', 'shopping', [
        _item('a', 'melk'),
        _item('b', 'eieren'),
      ]),
    ]);
    var opened = false;
    tester.view.physicalSize = const Size(800, 1400);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: HomeOverview(
            http: http.client(),
            mark: const SizedBox(),
            ready: true,
            voiceStarting: false,
            onTalk: null,
            onOpenTasks: () {},
            refreshRevision: 0,
            onOpenLists: () => opened = true,
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(const Key('home-lists')), findsOneWidget);
    expect(find.text('2 open across 1 list'), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-lists')));
    expect(opened, isTrue);
  });
}
