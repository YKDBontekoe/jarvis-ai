// Realistic sample data for the screenshot harness. Nothing here reaches a
// server; dates are relative to "now" so the screens read as current.

String _iso(DateTime value) => value.toUtc().toIso8601String();

DateTime _now() => DateTime.now();

String _day(DateTime value) =>
    '${value.year.toString().padLeft(4, '0')}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')}';

const conversationId = 'c-trip';

List<Map<String, Object?>> conversations() {
  final now = _now();
  return [
    {
      'id': conversationId,
      'title': 'Weekend in Lisbon',
      'updatedAt': _iso(now.subtract(const Duration(minutes: 4))),
    },
    {
      'id': 'c-garden',
      'title': 'Balcony herb garden',
      'updatedAt': _iso(now.subtract(const Duration(hours: 3))),
    },
    {
      'id': 'c-tax',
      'title': 'Q3 expense summary',
      'updatedAt': _iso(now.subtract(const Duration(days: 1, hours: 2))),
    },
    {
      'id': 'c-run',
      'title': 'Half marathon training plan',
      'updatedAt': _iso(now.subtract(const Duration(days: 2))),
    },
    {
      'id': 'c-gift',
      'title': 'Birthday gift ideas for Sam',
      'updatedAt': _iso(now.subtract(const Duration(days: 6))),
    },
  ];
}

List<Map<String, Object?>> messages() => [
  {
    'id': 'm1',
    'role': 'user',
    'content':
        'Can you plan a relaxed weekend in Lisbon for two? We land Friday at 18:40.',
  },
  {
    'id': 'm2',
    'role': 'assistant',
    'content': '''Here's an easy-going plan that leaves room to wander.

**Friday evening**
- Check in around Chiado, then dinner at *Cervejaria Ramiro* — book for 21:00.

**Saturday**
1. Pastéis and coffee in Belém before the crowds.
2. Walk the riverside to LX Factory for lunch.
3. Sunset at Miradouro da Senhora do Monte.

**Sunday**
- Tram 28 early, then a slow brunch in Príncipe Real.

Want me to add the dinner booking to your calendar and remind you to reserve on Wednesday?''',
  },
  {
    'id': 'm3',
    'role': 'user',
    'content': 'Yes please, and remind me Wednesday at 10.',
  },
  {
    'id': 'm4',
    'role': 'assistant',
    'content':
        'Done — I set a reminder for **Wednesday at 10:00** to book Cervejaria Ramiro. The calendar entry needs your OK first.',
  },
];

List<Map<String, Object?>> approvals() => [
  {
    'id': 'a1',
    'conversationId': conversationId,
    'toolName': 'CreateCalendarEvent',
    'argumentsJson':
        '{"title":"Dinner at Cervejaria Ramiro","start":"Friday 21:00","location":"Av. Almirante Reis 1"}',
    'status': 'pending',
  },
];

List<Map<String, Object?>> tasks() {
  final now = _now();
  return [
    {
      'id': 't1',
      'title': 'Compare flights to Lisbon',
      'status': 'running',
      'createdAt': _iso(now.subtract(const Duration(minutes: 12))),
      'conversationId': 'task-1',
    },
    {
      'id': 't2',
      'title': 'Summarize the quarterly bank statements',
      'status': 'needs_approval',
      'createdAt': _iso(now.subtract(const Duration(hours: 1))),
      'conversationId': 'task-2',
    },
    {
      'id': 't3',
      'title': 'Research espresso grinders under €300',
      'status': 'completed',
      'createdAt': _iso(now.subtract(const Duration(hours: 5))),
      'conversationId': 'task-3',
    },
  ];
}

Map<String, Object?> home() {
  final now = _now();
  final today = DateTime(now.year, now.month, now.day);
  return {
    'portrait':
        'A calm Friday. Two things need you: the dinner booking and the bank summary that is waiting on your approval.',
    'reminders': [
      {
        'id': 'r1',
        'title': 'Water the basil',
        'dueAt': _iso(today.add(const Duration(hours: 18))),
      },
      {
        'id': 'r2',
        'title': 'Call mum',
        'dueAt': _iso(today.add(const Duration(hours: 19, minutes: 30))),
      },
    ],
    'approvals': approvals(),
    'calendar': {
      'connected': true,
      'events': [
        {
          'title': 'Design review',
          'startAt': _iso(today.add(const Duration(hours: 15))),
        },
        {
          'title': 'Flight TP 1347 to Lisbon',
          'startAt': _iso(today.add(const Duration(hours: 16, minutes: 5))),
        },
      ],
    },
    'device': {'batteryPercent': 82, 'charging': false, 'hasLocation': true},
    'packs': [
      {'name': 'Calendar', 'installed': true},
    ],
  };
}

Map<String, Object?> usage() => {
  'activity': {
    'messagesSent': {'total': 142},
  },
  'personalization': {'band': 'Personal', 'score': 74, 'activeMemories': 38},
  'codex': {'totalTokens': 812000},
  'openRouter': {'totalTokens': 0, 'estimatedCostUsd': 0},
};

Map<String, Object?> _habit(
  String id,
  String name,
  String icon, {
  required int streak,
  required bool doneToday,
  required int thisWeek,
}) {
  final now = _now();
  return {
    'id': id,
    'name': name,
    'icon': icon,
    'cadence': 'daily',
    'targetPerWeek': 7,
    'stats': {
      'today': _day(now),
      'currentStreak': streak,
      'bestStreak': streak + 4,
      'streakUnit': 'days',
      'doneToday': doneToday,
      'thisWeekCount': thisWeek,
      'totalCheckIns': streak * 3,
      'openToday': !doneToday,
      'recentDates': [
        for (var i = doneToday ? 0 : 1; i < streak.clamp(0, 14); i++)
          _day(now.subtract(Duration(days: i))),
      ],
    },
  };
}

Map<String, Object?> habits() => {
  'habits': [
    _habit('h1', 'Morning run', '🏃', streak: 12, doneToday: true, thisWeek: 5),
    _habit(
      'h2',
      'Read 20 pages',
      '📚',
      streak: 4,
      doneToday: false,
      thisWeek: 3,
    ),
    _habit('h3', 'Drink water', '💧', streak: 21, doneToday: true, thisWeek: 5),
  ],
  'settings': {'eveningCheckIn': true, 'checkInTime': '20:30'},
};

List<Map<String, Object?>> projects() => [
  {'id': 'p1', 'name': 'Lisbon trip', 'color': 'orange'},
  {'id': 'p2', 'name': 'Home renovation', 'color': 'teal'},
];

List<Map<String, Object?>> reminders() {
  final now = _now();
  final today = DateTime(now.year, now.month, now.day);
  return [
    {
      'id': 'r1',
      'title': 'Water the basil',
      'status': 'pending',
      'dueAt': _iso(today.add(const Duration(hours: 18))),
      'createdAt': _iso(now.subtract(const Duration(days: 3))),
      'recurrence': 'daily',
      'localTime': '18:00:00',
    },
    {
      'id': 'r2',
      'title': 'Call mum',
      'status': 'pending',
      'dueAt': _iso(today.add(const Duration(hours: 19, minutes: 30))),
      'createdAt': _iso(now.subtract(const Duration(days: 1))),
    },
    {
      'id': 'r3',
      'title': 'Book Cervejaria Ramiro',
      'status': 'pending',
      'dueAt': _iso(today.add(const Duration(days: 5, hours: 10))),
      'createdAt': _iso(now.subtract(const Duration(minutes: 3))),
    },
    {
      'id': 'r4',
      'title': 'Renew passport',
      'status': 'pending',
      'dueAt': _iso(today.add(const Duration(days: 12, hours: 9))),
      'createdAt': _iso(now.subtract(const Duration(days: 9))),
    },
    {
      'id': 'r5',
      'title': 'Take out recycling',
      'status': 'delivered',
      'dueAt': _iso(today.subtract(const Duration(hours: 4))),
      'lastDeliveredAt': _iso(today.subtract(const Duration(hours: 4))),
      'createdAt': _iso(now.subtract(const Duration(days: 9))),
    },
  ];
}

List<Map<String, Object?>> memories() => [
  {
    'id': 'mem1',
    'kind': 'preference',
    'content': 'Prefers window seats and morning flights.',
    'isPinned': true,
    'importance': .9,
    'confidence': 1,
  },
  {
    'id': 'mem2',
    'kind': 'fact',
    'content': 'Partner Sam is vegetarian and allergic to walnuts.',
    'isPinned': true,
    'importance': .9,
    'confidence': 1,
  },
  {
    'id': 'mem3',
    'kind': 'fact',
    'content': 'Training for the Amsterdam half marathon in October.',
    'isPinned': false,
    'importance': .6,
    'confidence': .9,
  },
  {
    'id': 'mem4',
    'kind': 'preference',
    'content': 'Likes concise answers with a short summary first.',
    'isPinned': false,
    'importance': .5,
    'confidence': .8,
  },
];

Map<String, Object?> fixtureRoutes({bool withApproval = true}) => {
  'GET /api/v1/conversations': conversations(),
  'GET /api/v1/conversations/$conversationId': {
    'id': conversationId,
    'title': 'Weekend in Lisbon',
    'responding': false,
  },
  'GET /api/v1/conversations/$conversationId/messages': {
    'items': messages(),
    'hasMore': false,
  },
  'GET /api/v1/conversations/*': {'items': <Object>[]},
  'GET /api/v1/approvals': withApproval ? approvals() : <Object>[],
  'GET /api/v1/notifications': [
    {'id': 'n1', 'type': 'reminder.due', 'title': 'Water the basil'},
  ],
  'GET /api/v1/projects': projects(),
  'GET /api/v1/home': home(),
  'GET /api/v1/usage': usage(),
  'GET /api/v1/tasks': tasks(),
  'GET /api/v1/habits': habits(),
  'GET /api/v1/reminders': reminders(),
  'GET /api/v1/memory': memories(),
  'GET /api/v1/profiles': <Object>[],
  'PUT /api/v1/push-devices': <String, Object>{},
  'POST /*': <String, Object>{},
};
