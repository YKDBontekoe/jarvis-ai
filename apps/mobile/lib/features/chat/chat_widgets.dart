import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../theme.dart';
import 'chat_entries.dart';
import 'tool_catalog.dart';

class MessageBubble extends StatefulWidget {
  const MessageBubble({required this.message, this.onRetry, super.key});

  final MessageEntry message;
  final VoidCallback? onRetry;

  @override
  State<MessageBubble> createState() => _MessageBubbleState();
}

class _MessageBubbleState extends State<MessageBubble> {
  bool _copied = false;

  Future<void> _copy() async {
    await Clipboard.setData(ClipboardData(text: widget.message.content));
    if (!mounted) return;
    setState(() => _copied = true);
    await Future<void>.delayed(const Duration(seconds: 2));
    if (mounted) setState(() => _copied = false);
  }

  @override
  Widget build(BuildContext context) {
    final message = widget.message;
    if (message.isUser) return _userBubble(context, message);
    if (message.pending && message.content.isEmpty) {
      return const Padding(
        padding: EdgeInsets.only(bottom: 16, left: 2),
        child: Align(alignment: Alignment.centerLeft, child: TypingIndicator()),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: 18),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Padding(
            padding: EdgeInsets.only(top: 2, right: 12),
            child: JarvisAvatar(size: 26),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                JarvisMarkdown(data: message.content),
                if (!message.pending)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: IconButton(
                      tooltip: _copied ? 'Copied' : 'Copy reply',
                      visualDensity: VisualDensity.compact,
                      iconSize: 16,
                      color: JarvisColors.muted,
                      onPressed: _copy,
                      icon: Icon(
                        _copied ? Icons.check_rounded : Icons.copy_rounded,
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _userBubble(BuildContext context, MessageEntry message) => Align(
    alignment: Alignment.centerRight,
    child: Padding(
      padding: const EdgeInsets.only(bottom: 16, left: 48),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 11),
            decoration: BoxDecoration(
              color: JarvisColors.userBubble,
              borderRadius: const BorderRadius.only(
                topLeft: Radius.circular(20),
                topRight: Radius.circular(20),
                bottomLeft: Radius.circular(20),
                bottomRight: Radius.circular(6),
              ),
              border: message.failed
                  ? Border.all(color: JarvisColors.danger.withValues(alpha: .6))
                  : null,
            ),
            child: SelectableText(
              message.content,
              style: const TextStyle(fontSize: 15.5, height: 1.45),
            ),
          ),
          if (message.failed)
            TextButton.icon(
              onPressed: widget.onRetry,
              style: TextButton.styleFrom(
                foregroundColor: JarvisColors.danger,
                visualDensity: VisualDensity.compact,
              ),
              icon: const Icon(Icons.refresh_rounded, size: 16),
              label: const Text('Not sent — tap to retry'),
            ),
        ],
      ),
    ),
  );
}

class JarvisMarkdown extends StatelessWidget {
  const JarvisMarkdown({required this.data, super.key});

  final String data;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    const body = TextStyle(fontSize: 15.5, height: 1.55);
    final sheet = MarkdownStyleSheet.fromTheme(theme).copyWith(
      p: body,
      listBullet: body,
      h1: const TextStyle(
        fontSize: 22,
        fontWeight: FontWeight.w700,
        height: 1.3,
      ),
      h2: const TextStyle(
        fontSize: 19,
        fontWeight: FontWeight.w700,
        height: 1.3,
      ),
      h3: const TextStyle(
        fontSize: 17,
        fontWeight: FontWeight.w600,
        height: 1.3,
      ),
      strong: const TextStyle(fontWeight: FontWeight.w700),
      a: const TextStyle(
        color: JarvisColors.accent,
        decoration: TextDecoration.underline,
        decorationColor: JarvisColors.accent,
      ),
      code: TextStyle(
        fontFamily: 'monospace',
        fontSize: 13.5,
        backgroundColor: JarvisColors.surfaceRaised,
        color: theme.colorScheme.onSurface,
      ),
      codeblockPadding: const EdgeInsets.all(14),
      codeblockDecoration: BoxDecoration(
        color: const Color(0xff12131b),
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: JarvisColors.outline),
      ),
      blockquote: const TextStyle(color: JarvisColors.muted, height: 1.5),
      blockquotePadding: const EdgeInsets.fromLTRB(14, 6, 10, 6),
      blockquoteDecoration: const BoxDecoration(
        border: Border(left: BorderSide(color: JarvisColors.accent, width: 3)),
      ),
      tableHead: const TextStyle(fontWeight: FontWeight.w600),
      tableBody: const TextStyle(fontSize: 14.5),
      tableBorder: TableBorder.all(color: JarvisColors.outline),
      tableCellsPadding: const EdgeInsets.symmetric(
        horizontal: 10,
        vertical: 7,
      ),
      horizontalRuleDecoration: const BoxDecoration(
        border: Border(top: BorderSide(color: JarvisColors.outline)),
      ),
    );
    return MarkdownBody(
      data: data,
      selectable: true,
      styleSheet: sheet,
      onTapLink: (text, href, title) {
        final uri = href == null ? null : Uri.tryParse(href);
        if (uri != null && (uri.scheme == 'https' || uri.scheme == 'http')) {
          launchUrl(uri, mode: LaunchMode.externalApplication);
        }
      },
    );
  }
}

class ToolRunView extends StatelessWidget {
  const ToolRunView({required this.run, super.key});

  final ToolRunEntry run;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(left: 38, bottom: 14),
    child: Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [for (final step in run.steps) _ToolChip(step: step)],
    ),
  );
}

class _ToolChip extends StatelessWidget {
  const _ToolChip({required this.step});

  final ToolStep step;

  @override
  Widget build(BuildContext context) {
    final description = describeTool(step.tool);
    final (label, color) = switch (step.status) {
      ToolStepStatus.running => (description.active, JarvisColors.accent),
      ToolStepStatus.completed => (description.done, JarvisColors.success),
      ToolStepStatus.failed => (description.failed, JarvisColors.danger),
    };
    return AnimatedContainer(
      duration: const Duration(milliseconds: 250),
      padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 6),
      decoration: BoxDecoration(
        color: color.withValues(alpha: .08),
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: color.withValues(alpha: .28)),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(description.icon, size: 15, color: color),
          const SizedBox(width: 6),
          Text(label, style: const TextStyle(fontSize: 12.5)),
          const SizedBox(width: 6),
          switch (step.status) {
            ToolStepStatus.running => SizedBox.square(
              dimension: 11,
              child: CircularProgressIndicator(strokeWidth: 1.6, color: color),
            ),
            ToolStepStatus.completed => Icon(
              Icons.check_rounded,
              size: 14,
              color: color,
            ),
            ToolStepStatus.failed => Icon(
              Icons.error_outline_rounded,
              size: 14,
              color: color,
            ),
          },
        ],
      ),
    );
  }
}

class ApprovalCard extends StatelessWidget {
  const ApprovalCard({
    required this.approval,
    required this.onDecide,
    super.key,
  });

  final ApprovalEntry approval;
  final void Function(bool approved) onDecide;

  @override
  Widget build(BuildContext context) {
    final description = describeTool(approval.toolName);
    final arguments = approval.arguments.entries
        .where((entry) => entry.value != null && '${entry.value}'.isNotEmpty)
        .toList();
    final decided =
        approval.status == ApprovalStatus.approved ||
        approval.status == ApprovalStatus.denied;
    final accent = switch (approval.status) {
      ApprovalStatus.approved => JarvisColors.success,
      ApprovalStatus.denied => JarvisColors.muted,
      ApprovalStatus.failed => JarvisColors.danger,
      _ => JarvisColors.warning,
    };
    return Padding(
      padding: const EdgeInsets.only(left: 38, bottom: 16),
      child: Container(
        decoration: BoxDecoration(
          color: JarvisColors.surface,
          borderRadius: BorderRadius.circular(16),
          border: Border.all(color: accent.withValues(alpha: .45)),
        ),
        padding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                Icon(
                  decided
                      ? (approval.status == ApprovalStatus.approved
                            ? Icons.verified_user_outlined
                            : Icons.block_rounded)
                      : Icons.gpp_maybe_outlined,
                  color: accent,
                  size: 20,
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(switch (approval.status) {
                    ApprovalStatus.approved => 'Approved',
                    ApprovalStatus.denied => 'Declined',
                    _ =>
                      approval.retry
                          ? 'Approved, but not finished'
                          : 'Jarvis needs your approval',
                  }, style: const TextStyle(fontWeight: FontWeight.w600)),
                ),
              ],
            ),
            const SizedBox(height: 8),
            Row(
              children: [
                Icon(description.icon, size: 16, color: JarvisColors.muted),
                const SizedBox(width: 8),
                Expanded(
                  child: Text(
                    description.active,
                    style: const TextStyle(color: JarvisColors.muted),
                  ),
                ),
              ],
            ),
            if (arguments.isNotEmpty) ...[
              const SizedBox(height: 10),
              Container(
                width: double.infinity,
                padding: const EdgeInsets.all(10),
                decoration: BoxDecoration(
                  color: const Color(0xff12131b),
                  borderRadius: BorderRadius.circular(10),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    for (final entry in arguments.take(6))
                      Padding(
                        padding: const EdgeInsets.symmetric(vertical: 2),
                        child: Text.rich(
                          TextSpan(
                            children: [
                              TextSpan(
                                text: '${humanizeToolName(entry.key)}  ',
                                style: const TextStyle(
                                  color: JarvisColors.muted,
                                  fontSize: 12.5,
                                ),
                              ),
                              TextSpan(
                                text: '${entry.value}',
                                style: const TextStyle(
                                  fontFamily: 'monospace',
                                  fontSize: 12.5,
                                ),
                              ),
                            ],
                          ),
                          maxLines: 3,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                  ],
                ),
              ),
            ],
            if (approval.error case final error?) ...[
              const SizedBox(height: 10),
              Text(error, style: const TextStyle(color: JarvisColors.danger)),
            ],
            if (!decided) ...[
              const SizedBox(height: 12),
              Row(
                mainAxisAlignment: MainAxisAlignment.end,
                children: [
                  if (!approval.retry)
                    TextButton(
                      onPressed: approval.status == ApprovalStatus.submitting
                          ? null
                          : () => onDecide(false),
                      child: const Text('Decline'),
                    ),
                  const SizedBox(width: 8),
                  FilledButton.icon(
                    onPressed: approval.status == ApprovalStatus.submitting
                        ? null
                        : () => onDecide(approval.decision ?? true),
                    icon: approval.status == ApprovalStatus.submitting
                        ? const SizedBox.square(
                            dimension: 14,
                            child: CircularProgressIndicator(strokeWidth: 2),
                          )
                        : Icon(
                            approval.retry ||
                                    approval.status == ApprovalStatus.failed
                                ? Icons.refresh_rounded
                                : Icons.check_rounded,
                            size: 18,
                          ),
                    label: Text(
                      approval.retry || approval.status == ApprovalStatus.failed
                          ? 'Retry'
                          : 'Approve',
                    ),
                  ),
                ],
              ),
            ],
          ],
        ),
      ),
    );
  }
}

class TypingIndicator extends StatefulWidget {
  const TypingIndicator({super.key});

  @override
  State<TypingIndicator> createState() => _TypingIndicatorState();
}

class _TypingIndicatorState extends State<TypingIndicator>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1100),
  )..repeat();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Semantics(
    label: 'Jarvis is thinking',
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        const JarvisAvatar(size: 26),
        const SizedBox(width: 12),
        AnimatedBuilder(
          animation: _controller,
          builder: (context, _) => Row(
            children: [
              for (var i = 0; i < 3; i++)
                Container(
                  margin: const EdgeInsets.symmetric(horizontal: 2.5),
                  width: 7,
                  height: 7,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    color: JarvisColors.accent.withValues(
                      alpha:
                          .25 + .75 * _pulse((_controller.value + i * .18) % 1),
                    ),
                  ),
                ),
            ],
          ),
        ),
      ],
    ),
  );

  static double _pulse(double t) => t < .5 ? t * 2 : (1 - t) * 2;
}

class JarvisAvatar extends StatelessWidget {
  const JarvisAvatar({required this.size, super.key});

  final double size;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: const BoxDecoration(
      shape: BoxShape.circle,
      gradient: LinearGradient(
        begin: Alignment.topLeft,
        end: Alignment.bottomRight,
        colors: [Color(0xffd6ceff), Color(0xff8f7ff0), Color(0xff5a4fa0)],
      ),
    ),
    child: Icon(
      Icons.blur_on_rounded,
      size: size * .66,
      color: const Color(0xff1b1830),
    ),
  );
}

class ChatComposer extends StatefulWidget {
  const ChatComposer({
    required this.controller,
    required this.onSend,
    required this.onVoice,
    required this.sending,
    required this.voiceActive,
    required this.voiceStarting,
    this.hint = 'Message Jarvis',
    super.key,
  });

  final TextEditingController controller;
  final VoidCallback onSend;
  final VoidCallback? onVoice;
  final bool sending;
  final bool voiceActive;
  final bool voiceStarting;
  final String hint;

  @override
  State<ChatComposer> createState() => _ChatComposerState();
}

class _ChatComposerState extends State<ChatComposer> {
  late final FocusNode _focus = FocusNode(onKeyEvent: _onKey);

  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_changed);
  }

  @override
  void didUpdateWidget(ChatComposer oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller != widget.controller) {
      oldWidget.controller.removeListener(_changed);
      widget.controller.addListener(_changed);
    }
  }

  void _changed() => setState(() {});

  KeyEventResult _onKey(FocusNode node, KeyEvent event) {
    if (event is KeyDownEvent &&
        event.logicalKey == LogicalKeyboardKey.enter &&
        !HardwareKeyboard.instance.isShiftPressed) {
      if (_canSend) widget.onSend();
      return KeyEventResult.handled;
    }
    return KeyEventResult.ignored;
  }

  bool get _inputEnabled => !widget.voiceActive && !widget.voiceStarting;

  bool get _canSend =>
      _inputEnabled &&
      !widget.sending &&
      widget.controller.text.trim().isNotEmpty;

  @override
  void dispose() {
    widget.controller.removeListener(_changed);
    _focus.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.fromLTRB(6, 6, 6, 6),
    decoration: BoxDecoration(
      color: JarvisColors.surface,
      borderRadius: BorderRadius.circular(28),
      border: Border.all(color: JarvisColors.outline),
      boxShadow: [
        BoxShadow(
          color: Colors.black.withValues(alpha: .25),
          blurRadius: 24,
          offset: const Offset(0, 8),
        ),
      ],
    ),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.end,
      children: [
        Expanded(
          child: TextField(
            controller: widget.controller,
            focusNode: _focus,
            enabled: _inputEnabled,
            minLines: 1,
            maxLines: 6,
            textInputAction: TextInputAction.newline,
            keyboardType: TextInputType.multiline,
            style: const TextStyle(fontSize: 15.5),
            decoration: InputDecoration(
              hintText: widget.voiceActive ? 'Listening…' : widget.hint,
              fillColor: Colors.transparent,
              contentPadding: const EdgeInsets.fromLTRB(16, 13, 8, 13),
            ),
          ),
        ),
        IconButton(
          tooltip: widget.voiceActive ? 'Stop voice' : 'Talk to Jarvis',
          onPressed: widget.voiceStarting || widget.sending
              ? null
              : widget.onVoice,
          color: widget.voiceActive ? JarvisColors.danger : JarvisColors.muted,
          icon: widget.voiceStarting
              ? const SizedBox.square(
                  dimension: 20,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : Icon(
                  widget.voiceActive
                      ? Icons.stop_rounded
                      : Icons.mic_none_rounded,
                ),
          style: IconButton.styleFrom(minimumSize: const Size(46, 46)),
        ),
        const SizedBox(width: 2),
        AnimatedScale(
          duration: const Duration(milliseconds: 150),
          scale: _canSend || widget.sending ? 1 : .92,
          child: IconButton.filled(
            tooltip: 'Send',
            onPressed: _canSend ? widget.onSend : null,
            icon: widget.sending
                ? const SizedBox.square(
                    dimension: 18,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : const Icon(Icons.arrow_upward_rounded),
            style: IconButton.styleFrom(minimumSize: const Size(46, 46)),
          ),
        ),
      ],
    ),
  );
}

class SuggestionChips extends StatelessWidget {
  const SuggestionChips({required this.onSelected, super.key});

  final ValueChanged<String>? onSelected;

  static const suggestions = [
    (Icons.alarm_add_rounded, 'Remind me to stretch in 20 minutes'),
    (Icons.psychology_outlined, 'What do you know about me?'),
    (Icons.alarm_rounded, 'What reminders do I have?'),
    (
      Icons.travel_explore_rounded,
      'Research the best espresso grinders in the background',
    ),
  ];

  @override
  Widget build(BuildContext context) => Wrap(
    alignment: WrapAlignment.center,
    spacing: 8,
    runSpacing: 8,
    children: [
      for (final (icon, text) in suggestions)
        ActionChip(
          avatar: Icon(icon, size: 16, color: JarvisColors.accent),
          label: Text(text),
          onPressed: onSelected == null ? null : () => onSelected!(text),
        ),
    ],
  );
}
