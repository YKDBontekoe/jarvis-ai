import 'package:dio/dio.dart';

import '../../json_maps.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/tool_catalog.dart' show humanizeToolName;
import '../expenses/expense_models.dart';
import '../home/next_up.dart';
import '../usage/usage_screen.dart' show formatTokenCount;
import 'tile_models.dart';

const _icon = TileSize.icon;
const _strip = TileSize.strip;
const _square = TileSize.square;
const _wide = TileSize.wide;
const _large = TileSize.large;

/// Every feature that can be a tile, in the order Everything lists them.
/// Chats and Settings read live app state, so they have no loader.
final List<TileSpec> tileSpecs = [
  // Plan
  const TileSpec(
    id: 'today',
    description: 'Your calendar, reminders and plan for today.',
    name: 'Today',
    icon: PhosphorIconsRegular.sunHorizon,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square, _wide, _large],
    destination: 'today',
    load: _today,
    fallback: 'Your day, planned',
  ),
  const TileSpec(
    id: 'tasks',
    description: 'Track work you have asked Jarvis to do.',
    name: 'Tasks',
    icon: PhosphorIconsRegular.listChecks,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square, _wide, _large],
    destination: 'tasks',
    load: _tasks,
    fallback: 'Work Jarvis does for you',
  ),
  const TileSpec(
    id: 'reminders',
    description: 'Set reminders for a time or place.',
    name: 'Reminders',
    icon: PhosphorIconsRegular.bell,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'reminders',
    load: _reminders,
    fallback: 'Nothing due',
  ),
  const TileSpec(
    id: 'habits',
    description: 'Check in on routines and follow your streaks.',
    name: 'Habits',
    icon: PhosphorIconsRegular.target,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'habits',
    load: _habits,
    fallback: 'Build a streak',
  ),
  const TileSpec(
    id: 'journal',
    description: 'Write about your day and record your mood.',
    name: 'Journal',
    icon: PhosphorIconsRegular.pencilSimple,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square],
    destination: 'journal',
    load: _journal,
    fallback: 'How was today?',
  ),
  const TileSpec(
    id: 'decisions',
    description:
        'Log a call with how sure you are, answer later, and see how well calibrated you are.',
    name: 'Decisions',
    icon: PhosphorIconsRegular.hourglassMedium,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square],
    destination: 'decisions',
    load: _decisions,
    fallback: 'Track a call',
  ),
  const TileSpec(
    id: 'weekly-review',
    description: 'Look back at your week and mood trends.',
    name: 'Weekly review',
    icon: PhosphorIconsRegular.chartLine,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square],
    destination: 'weekly-review',
    fallback: 'Your week, summarised',
  ),
  const TileSpec(
    id: 'briefing',
    description: 'Read your daily summary and priorities.',
    name: 'Daily briefing',
    icon: PhosphorIconsRegular.sparkle,
    category: TileCategory.plan,
    sizes: [_icon, _strip, _square],
    destination: 'briefing',
    fallback: 'A short note each morning',
  ),
  // Talk
  const TileSpec(
    id: 'chats',
    description: 'Your Jarvis and WhatsApp conversations.',
    name: 'Chats',
    icon: PhosphorIconsRegular.chatCircle,
    category: TileCategory.talk,
    sizes: [_icon, _strip, _square, _wide, _large],
    destination: 'chats',
    fallback: 'Jarvis and WhatsApp',
  ),
  const TileSpec(
    id: 'voice',
    description: 'Have a hands-free conversation with Jarvis.',
    name: 'Voice',
    icon: PhosphorIconsRegular.waveform,
    category: TileCategory.talk,
    sizes: [_icon, _strip],
    destination: 'voice',
    fallback: 'Talk hands-free',
  ),
  const TileSpec(
    id: 'whatsapp',
    description: 'Manage linked accounts and chats Jarvis can read.',
    name: 'WhatsApp',
    icon: PhosphorIconsRegular.whatsappLogo,
    category: TileCategory.talk,
    sizes: [_icon, _strip],
    destination: 'whatsapp',
    fallback: 'Accounts and the chats Jarvis reads',
  ),
  const TileSpec(
    id: 'inbox',
    description: 'Review messages that need your attention.',
    name: 'Inbox',
    icon: PhosphorIconsRegular.chatsCircle,
    category: TileCategory.talk,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'inbox',
    load: _inbox,
    fallback: 'Mail that needs you',
  ),
  const TileSpec(
    id: 'approvals',
    description: 'Review sensitive actions before Jarvis proceeds.',
    name: 'Approvals',
    icon: PhosphorIconsRegular.shieldCheck,
    category: TileCategory.talk,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'approvals',
    load: _approvals,
    fallback: 'Nothing waiting',
  ),
  const TileSpec(
    id: 'channels',
    description: 'Connect messaging accounts to Jarvis.',
    name: 'Channels',
    icon: PhosphorIconsRegular.broadcast,
    category: TileCategory.talk,
    sizes: [_icon, _strip],
    destination: 'channels',
    load: _channels,
    fallback: 'WhatsApp and Signal',
  ),
  // Know
  const TileSpec(
    id: 'memory',
    description: 'Review and edit what Jarvis remembers about you.',
    name: 'Memory',
    icon: PhosphorIconsRegular.notebook,
    category: TileCategory.know,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'memory',
    load: _memory,
    fallback: 'What Jarvis knows about you',
  ),
  const TileSpec(
    id: 'library',
    description: 'Find articles, links and notes you have saved.',
    name: 'Library',
    icon: PhosphorIconsRegular.bookOpen,
    category: TileCategory.know,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'library',
    load: _library,
    fallback: 'Things you saved',
  ),
  const TileSpec(
    id: 'people',
    description: 'Keep track of people, birthdays and follow-ups.',
    name: 'People',
    icon: PhosphorIconsRegular.users,
    category: TileCategory.know,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'people',
    load: _people,
    fallback: 'Birthdays and follow-ups',
  ),
  const TileSpec(
    id: 'timeline',
    description: 'Browse your activity and events over time.',
    name: 'Timeline',
    icon: PhosphorIconsRegular.clockCounterClockwise,
    category: TileCategory.know,
    sizes: [_icon, _strip, _square],
    destination: 'timeline',
    fallback: 'Your life, in order',
  ),
  const TileSpec(
    id: 'files',
    description: 'Manage documents Jarvis can search and use.',
    name: 'Files',
    icon: PhosphorIconsRegular.folderOpen,
    category: TileCategory.know,
    sizes: [_icon, _strip, _square],
    destination: 'files',
    load: _files,
    fallback: 'Documents Jarvis can search',
  ),
  const TileSpec(
    id: 'graph',
    description: 'Explore connections between people and ideas.',
    name: 'Knowledge graph',
    icon: PhosphorIconsRegular.graph,
    category: TileCategory.know,
    sizes: [_icon, _strip],
    destination: 'graph',
    fallback: 'How things connect',
  ),
  const TileSpec(
    id: 'projects',
    description: 'Keep related chats, files and tasks together.',
    name: 'Projects',
    icon: PhosphorIconsRegular.folderSimple,
    category: TileCategory.know,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'projects',
    load: _projects,
    fallback: 'Group chats, files and tasks',
  ),
  // Money
  const TileSpec(
    id: 'expenses',
    description: 'Log purchases and see where your money goes.',
    name: 'Expenses',
    icon: PhosphorIconsRegular.wallet,
    category: TileCategory.money,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'expenses',
    load: _expenses,
    fallback: 'What you spend',
  ),
  const TileSpec(
    id: 'finance',
    description: 'Review budgets and recurring subscriptions.',
    name: 'Finance',
    icon: PhosphorIconsRegular.chartBar,
    category: TileCategory.money,
    sizes: [_icon, _strip, _square],
    destination: 'finance',
    load: _finance,
    fallback: 'Budgets and subscriptions',
  ),
  // Automate
  const TileSpec(
    id: 'missions',
    description: 'Plan larger goals with a team of agents.',
    name: 'Missions',
    icon: PhosphorIconsRegular.flowArrow,
    category: TileCategory.automate,
    sizes: [_icon, _strip, _square, _wide, _large],
    destination: 'missions',
    load: _missions,
    fallback: 'Bigger goals, in steps',
  ),
  const TileSpec(
    id: 'automations',
    description: 'Set up actions that repeat on a schedule.',
    name: 'Automations',
    icon: PhosphorIconsRegular.lightning,
    category: TileCategory.automate,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'automations',
    load: _automations,
    fallback: 'Rules that run on their own',
  ),
  const TileSpec(
    id: 'watches',
    description: 'Ask Jarvis to monitor changes and notify you.',
    name: 'Watches',
    icon: PhosphorIconsRegular.eye,
    category: TileCategory.automate,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'watches',
    load: _watches,
    fallback: 'Jarvis keeps an eye out',
  ),
  const TileSpec(
    id: 'modes',
    description: 'Adjust Jarvis’s behavior and notifications.',
    name: 'Modes',
    icon: PhosphorIconsRegular.circleHalf,
    category: TileCategory.automate,
    sizes: [_icon, _strip],
    destination: 'modes',
    fallback: 'Change how Jarvis behaves',
  ),
  const TileSpec(
    id: 'coding',
    description: 'Follow coding runs and review their progress.',
    name: 'Coding',
    icon: PhosphorIconsRegular.code,
    category: TileCategory.automate,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'coding',
    load: _coding,
    fallback: 'Review coding runs',
  ),
  const TileSpec(
    id: 'agents',
    description: 'Connect other assistants that Jarvis can work with.',
    name: 'Agents',
    icon: PhosphorIconsRegular.robot,
    category: TileCategory.automate,
    sizes: [_icon, _strip],
    destination: 'agents',
    load: _agents,
    fallback: 'Other agents you work with',
  ),
  // System
  const TileSpec(
    id: 'integrations',
    description: 'Connect apps and services to Jarvis.',
    name: 'Integrations',
    icon: PhosphorIconsRegular.plugsConnected,
    category: TileCategory.system,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'integrations',
    load: _integrations,
    fallback: 'Connect your apps',
  ),
  const TileSpec(
    id: 'skills',
    description: 'Manage the instructions and skills Jarvis uses.',
    name: 'Skills',
    icon: PhosphorIconsRegular.graduationCap,
    category: TileCategory.system,
    sizes: [_icon, _strip],
    destination: 'skills',
    load: _skills,
    fallback: 'What Jarvis can do',
  ),
  const TileSpec(
    id: 'persona',
    description: 'Choose how Jarvis speaks and responds to you.',
    name: 'Persona',
    icon: PhosphorIconsRegular.userCircle,
    category: TileCategory.system,
    sizes: [_icon, _strip],
    destination: 'persona',
    fallback: 'How Jarvis talks to you',
  ),
  const TileSpec(
    id: 'profiles',
    description: 'Create assistants with separate context and settings.',
    name: 'Profiles',
    icon: PhosphorIconsRegular.identificationCard,
    category: TileCategory.system,
    sizes: [_icon, _strip],
    destination: 'profiles',
    load: _profiles,
    fallback: 'Separate assistants',
  ),
  const TileSpec(
    id: 'learning',
    description: 'Manage how Jarvis learns your preferences.',
    name: 'Learning',
    icon: PhosphorIconsRegular.brain,
    category: TileCategory.system,
    sizes: [_icon, _strip],
    destination: 'learning',
    fallback: 'What Jarvis picks up',
  ),
  const TileSpec(
    id: 'devices',
    description: 'Manage your devices and their capabilities.',
    name: 'Devices',
    icon: PhosphorIconsRegular.deviceMobile,
    category: TileCategory.system,
    sizes: [_icon, _strip, _square],
    destination: 'devices',
    load: _devices,
    fallback: 'This device',
  ),
  const TileSpec(
    id: 'usage',
    description: 'See model usage, token counts and costs.',
    name: 'Usage',
    icon: PhosphorIconsRegular.pulse,
    category: TileCategory.system,
    sizes: [_icon, _strip, _square],
    destination: 'usage',
    load: _usage,
    fallback: 'Tokens and cost',
  ),
  const TileSpec(
    id: 'audit',
    description: 'Inspect the actions Jarvis has taken.',
    name: 'Audit log',
    icon: PhosphorIconsRegular.scroll,
    category: TileCategory.system,
    sizes: [_icon, _strip],
    destination: 'audit',
    fallback: 'What Jarvis did',
  ),
  const TileSpec(
    id: 'notifications',
    description: 'Read updates and alerts from Jarvis.',
    name: 'Notifications',
    icon: PhosphorIconsRegular.bellRinging,
    category: TileCategory.system,
    sizes: [_icon, _strip, _square, _wide],
    destination: 'notifications',
    load: _notifications,
    fallback: 'Nothing new',
  ),
  const TileSpec(
    id: 'settings',
    description: 'Appearance, connection and account preferences.',
    name: 'Settings',
    icon: PhosphorIconsRegular.gearSix,
    category: TileCategory.system,
    sizes: [_icon, _strip],
    destination: 'settings',
    fallback: 'Connection and account',
  ),
];

final Map<String, TileSpec> _byId = {for (final s in tileSpecs) s.id: s};

TileSpec? tileSpecFor(String id) => _byId[id];

// ---------------------------------------------------------------- loaders

const _maxRows = 8;

List<Map<String, dynamic>> _items(Response<dynamic> response, [String? key]) =>
    key == null
    ? jsonMaps(response.data)
    : jsonMaps(jsonObject(response.data)?[key]);

String? _first(Map<String, dynamic> map, List<String> keys) {
  for (final key in keys) {
    final value = asJsonString(map[key]);
    if (value != null && value.trim().isNotEmpty) return value.trim();
  }
  return null;
}

String _plural(int count, String one, String many) => count == 1 ? one : many;

/// "18:00", "tomorrow 09:00" or "12 Oct" for a due time.
String shortWhen(DateTime time, DateTime now) {
  final today = DateTime(now.year, now.month, now.day);
  final day = DateTime(time.year, time.month, time.day);
  final gap = day.difference(today).inDays;
  if (gap == 0) return clockTime(time);
  if (gap == 1) return 'Tomorrow';
  if (gap > 1 && gap < 7) {
    return const [
      'Mon',
      'Tue',
      'Wed',
      'Thu',
      'Fri',
      'Sat',
      'Sun',
    ][time.weekday - 1];
  }
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${time.day} ${months[time.month - 1]}';
}

Future<TileData?> _today(TileEnv env) async {
  final briefing = await env.briefing();
  if (briefing == null) return null;
  final now = env.clock;
  final items = upcomingItems(briefing, now);
  if (items.isEmpty) {
    return TileData(
      stat: '0',
      unit: 'events',
      subtitle: 'A free day',
      visual: TileVisual.timeline,
      timeline: _timeline(const [], now),
    );
  }
  final next = items.first;
  // The tile is about today: what comes later only shows as "next".
  final today = items.where((item) => _sameDay(item.start, now)).length;
  return TileData(
    stat: '$today',
    unit: _plural(today, 'thing', 'things'),
    subtitle: today > 0
        ? '${next.title} · ${clockTime(next.start)}'
        : 'Nothing else today',
    focusLabel: today > 0 ? next.title : null,
    countdownTo: today > 0 ? next.start : null,
    visual: TileVisual.timeline,
    timeline: _timeline(items, now),
    rows: [
      for (final item in items.take(_maxRows))
        TileRow(
          item.title,
          meta: shortWhen(item.start, now),
          attention: identical(item, next),
        ),
    ],
  );
}

bool _sameDay(DateTime a, DateTime b) =>
    a.year == b.year && a.month == b.month && a.day == b.day;

/// The rest of today as a strip: what is on it, and where the clock is.
TileTimeline _timeline(List<UpNext> items, DateTime now) {
  final midnight = DateTime(now.year, now.month, now.day);
  final nowMinute = now.difference(midnight).inMinutes;
  final spans = <TileSpan>[
    for (final item in items)
      if (item.start.difference(midnight).inMinutes case final start
          when item.start.year == now.year &&
              item.start.month == now.month &&
              item.start.day == now.day)
        TileSpan(
          start,
          item.reminder ? start + 10 : start + 45,
          item.title,
          reminder: item.reminder,
        ),
  ];
  var from = nowMinute - 60;
  var to = nowMinute + 360;
  for (final span in spans) {
    if (span.startMinute < from) from = span.startMinute - 30;
    if (span.endMinute > to) to = span.endMinute + 60;
  }
  // Keep six hours within the day, even late at night. Otherwise the
  // lower clamp bound exceeds midnight and Today falls back to a placeholder.
  from = (from ~/ 60 * 60).clamp(0, 1080);
  to = ((to + 59) ~/ 60 * 60).clamp(from + 360, 1440);
  return TileTimeline(
    startMinute: from,
    endMinute: to,
    nowMinute: nowMinute,
    spans: spans,
  );
}

const _activeTaskStatuses = {'queued', 'running', 'waiting', 'needs_approval'};

Future<TileData?> _tasks(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/tasks');
  final active = [
    for (final task in _items(response))
      if (_activeTaskStatuses.contains(asJsonString(task['status']))) task,
  ];
  if (active.isEmpty) {
    return const TileData(stat: '0', unit: 'active', subtitle: 'All quiet');
  }
  String label(String? status) => switch (status) {
    'needs_approval' => 'Needs you',
    'running' => 'Running',
    'waiting' => 'Waiting',
    _ => 'Queued',
  };
  final waiting = active.where((t) => t['status'] == 'needs_approval').length;
  final needs = waiting > 0;
  return TileData(
    stat: '${active.length}',
    unit: 'active',
    // Say why the tile is marked rather than leave a bare dot.
    subtitle: needs
        ? '$waiting ${_plural(waiting, 'needs', 'need')} you'
        : _first(active.first, ['title']),
    attention: needs,
    rows: [
      for (final task in active.take(_maxRows))
        TileRow(
          _first(task, ['title']) ?? 'Task',
          meta: label(asJsonString(task['status'])),
          attention: task['status'] == 'needs_approval',
        ),
    ],
  );
}

Future<TileData?> _reminders(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/reminders');
  final now = env.clock;
  final pending =
      [
        for (final item in _items(response))
          if (asJsonString(item['status']) == 'pending') item,
      ]..sort((a, b) {
        final x = jsonDate(a['dueAt']) ?? DateTime(9999);
        final y = jsonDate(b['dueAt']) ?? DateTime(9999);
        return x.compareTo(y);
      });
  if (pending.isEmpty) {
    return const TileData(stat: '0', unit: 'upcoming', subtitle: 'Nothing due');
  }
  final today = DateTime(now.year, now.month, now.day);
  final dueToday = pending.where((item) {
    final due = jsonDate(item['dueAt'], local: true);
    return due != null && due.isBefore(today.add(const Duration(days: 1)));
  }).length;
  String? when(Map<String, dynamic> item) {
    final due = jsonDate(item['dueAt'], local: true);
    return due == null ? null : shortWhen(due, now);
  }

  const done = TileAction(
    'done',
    'Done',
    icon: PhosphorIconsRegular.check,
    primary: true,
  );
  const snooze = TileAction(
    'snooze',
    '+10 min',
    icon: PhosphorIconsRegular.clock,
  );
  final first = pending.first;
  return TileData(
    stat: '${dueToday > 0 ? dueToday : pending.length}',
    unit: dueToday > 0 ? 'today' : 'upcoming',
    subtitle: '${_first(first, ['title'])} · ${when(first) ?? ''}',
    attention: dueToday > 0,
    focusId: asJsonString(first['id']),
    focusLabel: _first(first, ['title']),
    countdownTo: jsonDate(first['dueAt'], local: true),
    actions: const [done, snooze],
    rows: [
      for (final item in pending.take(_maxRows))
        TileRow(
          _first(item, ['title']) ?? 'Reminder',
          meta: when(item),
          id: asJsonString(item['id']),
          actions: const [done],
        ),
    ],
  );
}

Future<TileData?> _habits(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/habits');
  final habits = _items(response, 'habits');
  if (habits.isEmpty) {
    return const TileData(subtitle: 'Add your first habit');
  }
  bool doneToday(Map<String, dynamic> habit) =>
      asJsonBool(jsonObject(habit['stats'])?['doneToday']);
  final done = habits.where(doneToday).length;
  final open = [
    for (final habit in habits)
      if (!doneToday(habit)) habit,
  ];
  const check = TileAction(
    'check',
    'Check in',
    icon: PhosphorIconsRegular.check,
    primary: true,
  );
  const uncheck = TileAction(
    'uncheck',
    'Undo',
    icon: PhosphorIconsRegular.check,
  );
  return TileData(
    stat: '$done/${habits.length}',
    unit: 'done',
    subtitle: open.isEmpty
        ? 'All done today'
        : '${_first(open.first, ['name'])} left',
    attention: open.isNotEmpty && done == 0,
    progress: done / habits.length,
    visual: TileVisual.ring,
    focusId: open.isEmpty ? null : asJsonString(open.first['id']),
    focusLabel: open.isEmpty ? null : _first(open.first, ['name']),
    actions: open.isEmpty ? const [] : const [check],
    rows: [
      for (final habit in habits.take(_maxRows))
        TileRow(
          _first(habit, ['name']) ?? 'Habit',
          meta: doneToday(habit)
              ? 'Done'
              : '${asJsonInt(jsonObject(habit['stats'])?['currentStreak'])} day streak',
          attention: !doneToday(habit),
          id: asJsonString(habit['id']),
          done: doneToday(habit),
          actions: [doneToday(habit) ? uncheck : check],
        ),
    ],
  );
}

Future<TileData?> _journal(TileEnv env) async {
  final response = await env.http.get<dynamic>(
    '/api/v1/journal/summary',
    queryParameters: const {'days': 30},
  );
  final data = jsonObject(response.data);
  final streak = asJsonInt(data?['currentStreak']);
  return TileData(
    stat: '$streak',
    unit: 'day streak',
    subtitle: streak == 0 ? 'Write today’s entry' : 'How was today?',
  );
}

Future<TileData?> _decisions(TileEnv env) async {
  final response = await env.http.get<dynamic>(
    '/api/v1/decisions',
    queryParameters: const {'status': 'due'},
  );
  final due = _items(response);
  if (due.isEmpty) {
    return const TileData(
      stat: '0',
      unit: 'to answer',
      subtitle: 'Nothing waiting',
    );
  }
  return TileData(
    stat: '${due.length}',
    unit: 'to answer',
    subtitle: _first(due.first, ['title']),
    attention: true,
    rows: [
      for (final decision in due.take(_maxRows))
        TileRow(
          _first(decision, ['title']) ?? 'Decision',
          meta: _first(decision, ['prediction']),
          id: asJsonString(decision['id']),
        ),
    ],
  );
}

Future<TileData?> _inbox(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/inbox');
  final threads = _items(response, 'threads');
  if (threads.isEmpty) {
    return const TileData(stat: '0', unit: 'to triage', subtitle: 'Inbox zero');
  }
  return TileData(
    stat: '${threads.length}',
    unit: 'to triage',
    subtitle: _first(threads.first, ['subject', 'title']),
    attention: true,
    rows: [
      for (final thread in threads.take(_maxRows))
        TileRow(
          _first(thread, ['subject', 'title']) ?? 'Message',
          meta: _first(thread, ['from', 'sender']),
        ),
    ],
  );
}

Future<TileData?> _approvals(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/approvals');
  final pending = _items(response);
  if (pending.isEmpty) {
    return const TileData(
      stat: '0',
      unit: 'waiting',
      subtitle: 'Nothing waiting',
    );
  }
  String label(Map<String, dynamic> item) {
    final name = humanizeToolName(asJsonString(item['toolName']) ?? 'tool');
    return name[0].toUpperCase() + name.substring(1);
  }

  const deny = TileAction('deny', 'Decline', icon: PhosphorIconsRegular.x);
  const review = TileAction(
    'review',
    'Review',
    icon: PhosphorIconsRegular.arrowUpRight,
    primary: true,
  );
  return TileData(
    stat: '${pending.length}',
    unit: 'waiting',
    subtitle: label(pending.first),
    attention: true,
    focusId: asJsonString(pending.first['id']),
    focusLabel: label(pending.first),
    actions: const [deny, review],
    rows: [
      for (final item in pending.take(_maxRows))
        TileRow(
          label(item),
          meta: 'Review',
          attention: true,
          id: asJsonString(item['id']),
          actions: const [deny],
        ),
    ],
  );
}

Future<TileData?> _channels(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/channels');
  final channels = _items(response);
  return TileData(
    stat: '${channels.length}',
    unit: 'linked',
    subtitle: channels.isEmpty
        ? 'Link WhatsApp or Signal'
        : 'WhatsApp and Signal',
  );
}

Future<TileData?> _memory(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/memory');
  final memories = _items(response);
  memories.sort(
    (a, b) => (asJsonBool(b['isPinned']) ? 1 : 0).compareTo(
      asJsonBool(a['isPinned']) ? 1 : 0,
    ),
  );
  return TileData(
    stat: '${memories.length}',
    unit: _plural(memories.length, 'memory', 'memories'),
    subtitle: memories.isEmpty ? null : _first(memories.first, ['content']),
    rows: [
      for (final memory in memories.take(_maxRows))
        TileRow(_first(memory, ['content']) ?? 'Memory'),
    ],
  );
}

Future<TileData?> _library(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/library');
  final items = _items(response, 'items');
  return TileData(
    stat: '${items.length}',
    unit: 'saved',
    subtitle: items.isEmpty ? null : _first(items.first, ['title', 'name']),
    rows: [
      for (final item in items.take(_maxRows))
        TileRow(_first(item, ['title', 'name']) ?? 'Item'),
    ],
  );
}

Future<TileData?> _people(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/people');
  final people = jsonMaps(response.data);
  final due = [
    for (final person in people)
      if (asJsonBool(person['contactDue'])) person,
  ];
  final soon = [
    for (final person in people)
      if (person['daysUntilBirthday'] is num &&
          asJsonInt(person['daysUntilBirthday']) <= 14)
        person,
  ];
  final focus = [...due, ...soon];
  return TileData(
    stat: '${focus.isEmpty ? people.length : due.length}',
    unit: focus.isEmpty ? 'people' : 'to follow up',
    subtitle: focus.isEmpty ? null : '${_first(focus.first, ['name'])}',
    attention: due.isNotEmpty,
    rows: [
      for (final person in (focus.isEmpty ? people : focus).take(_maxRows))
        TileRow(
          _first(person, ['name']) ?? 'Person',
          meta: asJsonBool(person['contactDue'])
              ? 'Catch up'
              : person['daysUntilBirthday'] is num &&
                    asJsonInt(person['daysUntilBirthday']) <= 14
              ? 'Birthday in ${asJsonInt(person['daysUntilBirthday'])} d'
              : null,
          attention: asJsonBool(person['contactDue']),
        ),
    ],
  );
}

Future<TileData?> _files(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/files');
  final files = _items(response);
  return TileData(
    stat: '${files.length}',
    unit: _plural(files.length, 'file', 'files'),
    subtitle: files.isEmpty ? null : _first(files.first, ['name', 'fileName']),
  );
}

Future<TileData?> _projects(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/projects');
  final projects = _items(response);
  return TileData(
    stat: '${projects.length}',
    unit: _plural(projects.length, 'project', 'projects'),
    subtitle: projects.isEmpty ? null : _first(projects.first, ['name']),
    rows: [
      for (final project in projects.take(_maxRows))
        TileRow(_first(project, ['name']) ?? 'Project'),
    ],
  );
}

Future<TileData?> _expenses(TileEnv env) async {
  final now = env.clock;
  final response = await env.http.get<dynamic>(
    '/api/v1/expenses',
    queryParameters: {'month': monthKey(now.year, now.month)},
  );
  final data = ExpenseMonthData.fromJson(response.data);
  if (data == null) return null;
  final change = data.change;
  const weekdays = ['Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat', 'Sun'];
  final perDay = {
    for (final day in data.days)
      DateTime(day.date.year, day.date.month, day.date.day): day.total,
  };
  final bars = [
    for (var back = 6; back >= 0; back--)
      if (DateTime(now.year, now.month, now.day - back) case final day)
        TileBar(
          weekdays[day.weekday - 1][0],
          perDay[day] ?? 0,
          '${weekdays[day.weekday - 1]} · ${formatMoney(perDay[day] ?? 0, data.currency, cents: false)}',
        ),
  ];
  return TileData(
    stat: formatMoney(data.total, data.currency, cents: false),
    unit: 'this month',
    subtitle: data.isEmpty
        ? 'Nothing logged yet'
        : change == null
        ? '${data.count} ${_plural(data.count, 'expense', 'expenses')}'
        : '${change >= 0 ? '+' : '−'}${(change.abs() * 100).round()}% vs last month',
    visual: bars.any((bar) => bar.value > 0)
        ? TileVisual.bars
        : TileVisual.none,
    bars: bars,
    rows: [
      for (final merchant in data.topMerchants.take(_maxRows))
        TileRow(
          merchant.merchant,
          meta: formatMoney(merchant.total, data.currency),
        ),
    ],
  );
}

Future<TileData?> _finance(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/finance/overview');
  final map = jsonObject(response.data);
  if (map == null) return null;
  final alerts = jsonMaps(map['alerts']);
  final subscriptions = jsonMaps(map['subscriptions']);
  final currency = asJsonString(map['currency']) ?? 'EUR';
  final forecast = jsonObject(map['forecast']);
  final projected = forecast?['projectedTotal'];
  return TileData(
    stat: projected is num
        ? formatMoney(projected.toDouble(), currency, cents: false)
        : '${subscriptions.length}',
    unit: projected is num ? 'expected' : 'subscriptions',
    subtitle: alerts.isEmpty
        ? '${subscriptions.length} ${_plural(subscriptions.length, 'subscription', 'subscriptions')}'
        : '${alerts.length} ${_plural(alerts.length, 'alert', 'alerts')}',
    attention: alerts.isNotEmpty,
  );
}

Future<TileData?> _missions(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/missions');
  final all = _items(response);
  final active = [
    for (final mission in all)
      if (const {
        'ready',
        'running',
        'paused',
      }.contains(asJsonString(mission['status'])))
        mission,
  ];
  String label(String? status) => switch (status) {
    'running' => 'Running',
    'paused' => 'Paused',
    'ready' => 'Ready',
    'completed' => 'Done',
    _ => status ?? '',
  };
  return TileData(
    stat: '${active.length}',
    unit: 'active',
    subtitle: active.isEmpty
        ? 'No active missions'
        : _first(active.first, ['title']),
    rows: [
      for (final mission in (active.isEmpty ? all : active).take(_maxRows))
        TileRow(
          _first(mission, ['title']) ?? 'Mission',
          meta: label(asJsonString(mission['status'])),
        ),
    ],
  );
}

Future<TileData?> _automations(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/automations');
  final rules = _items(response);
  final enabled = rules.where((rule) => asJsonBool(rule['enabled'], true));
  return TileData(
    stat: '${enabled.length}',
    unit: 'active',
    subtitle: rules.isEmpty
        ? 'Set up your first rule'
        : _first(rules.first, ['name', 'title']),
    rows: [
      for (final rule in rules.take(_maxRows))
        TileRow(
          _first(rule, ['name', 'title']) ?? 'Automation',
          meta: asJsonBool(rule['enabled'], true) ? 'On' : 'Off',
        ),
    ],
  );
}

Future<TileData?> _watches(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/watches');
  final watches = _items(response);
  return TileData(
    stat: '${watches.length}',
    unit: 'watching',
    subtitle: watches.isEmpty ? null : _first(watches.first, ['title', 'name']),
    rows: [
      for (final watch in watches.take(_maxRows))
        TileRow(_first(watch, ['title', 'name']) ?? 'Watch'),
    ],
  );
}

Future<TileData?> _coding(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/coding/runs');
  final runs = _items(response);
  final failed = runs.where((r) => r['status'] == 'failed').length;
  return TileData(
    stat: '${runs.length}',
    unit: _plural(runs.length, 'run', 'runs'),
    subtitle: runs.isEmpty
        ? 'No coding runs yet'
        : _first(runs.first, ['title', 'prompt', 'task']),
    attention: failed > 0,
    rows: [
      for (final run in runs.take(_maxRows))
        TileRow(
          _first(run, ['title', 'prompt', 'task']) ?? 'Run',
          meta: _first(run, ['status']),
          attention: run['status'] == 'failed',
        ),
    ],
  );
}

Future<TileData?> _agents(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/agents');
  final agents = _items(response);
  return TileData(
    stat: '${agents.length}',
    unit: _plural(agents.length, 'agent', 'agents'),
  );
}

Future<TileData?> _integrations(TileEnv env) async {
  final response = await env.http.get<dynamic>(
    '/api/v1/integrations/connections',
  );
  final connections = _items(response);
  return TileData(
    stat: '${connections.length}',
    unit: _plural(connections.length, 'app', 'apps'),
    subtitle: connections.isEmpty
        ? 'Connect your first app'
        : _first(connections.first, ['name', 'displayName', 'provider']),
    rows: [
      for (final connection in connections.take(_maxRows))
        TileRow(
          _first(connection, ['name', 'displayName', 'provider']) ?? 'App',
          meta: 'Connected',
        ),
    ],
  );
}

Future<TileData?> _skills(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/skills');
  final skills = _items(response);
  return TileData(
    stat: '${skills.length}',
    unit: _plural(skills.length, 'skill', 'skills'),
  );
}

Future<TileData?> _profiles(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/profiles');
  final profiles = _items(response);
  return TileData(
    stat: '${profiles.length}',
    unit: _plural(profiles.length, 'profile', 'profiles'),
  );
}

Future<TileData?> _devices(TileEnv env) async {
  final briefing = await env.briefing();
  final device = jsonObject(briefing?['device']);
  if (device == null) return null;
  final battery = device['batteryPercent'];
  return TileData(
    stat: battery is num ? '${battery.round()}%' : null,
    unit: asJsonBool(device['charging']) ? 'charging' : 'battery',
    subtitle: [
      if (asJsonBool(device['charging'])) 'Charging',
      if (asJsonBool(device['hasLocation'])) 'Location available',
    ].join(' · '),
    visual: battery is num ? TileVisual.ring : TileVisual.none,
    progress: battery is num ? (battery / 100).clamp(0, 1).toDouble() : null,
  );
}

Future<TileData?> _usage(TileEnv env) async {
  final response = await env.http.get<dynamic>(
    '/api/v1/usage',
    queryParameters: const {'period': '7d'},
  );
  final data = jsonObject(response.data);
  if (data == null) return null;
  final codex = jsonObject(data['codex']) ?? const {};
  final openRouter = jsonObject(data['openRouter']) ?? const {};
  final tokens =
      asJsonInt(codex['totalTokens']) + asJsonInt(openRouter['totalTokens']);
  final personalization = jsonObject(data['personalization']) ?? const {};
  return TileData(
    stat: formatTokenCount(tokens),
    unit: 'tokens',
    subtitle: 'this week · ${asJsonString(personalization['band']) ?? 'New'}',
  );
}

Future<TileData?> _notifications(TileEnv env) async {
  final response = await env.http.get<dynamic>('/api/v1/notifications');
  final all = _items(response);
  final unread = [
    for (final item in all)
      if (item['readAt'] == null) item,
  ];
  return TileData(
    stat: '${unread.length}',
    unit: 'new',
    subtitle: unread.isEmpty
        ? 'Nothing new'
        : _first(unread.first, ['title', 'body']),
    attention: unread.isNotEmpty,
    rows: [
      for (final item in unread.take(_maxRows))
        TileRow(_first(item, ['title', 'body']) ?? 'Notification'),
    ],
  );
}
