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
  });

  test('tool catalog describes known and unknown tools', () {
    expect(describeTool('CreateReminder').done, 'Scheduled a reminder');
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
          ],
        ),
      ),
    );
    expect(find.text('Approved'), findsOneWidget);
    expect(find.text('Approved, but not finished'), findsOneWidget);
    expect(find.text('Retry'), findsOneWidget);
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

  testWidgets('composer stop stays enabled while voice is starting', (tester) async {
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

  testWidgets('suggestion chips send their prompt', (tester) async {
    String? selected;
    await tester.pumpWidget(
      _host(SuggestionChips(onSelected: (text) => selected = text)),
    );
    await tester.tap(find.text('What do you know about me?'));
    expect(selected, 'What do you know about me?');
  });
}
