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
  'ListMcpConnections': ToolDescription(
    'Checking live integrations',
    'Checked live integrations',
    PhosphorIconsRegular.plugsConnected,
  ),
  'ListHostMcpServers': ToolDescription(
    'Checking host integrations',
    'Checked host integrations',
    PhosphorIconsRegular.plugsConnected,
  ),
  'ListMcpServerTools': ToolDescription(
    'Looking at an integration’s tools',
    'Looked at an integration’s tools',
    PhosphorIconsRegular.plugsConnected,
  ),
  'DiscoverMcpServerTools': ToolDescription(
    'Discovering integration tools',
    'Discovered integration tools',
    PhosphorIconsRegular.globeSimple,
  ),
  'RequestMcpAuthorization': ToolDescription(
    'Asking you to authorize an integration',
    'Asked you to authorize an integration',
    PhosphorIconsRegular.key,
  ),
  'OfferMcpSetup': ToolDescription(
    'Opening integration setup',
    'Opened integration setup',
    PhosphorIconsRegular.plugsConnected,
  ),
  'AskForMcpCredential': ToolDescription(
    'Asking for a token in chat',
    'Asked for a token in chat',
    PhosphorIconsRegular.key,
  ),
  'InstallIntegrationPack': ToolDescription(
    'Installing a guided integration',
    'Installed a guided integration',
    PhosphorIconsRegular.plugsConnected,
  ),
  'AddMcpServer': ToolDescription(
    'Adding an integration',
    'Added an integration',
    PhosphorIconsRegular.linkSimple,
  ),
  'AddMcpStdioServer': ToolDescription(
    'Installing a local integration',
    'Installed a local integration',
    PhosphorIconsRegular.linkSimple,
  ),
  'UpdateMcpServer': ToolDescription(
    'Updating an integration',
    'Updated an integration',
    PhosphorIconsRegular.linkSimple,
  ),
  'SetMcpServerEnabled': ToolDescription(
    'Updating an integration',
    'Updated an integration',
    PhosphorIconsRegular.plugsConnected,
  ),
  'SetMcpServerTools': ToolDescription(
    'Choosing integration tools',
    'Chose integration tools',
    PhosphorIconsRegular.listChecks,
  ),
  'InvokeMcpTool': ToolDescription(
    'Using an integration',
    'Used an integration',
    PhosphorIconsRegular.plugsConnected,
  ),
  'ReadMcpResource': ToolDescription(
    'Reading an integration resource',
    'Read an integration resource',
    PhosphorIconsRegular.fileText,
  ),
  'GetMcpPrompt': ToolDescription(
    'Fetching an integration prompt',
    'Fetched an integration prompt',
    PhosphorIconsRegular.chatText,
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
  'LoadSkill': ToolDescription(
    'Loading a skill',
    'Loaded a skill',
    PhosphorIconsRegular.magicWand,
  ),
  'ListSkills': ToolDescription(
    'Checking skills',
    'Checked skills',
    PhosphorIconsRegular.magicWand,
  ),
  'SaveSkill': ToolDescription(
    'Saving a skill',
    'Saved a skill',
    PhosphorIconsRegular.magicWand,
  ),
  'LearnPreference': ToolDescription(
    'Learning a preference',
    'Learned a preference',
    PhosphorIconsRegular.userCircle,
  ),
  'QueryKnowledgeGraph': ToolDescription(
    'Checking the knowledge graph',
    'Checked the knowledge graph',
    PhosphorIconsRegular.graph,
  ),
  'RenderUi': ToolDescription(
    'Building a card for you',
    'Built a card for you',
    PhosphorIconsRegular.appWindow,
  ),
  'ListRemoteAgents': ToolDescription(
    'Checking connected agents',
    'Checked connected agents',
    PhosphorIconsRegular.robot,
  ),
  'DelegateToAgent': ToolDescription(
    'Asking another agent',
    'Asked another agent',
    PhosphorIconsRegular.shareNetwork,
  ),
  'ListDevices': ToolDescription(
    'Checking your devices',
    'Checked your devices',
    PhosphorIconsRegular.deviceMobile,
  ),
  'GetDeviceLocation': ToolDescription(
    'Reading your location',
    'Read your location',
    PhosphorIconsRegular.mapPin,
  ),
  'ReadDeviceClipboard': ToolDescription(
    'Reading the clipboard',
    'Read the clipboard',
    PhosphorIconsRegular.clipboardText,
  ),
  'OpenUrlOnDevice': ToolDescription(
    'Opening a link on your device',
    'Opened a link on your device',
    PhosphorIconsRegular.globe,
  ),
  'NotifyDevice': ToolDescription(
    'Sending a device notification',
    'Sent a device notification',
    PhosphorIconsRegular.bell,
  ),
  'GetDeviceBattery': ToolDescription(
    'Checking battery',
    'Checked battery',
    PhosphorIconsRegular.batteryFull,
  ),
  'BrowseTheWeb': ToolDescription(
    'Using the isolated browser',
    'Used the isolated browser',
    PhosphorIconsRegular.browser,
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
