import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chats/chat_list.dart';
import 'package:jarvis_mobile/features/chats/chat_row.dart';
import 'package:jarvis_mobile/features/chats/quick_replies.dart';
import 'package:jarvis_mobile/features/home/day_one_card.dart';
import 'package:jarvis_mobile/features/home/evening_card.dart';
import 'package:jarvis_mobile/features/home/home_context.dart';
import 'package:jarvis_mobile/features/home/home_insights.dart';
import 'package:jarvis_mobile/features/home/jarvis_home.dart';
import 'package:jarvis_mobile/features/home/needs_you_card.dart';
import 'package:jarvis_mobile/features/chats/chats_screen.dart';
import 'package:jarvis_mobile/features/home/noticed_card.dart';
import 'package:jarvis_mobile/features/home/right_now_card.dart';
import 'package:jarvis_mobile/features/tiles/tile_controller.dart';
import 'package:jarvis_mobile/theme.dart';
import 'package:jarvis_mobile/ui/phosphor_icons.dart';
import 'package:shared_preferences/shared_preferences.dart';

import 'support/fixture_http.dart';

String _iso(DateTime time) => time.toUtc().toIso8601String();

final _now = DateTime(2026, 10, 16, 9, 14);

Map<String, Object?> _chat(String id, String name, Duration ago, int unread) =>
    {
      'chatId': id,
      'name': name,
      'isGroup': false,
      'readAlong': true,
      'lastMessageAt': _iso(_now.subtract(ago)),
      'preview': 'hello from $name',
      'previewFromMe': false,
      'unreadCount': unread,
    };

Map<String, Object?> _person(
  String id,
  String name,
  String chat, {
  String? relationship,
  double? usual,
  double? waiting,
  bool quiet = false,
}) => {
  'personId': id,
  'name': name,
  'relationship': relationship,
  'connectionId': 'wa',
  'chatId': chat,
  'usualReplyMinutes': usual,
  'waitingHours': waiting,
  'quiet': quiet,
  'baselinePerWeek': 3.0,
};

Map<String, Object?> _context({Map<String, Object?>? hero}) => {
  'dayLine': 'Just one thing today: Joëlle on campus at 10:00.',
  'hero':
      hero ??
      {
        'id': 'soon:r1',
        'kind': 'soon',
        'title': 'Joëlle on campus',
        'startsAt': _iso(_now.add(const Duration(minutes: 46))),
        'reason': 'It starts in 46 min',
        'facts': ['Reminder at 10:00'],
        'reminderId': 'r1',
      },
  'people': [
    _person('p1', 'Rosa', '31611@s.whatsapp.net', relationship: 'Partner'),
  ],
  'noticed': [
    {
      'id': 'routine:x',
      'kind': 'routine',
      'title': 'Leave for Dordrecht on Fridays',
      'evidence': 'Seen 4 of your last 5 Fridays',
      'confidence': .8,
      'actionLabel': 'Set it up',
      'sourceId': 'x',
    },
    {
      'id': 'drift:p2',
      'kind': 'drift',
      'title': '3 weeks since you talked to Danielle',
      'evidence': 'You usually talk weekly',
      'actionLabel': 'Say hi',
      'personId': 'p2',
    },
  ],
  'tomorrow': [
    {
      'title': 'Mannen middag',
      'at': _iso(DateTime(2026, 10, 17, 14)),
      'kind': 'event',
    },
  ],
  'dayRecap': {
    'remindersDone': 2,
    'tasksDone': 1,
    'done': ['Water the basil'],
  },
};

Widget _app(Widget child, {Size size = const Size(400, 900)}) => MaterialApp(
  theme: buildJarvisTheme(),
  builder: (context, page) => MediaQuery(
    data: MediaQuery.of(context).copyWith(disableAnimations: true, size: size),
    child: page!,
  ),
  home: Scaffold(body: SingleChildScrollView(child: child)),
);

void main() {
  group('model', () {
    test('parses the context and finds the person behind a chat', () async {
      final context = HomeContext.fromJson(_context())!;
      expect(context.dayLine, startsWith('Just one thing'));
      expect(context.hero!.reminderId, 'r1');
      expect(context.hero!.facts, ['Reminder at 10:00']);
      expect(context.noticed.map((i) => i.kind), ['routine', 'drift']);
      expect(context.tomorrow.single.kind, 'event');
      expect(context.recap.remindersDone, 2);

      final http = FixtureHttp();
      http.on('GET', '/api/v1/channels', [
        {'id': 'wa', 'kind': 'whatsapp_linked'},
      ]);
      http.on('GET', '/api/v1/channels/wa/chats', {
        'chats': [_chat('+31611', 'Rosa', const Duration(hours: 1), 1)],
      });
      final list = ChatList();
      await list.loadWhatsApp(http.client());
      // "+31611" and "31611@s.whatsapp.net" are the same phone.
      expect(context.personFor(list.whatsApp.single)?.name, 'Rosa');
      expect(sameChatId('fam@g.us'), 'fam@g.us');
    });

    test('says how you keep in touch', () {
      final person = HomePerson.fromJson(
        _person('p', 'Dani', 'x', usual: 20, waiting: 2, quiet: true),
      )!;
      expect(person.usualReply, 'You reply in ~20 min');
      expect(person.drift, 'Quiet lately · you usually talk weekly');
      expect(person.overdue, 6);
      expect(
        HomePerson.fromJson(_person('p', 'A', 'x', usual: 300))!.usualReply,
        'You reply in ~5 h',
      );
    });

    test('Waiting on you puts people close to you first', () async {
      final http = FixtureHttp();
      http.on('GET', '/api/v1/channels', [
        {'id': 'wa', 'kind': 'whatsapp_linked'},
      ]);
      http.on('GET', '/api/v1/channels/wa/chats', {
        'chats': [
          // Sam has waited longest, but Rosa is your partner.
          _chat('+31600', 'Sam', const Duration(hours: 5), 1),
          _chat('+31611', 'Rosa', const Duration(minutes: 10), 1),
        ],
      });
      final list = ChatList();
      await list.loadWhatsApp(http.client());
      expect(
        list.grouped(ChatFilter.forYou, now: _now).first.items.first.title,
        'Sam',
      );

      list.setPeople(HomeContext.fromJson(_context())!.people);

      final waiting = list.grouped(ChatFilter.forYou, now: _now).first;
      expect(waiting.items.map((i) => i.title), ['Rosa', 'Sam']);
      // The Ask bar suggests the same person Needs you leads with.
      expect(
        homeSuggestions(now: _now, chats: list).first.label,
        'Reply to Rosa',
      );
      expect(
        needsYouItems(now: _now, chats: list).first.relationship,
        'Partner',
      );
    });
  });

  group('Right now', () {
    testWidgets('a reminder starting soon offers Done and +10 min', (
      tester,
    ) async {
      final actions = <HeroAction>[];
      final hero = HomeContext.fromJson(_context())!.hero!;
      await tester.pumpWidget(
        _app(
          RightNowCard(
            hero: hero,
            now: _now,
            onAction: actions.add,
            onDismiss: ({required mute}) {},
          ),
        ),
      );

      expect(find.text('Joëlle on campus'), findsOneWidget);
      expect(find.byKey(const Key('home-right-now-minutes')), findsOneWidget);
      await tester.tap(find.byKey(const Key('home-right-now-done')));
      await tester.tap(find.byKey(const Key('home-right-now-snooze')));
      expect(actions, [HeroAction.done, HeroAction.snooze]);
    });

    testWidgets('the card morphs to the next moment in place', (tester) async {
      Widget card(HomeHero hero) => _app(
        RightNowCard(
          hero: hero,
          now: _now,
          onAction: (_) {},
          onDismiss: ({required mute}) {},
        ),
      );
      await tester.pumpWidget(card(HomeContext.fromJson(_context())!.hero!));
      await tester.pumpWidget(
        card(
          const HomeHero(
            id: 'battery:x',
            kind: 'battery',
            title: 'Charge before you go',
            detail: '14% left and 1 more plan today',
            reason: 'Your phone reported 14%',
          ),
        ),
      );
      await tester.pumpAndSettle();

      expect(find.text('Charge before you go'), findsOneWidget);
      expect(find.text('Joëlle on campus'), findsNothing);
      expect(find.byKey(const Key('home-right-now-ok')), findsOneWidget);
      expect(find.text('BEFORE YOU GO'), findsOneWidget);
    });

    testWidgets('why lists the facts and can mute the kind', (tester) async {
      bool? muted;
      await tester.pumpWidget(
        _app(
          RightNowCard(
            hero: HomeContext.fromJson(_context())!.hero!,
            now: _now,
            onAction: (_) {},
            onDismiss: ({required mute}) => muted = mute,
          ),
        ),
      );
      await tester.tap(find.byKey(const Key('home-right-now-why')));
      await tester.pumpAndSettle();
      expect(find.byKey(const Key('home-why-sheet')), findsOneWidget);
      expect(find.text('Reminder at 10:00'), findsOneWidget);

      await tester.tap(find.byKey(const Key('home-why-fewer')));
      await tester.pumpAndSettle();
      expect(muted, isTrue);
    });
  });

  testWidgets('Jarvis noticed steps through insights and waves one away', (
    tester,
  ) async {
    final acted = <String>[];
    final dismissed = <String>[];
    await tester.pumpWidget(
      _app(
        NoticedCard(
          insights: HomeContext.fromJson(_context())!.noticed,
          onAction: (item) => acted.add(item.id),
          onDismiss: (item) => dismissed.add(item.id),
        ),
      ),
    );
    expect(find.text('Leave for Dordrecht on Fridays'), findsOneWidget);
    expect(find.text('80% sure'), findsOneWidget);

    await tester.tap(find.byKey(const Key('home-noticed-action')));
    expect(acted, ['routine:x']);

    await tester.tap(find.byKey(const Key('home-noticed-dismiss')));
    await tester.pumpAndSettle();
    expect(dismissed, ['routine:x']);
    expect(find.text('3 weeks since you talked to Danielle'), findsOneWidget);
  });

  testWidgets('the evening card counts the day and shows tomorrow', (
    tester,
  ) async {
    final context = HomeContext.fromJson(_context())!;
    var wrote = 0;
    await tester.pumpWidget(
      _app(
        EveningCard(
          recap: context.recap,
          tomorrow: context.tomorrow,
          onWrite: () => wrote++,
        ),
      ),
    );
    expect(find.text('2'), findsOneWidget);
    expect(find.text('reminders'), findsOneWidget);
    expect(find.text('Done: Water the basil'), findsOneWidget);
    expect(find.text('Mannen middag'), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-evening-write')));
    expect(wrote, 1);
    expect(EveningCard.shows(const HomeRecap(), const []), isFalse);
  });

  testWidgets('day one saves when the brief should arrive', (tester) async {
    final http = FixtureHttp();
    http.on('GET', '/api/v1/briefings/daily', {
      'enabled': false,
      'localTime': '08:00:00',
      'timeZoneId': 'Europe/Amsterdam',
    });
    http.on('PUT', '/api/v1/briefings/daily', {
      'enabled': true,
      'localTime': '07:00:00',
      'timeZoneId': 'Europe/Amsterdam',
    });
    var tapped = 0;
    final steps = [
      DayOneStep(
        id: 'calendar',
        label: 'Connect your calendar',
        unlocks: 'Countdowns',
        icon: PhosphorIconsRegular.calendarBlank,
        done: false,
        onTap: () => tapped++,
      ),
      DayOneStep(
        id: 'people',
        label: 'Tell Jarvis who matters',
        unlocks: 'Birthdays',
        icon: PhosphorIconsRegular.users,
        done: true,
        onTap: () {},
      ),
    ];
    expect(DayOneCard.shows(steps), isTrue);
    await tester.pumpWidget(
      _app(DayOneCard(steps: steps, http: http.client())),
    );
    await tester.pumpAndSettle();

    expect(find.text('50%'), findsOneWidget);
    await tester.tap(find.byKey(const Key('home-day-one-calendar')));
    expect(tapped, 1);

    await tester.tap(find.byKey(const Key('home-day-one-brief-07:00')));
    await tester.pumpAndSettle();
    expect(http.sent('PUT', '/api/v1/briefings/daily').single.body, {
      'enabled': true,
      'localTime': '07:00:00',
      'timeZoneId': 'Europe/Amsterdam',
    });
  });

  testWidgets('quick replies fill the composer instead of sending', (
    tester,
  ) async {
    clearQuickReplies();
    final http = FixtureHttp();
    http.on('GET', '/api/v1/channels', [
      {'id': 'wa', 'kind': 'whatsapp_linked'},
    ]);
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [_chat('+31611', 'Rosa', const Duration(minutes: 10), 1)],
    });
    http.on('POST', '/api/v1/channels/wa/chats/open/quick-replies', {
      'replies': ['Sounds good!', 'What time?'],
    });
    final list = ChatList();
    await tester.runAsync(() => list.loadWhatsApp(http.client()));
    list.setPeople(HomeContext.fromJson(_context())!.people);
    final item = list.whatsApp.single;
    final picked = <String>[];
    await tester.pumpWidget(
      _app(
        ChatRow(
          item: item,
          onTap: () {},
          waiting: true,
          now: _now,
          person: list.personFor(item),
          footer: QuickReplies(
            http: http.client(),
            item: item,
            onPick: picked.add,
          ),
        ),
      ),
    );
    // The replies arrive over the (fixture) network.
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 50)),
    );
    await tester.pumpAndSettle();

    expect(find.byKey(Key('chat-relationship-${item.key}')), findsOneWidget);
    await tester.tap(find.byKey(const Key('quick-reply-What time?')));
    expect(picked, ['What time?']);
    expect(
      http
          .sent('POST', '/api/v1/channels/wa/chats/open/quick-replies')
          .single
          .query,
      {'chatId': '+31611'},
    );
    expect(http.sent('POST', '/api/v1/channels/wa/chats/open/send'), isEmpty);
  });

  group('Needs you folds', () {
    NeedsYouItem item(NeedsYouKind kind, String id) =>
        NeedsYouItem(kind: kind, id: id, title: id, detail: '');
    final items = [
      item(NeedsYouKind.reminder, 'r1'),
      item(NeedsYouKind.reminder, 'r2'),
      item(NeedsYouKind.reminder, 'r3'),
      item(NeedsYouKind.person, 'rosa'),
      item(NeedsYouKind.task, 't'),
    ];

    test('three reminders never hide the person waiting on you', () {
      expect(folded(items).map((i) => i.id), ['r1', 'rosa', 't']);
      expect(folded(items.take(3).toList()).map((i) => i.id), [
        'r1',
        'r2',
        'r3',
      ]);
    });

    testWidgets('Show more opens the rest', (tester) async {
      await tester.pumpWidget(
        _app(NeedsYouCard(items: items, onAction: (_, _) {})),
      );
      expect(find.text('r2'), findsNothing);
      await tester.tap(find.byKey(const Key('home-needs-you-more')));
      await tester.pumpAndSettle();
      expect(find.text('r2'), findsOneWidget);
      expect(find.byKey(const Key('home-needs-you-more')), findsNothing);
    });

    testWidgets('a promise offers Done, and a failed Done brings it back', (
      tester,
    ) async {
      const promise = NeedsYouItem(
        kind: NeedsYouKind.commitment,
        id: 'c1',
        title: 'Send the deck',
        detail: 'You promised Rosa · Due today',
      );
      const decision = NeedsYouItem(
        kind: NeedsYouKind.decision,
        id: 'd1',
        title: 'Switch bank',
        detail: 'Time to check how it went',
      );
      final acted = <(NeedsYouKind, NeedsYouAction)>[];
      var failed = <String>{};
      late StateSetter rebuild;
      await tester.pumpWidget(
        _app(
          StatefulBuilder(
            builder: (context, setState) {
              rebuild = setState;
              return NeedsYouCard(
                items: const [promise, decision],
                failed: failed,
                onAction: (item, action) => acted.add((item.kind, action)),
              );
            },
          ),
        ),
      );

      await tester.tap(find.byKey(const Key('home-needs-you-button-Review')));
      expect(acted.last, (NeedsYouKind.decision, NeedsYouAction.open));

      await tester.tap(find.byKey(const Key('home-needs-you-button-Done')));
      await tester.pumpAndSettle();
      expect(acted.last, (NeedsYouKind.commitment, NeedsYouAction.done));
      expect(find.text('Send the deck'), findsNothing);

      // The server refused: the row is back instead of hidden for good.
      rebuild(() => failed = {'c1'});
      await tester.pumpAndSettle();
      expect(find.text('Send the deck'), findsOneWidget);
    });
  });

  testWidgets('the hold preview offers quick replies to a person', (
    tester,
  ) async {
    clearQuickReplies();
    final http = FixtureHttp();
    http.on('GET', '/api/v1/channels', [
      {'id': 'wa', 'kind': 'whatsapp_linked'},
    ]);
    http.on('GET', '/api/v1/channels/wa/chats', {
      'chats': [_chat('+31611', 'Rosa', const Duration(minutes: 10), 1)],
    });
    http.on('POST', '/api/v1/channels/wa/chats/open/quick-replies', {
      'replies': ['Tot vanavond!'],
    });
    final list = ChatList();
    await tester.runAsync(() => list.loadWhatsApp(http.client()));
    final replied = <String>[];
    late BuildContext page;
    await tester.pumpWidget(
      _app(
        Builder(
          builder: (context) {
            page = context;
            return const SizedBox();
          },
        ),
      ),
    );
    unawaited(
      showChatPreview(
        page,
        item: list.whatsApp.single,
        http: http.client(),
        relationship: 'Partner',
        onReply: replied.add,
      ),
    );
    await tester.pump();
    await tester.runAsync(
      () => Future<void>.delayed(const Duration(milliseconds: 50)),
    );
    await tester.pumpAndSettle();

    expect(find.text('Partner · 1 new'), findsOneWidget);
    await tester.tap(find.byKey(const Key('quick-reply-Tot vanavond!')));
    await tester.pumpAndSettle();
    expect(replied, ['Tot vanavond!']);
    expect(find.byKey(const Key('chat-preview')), findsNothing);
  });

  group('Home', () {
    late FixtureHttp http;
    late List<String> suggestions;

    setUp(() {
      SharedPreferences.setMockInitialValues({});
      http = FixtureHttp();
      suggestions = [];
      http.on('GET', '/api/v1/home', {
        'calendar': {'connected': true, 'events': <Object>[]},
        'reminders': <Object>[],
        'approvals': <Object>[],
        'packs': <Object>[],
      });
      http.on('GET', '/api/v1/home/context', _context());
    });

    Future<void> show(WidgetTester tester, {DateTime? at}) async {
      tester.view.physicalSize = const Size(400, 1400);
      tester.view.devicePixelRatio = 1;
      addTearDown(tester.view.resetPhysicalSize);
      addTearDown(tester.view.resetDevicePixelRatio);
      await tester.pumpWidget(
        MaterialApp(
          theme: buildJarvisTheme(),
          builder: (context, child) => MediaQuery(
            data: MediaQuery.of(context).copyWith(disableAnimations: true),
            child: child!,
          ),
          home: Scaffold(
            body: JarvisHome(
              source: TileDataSource(http.client(), clock: () => at ?? _now),
              layout: TileLayoutController(),
              chats: ChatList(),
              ready: true,
              refreshRevision: 0,
              clock: () => at ?? _now,
              onOpen: (_) {},
              onOpenChat: (_) {},
              onAddTile: () {},
              onSuggestion: suggestions.add,
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      await tester.pump(const Duration(milliseconds: 50));
      await tester.pumpAndSettle();
    }

    testWidgets('leads with the day line and the Right now card', (
      tester,
    ) async {
      await show(tester);

      expect(find.byKey(const Key('home-day-line')), findsOneWidget);
      expect(find.byKey(const Key('home-right-now')), findsOneWidget);
      expect(find.byKey(const Key('home-clock-tap')), findsNothing);
      expect(find.byKey(const Key('home-noticed')), findsOneWidget);
      // Morning: the evening card waits.
      expect(find.byKey(const Key('home-evening')), findsNothing);
    });

    testWidgets('Not now puts the card away on the server', (tester) async {
      await show(tester);
      await tester.tap(find.byKey(const Key('home-right-now-why')));
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('home-why-not-now')));
      await tester.pumpAndSettle();

      expect(find.byKey(const Key('home-right-now')), findsNothing);
      expect(find.byKey(const Key('home-clock-tap')), findsOneWidget);
      expect(http.sent('POST', '/api/v1/home/context/dismiss').single.body, {
        'id': 'soon:r1',
        'kind': 'soon',
        'mute': false,
      });
    });

    testWidgets('a routine is saved as a draft, not switched on', (
      tester,
    ) async {
      http.on('POST', '/api/v1/routines/suggestions/x/accept', {'id': 'r'});
      await show(tester);
      await Scrollable.ensureVisible(
        tester.element(find.byKey(const Key('home-noticed-action'))),
        alignment: .5,
      );
      await tester.pumpAndSettle();
      await tester.tap(find.byKey(const Key('home-noticed-action')));
      await tester.pumpAndSettle();

      expect(
        http.sent('POST', '/api/v1/routines/suggestions/x/accept'),
        hasLength(1),
      );
      expect(find.textContaining('draft routine'), findsOneWidget);
    });

    testWidgets('the evening swaps the brief for the day and tomorrow', (
      tester,
    ) async {
      http.on('GET', '/api/v1/home/context', {
        ..._context(),
        'hero': null,
        'noticed': <Object>[],
      });
      await show(tester, at: DateTime(2026, 10, 16, 19, 30));

      expect(find.byKey(const Key('home-evening')), findsOneWidget);
      expect(find.text('Mannen middag'), findsOneWidget);
    });
  });
}
