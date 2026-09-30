import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:jarvis_mobile/features/chat/chat_entries.dart';
import 'package:jarvis_mobile/features/chat/chat_widgets.dart';
import 'package:jarvis_mobile/features/chat/tool_catalog.dart';
import 'package:jarvis_mobile/theme.dart';

Widget _host(Widget child) => MaterialApp(
  theme: buildJarvisTheme(),
  home: Scaffold(body: SingleChildScrollView(child: child)),
);

void main() {
  group('ToolRunEntry', () {
    test('tracks running, completed, and failed steps in order', () {
      var run = const ToolRunEntry([]).started('SearchMemory');
      run = run.started('CreateReminder');
      expect(run.running, isTrue);

      run = run.finished('SearchMemory', success: true);
      run = run.finished('CreateReminder', success: false);

      expect(run.steps.map((step) => step.status), [
        ToolStepStatus.completed,
        ToolStepStatus.failed,
      ]);
      expect(run.running, isFalse);
    });

    test('settle marks unfinished steps as completed', () {
      final run = const ToolRunEntry([]).started('ListReminders').settle();
      expect(run.steps.single.status, ToolStepStatus.completed);
    });
  });

  group('appendAssistantDelta', () {
    test('keeps writing the preface when an approval card is last', () {
      final entries = <ChatEntry>[
        const MessageEntry(role: 'user', content: 'Remind me'),
        const MessageEntry(
          role: 'assistant',
          content: 'I can schedule that.',
          pending: true,
        ),
        const ApprovalEntry(
          id: 'a1',
          toolName: 'CreateReminder',
          argumentsJson: '{}',
        ),
      ];
      appendAssistantDelta(entries, ' Checking the calendar.');
      expect(entries, hasLength(3));
      final pending = entries[1] as MessageEntry;
      expect(pending.content, 'I can schedule that. Checking the calendar.');
      expect(pending.pending, isTrue);
    });

    test('ignores stale deltas after the assistant turn completed', () {
      final entries = <ChatEntry>[
        const MessageEntry(role: 'user', content: 'Hi'),
        const MessageEntry(role: 'assistant', content: 'Hello.'),
      ];
      appendAssistantDelta(entries, ' extra');
      expect(entries, hasLength(2));
      expect((entries[1] as MessageEntry).content, 'Hello.');
    });

    test('starts a pending bubble after the latest user message', () {
      final entries = <ChatEntry>[
        const MessageEntry(role: 'user', content: 'Hi'),
        const MessageEntry(role: 'assistant', content: 'Hello.'),
        const MessageEntry(role: 'user', content: 'And then?'),
      ];
      appendAssistantDelta(entries, 'Next.');
      expect(entries, hasLength(4));
      final pending = entries.last as MessageEntry;
      expect(pending.content, 'Next.');
      expect(pending.pending, isTrue);
    });
  });

  group('ApprovalEntry.fromJson', () {
    test('reads API approvals and marks resumed decisions for retry', () {
      final approval = ApprovalEntry.fromJson({
        'id': 'a1',
        'toolName': 'ForgetMemory',
        'argumentsJson': '{"memoryId":"m1"}',
        'status': 'approved',
        'approved': true,
      })!;
      expect(approval.retry, isTrue);
      expect(approval.decision, isTrue);
      expect(approval.arguments['memoryId'], 'm1');
    });

    test('reads SignalR approval events with PascalCase keys', () {
      final approval = ApprovalEntry.fromJson({
        'Id': 'a2',
        'ToolName': 'AddMcpServer',
        'ArgumentsJson': 'not json',
      })!;
      expect(approval.id, 'a2');
      expect(approval.retry, isFalse);
      expect(approval.arguments, isEmpty);
    });

    test('reads Map payloads that are not Map<String, dynamic>', () {
      final approval = ApprovalEntry.fromJson(<dynamic, dynamic>{
        'id': 'a3',
        'toolName': 'SearchMemory',
        'argumentsJson': '{"query":"hello"}',
      })!;
      expect(approval.id, 'a3');
      expect(approval.arguments['query'], 'hello');
    });

    test('rejects payloads without an id', () {
      expect(ApprovalEntry.fromJson({'toolName': 'x'}), isNull);
      expect(ApprovalEntry.fromJson('nope'), isNull);
    });

    test('non-object argument JSON does not throw', () {
      expect(
        ApprovalEntry.fromJson({
          'id': 'a4',
          'toolName': 'x',
          'argumentsJson': '[1,2]',
        })!.arguments,
        isEmpty,
      );
      expect(
        ApprovalEntry.fromJson({
          'id': 'a5',
          'toolName': 'x',
          'argumentsJson': 'null',
        })!.arguments,
        isEmpty,
      );
    });
  });

  test('tool catalog describes known and unknown tools', () {
    expect(describeTool('CreateReminder').done, 'Scheduled a reminder');
    expect(describeTool('RequestMcpAuthorization').active,
        'Asking you to authorize an integration');
    expect(describeTool('OfferMcpSetup').active, 'Opening integration setup');
    expect(describeTool('AskForMcpCredential').done, 'Asked for a token in chat');
    expect(describeTool('SearchMemoryAsync').active, 'Searching memory');
    expect(
      describeTool('github_create_issue').active,
      'Using github create issue',
    );
    expect(describeTool('browser_navigate').active, 'Using the browser');
    expect(humanizeToolName('memoryId'), 'memory id');
  });

  testWidgets('assistant replies render Markdown and can be copied', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        const MessageBubble(
          message: MessageEntry(
            role: 'assistant',
            content: 'Here is **bold** text\n\n- first item\n- second item',
          ),
        ),
      ),
    );

    expect(find.textContaining('bold', findRichText: true), findsWidgets);
    expect(
      find.textContaining('first item', findRichText: true),
      findsOneWidget,
    );
    expect(find.textContaining('**', findRichText: true), findsNothing);
    expect(find.byTooltip('Copy reply'), findsOneWidget);
  });

  testWidgets('pending empty replies show the typing indicator', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        const MessageBubble(
          message: MessageEntry(role: 'assistant', content: '', pending: true),
        ),
      ),
    );
    expect(find.byType(TypingIndicator), findsOneWidget);
    expect(find.byTooltip('Copy reply'), findsNothing);
  });

  testWidgets('the typing indicator names the step and counts time', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        const MessageBubble(
          message: MessageEntry(role: 'assistant', content: '', pending: true),
          thinkingLabel: 'Working out the next step',
        ),
      ),
    );
    expect(find.text('Working out the next step'), findsOneWidget);

    await tester.pump(const Duration(seconds: 12));
    expect(find.text('Working out the next step · 12s'), findsOneWidget);
    expect(
      find.bySemanticsLabel('Jarvis: Working out the next step'),
      findsOneWidget,
    );
  });

  test('thinking label follows the tools of the current reply', () {
    const user = MessageEntry(role: 'user', content: 'Plan my day');
    const placeholder = MessageEntry(
      role: 'assistant',
      content: '',
      pending: true,
    );
    expect(thinkingLabel([user, placeholder]), 'Thinking');
    expect(
      thinkingLabel([
        user,
        const ToolRunEntry([ToolStep('ListReminders', ToolStepStatus.running)]),
        placeholder,
      ]),
      'Working',
    );
    expect(
      thinkingLabel([
        user,
        const ToolRunEntry([
          ToolStep('ListReminders', ToolStepStatus.completed),
          ToolStep('WebSearch', ToolStepStatus.failed),
        ]),
        placeholder,
      ]),
      'Working out the next step · 2 steps done',
    );
    expect(
      thinkingLabel([
        const ToolRunEntry([ToolStep('Old', ToolStepStatus.completed)]),
        user,
        placeholder,
      ]),
      'Thinking',
    );
    expect(describeTool('WebSearch').active, 'Searching the web');
  });

  testWidgets('failed user messages offer a retry', (tester) async {
    var retried = false;
    await tester.pumpWidget(
      _host(
        MessageBubble(
          message: const MessageEntry(
            role: 'user',
            content: 'Hello',
            failed: true,
          ),
          onRetry: () => retried = true,
        ),
      ),
    );
    await tester.tap(find.text('Not sent — tap to retry'));
    expect(retried, isTrue);
  });

  testWidgets('tool run chips reflect progress', (tester) async {
    await tester.pumpWidget(
      _host(
        ToolRunView(
          run: const ToolRunEntry([
            ToolStep('SearchMemory', ToolStepStatus.completed),
            ToolStep('CreateReminder', ToolStepStatus.running),
            ToolStep('ListFiles', ToolStepStatus.failed),
          ]),
        ),
      ),
    );
    expect(find.text('Searched memory'), findsOneWidget);
    expect(find.text('Scheduling a reminder'), findsOneWidget);
    expect(find.text('Checking your files failed'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
  });

  testWidgets('approval card shows arguments and reports decisions', (
    tester,
  ) async {
    final decisions = <bool>[];
    await tester.pumpWidget(
      _host(
        ApprovalCard(
          approval: const ApprovalEntry(
            id: 'a1',
            toolName: 'ForgetMemory',
            argumentsJson: '{"memoryId":"0199-abc"}',
          ),
          onDecide: decisions.add,
        ),
      ),
    );

    expect(find.text('Jarvis needs your approval'), findsOneWidget);
    expect(find.text('Forgetting a memory'), findsOneWidget);
    expect(find.textContaining('0199-abc', findRichText: true), findsOneWidget);

    await tester.tap(find.text('Approve'));
    await tester.tap(find.text('Decline'));
    expect(decisions, [true, false]);
  });

  test('copyWith can clear a leftover decision after a cancelled decline', () {
    const approval = ApprovalEntry(
      id: 'a1',
      toolName: 'ForgetMemory',
      argumentsJson: '{}',
      status: ApprovalStatus.submitting,
      decision: false,
    );
    final reset = approval.copyWith(
      status: ApprovalStatus.pending,
      clearDecision: true,
    );
    expect(reset.decision, isNull);
    expect(reset.status, ApprovalStatus.pending);
  });

  test(
    'resolveSubmittingApproval maps a recorded decision to a terminal state',
    () {
      const submitting = ApprovalEntry(
        id: 'a1',
        toolName: 'ForgetMemory',
        argumentsJson: '{}',
        status: ApprovalStatus.submitting,
        decision: true,
      );
      expect(
        resolveSubmittingApproval(submitting).status,
        ApprovalStatus.approved,
      );
      expect(
        resolveSubmittingApproval(submitting.copyWith(decision: false)).status,
        ApprovalStatus.denied,
      );
      expect(
        resolveSubmittingApproval(
          submitting.copyWith(clearDecision: true),
        ).status,
        ApprovalStatus.pending,
      );
      expect(
        resolveSubmittingApproval(
          submitting,
          fallback: ApprovalStatus.failed,
        ).status,
        ApprovalStatus.failed,
      );
    },
  );

  testWidgets('forget approval shows the memory text instead of its id', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        ApprovalCard(
          approval: const ApprovalEntry(
            id: 'a9',
            toolName: 'ForgetMemory',
            argumentsJson: '{"memoryId":"0199-abc"}',
          ),
          onDecide: (_) {},
          loadMemoryText: (id) async => id == '0199-abc' ? 'I like jazz' : null,
        ),
      ),
    );
    await tester.pumpAndSettle();

    expect(find.textContaining('I like jazz', findRichText: true), findsOneWidget);
    expect(find.textContaining('0199-abc', findRichText: true), findsNothing);
  });

  testWidgets('pending Approve ignores a leftover decline decision', (
    tester,
  ) async {
    final decisions = <bool>[];
    await tester.pumpWidget(
      _host(
        ApprovalCard(
          approval: const ApprovalEntry(
            id: 'a1',
            toolName: 'ForgetMemory',
            argumentsJson: '{}',
            decision: false,
          ),
          onDecide: decisions.add,
        ),
      ),
    );

    await tester.tap(find.text('Approve'));
    expect(decisions, [true]);
  });

  testWidgets('declined retry still resubmits the previous decline', (
    tester,
  ) async {
    final decisions = <bool>[];
    await tester.pumpWidget(
      _host(
        ApprovalCard(
          approval: const ApprovalEntry(
            id: 'a3',
            toolName: 'ForgetMemory',
            argumentsJson: '{}',
            retry: true,
            decision: false,
          ),
          onDecide: decisions.add,
        ),
      ),
    );

    await tester.tap(find.text('Retry'));
    expect(decisions, [false]);
  });

  testWidgets(
    'failed approval cards retry the leftover decision and hide Decline',
    (tester) async {
      final decisions = <bool>[];
      await tester.pumpWidget(
        _host(
          ApprovalCard(
            approval: const ApprovalEntry(
              id: 'a4',
              toolName: 'ForgetMemory',
              argumentsJson: '{}',
              status: ApprovalStatus.failed,
              decision: false,
              error: 'Jarvis could not finish this step. You can retry.',
            ),
            onDecide: decisions.add,
          ),
        ),
      );

      expect(find.text('Declined, but not finished'), findsOneWidget);
      expect(find.text('Retry'), findsOneWidget);
      expect(find.text('Decline'), findsNothing);
      await tester.tap(find.text('Retry'));
      expect(decisions, [false]);
    },
  );

  testWidgets('decided and retryable approval cards change their actions', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        Column(
          children: [
            ApprovalCard(
              approval: const ApprovalEntry(
                id: 'a1',
                toolName: 'ForgetMemory',
                argumentsJson: '{}',
                status: ApprovalStatus.approved,
              ),
              onDecide: (_) {},
            ),
            ApprovalCard(
              approval: const ApprovalEntry(
                id: 'a2',
                toolName: 'AddMcpServer',
                argumentsJson: '{}',
                retry: true,
                decision: true,
              ),
              onDecide: (_) {},
            ),
            ApprovalCard(
              approval: const ApprovalEntry(
                id: 'a3',
                toolName: 'ForgetMemory',
                argumentsJson: '{}',
                retry: true,
                decision: false,
              ),
              onDecide: (_) {},
            ),
          ],
        ),
      ),
    );
    expect(find.text('Approved'), findsOneWidget);
    expect(find.text('Approved, but not finished'), findsOneWidget);
    expect(find.text('Declined, but not finished'), findsOneWidget);
    expect(find.text('Retry'), findsNWidgets(2));
    expect(find.text('Decline'), findsNothing);
  });

  testWidgets('composer sends on tap and blocks empty input', (tester) async {
    final controller = TextEditingController();
    var sent = 0;
    await tester.pumpWidget(
      _host(
        ChatComposer(
          controller: controller,
          onSend: () => sent++,
          onVoice: null,
          sending: false,
          voiceActive: false,
          voiceStarting: false,
        ),
      ),
    );

    await tester.tap(find.byTooltip('Send'));
    expect(sent, 0);

    await tester.enterText(find.byType(TextField), 'Hi Jarvis');
    await tester.pump();
    await tester.tap(find.byTooltip('Send'));
    expect(sent, 1);
  });

  testWidgets('composer stop stays enabled while voice is starting', (
    tester,
  ) async {
    var stopped = 0;
    await tester.pumpWidget(
      _host(
        ChatComposer(
          controller: TextEditingController(),
          onSend: () {},
          onVoice: () => stopped++,
          sending: false,
          voiceActive: false,
          voiceStarting: true,
        ),
      ),
    );

    await tester.tap(find.byTooltip('Stop voice'));
    expect(stopped, 1);
  });

  testWidgets('composer stop cancels an in-flight send', (tester) async {
    var cancelled = 0;
    await tester.pumpWidget(
      _host(
        ChatComposer(
          controller: TextEditingController(),
          onSend: () {},
          onCancel: () => cancelled++,
          onVoice: null,
          sending: true,
          voiceActive: false,
          voiceStarting: false,
        ),
      ),
    );

    await tester.tap(find.byTooltip('Stop'));
    expect(cancelled, 1);
  });

  testWidgets('composer awaiting approval disables send without a spinner', (
    tester,
  ) async {
    await tester.pumpWidget(
      _host(
        ChatComposer(
          controller: TextEditingController(text: 'Approve first'),
          onSend: () {},
          onVoice: null,
          sending: false,
          awaitingApproval: true,
          voiceActive: false,
          voiceStarting: false,
        ),
      ),
    );

    expect(find.byType(CircularProgressIndicator), findsNothing);
    expect(
      tester
          .widget<IconButton>(
            find.ancestor(
              of: find.byTooltip('Send'),
              matching: find.byType(IconButton),
            ),
          )
          .onPressed,
      isNull,
    );
  });

  testWidgets('suggestion chips send their prompt', (tester) async {
    String? selected;
    await tester.pumpWidget(
      _host(SuggestionChips(onSelected: (text) => selected = text)),
    );
    await tester.tap(find.text('What do you know about me?'));
    expect(selected, 'What do you know about me?');
    final setup = find.textContaining('Show the setup card.');
    await tester.ensureVisible(setup);
    await tester.tap(setup);
    expect(
      selected,
      'Help me add or manage an MCP server or integration in this chat. Show the setup card.',
    );
  });
}
