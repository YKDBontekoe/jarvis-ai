import 'package:flutter/material.dart';
import '../../ui/phosphor_icons.dart';

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
    PhosphorIconsRegular.clock,
  ),
  'CreateReminder': ToolDescription(
    'Scheduling a reminder',
    'Scheduled a reminder',
    PhosphorIconsRegular.alarm,
  ),
  'ListReminders': ToolDescription(
    'Checking your reminders',
    'Checked your reminders',
    PhosphorIconsRegular.alarm,
  ),
  'CancelReminder': ToolDescription(
    'Cancelling a reminder',
    'Cancelled a reminder',
    PhosphorIconsRegular.bellSlash,
  ),
  'SearchMemory': ToolDescription(
    'Searching memory',
    'Searched memory',
    PhosphorIconsRegular.brain,
  ),
  'Remember': ToolDescription(
    'Saving to memory',
    'Saved to memory',
    PhosphorIconsRegular.bookmarkSimple,
  ),
  'ForgetMemory': ToolDescription(
    'Forgetting a memory',
    'Forgot a memory',
    PhosphorIconsRegular.bookmarkSimple,
  ),
  'SearchFiles': ToolDescription(
    'Searching your files',
    'Searched your files',
    PhosphorIconsRegular.magnifyingGlass,
  ),
  'ListFiles': ToolDescription(
    'Checking your files',
    'Checked your files',
    PhosphorIconsRegular.folderOpen,
  ),
  'CreateTask': ToolDescription(
    'Starting a background task',
    'Started a background task',
    PhosphorIconsRegular.rocketLaunch,
  ),
  'ListTasks': ToolDescription(
    'Checking your tasks',
    'Checked your tasks',
    PhosphorIconsRegular.listChecks,
  ),
  'CancelTask': ToolDescription(
    'Cancelling a task',
    'Cancelled a task',
    PhosphorIconsRegular.xCircle,
  ),
  'CreateConditionWatch': ToolDescription(
    'Creating a watch',
    'Created a watch',
    PhosphorIconsRegular.pulse,
  ),
  'ListConditionWatches': ToolDescription(
    'Checking your watches',
    'Checked your watches',
    PhosphorIconsRegular.pulse,
  ),
  'CancelConditionWatch': ToolDescription(
    'Stopping a watch',
    'Stopped a watch',
    PhosphorIconsRegular.pulse,
  ),
  'ListMcpServers': ToolDescription(
    'Checking integrations',
    'Checked integrations',
    PhosphorIconsRegular.plugsConnected,
  ),
  'DiscoverMcpServerTools': ToolDescription(
    'Discovering integration tools',
    'Discovered integration tools',
    PhosphorIconsRegular.globeSimple,
  ),
  'AddMcpServer': ToolDescription(
    'Adding an integration',
    'Added an integration',
    PhosphorIconsRegular.linkSimple,
  ),
  'UpdateMcpServer': ToolDescription(
    'Updating an integration',
    'Updated an integration',
    PhosphorIconsRegular.linkSimple,
  ),
  'RemoveMcpServer': ToolDescription(
    'Removing an integration',
    'Removed an integration',
    PhosphorIconsRegular.linkBreak,
  ),
  'RunCodingTask': ToolDescription(
    'Running a coding task',
    'Ran a coding task',
    PhosphorIconsRegular.code,
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
      PhosphorIconsRegular.globeSimple,
    );
  }
  return ToolDescription(
    'Using $name',
    'Used $name',
    PhosphorIconsRegular.puzzlePiece,
  );
}
