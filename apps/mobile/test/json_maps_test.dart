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
}
