import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/shell/incoming_routes.dart';

void main() {
  late DateTime now;
  late IncomingRoutes routes;
  late int notified;

  setUp(() {
    now = DateTime(2026, 11, 2, 9);
    routes = IncomingRoutes(clock: () => now);
    notified = 0;
    routes.addListener(() => notified++);
  });

  test('a request waits until it is taken, and is handed out once', () {
    expect(routes.offer('widget', 'ask'), isTrue);
    expect(routes.hasPending, isTrue);
    expect(notified, 1);

    final route = routes.take();
    expect(route?.destination, 'ask');
    expect(route?.source, 'widget');
    expect(routes.hasPending, isFalse);
    expect(routes.take(), isNull);
  });

  test('the same request offered twice while waiting counts once', () {
    expect(routes.offer('notification', 'x', id: 'notification:n1'), isTrue);
    expect(routes.offer('notification', 'x', id: 'notification:n1'), isFalse);
    expect(notified, 1);
  });

  test('a launch message plus an opened-app event opens only once', () {
    routes.offer('notification', 'x', id: 'notification:n1');
    routes.take();
    // The second report of the same notification a moment later.
    now = now.add(const Duration(seconds: 2));
    expect(routes.offer('notification', 'x', id: 'notification:n1'), isFalse);
    expect(routes.hasPending, isFalse);
  });

  test('the same destination can be opened again later', () {
    routes.offer('shortcut', 'voice');
    routes.take();
    now = now.add(const Duration(seconds: 6));
    expect(routes.offer('shortcut', 'voice'), isTrue);
  });

  test('a different request replaces one that is still waiting', () {
    routes.offer('widget', 'chats');
    routes.offer('shortcut', 'voice');
    expect(routes.take()?.destination, 'voice');
  });

  test('a source can carry its own way of opening', () {
    var ran = 0;
    routes.offer('notification', 'notification', id: 'n', run: () => ran++);
    routes.take()!.run!();
    expect(ran, 1);
  });

  test('clear forgets what is waiting and what was handled', () {
    routes.offer('widget', 'ask');
    routes.clear();
    expect(routes.hasPending, isFalse);
    routes.offer('widget', 'ask');
    routes.take();
    routes.clear();
    expect(routes.offer('widget', 'ask'), isTrue);
  });
}
