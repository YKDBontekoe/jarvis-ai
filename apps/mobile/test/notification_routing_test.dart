import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/notification_routing.dart';

void main() {
  test('watch notifications open their linked item', () {
    expect(opensNotificationDetails('watch.triggered'), isTrue);
    expect(opensNotificationDetails('watch.failed'), isTrue);
    expect(opensNotificationDetails('reminder.due'), isTrue);
    expect(opensNotificationDetails('briefing.ready'), isFalse);
  });

  test('approval notifications stay on the approvals screen', () {
    expect(opensApprovalScreen('approval.required'), isTrue);
    expect(opensTaskDetails('task.completed'), isTrue);
    expect(opensTaskDetails('task.failed'), isTrue);
    expect(opensNotificationDetails('task.failed'), isTrue);
    expect(opensApprovalScreen('watch.triggered'), isFalse);
  });

  test('daily briefing notifications open the briefing screen', () {
    expect(opensDailyBriefing('briefing.daily'), isTrue);
    expect(opensDailyBriefing('reminder.due'), isFalse);
    expect(opensNotificationDetails('briefing.daily'), isFalse);
  });
}
