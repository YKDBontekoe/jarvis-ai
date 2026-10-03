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
  'LogExpense': ToolDescription(
    'Logging an expense',
    'Logged an expense',
    PhosphorIconsRegular.wallet,
  ),
  'GetExpenses': ToolDescription(
    'Checking your spending',
    'Checked your spending',
    PhosphorIconsRegular.wallet,
  ),
  'UpdateExpense': ToolDescription(
    'Correcting an expense',
    'Corrected an expense',
    PhosphorIconsRegular.pencilSimple,
  ),
  'DeleteExpense': ToolDescription(
    'Deleting an expense',
    'Deleted an expense',
    PhosphorIconsRegular.trash,
  ),
  'QueryTimeline': ToolDescription(
    'Looking back through your timeline',
    'Looked back through your timeline',
    PhosphorIconsRegular.clockCounterClockwise,
  ),
  'OnThisDay': ToolDescription(
    'Checking this day in earlier years',
    'Checked this day in earlier years',
    PhosphorIconsRegular.calendarBlank,
  ),
  'GetLifeInsights': ToolDescription(
    'Looking for patterns in your life',
    'Looked for patterns in your life',
    PhosphorIconsRegular.chartLine,
  ),
  'CheckInbox': ToolDescription(
    'Checking your inbox',
    'Checked your inbox',
    PhosphorIconsRegular.chatsCircle,
  ),
  'TriageInboxThread': ToolDescription(
    'Reading a conversation',
    'Read a conversation',
    PhosphorIconsRegular.sparkle,
  ),
  'SetInboxState': ToolDescription(
    'Updating your inbox',
    'Updated your inbox',
    PhosphorIconsRegular.checkCircle,
  ),
  'SnoozeInboxThread': ToolDescription(
    'Snoozing a conversation',
    'Snoozed a conversation',
    PhosphorIconsRegular.clock,
  ),
  'TrackInboxItem': ToolDescription(
    'Adding to your inbox',
    'Added to your inbox',
    PhosphorIconsRegular.paperPlaneTilt,
  ),
  'GetCommitments': ToolDescription(
    'Checking your promises',
    'Checked your promises',
    PhosphorIconsRegular.listChecks,
  ),
  'AddCommitment': ToolDescription(
    'Noting a promise',
    'Noted a promise',
    PhosphorIconsRegular.checkCircle,
  ),
  'AcceptCommitment': ToolDescription(
    'Keeping a promise',
    'Kept a promise',
    PhosphorIconsRegular.checkCircle,
  ),
  'SetCommitmentStatus': ToolDescription(
    'Updating a promise',
    'Updated a promise',
    PhosphorIconsRegular.checkCircle,
  ),
  'GetFinanceOverview': ToolDescription(
    'Checking your finances',
    'Checked your finances',
    PhosphorIconsRegular.chartLine,
  ),
  'GetBudgets': ToolDescription(
    'Checking your budgets',
    'Checked your budgets',
    PhosphorIconsRegular.wallet,
  ),
  'SetBudget': ToolDescription(
    'Setting a budget',
    'Set a budget',
    PhosphorIconsRegular.wallet,
  ),
  'RemoveBudget': ToolDescription(
    'Removing a budget',
    'Removed a budget',
    PhosphorIconsRegular.trash,
  ),
  'GetSubscriptions': ToolDescription(
    'Looking at your subscriptions',
    'Looked at your subscriptions',
    PhosphorIconsRegular.repeat,
  ),
  'SetSubscriptionStatus': ToolDescription(
    'Updating a subscription',
    'Updated a subscription',
    PhosphorIconsRegular.repeat,
  ),
  'RemindBeforeCharge': ToolDescription(
    'Setting a charge reminder',
    'Set a charge reminder',
    PhosphorIconsRegular.bell,
  ),
  'ImportBankStatement': ToolDescription(
    'Reading a bank statement',
    'Read a bank statement',
    PhosphorIconsRegular.uploadSimple,
  ),
  'PreviewAutomation': ToolDescription(
    'Previewing an automation',
    'Previewed an automation',
    PhosphorIconsRegular.play,
  ),
  'ListAutomationTemplates': ToolDescription(
    'Looking at automation templates',
    'Looked at automation templates',
    PhosphorIconsRegular.lightning,
  ),
  'CreateAutomationFromTemplate': ToolDescription(
    'Creating an automation',
    'Created an automation',
    PhosphorIconsRegular.lightning,
  ),
  'SearchLibrary': ToolDescription(
    'Searching your library',
    'Searched your library',
    PhosphorIconsRegular.bookOpen,
  ),
  'GetLibraryItem': ToolDescription(
    'Opening a saved item',
    'Opened a saved item',
    PhosphorIconsRegular.bookOpen,
  ),
  'SaveToLibrary': ToolDescription(
    'Saving to your library',
    'Saved to your library',
    PhosphorIconsRegular.bookmarkSimple,
  ),
  'ClipUrlToLibrary': ToolDescription(
    'Saving a web page',
    'Saved a web page',
    PhosphorIconsRegular.globeSimple,
  ),
  'StartDeepResearch': ToolDescription(
    'Starting deep research',
    'Started deep research',
    PhosphorIconsRegular.atom,
  ),
  'GetLibraryDigest': ToolDescription(
    'Summing up your library',
    'Summed up your library',
    PhosphorIconsRegular.bookOpen,
  ),
  'GetDueFlashcards': ToolDescription(
    'Picking flashcards',
    'Picked flashcards',
    PhosphorIconsRegular.graduationCap,
  ),
  'GradeFlashcard': ToolDescription(
    'Scheduling a flashcard',
    'Scheduled a flashcard',
    PhosphorIconsRegular.graduationCap,
  ),
  'AddFlashcards': ToolDescription(
    'Adding flashcards',
    'Added flashcards',
    PhosphorIconsRegular.graduationCap,
  ),
  'GetCurrentMode': ToolDescription(
    'Checking your mode',
    'Checked your mode',
    PhosphorIconsRegular.moon,
  ),
  'SetMode': ToolDescription(
    'Switching mode',
    'Switched mode',
    PhosphorIconsRegular.moon,
  ),
  'PlanMission': ToolDescription(
    'Planning a mission',
    'Planned a mission',
    PhosphorIconsRegular.flowArrow,
  ),
  'RunMission': ToolDescription(
    'Starting a mission',
    'Started a mission',
    PhosphorIconsRegular.play,
  ),
  'GetMissions': ToolDescription(
    'Checking your missions',
    'Checked your missions',
    PhosphorIconsRegular.flowArrow,
  ),
  'PauseOrResumeMission': ToolDescription(
    'Pausing a mission',
    'Changed a mission',
    PhosphorIconsRegular.pauseCircle,
  ),
  'CancelMission': ToolDescription(
    'Cancelling a mission',
    'Cancelled a mission',
    PhosphorIconsRegular.stopCircle,
  ),
  'PostToBlackboard': ToolDescription(
    'Sharing a note with the crew',
    'Shared a note with the crew',
    PhosphorIconsRegular.notePencil,
  ),
  'ReadBlackboard': ToolDescription(
    'Reading the crew notes',
    'Read the crew notes',
    PhosphorIconsRegular.notePencil,
  ),
  'ListWhatsAppChats': ToolDescription(
    'Checking your WhatsApp',
    'Checked your WhatsApp',
    PhosphorIconsRegular.whatsappLogo,
  ),
  'ReadWhatsAppChat': ToolDescription(
    'Reading a WhatsApp chat',
    'Read a WhatsApp chat',
    PhosphorIconsRegular.whatsappLogo,
  ),
  'SearchWhatsAppMessages': ToolDescription(
    'Searching your WhatsApp',
    'Searched your WhatsApp',
    PhosphorIconsRegular.magnifyingGlass,
  ),
  'SendWhatsAppMessage': ToolDescription(
    'Sending a WhatsApp message',
    'Sent a WhatsApp message',
    PhosphorIconsRegular.paperPlaneTilt,
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
  'SnoozeReminder': ToolDescription(
    'Snoozing a reminder',
    'Snoozed a reminder',
    PhosphorIconsRegular.clockCounterClockwise,
  ),
  'CompleteReminder': ToolDescription(
    'Marking a reminder done',
    'Marked a reminder done',
    PhosphorIconsRegular.checkCircle,
  ),
  'GetPeople': ToolDescription(
    'Looking up your people',
    'Looked up your people',
    PhosphorIconsRegular.users,
  ),
  'SavePerson': ToolDescription(
    'Saving to your people',
    'Saved to your people',
    PhosphorIconsRegular.userPlus,
  ),
  'LogContact': ToolDescription(
    'Noting that you talked',
    'Noted that you talked',
    PhosphorIconsRegular.handWaving,
  ),
  'RemovePerson': ToolDescription(
    'Removing a person',
    'Removed a person',
    PhosphorIconsRegular.trash,
  ),
  'automation_channel_message': ToolDescription(
    'Sending a message for an automation',
    'Sent a message for an automation',
    PhosphorIconsRegular.paperPlaneTilt,
  ),
  'automation_agent_run': ToolDescription(
    'Starting an agent task for an automation',
    'Started an agent task for an automation',
    PhosphorIconsRegular.lightning,
  ),
  'GetHabits': ToolDescription(
    'Checking your habits',
    'Checked your habits',
    PhosphorIconsRegular.target,
  ),
  'CheckInHabits': ToolDescription(
    'Checking off a habit',
    'Checked off a habit',
    PhosphorIconsRegular.checkCircle,
  ),
  'CreateHabit': ToolDescription(
    'Starting a habit',
    'Started a habit',
    PhosphorIconsRegular.target,
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
  'WebSearch': ToolDescription(
    'Searching the web',
    'Searched the web',
    PhosphorIconsRegular.magnifyingGlass,
  ),
  'RunCommand': ToolDescription(
    'Running a command',
    'Ran a command',
    PhosphorIconsRegular.terminalWindow,
  ),
  'EditFiles': ToolDescription(
    'Editing files',
    'Edited files',
    PhosphorIconsRegular.pencilSimple,
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
