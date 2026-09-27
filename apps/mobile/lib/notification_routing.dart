bool opensApprovalScreen(String? type) => type == 'approval.required';

bool opensDailyBriefing(String? type) => type == 'briefing.daily';

bool opensTaskDetails(String? type) =>
    type == 'task.completed' || type == 'task.failed';

bool opensNotificationDetails(String? type) =>
    type == 'reminder.due' ||
    type == 'reminder.failed' ||
    type == 'task.completed' ||
    type == 'task.failed' ||
    type == 'watch.triggered' ||
    type == 'watch.failed';
