bool opensApprovalScreen(String? type) => type == 'approval.required';

bool opensTaskDetails(String? type) =>
    type == 'task.completed' || type == 'task.failed';

bool opensNotificationDetails(String? type) =>
    type == 'reminder.due' ||
    type == 'task.completed' ||
    type == 'task.failed' ||
    type == 'watch.triggered' ||
    type == 'watch.failed';
