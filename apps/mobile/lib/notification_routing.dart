bool opensApprovalScreen(String? type) => type == 'approval.required';

bool opensTaskDetails(String? type) => type == 'task.completed';

bool opensNotificationDetails(String? type) =>
    type == 'reminder.due' ||
    type == 'task.completed' ||
    type == 'watch.triggered' ||
    type == 'watch.failed';
