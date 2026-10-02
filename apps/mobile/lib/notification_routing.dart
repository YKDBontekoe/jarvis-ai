bool opensApprovalScreen(String? type) =>
    type == 'approval.required' || type == 'automation.approval';

bool opensCodingRun(String? type) =>
    type == 'coding.pr.ready' || type == 'coding.run.ready';

bool opensDailyBriefing(String? type) => type == 'briefing.daily';

bool opensWeeklyReview(String? type) => type == 'briefing.weekly';
bool opensHabits(String? type) => type == 'habit.checkin';

bool opensTaskDetails(String? type) =>
    type == 'task.completed' || type == 'task.failed';

bool opensSearchRoute(Map<String, dynamic> data) =>
    data['routeKind'] is String && (data['routeKind'] as String).isNotEmpty;

bool opensNotificationDetails(String? type) =>
    type == 'reminder.due' ||
    type == 'reminder.failed' ||
    type == 'task.completed' ||
    type == 'task.failed' ||
    type == 'watch.triggered' ||
    type == 'watch.failed';
