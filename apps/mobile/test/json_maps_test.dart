import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/json_maps.dart';

void main() {
  test('jsonMaps keeps object rows and skips invalid entries', () {
    expect(
      jsonMaps([
        {'id': '1'},
        'nope',
        {'id': '2', 'nested': true},
        3,
      ]),
      [
        {'id': '1'},
        {'id': '2', 'nested': true},
      ],
    );
    expect(jsonMaps(null), isEmpty);
    expect(jsonMaps('x'), isEmpty);
  });

  test('jsonStrings keeps string entries', () {
    expect(jsonStrings(['a', 1, 'b', null]), ['a', 'b']);
    expect(jsonStrings(null), isEmpty);
  });

  test('jsonString reads non-empty string values', () {
    expect(jsonString({'id': 'abc'}, 'id'), 'abc');
    expect(jsonString({'id': ''}, 'id'), isNull);
    expect(jsonString({'id': 1}, 'id'), isNull);
    expect(jsonString({'name': 'x'}, 'id'), isNull);
  });
}
