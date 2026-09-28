import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/http_urls.dart';

void main() {
  test('parseHttpUrl accepts http and https URLs', () {
    expect(parseHttpUrl('https://example.com/docs')?.host, 'example.com');
    expect(parseHttpUrl('http://example.com')?.scheme, 'http');
    expect(parseHttpUrl('https://example.com/path?q=1#frag')?.hasQuery, isTrue);
  });

  test('parseHttpUrl rejects blank, relative, and non-http schemes', () {
    expect(parseHttpUrl(null), isNull);
    expect(parseHttpUrl(''), isNull);
    expect(parseHttpUrl('   '), isNull);
    expect(parseHttpUrl('/relative'), isNull);
    expect(parseHttpUrl('example.com'), isNull);
    expect(parseHttpUrl('file:///tmp/secret'), isNull);
    expect(parseHttpUrl('javascript:alert(1)'), isNull);
    expect(parseHttpUrl('ftp://example.com/file'), isNull);
    expect(parseHttpUrl('data:text/html,hi'), isNull);
  });

  test('isHttpUrl is scheme-only', () {
    expect(isHttpUrl(Uri.parse('https://example.com')), isTrue);
    expect(isHttpUrl(Uri.parse('http://localhost:8080')), isTrue);
    expect(isHttpUrl(Uri.parse('file:///tmp/secret')), isFalse);
    expect(isHttpUrl(Uri.parse('javascript:alert(1)')), isFalse);
  });
}
