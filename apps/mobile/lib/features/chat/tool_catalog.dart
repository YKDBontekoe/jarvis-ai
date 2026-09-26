import 'package:flutter/material.dart';

/// User-facing wording for Jarvis tools. Unknown tools (for example MCP tools)
/// fall back to a humanized version of their name.
class ToolDescription {
  const ToolDescription(this.active, this.done, this.icon);

  final String active;
  final String done;
  final IconData icon;

  String get failed => '$active failed';
}

const _catalog = <String, ToolDescription>{
  'GetCurrentTime': ToolDescription(
    'Checking the time',
    'Checked the time',
    Icons.schedule_rounded,
  ),
  'CreateReminder': ToolDescription(
    'Scheduling a reminder',
    'Scheduled a reminder',
    Icons.alarm_add_rounded,
  ),
  'ListReminders': ToolDescription(
    'Checking your reminders',
    'Checked your reminders',
    Icons.alarm_rounded,
  ),
  'CancelReminder': ToolDescription(
    'Cancelling a reminder',
    'Cancelled a reminder',
    Icons.alarm_off_rounded,
  ),
  'SearchMemory': ToolDescription(
    'Searching memory',
    'Searched memory',
    Icons.psychology_outlined,
  ),
  'Remember': ToolDescription(
    'Saving to memory',
    'Saved to memory',
    Icons.bookmark_add_outlined,
  ),
  'ForgetMemory': ToolDescription(
    'Forgetting a memory',
    'Forgot a memory',
    Icons.bookmark_remove_outlined,
  ),
  'SearchFiles': ToolDescription(
    'Searching your files',
    'Searched your files',
    Icons.find_in_page_outlined,
  ),
  'ListFiles': ToolDescription(
    'Checking your files',
    'Checked your files',
    Icons.folder_open_outlined,
  ),
  'CreateTask': ToolDescription(
    'Starting a background task',
    'Started a background task',
    Icons.rocket_launch_outlined,
  ),
  'ListTasks': ToolDescription(
    'Checking your tasks',
    'Checked your tasks',
    Icons.checklist_rounded,
  ),
  'CancelTask': ToolDescription(
    'Cancelling a task',
    'Cancelled a task',
    Icons.cancel_outlined,
  ),
  'CreateConditionWatch': ToolDescription(
    'Creating a watch',
    'Created a watch',
    Icons.monitor_heart_outlined,
  ),
  'ListConditionWatches': ToolDescription(
    'Checking your watches',
    'Checked your watches',
    Icons.monitor_heart_outlined,
  ),
  'CancelConditionWatch': ToolDescription(
    'Stopping a watch',
    'Stopped a watch',
    Icons.heart_broken_outlined,
  ),
  'ListMcpServers': ToolDescription(
    'Checking integrations',
    'Checked integrations',
    Icons.hub_outlined,
  ),
  'DiscoverMcpServerTools': ToolDescription(
    'Discovering integration tools',
    'Discovered integration tools',
    Icons.travel_explore_rounded,
  ),
  'AddMcpServer': ToolDescription(
    'Adding an integration',
    'Added an integration',
    Icons.add_link_rounded,
  ),
  'UpdateMcpServer': ToolDescription(
    'Updating an integration',
    'Updated an integration',
    Icons.link_rounded,
  ),
  'RemoveMcpServer': ToolDescription(
    'Removing an integration',
    'Removed an integration',
    Icons.link_off_rounded,
  ),
  'RunCodingTask': ToolDescription(
    'Running a coding task',
    'Ran a coding task',
    Icons.code_rounded,
  ),
};

String humanizeToolName(String tool) {
  final cleaned = tool
      .replaceAll(RegExp(r'Async$'), '')
      .replaceAll(RegExp(r'[_\-.]+'), ' ')
      .replaceAllMapped(
        RegExp(r'([a-z0-9])([A-Z])'),
        (match) => '${match[1]} ${match[2]}',
      )
      .trim()
      .toLowerCase();
  if (cleaned.isEmpty) return 'a tool';
  return cleaned;
}

ToolDescription describeTool(String tool) {
  final known = _catalog[tool.replaceAll(RegExp(r'Async$'), '')];
  if (known != null) return known;
  final name = humanizeToolName(tool);
  if (name.startsWith('browser ')) {
    return const ToolDescription(
      'Using the browser',
      'Used the browser',
      Icons.language_rounded,
    );
  }
  return ToolDescription('Using $name', 'Used $name', Icons.extension_outlined);
}
