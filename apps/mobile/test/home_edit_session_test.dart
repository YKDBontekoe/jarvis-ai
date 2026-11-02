import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/features/tiles/tile_layout.dart';
import 'package:jarvis_mobile/features/tiles/tile_models.dart';
import 'package:shared_preferences/shared_preferences.dart';

List<String> ids(TileLayoutController controller) => [
  for (final item in controller.layout) item.id,
];

void main() {
  late SharedPreferences prefs;
  late TileLayoutController layout;

  Future<TileLayoutController> open() async {
    final controller = TileLayoutController.device();
    await controller.open();
    return controller;
  }

  setUp(() async {
    SharedPreferences.setMockInitialValues({});
    prefs = await SharedPreferences.getInstance();
    layout = await open();
    layout.reset();
    // Start every test from a known, saved layout.
    layout
      ..unpin('x')
      ..pin('tasks', TileSize.square)
      ..pin('memory', TileSize.icon)
      ..pin('journal', TileSize.icon);
    await Future<void>.delayed(Duration.zero);
  });

  List<String> saved() => [
    for (final item in decodeTileLayout(prefs.getString('home.tiles.v1'))!)
      item.id,
  ];

  test('changes made while editing are not saved until Done', () async {
    final before = saved();
    layout.beginEdit();
    layout.unpin('memory');
    layout.move(0, 1);
    await Future<void>.delayed(Duration.zero);

    expect(layout.contains('memory'), isFalse);
    expect(saved(), before, reason: 'nothing is persisted mid-edit');

    layout.commitEdit();
    await Future<void>.delayed(Duration.zero);
    expect(layout.editing, isFalse);
    expect(saved(), ids(layout));
    expect(saved(), isNot(before));
  });

  test('Cancel puts back the layout from before the edit', () async {
    final before = ids(layout);
    final sizeBefore = layout.sizeOf('tasks');
    layout.beginEdit();
    layout.unpin('memory');
    layout.cycleSize('tasks');
    layout.move(0, 1);

    layout.cancelEdit();
    expect(ids(layout), before);
    expect(layout.sizeOf('tasks'), sizeBefore);
    expect(layout.editing, isFalse);
    expect(layout.canUndo, isFalse);
  });

  test('Undo takes back one move, resize or removal at a time', () {
    layout.beginEdit();
    final start = ids(layout);
    final startSize = layout.sizeOf('tasks');

    layout.cycleSize('tasks');
    layout.unpin('memory');
    layout.move(0, 1);
    expect(layout.canUndo, isTrue);

    layout.undo(); // the move
    expect(ids(layout), isNot(start));
    expect(layout.contains('memory'), isFalse);
    layout.undo(); // the removal
    expect(layout.contains('memory'), isTrue);
    expect(ids(layout), start);
    layout.undo(); // the resize
    expect(layout.sizeOf('tasks'), startSize);
    expect(layout.canUndo, isFalse);
  });

  test('Undo does nothing outside an editing session', () {
    layout.unpin('memory');
    expect(layout.canUndo, isFalse);
    layout.undo();
    expect(layout.contains('memory'), isFalse);
  });

  test(
    'outside a session changes are saved straight away, as before',
    () async {
      layout.unpin('journal');
      await Future<void>.delayed(Duration.zero);
      expect(saved().contains('journal'), isFalse);
    },
  );

  test('Done with nothing changed leaves the saved layout alone', () async {
    final before = prefs.getString('home.tiles.v1');
    layout.beginEdit();
    layout.commitEdit();
    await Future<void>.delayed(Duration.zero);
    expect(prefs.getString('home.tiles.v1'), before);
  });

  test('the history is bounded', () {
    layout.beginEdit();
    for (var i = 0; i < TileLayoutController.maxUndo + 20; i++) {
      layout.cycleSize('tasks');
    }
    var steps = 0;
    while (layout.canUndo) {
      layout.undo();
      steps++;
    }
    expect(steps, TileLayoutController.maxUndo);
  });
}
