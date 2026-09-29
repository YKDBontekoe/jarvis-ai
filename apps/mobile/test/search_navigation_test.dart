import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/search/search_navigation.dart';

void main() {
  test('notification payload maps to typed search routes', () {
    final route = searchRouteFromNotification({
      'routeKind': 'conversation',
      'conversationId': 'abc-123',
    });
    expect(route?.kind, 'conversation');
    expect(route?.parameters['conversationId'], 'abc-123');
  });

  test('channel thread route reads connection and peer', () {
    final route = searchRouteFromNotification({
      'routeKind': 'channel_thread',
      'connectionId': 'conn',
      'peer': '+15551212',
    });
    expect(route?.parameters['connectionId'], 'conn');
    expect(route?.parameters['peer'], '+15551212');
  });

  test('reminder route prefers a linked conversation', () {
    final route = searchRouteFromNotification({
      'routeKind': 'reminder',
      'reminderId': 'r1',
      'conversationId': 'c1',
    });
    expect(route?.kind, 'reminder');
    expect(route?.parameters['conversationId'], 'c1');
    expect(route?.parameters['reminderId'], 'r1');
  });
}
