import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/json_maps.dart';
import 'package:jarvis_mobile/oidc_issuer.dart';

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

  test('firstProblemMessage reads ASP.NET detail and error lists', () {
    expect(
      firstProblemMessage({
        'detail': 'Title must contain 1 to 200 characters.',
      }),
      'Title must contain 1 to 200 characters.',
    );
    expect(
      firstProblemMessage({
        'errors': {
          'timeZoneId': ['Unknown time zone.'],
        },
      }),
      'Unknown time zone.',
    );
    expect(firstProblemMessage('nope'), isNull);
    expect(firstProblemMessage(null), isNull);
  });

  test('asJsonString, asJsonInt, and asJsonBool ignore the wrong JSON types', () {
    expect(asJsonString('ok'), 'ok');
    expect(asJsonString(''), '');
    expect(asJsonString(1), isNull);
    expect(asJsonInt(3), 3);
    expect(asJsonInt(3.2), 3);
    expect(asJsonInt('3'), 0);
    expect(asJsonBool(true), isTrue);
    expect(asJsonBool('true'), isFalse);
  });

  test('oidcRedirectOrigin drops query, fragment, and credentials', () {
    expect(
      oidcRedirectOrigin(
        Uri.parse('https://app.example.com/chat/?code=abc&state=xyz#frag'),
      ).toString(),
      'https://app.example.com/chat/',
    );
    expect(
      oidcRedirectOrigin(Uri.parse('http://localhost:5082/?error=access_denied'))
          .toString(),
      'http://localhost:5082/',
    );
    expect(
      Uri.parse('https://app.example.com/?code=abc')
          .replace(query: null, fragment: null)
          .hasQuery,
      isTrue,
    );
  });

  test('normalizeOidcIssuer trims trailing slashes and compares issuers', () {
    expect(normalizeOidcIssuer('https://idp.example.com/'), 'https://idp.example.com');
    expect(
      normalizeOidcIssuer('https://idp.example.com/realms/foo/'),
      'https://idp.example.com/realms/foo',
    );
    expect(
      oidcIssuersMatch('https://idp.example.com', 'https://idp.example.com/'),
      isTrue,
    );
    expect(
      oidcIssuersMatch(
        'https://idp.example.com/realms/foo',
        'https://idp.example.com/realms/bar',
      ),
      isFalse,
    );
  });

  test('activeValidUntil keeps a future TTL and clears expired ones', () {
    final now = DateTime.utc(2026, 9, 27, 12);
    expect(
      activeValidUntil({'validUntil': '2026-12-01T00:00:00.000Z'}, now),
      '2026-12-01T00:00:00.000Z',
    );
    expect(
      activeValidUntil({'validUntil': '2026-01-01T00:00:00.000Z'}, now),
      isNull,
    );
    expect(activeValidUntil({'validUntil': null}, now), isNull);
    expect(activeValidUntil({}, now), isNull);
  });
}
