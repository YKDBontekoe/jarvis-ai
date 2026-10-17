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
      'lastMessage': "Here's an easy-going plan that leaves room to wander.",
      'lastMessageRole': 'assistant',
      'pinned': true,
    },
    {
      'id': 'c-garden',
      'title': 'Balcony herb garden',
      'updatedAt': _iso(now.subtract(const Duration(hours: 3))),
      'lastMessage': 'Which herbs survive a windy north balcony?',
      'lastMessageRole': 'user',
      'responding': true,
    },
    {
      'id': 'c-tax',
      'title': 'Q3 expense summary',
      'updatedAt': _iso(now.subtract(const Duration(days: 1, hours: 2))),
      'lastMessage': 'You spent €412 on groceries, 8% less than Q2.',
      'lastMessageRole': 'assistant',
      'profileName': 'Finance',
    },
    {
      'id': 'c-run',
      'title': 'Half marathon training plan',
      'updatedAt': _iso(now.subtract(const Duration(days: 2))),
      'lastMessage': 'Thursday is an easy 6 km. Keep it conversational.',
      'lastMessageRole': 'assistant',
      'profileName': 'Coach',
    },
    {
      'id': 'c-gift',
      'title': 'Pick up the dry cleaning',
      'updatedAt': _iso(now.subtract(const Duration(days: 1, hours: 4))),
      'lastMessage': 'Reminder · the shop closes at 18:00.',
      'lastMessageRole': 'assistant',
      'origin': 'reminder',
    },
    {
      'id': 'c-gift2',
      'title': 'Birthday gift ideas for Sam',
      'updatedAt': _iso(now.subtract(const Duration(days: 6))),
      'lastMessage': 'A good pair of trail socks never fails.',
      'lastMessageRole': 'assistant',
    },
  ];
}

List<Map<String, Object?>> messages() => [
  {
    'id': 'm1',
    'role': 'user',
    'content': 'Can you plan a relaxed weekend in Lisbon for two? We land Friday at 18:40.',
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
    'content': 'Done — I set a reminder for **Wednesday at 10:00** to book Cervejaria Ramiro. The calendar entry needs your OK first.',
  },
];

List<Map<String, Object?>> approvals() => [
  {
    'id': 'a1',
    'conversationId': conversationId,
    'toolName': 'CreateCalendarEvent',
    'argumentsJson': '{"title":"Dinner at Cervejaria Ramiro","start":"Friday 21:00","location":"Av. Almirante Reis 1"}',
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
    'portrait': 'A calm Friday. Two things need you: the dinner booking and the bank summary that is waiting on your approval.',
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
      {
        'id': 'r-late',
        'title': 'Bring the charger',
        'status': 'pending',
        'dueAt': _iso(now.subtract(const Duration(minutes: 20))),
      },
    ],
    'approvals': approvals(),
    'commitments': [
      {
        'id': 'c-deck',
        'direction': 'i_owe',
        'counterparty': 'Rosa',
        'description': 'Send Rosa the pitch deck',
        'daysOverdue': 2,
        'source': 'whatsapp',
      },
    ],
    'decisions': [
      {
        'id': 'd-bank',
        'title': 'Switch to the new bank',
        'prediction': 'Monthly fees drop by half',
        'probability': 0.7,
        'daysOverdue': 0,
      },
    ],
    'latestBriefing': {
      'id': 'n-brief',
      'title': 'Your morning briefing',
      'body':
          'A calm Friday. The **design review** is at the top of the afternoon, then you fly to Lisbon.\n\n'
          '- Leave for Schiphol by 15:30\n'
          '- Sam asked to move dinner to 19:45\n'
          '- The bank summary is waiting on your approval',
      'createdAt': _iso(today.add(const Duration(hours: 8))),
      'read': false,
    },
    'calendar': {
      'connected': true,
      'events': [
        {
          'title': 'Design review',
          'startAt': _iso(now.add(const Duration(hours: 1, minutes: 25))),
        },
        {
          'title': 'Flight TP 1347 to Lisbon',
          'startAt': _iso(now.add(const Duration(hours: 4, minutes: 10))),
          'location': 'Schiphol, gate D7',
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

Map<String, Object?> projectDetails() => {
  'project': {
    'id': 'p2',
    'name': 'Home renovation',
    'description': 'A new kitchen before summer',
    'instructions': 'Compare at least three quotes and answer in Dutch.',
    'color': 'teal',
    'createdAt': '2026-09-01T10:00:00Z',
    'updatedAt': '2026-10-10T10:00:00Z',
    'conversationCount': 1,
    'fileCount': 0,
    'taskCount': 0,
    'outcome': 'Cook for ten people in the new kitchen by June.',
    'status': 'active',
  },
  'nextAction': {
    'type': 'commitment',
    'id': 'c-deck',
    'note': 'Before the plumber comes on Friday',
    'title': 'Send Rosa the tile quotes',
    'state': 'open',
  },
  'conversations': <Object>[],
  'files': <Object>[],
  'tasks': <Object>[],
};

Map<String, Object?> comparisonDetails() => {
  'id': 'c1',
  'definition': {
    'title': 'Which boiler?',
    'question': 'Replace the 2010 boiler before winter',
    'criteria': [
      {'id': 'price', 'name': 'Price', 'weight': 8},
      {'id': 'noise', 'name': 'Noise', 'weight': 3},
      {'id': 'warranty', 'name': 'Warranty', 'weight': 5},
    ],
    'options': [
      {
        'id': 'bosch',
        'name': 'Bosch Condens 2300i',
        'summary': null,
        'cells': {
          'price': {'score': 7, 'note': '€1,900 incl. fitting', 'source': 'quote-bosch.pdf', 'asOf': '2026-10-01'},
          'noise': {'score': 8},
          'warranty': {'score': null},
        },
      },
      {
        'id': 'vaillant',
        'name': 'Vaillant ecoTEC',
        'summary': null,
        'cells': {
          'price': {'score': 5, 'note': '€2,350 incl. fitting', 'source': 'quote-vaillant.pdf', 'asOf': '2026-10-03'},
          'noise': {'score': 9},
          'warranty': {'score': 9, 'note': '10 years'},
        },
      },
    ],
    'chosenOptionId': null,
    'decisionId': null,
  },
  'result': {
    'rows': [
      {'optionId': 'bosch', 'name': 'Bosch Condens 2300i', 'score': 7.27, 'coverage': .69, 'unknownCriteria': ['Warranty'], 'rank': 1, 'tied': false, 'thin': false},
      {'optionId': 'vaillant', 'name': 'Vaillant ecoTEC', 'score': 6.84, 'coverage': 1.0, 'unknownCriteria': <Object>[], 'rank': 2, 'tied': false, 'thin': false},
    ],
    'leaderId': 'bosch',
    'leaderIsTied': false,
    'sensitivity': [
      {'criterionId': 'price', 'criterionName': 'Price', 'changesLeader': true, 'leaderWithout': 'Vaillant ecoTEC'},
    ],
    'warnings': <Object>[],
  },
  'libraryItemId': null,
};

Map<String, Object?> meetingDetails() => {
  'id': 'm1',
  'title': 'Kitchen sync with Rosa',
  'startsAt': '2026-10-20T14:00:00Z',
  'projectId': 'p2',
  'document': {
    'purpose': 'Agree the tiles and who books the plumber',
    'template': 'project_update',
    'participants': [
      {'name': 'Rosa Visser', 'personId': null},
    ],
    'agenda': [
      {'id': 'a1', 'text': 'Agree the tiles and who books the plumber', 'shared': true},
      {'id': 'a2', 'text': 'Close the loop with Rosa: Send the tile quotes', 'shared': true},
      {'id': 'a3', 'text': 'Is the budget realistic?', 'shared': false},
    ],
    'privateNotes': null,
    'prep': {
      'builtAt': '2026-10-17T09:00:00Z',
      'items': [
        {'section': 'Project', 'kind': 'fact', 'text': '“Home renovation” is for: Cook for ten people in the new kitchen by June.', 'ref': 'project:11111111-1111-7111-8111-111111111111'},
        {'section': 'Open loops', 'kind': 'fact', 'text': 'You owe Rosa Visser: Send the tile quotes — 3 days overdue', 'ref': 'commitment:22222222-2222-7222-8222-222222222222'},
        {'section': 'Decisions', 'kind': 'fact', 'text': 'Open decision: Pick a plumber. You predicted “It arrives on time” (60% sure), review 2026-11-02.', 'ref': 'decision:33333333-3333-7333-8333-333333333333'},
        {'section': 'Questions', 'kind': 'question', 'text': 'What is the next step, who owns it and by when?', 'ref': null},
      ],
    },
    'debriefText': null,
    'candidates': [
      {'id': 'i_owe-1', 'kind': 'i_owe', 'text': "I'll send Rosa the revised quote by Friday.", 'counterparty': 'Rosa Visser', 'dueOn': '2026-10-23', 'dueText': 'friday', 'evidence': '', 'state': 'pending', 'resultRef': null},
      {'id': 'they_owe-1', 'kind': 'they_owe', 'text': 'Rosa will confirm the plumber next week.', 'counterparty': 'Rosa Visser', 'dueOn': null, 'dueText': 'next week', 'evidence': '', 'state': 'pending', 'resultRef': null},
    ],
  },
};

Map<String, Object?> goalDetails() => {
  'goal': {
    'id': 'g1',
    'title': 'Run a 10k',
    'motivation': 'Feel fit before the summer.',
    'status': 'active',
    'weeklyMinutes': 90,
    'checkInEveryDays': 7,
    'habitIds': ['h1'],
    'commitmentIds': <Object>[],
    'plan': {
      'weekStart': '2026-10-12',
      'omittedForTime': 1,
      'items': [
        {'id': 's1', 'title': 'Pick a training plan', 'minutes': 30, 'status': 'proposed', 'ref': 'project:p2', 'key': 'project:p2'},
        {'id': 's2', 'title': 'Run ×2 this week', 'minutes': 40, 'status': 'accepted', 'ref': 'habit:h1', 'key': 'habit:h1'},
      ],
    },
    'experiments': [
      {
        'id': 'e1',
        'change': 'Run before breakfast',
        'expectedBenefit': 'More energy',
        'status': 'running',
        'reviewOn': '2026-10-31',
        'notes': [
          {'on': '2026-10-15', 'text': 'Slept badly, ran anyway'},
        ],
      },
    ],
  },
  'checkInDue': true,
  'habits': [
    {'id': 'h1', 'name': 'Run', 'thisWeekCount': 1, 'targetPerWeek': 3},
  ],
  'project': {'id': 'p2', 'name': 'Marathon', 'nextAction': 'Pick a training plan'},
  'commitments': <Object>[],
  'progress': {
    'measured': ['Run: 1 of 3 this week', '0 planned steps done this week'],
    'reflections': [
      {'feeling': 'slipping', 'progress': 'Ran once', 'friction': 'Rainy week', 'adjustment': 'Try mornings'},
    ],
    'interpretation': {
      'verdict': 'change',
      'reason': 'Your last 2 check-ins mention slipping or stuck. What would make it easier?',
    },
  },
};

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

Map<String, Object?> expenseMonth() {
  final now = _now();
  const spent = [22.4, 38.2, 12.9, 64.0, 18.5, 91.3, 31.0];
  return {
    'year': now.year,
    'month': now.month,
    'currency': 'EUR',
    'total': 412.3,
    'count': 14,
    'previousTotal': 380.0,
    'categories': <Object>[],
    'days': [
      for (var back = 6; back >= 0; back--)
        {
          'date': _day(now.subtract(Duration(days: back))),
          'total': spent[6 - back],
        },
    ],
    'topMerchants': [
      {'merchant': 'Albert Heijn', 'total': 128.4, 'count': 6},
      {'merchant': 'NS', 'total': 64.2, 'count': 4},
      {'merchant': 'Café Loetje', 'total': 58.0, 'count': 1},
    ],
    'otherCurrencies': <Object>[],
    'expenses': [
      {
        'id': 'e1',
        'amount': 31.0,
        'currency': 'EUR',
        'category': 'dining',
        'merchant': 'Café Loetje',
        'spentOn': _day(now),
      },
    ],
  };
}

const whatsAppChannelId = 'wa1';

/// `/home/context`: the design review coming up, Rosa and Sam linked to their
/// chats, and three things Jarvis noticed.
Map<String, Object?> homeContext({bool evening = false}) {
  final now = _now();
  final tomorrow = DateTime(now.year, now.month, now.day + 1);
  Map<String, Object?> person(
    String id,
    String name,
    String chat,
    String? relationship, {
    double? usual,
    double? waiting,
    bool quiet = false,
  }) => {
    'personId': id,
    'name': name,
    'relationship': relationship,
    'connectionId': whatsAppChannelId,
    'chatId': chat,
    'usualReplyMinutes': usual,
    'waitingHours': waiting,
    'quiet': quiet,
    'baselinePerWeek': 6.0,
  };
  return {
    'dayLine': evening
        ? 'A quiet evening. Tomorrow starts with Brunch with Rosa at 10:30.'
        : 'Design review soon, then you fly to Lisbon. Free after 21:00.',
    'hero': evening
        ? {
            'id': 'wind_down:x',
            'kind': 'wind_down',
            'title': "Tomorrow: Quinty's birthday",
            'detail': 'Then Brunch with Rosa at 10:30',
            'reason': 'It is late and tomorrow has plans',
            'facts': ['2 plans tomorrow', 'Brief at 08:00'],
          }
        : {
            'id': 'soon:design',
            'kind': 'soon',
            'title': 'Design review',
            'detail': 'With Joëlle and the product team',
            'startsAt': _iso(now.add(const Duration(minutes: 34))),
            'reason': 'From your calendar',
            'facts': ['Calendar event at the top of the afternoon'],
          },
    'people': [
      person(
        'p-rosa',
        'Rosa',
        '+31633333333',
        'Partner',
        usual: 18,
        waiting: 14,
      ),
      person('p-sam', 'Sam', '+31611111111', 'Friend', usual: 45, waiting: .2),
      person('p-tom', 'Tom', '+31622222222', 'Brother', quiet: true),
    ],
    'noticed': [
      {
        'id': 'routine:fri',
        'kind': 'routine',
        'title': 'Leave for Dordrecht on Fridays at 17:30',
        'evidence': 'You did this 4 of your last 5 Fridays',
        'confidence': .82,
        'actionLabel': 'Set it up',
        'sourceId': 'fri',
      },
      {
        'id': 'drift:p-tom',
        'kind': 'drift',
        'title': '3 weeks since you talked to Tom',
        'evidence': 'You usually talk every few days',
        'actionLabel': 'Say hi',
        'personId': 'p-tom',
      },
      {
        'id': 'birthday:q',
        'kind': 'birthday',
        'title': 'Quinty turns 25 tomorrow',
        'evidence': 'From your People list',
        'actionLabel': 'Plan a message',
      },
    ],
    'tomorrow': [
      {
        'title': "Quinty's birthday",
        'at': _iso(tomorrow.add(const Duration(hours: 9))),
        'kind': 'reminder',
      },
      {
        'title': 'Brunch with Rosa',
        'at': _iso(tomorrow.add(const Duration(hours: 10, minutes: 30))),
        'kind': 'event',
      },
    ],
    'dayRecap': {
      'remindersDone': 4,
      'tasksDone': 2,
      'done': ['Water the basil', 'Call mum', 'Book the gym'],
    },
  };
}

Map<String, Object?> whatsAppChats() {
  final now = _now();
  Map<String, Object?> chat(
    String id,
    String name,
    String preview,
    Duration ago, {
    bool group = false,
    int unread = 0,
    bool fromMe = false,
  }) => {
    'chatId': id,
    'name': name,
    'isGroup': group,
    'readAlong': true,
    'autoReminders': true,
    'lastMessageAt': _iso(now.subtract(ago)),
    'preview': preview,
    'previewFromMe': fromMe,
    'unreadCount': unread,
  };
  return {
    'live': true,
    'state': 'open',
    'account': '+31600000000',
    'chats': [
      chat(
        '+31611111111',
        'Sam',
        'Can we push dinner to 19:45?',
        const Duration(minutes: 12),
        unread: 2,
      ),
      chat(
        'family@g.us',
        'Family',
        'Photos from Sunday are in the album',
        const Duration(hours: 1),
        group: true,
        unread: 3,
      ),
      chat(
        '+31622222222',
        'Tom',
        'Running 10 min late',
        const Duration(hours: 2),
        fromMe: true,
      ),
      chat(
        '+31633333333',
        'Rosa',
        'Je bent de beste',
        const Duration(hours: 14),
        unread: 1,
      ),
      {
        ...chat(
          'climb@g.us',
          'Climbing crew',
          'Noor: Saturday 10:00 at the gym?',
          const Duration(hours: 3),
          group: true,
          unread: 9,
        ),
        'catchUp': {
          'summary': 'Noor is booking the bouldering gym for Saturday at 10:00 and asked who is in.',
          'toReply': [
            {'who': 'Noor', 'about': 'Are you in on Saturday?'},
          ],
          'messageCount': 9,
        },
      },
      chat(
        'street@g.us',
        'Street party',
        'Lena: who has a long table?',
        const Duration(hours: 5),
        group: true,
        unread: 23,
      ),
    ],
  };
}

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
  'GET /api/v1/projects/p2': projectDetails(),
  'GET /api/v1/comparisons': [
    {'id': 'c1', 'title': 'Which boiler?', 'options': 2, 'criteria': 3, 'leader': 'Bosch Condens 2300i', 'decided': false, 'updatedAt': '2026-10-10T10:00:00Z'},
  ],
  'GET /api/v1/comparisons/c1': comparisonDetails(),
  'POST /api/v1/scenarios/evaluate': {
    'kind': 'savings',
    'baseline': {'label': 'Now', 'primary': 311.76, 'unit': 'EUR', 'summary': '12.99 EUR a month for 24 months, 311.76 in total.'},
    'alternative': {'label': 'Alternative', 'primary': 227.76, 'unit': 'EUR', 'summary': '9.49 EUR a month for 24 months plus 0.00 once, 227.76 in total.'},
    'deltas': [
      {'label': 'Total over the period', 'value': 84.0, 'unit': 'EUR', 'explanation': '(12.99 × 24 + 0.00) − (9.49 × 24 + 0.00) = 84.00'},
      {'label': 'Per month', 'value': 3.5, 'unit': 'EUR', 'explanation': '12.99 − 9.49 = 3.50 a month'},
    ],
    'notes': ['Assumes the prices stay as entered. Nothing here changes a budget, account or subscription.'],
  },
  'GET /api/v1/apps/goals': {
    'goals': [
      {
        'id': 'calendar', 'title': 'Manage my calendar', 'purpose': 'See what is on today and plan around it. Jarvis reads your events; adding one asks you first.',
        'packId': 'calendar', 'packName': 'Calendar', 'status': 'not_connected',
        'nextStep': 'Paste your calendar\'s private link to connect.',
        'capabilities': {'reads': ['calendar events list'], 'prepares': <Object>[], 'changes': ['calendar event create'], 'needs': 'A private calendar link from your calendar provider.'},
        'try': null, 'pinned': false,
      },
      {
        'id': 'mail', 'title': 'Deal with my email', 'purpose': 'Search your mail and draft replies for you to send.',
        'packId': 'mail', 'packName': 'Mail', 'status': 'ready', 'nextStep': 'Connected. Try it below or pin it.',
        'capabilities': {'reads': ['list emails', 'search emails'], 'prepares': ['draft email'], 'changes': <Object>[], 'needs': 'You sign in once with your provider. Jarvis never sees your password.'},
        'try': {'label': 'Show my latest emails', 'kind': 'chat_prompt', 'prompt': 'x'}, 'pinned': true,
      },
    ],
    'unmatched': false,
    'suggestion': null,
  },
  'GET /api/v1/library/i1/lesson': {
    'itemId': 'i1', 'title': 'Sourdough basics', 'overview': 'How a sourdough starter works.', 'minutes': 4,
    'note': 'Every point is a sentence from your saved text, quoted with where it is. Nothing here is invented.',
    'points': [
      {'text': 'Feeding the starter every day keeps the yeast active and the dough rising reliably.', 'source': {'quote': 'x', 'offset': 0}},
      {'text': 'A warm kitchen speeds up fermentation, while a cold fridge slows it down.', 'source': {'quote': 'x', 'offset': 90}},
    ],
    'questions': [
      {'id': 'b1', 'kind': 'blank', 'prompt': 'Fill in the missing word: Feeding the starter every day keeps the _____ active and the dough rising reliably.', 'hint': 'It starts with “y” and has 5 letters.', 'source': {'quote': 'x', 'offset': 0}},
      {'id': 'e1', 'kind': 'explain_back', 'prompt': 'In your own words, what is the main idea of “Sourdough basics”?', 'hint': 'Mention the key terms you remember from the points above.', 'source': {'quote': 'x', 'offset': 0}},
    ],
  },
  'GET /api/v1/stories': [
    {'id': 's1', 'title': 'Recap: 18 Sep – 17 Oct 2026', 'style': 'recap', 'from': '2026-09-18', 'to': '2026-10-17', 'version': 2, 'savedVersion': 1},
  ],
  'GET /api/v1/stories/s1': {
    'id': 's1', 'title': 'Recap: 18 Sep – 17 Oct 2026', 'style': 'recap', 'from': '2026-09-18', 'to': '2026-10-17',
    'kinds': ['decision', 'task', 'journal'], 'version': 2, 'savedVersion': 1,
    'counts': {'decision': 1, 'task': 2, 'journal': 1},
    'items': [
      {'id': 'i1', 'kind': 'decision', 'date': '2026-09-25', 'text': 'Switch to the credit union', 'originalText': 'Switch bank', 'sourceRef': 'decision:d1', 'removed': false},
      {'id': 'i2', 'kind': 'task', 'date': '2026-10-02', 'text': 'Book the plumber', 'originalText': 'Book the plumber', 'sourceRef': 'task:t1', 'removed': false},
      {'id': 'i3', 'kind': 'journal', 'date': '2026-10-09', 'text': 'Good day: Walked along the canal and felt calm.', 'originalText': 'Good day: Walked along the canal and felt calm.', 'sourceRef': 'journal:j1', 'removed': true},
    ],
    'patterns': [
      {'headline': 'Mood is higher on days with exercise', 'detail': 'Across the window.', 'limits': 'Based on 20 days with data over 18 Sep – 17 Oct. This shows what went together, not why.'},
    ],
    'patternsNote': null,
    'reflection': 'What did choosing “Switch to the credit union” teach you about what matters to you?',
  },
  'GET /api/v1/documents': [
    {'id': 'd1', 'title': 'Home renovation: update 17 Oct 2026', 'kind': 'project_update', 'version': 3, 'savedVersion': 2},
  ],
  'GET /api/v1/documents/d1': {
    'draft': {
      'id': 'd1', 'title': 'Home renovation: update 17 Oct 2026', 'version': 3, 'savedVersion': 2,
      'sections': [
        {'id': 'where', 'heading': 'Where we are', 'body': 'Home renovation: Cook for ten people in the new kitchen by June. (status: active).', 'refs': ['project:p2'], 'edited': false, 'previous': null},
        {'id': 'next', 'heading': 'Next step', 'body': 'Order the oak worktop first.', 'refs': ['project:p2'], 'edited': true, 'previous': 'Choose the tiles'},
        {'id': 'promises', 'heading': 'Open promises', 'body': '- Owed to us by Rosa Visser: Send the tile quote (due 14 Oct 2026, late)', 'refs': ['commitment:c1'], 'edited': false, 'previous': null},
        {'id': 'background', 'heading': 'Background', 'body': '[ADD: a source to cite]', 'refs': <Object>[], 'edited': false, 'previous': null},
      ],
    },
    'checklist': [
      {'text': '1 section(s) still have [ADD…] placeholders to fill or remove.', 'ok': false},
      {'text': 'Names of people appear in the promises: check they should be seen by this audience.', 'ok': false},
      {'text': 'Saving or exporting does not send anything. Sharing is a separate step you take yourself.', 'ok': true},
    ],
  },
  'GET /api/v1/opportunities': {
    'items': [
      {
        'key': 'meeting_prep:m1', 'kind': 'meeting_prep', 'groupKey': 'project:p2', 'title': 'Prepare for “Kitchen sync”',
        'whyNow': 'It starts in about 2 hours and has no prep note yet.', 'evidence': ['meeting:m1'], 'destination': 'meeting:m1',
        'urgency': 3, 'windowEnds': '2026-10-17', 'also': ['You promised this to Rosa; it is 2 days late.'],
      },
      {
        'key': 'renewal:s1:20261022', 'kind': 'renewal', 'groupKey': 'subscription:s1', 'title': 'Review Netflix before it renews',
        'whyNow': 'The next charge (15.99 EUR) is expected 22 Oct; decide by 20 Oct. A comparison and a price pack can be prepared.',
        'evidence': ['subscription:s1'], 'destination': 'subscription:s1', 'urgency': 2, 'windowEnds': '2026-10-20', 'also': <Object>[],
      },
    ],
    'history': [
      {'key': 'k1', 'kind': 'follow_up', 'title': 'Plan time for: Send the deck', 'on': '2026-10-10'},
    ],
    'mutedKinds': ['free_slot'],
  },
  'GET /api/v1/goals': [
    {'id': 'g1', 'title': 'Run a 10k', 'status': 'active', 'checkInDue': true, 'checkInCount': 1, 'feeling': 'slipping', 'openPlanItems': 2, 'review': {'verdict': 'change', 'reason': 'Your last 2 check-ins mention slipping or stuck. What would make it easier?'}},
  ],
  'GET /api/v1/goals/g1': goalDetails(),
  'GET /api/v1/meetings': [
    {'id': 'm1', 'title': 'Kitchen sync with Rosa', 'startsAt': '2026-10-20T14:00:00Z', 'projectId': 'p2', 'open': 2, 'accepted': 0, 'updatedAt': '2026-10-17T09:00:00Z'},
  ],
  'GET /api/v1/meetings/events': <Object>[],
  'GET /api/v1/meetings/m1': meetingDetails(),
  'GET /api/v1/home': home(),
  'GET /api/v1/home/context': homeContext(),
  // Every chat path is ".../chats/open" with the chat in the query, so one
  // sample conversation serves all of them.
  'GET /api/v1/channels/$whatsAppChannelId/chats/open/messages': [
    for (final (id, fromMe, text, minutes) in [
      ('m1', false, 'Zullen we zondag naar het Rijks?', 900),
      ('m2', true, 'Ja leuk! Om 11 uur?', 880),
      ('m3', false, 'Perfect. Ik reserveer', 870),
      ('m4', false, 'Je bent de beste', 840),
    ])
      {
        'id': id,
        'chatId': '+31633333333',
        'fromMe': fromMe,
        'sender': fromMe ? null : 'Rosa',
        'text': text,
        'sentAt': _iso(_now().subtract(Duration(minutes: minutes))),
      },
  ],
  'POST /api/v1/channels/$whatsAppChannelId/chats/open/quick-replies': {
    'replies': ['Ik ook van jou', 'Tot vanavond!', 'Zal ik koken?'],
  },
  'GET /api/v1/persona': {
    'preferredName': 'Youri',
    'language': 'en',
    'customInstructions': '',
    'traits': <Object>[],
  },
  'GET /api/v1/usage': usage(),
  'GET /api/v1/tasks': tasks(),
  'GET /api/v1/habits': habits(),
  'GET /api/v1/expenses': expenseMonth(),
  'GET /api/v1/journal/summary': {'currentStreak': 12, 'days': 30},
  'GET /api/v1/channels': [
    {
      'id': whatsAppChannelId,
      'kind': 'whatsapp_linked',
      'displayName': 'WhatsApp',
      'account': '+31600000000',
      'allowedSenders': <Object>[],
    },
  ],
  'GET /api/v1/channels/providers': {'whatsAppLink': true},
  'GET /api/v1/channels/$whatsAppChannelId/chats': whatsAppChats(),
  'GET /api/v1/reminders': reminders(),
  'GET /api/v1/memory': memories(),
  'GET /api/v1/profiles': <Object>[],
  'GET /api/v1/search': {
    'results': [
      {
        'kind': 'reminder',
        'id': 'r3',
        'title': 'Book Cervejaria Ramiro',
        'summary': 'Wednesday at 10:00',
        'route': {'kind': 'reminders'},
        'relevance': .9,
      },
      {
        'kind': 'conversation',
        'id': conversationId,
        'title': 'Weekend in Lisbon',
        'summary': '…remind you to reserve on Wednesday?',
        'route': {
          'kind': 'conversation',
          'parameters': {'id': conversationId},
        },
        'relevance': .7,
      },
    ],
  },
  'PUT /api/v1/push-devices': <String, Object>{},
  'POST /*': <String, Object>{},
};
