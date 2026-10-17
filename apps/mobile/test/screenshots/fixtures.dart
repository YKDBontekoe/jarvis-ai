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
