import 'package:flutter/material.dart';
import '../../ui/phosphor_icons.dart';
import 'package:flutter/services.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
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
        padding: EdgeInsets.only(bottom: 18),
        child: Align(alignment: Alignment.centerLeft, child: TypingIndicator()),
      );
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: 22),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          const Padding(
            padding: EdgeInsets.only(top: 1, right: 12),
            child: JarvisAvatar(size: 28),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.only(top: 3),
                  child: JarvisMarkdown(data: message.content),
                ),
                if (!message.pending)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: _CopyButton(copied: _copied, onPressed: _copy),
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
      padding: const EdgeInsets.only(bottom: 18, left: 56),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 11),
            decoration: BoxDecoration(
              color: message.failed
                  ? JarvisColors.dangerSoft
                  : JarvisColors.surfaceRaised,
              borderRadius: const BorderRadius.only(
                topLeft: Radius.circular(20),
                topRight: Radius.circular(20),
                bottomLeft: Radius.circular(20),
                bottomRight: Radius.circular(6),
              ),
              border: message.failed
                  ? Border.all(
                      color: JarvisColors.danger.withValues(alpha: .35),
                    )
                  : null,
            ),
            child: SelectableText(
              message.content,
              style: TextStyle(
                fontSize: 15.5,
                height: 1.45,
                color: JarvisColors.ink,
              ),
            ),
          ),
          if (message.failed)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: TextButton.icon(
                onPressed: widget.onRetry,
                style: TextButton.styleFrom(
                  foregroundColor: JarvisColors.danger,
                  visualDensity: VisualDensity.compact,
                  textStyle: const TextStyle(
                    fontSize: 13,
                    fontWeight: FontWeight.w600,
                  ),
                ),
                icon: const Icon(
                  PhosphorIconsRegular.arrowsClockwise,
                  size: 16,
                ),
                label: const Text('Not sent — tap to retry'),
              ),
            ),
        ],
      ),
    ),
  );
}

class _CopyButton extends StatelessWidget {
  const _CopyButton({required this.copied, required this.onPressed});

  final bool copied;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) => Tooltip(
    message: copied ? 'Copied' : 'Copy reply',
    child: TextButton.icon(
      onPressed: onPressed,
      style: TextButton.styleFrom(
        foregroundColor: copied ? JarvisColors.success : JarvisColors.muted,
        visualDensity: VisualDensity.compact,
        minimumSize: const Size(0, 30),
        padding: const EdgeInsets.symmetric(horizontal: 8),
        textStyle: const TextStyle(fontSize: 12.5, fontWeight: FontWeight.w500),
      ),
      icon: Icon(
        copied ? PhosphorIconsRegular.check : PhosphorIconsRegular.copySimple,
        size: 15,
      ),
      label: Text(copied ? 'Copied' : 'Copy'),
    ),
  );
}

class JarvisMarkdown extends StatelessWidget {
  const JarvisMarkdown({required this.data, super.key});

  final String data;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    const body = TextStyle(
      fontSize: 15.5,
      height: 1.6,
      color: JarvisColors.ink,
      letterSpacing: -.1,
    );
    final sheet = MarkdownStyleSheet.fromTheme(theme).copyWith(
      p: body,
      listBullet: body.copyWith(color: JarvisColors.muted),
      h1: const TextStyle(
        fontSize: 22,
        fontWeight: FontWeight.w700,
        height: 1.3,
        letterSpacing: -.5,
        color: JarvisColors.ink,
      ),
      h2: const TextStyle(
        fontSize: 19,
        fontWeight: FontWeight.w700,
        height: 1.3,
        letterSpacing: -.4,
        color: JarvisColors.ink,
      ),
      h3: const TextStyle(
        fontSize: 17,
        fontWeight: FontWeight.w600,
        height: 1.3,
        color: JarvisColors.ink,
      ),
      strong: const TextStyle(fontWeight: FontWeight.w700),
      a: const TextStyle(
        color: JarvisColors.ink,
        fontWeight: FontWeight.w500,
        decoration: TextDecoration.underline,
        decorationColor: JarvisColors.outlineStrong,
      ),
      code: const TextStyle(
        fontFamily: 'monospace',
        fontSize: 13.5,
        backgroundColor: JarvisColors.surfaceMuted,
        color: JarvisColors.ink,
      ),
      codeblockPadding: const EdgeInsets.all(16),
      codeblockDecoration: BoxDecoration(
        color: JarvisColors.canvas,
        borderRadius: BorderRadius.circular(JarvisRadii.md),
        border: Border.all(color: JarvisColors.outline),
      ),
      blockquote: const TextStyle(color: JarvisColors.inkSoft, height: 1.55),
      blockquotePadding: const EdgeInsets.fromLTRB(14, 8, 12, 8),
      blockquoteDecoration: const BoxDecoration(
        border: Border(
          left: BorderSide(color: JarvisColors.outlineStrong, width: 2),
        ),
      ),
      tableHead: const TextStyle(
        fontWeight: FontWeight.w600,
        color: JarvisColors.ink,
      ),
      tableBody: const TextStyle(fontSize: 14.5, color: JarvisColors.ink),
      tableHeadAlign: TextAlign.left,
      tableBorder: TableBorder.all(
        color: JarvisColors.outline,
        borderRadius: BorderRadius.circular(10),
      ),
      tableCellsDecoration: const BoxDecoration(color: JarvisColors.surface),
      tableCellsPadding: const EdgeInsets.symmetric(
        horizontal: 12,
        vertical: 8,
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
    padding: const EdgeInsets.only(left: 40, bottom: 14),
    child: Wrap(
      spacing: 6,
      runSpacing: 6,
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
      ToolStepStatus.running => (description.active, JarvisColors.inkSoft),
      ToolStepStatus.completed => (description.done, JarvisColors.success),
      ToolStepStatus.failed => (description.failed, JarvisColors.danger),
    };
    return AnimatedContainer(
      duration: const Duration(milliseconds: 250),
      padding: const EdgeInsets.fromLTRB(10, 6, 10, 6),
      decoration: BoxDecoration(
        color: JarvisColors.surface,
        borderRadius: BorderRadius.circular(8),
        border: Border.all(color: JarvisColors.outline),
      ),
      child: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          Icon(description.icon, size: 14, color: JarvisColors.inkSoft),
          const SizedBox(width: 7),
          Text(
            label,
            style: const TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w500,
              color: JarvisColors.inkSoft,
            ),
          ),
          const SizedBox(width: 7),
          switch (step.status) {
            ToolStepStatus.running => SizedBox.square(
              dimension: 11,
              child: CircularProgressIndicator(strokeWidth: 1.6, color: color),
            ),
            ToolStepStatus.completed => Icon(
              PhosphorIconsRegular.check,
              size: 14,
              color: color,
            ),
            ToolStepStatus.failed => Icon(
              PhosphorIconsRegular.x,
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
    final submitting = approval.status == ApprovalStatus.submitting;
    return Padding(
      padding: const EdgeInsets.only(left: 40, bottom: 18),
      child: Container(
        decoration: BoxDecoration(
          color: JarvisColors.surface,
          borderRadius: BorderRadius.circular(JarvisRadii.lg),
          border: Border.all(color: JarvisColors.outline),
          boxShadow: decided ? null : JarvisShadows.soft,
        ),
        clipBehavior: Clip.antiAlias,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Container(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
              decoration: const BoxDecoration(
                color: JarvisColors.canvas,
                border: Border(bottom: BorderSide(color: JarvisColors.outline)),
              ),
              child: Row(
                children: [
                  Icon(
                    decided
                        ? (approval.status == ApprovalStatus.approved
                              ? PhosphorIconsFill.shieldCheck
                              : PhosphorIconsRegular.prohibit)
                        : PhosphorIconsRegular.shieldCheck,
                    color: accent,
                    size: 19,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      switch (approval.status) {
                        ApprovalStatus.approved => 'Approved',
                        ApprovalStatus.denied => 'Declined',
                        _ =>
                          approval.retry
                              ? 'Approved, but not finished'
                              : 'Jarvis needs your approval',
                      },
                      style: const TextStyle(
                        fontWeight: FontWeight.w600,
                        fontSize: 14.5,
                        color: JarvisColors.ink,
                      ),
                    ),
                  ),
                ],
              ),
            ),
            Padding(
              padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      IconBadge(icon: description.icon, size: 30),
                      const SizedBox(width: 10),
                      Expanded(
                        child: Text(
                          description.active,
                          style: const TextStyle(
                            color: JarvisColors.ink,
                            fontWeight: FontWeight.w500,
                          ),
                        ),
                      ),
                    ],
                  ),
                  if (arguments.isNotEmpty) ...[
                    const SizedBox(height: 12),
                    Container(
                      width: double.infinity,
                      padding: const EdgeInsets.all(12),
                      decoration: BoxDecoration(
                        color: JarvisColors.canvas,
                        borderRadius: BorderRadius.circular(JarvisRadii.sm),
                        border: Border.all(color: JarvisColors.outline),
                      ),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          for (final entry in arguments.take(6))
                            Padding(
                              padding: const EdgeInsets.symmetric(vertical: 3),
                              child: Text.rich(
                                TextSpan(
                                  children: [
                                    TextSpan(
                                      text: '${humanizeToolName(entry.key)}  ',
                                      style: const TextStyle(
                                        color: JarvisColors.muted,
                                        fontSize: 12.5,
                                        fontWeight: FontWeight.w500,
                                      ),
                                    ),
                                    TextSpan(
                                      text: '${entry.value}',
                                      style: const TextStyle(
                                        fontFamily: 'monospace',
                                        fontSize: 12.5,
                                        color: JarvisColors.ink,
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
                    const SizedBox(height: 12),
                    InlineNotice(message: error, tone: NoticeTone.danger),
                  ],
                  if (!decided) ...[
                    const SizedBox(height: 14),
                    Row(
                      mainAxisAlignment: MainAxisAlignment.end,
                      children: [
                        if (!approval.retry)
                          OutlinedButton(
                            onPressed: submitting
                                ? null
                                : () => onDecide(false),
                            style: OutlinedButton.styleFrom(
                              minimumSize: const Size(0, 42),
                            ),
                            child: const Text('Decline'),
                          ),
                        const SizedBox(width: 8),
                        FilledButton.icon(
                          onPressed: submitting
                              ? null
                              : () => onDecide(approval.decision ?? true),
                          style: FilledButton.styleFrom(
                            minimumSize: const Size(0, 42),
                          ),
                          icon: submitting
                              ? const SizedBox.square(
                                  dimension: 14,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                  ),
                                )
                              : Icon(
                                  approval.retry ||
                                          approval.status ==
                                              ApprovalStatus.failed
                                      ? PhosphorIconsRegular.arrowsClockwise
                                      : PhosphorIconsRegular.check,
                                  size: 18,
                                ),
                          label: Text(
                            approval.retry ||
                                    approval.status == ApprovalStatus.failed
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
        const JarvisAvatar(size: 28),
        const SizedBox(width: 12),
        Container(
          padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
          decoration: BoxDecoration(
            color: JarvisColors.surface,
            borderRadius: BorderRadius.circular(18),
            border: Border.all(color: JarvisColors.outline),
          ),
          child: AnimatedBuilder(
            animation: _controller,
            builder: (context, _) => Row(
              children: [
                for (var i = 0; i < 3; i++)
                  Transform.translate(
                    offset: Offset(
                      0,
                      -3 * _pulse((_controller.value + i * .18) % 1),
                    ),
                    child: Container(
                      margin: const EdgeInsets.symmetric(horizontal: 2.5),
                      width: 7,
                      height: 7,
                      decoration: BoxDecoration(
                        shape: BoxShape.circle,
                        color: Color.lerp(
                          JarvisColors.outlineStrong,
                          JarvisColors.inkSoft,
                          _pulse((_controller.value + i * .18) % 1),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
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
  Widget build(BuildContext context) => JarvisOrb(size: size, glow: false);
}

class ChatComposer extends StatefulWidget {
  const ChatComposer({
    required this.controller,
    required this.onSend,
    required this.onVoice,
    required this.sending,
    required this.voiceActive,
    required this.voiceStarting,
    this.onAttach,
    this.hint = 'Ask Jarvis anything',
    super.key,
  });

  final TextEditingController controller;
  final VoidCallback onSend;
  final VoidCallback? onVoice;
  final bool sending;
  final bool voiceActive;
  final bool voiceStarting;

  /// Opens extra actions (files, tasks, reminders); hidden when null.
  final VoidCallback? onAttach;
  final String hint;

  @override
  State<ChatComposer> createState() => _ChatComposerState();
}

class _ChatComposerState extends State<ChatComposer> {
  late final FocusNode _focus = FocusNode(onKeyEvent: _onKey)
    ..addListener(_changed);

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
  Widget build(BuildContext context) {
    final focused = _focus.hasFocus;
    final hasText = widget.controller.text.trim().isNotEmpty;
    final showVoice =
        widget.onVoice != null &&
        !hasText &&
        !widget.sending &&
        !widget.voiceActive;
    return AnimatedContainer(
      duration: const Duration(milliseconds: 200),
      padding: const EdgeInsets.fromLTRB(6, 4, 8, 8),
      decoration: BoxDecoration(
        color: JarvisColors.surface,
        borderRadius: BorderRadius.circular(26),
        border: Border.all(
          color: focused ? JarvisColors.outlineStrong : JarvisColors.outline,
        ),
        boxShadow: JarvisShadows.floating,
      ),
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          TextField(
            controller: widget.controller,
            focusNode: _focus,
            enabled: _inputEnabled,
            minLines: 1,
            maxLines: 6,
            textInputAction: TextInputAction.newline,
            keyboardType: TextInputType.multiline,
            style: const TextStyle(fontSize: 16, color: JarvisColors.ink),
            decoration: InputDecoration(
              hintText: widget.voiceActive ? 'Listening…' : widget.hint,
              hintStyle: const TextStyle(
                fontSize: 16,
                color: JarvisColors.muted,
              ),
              filled: false,
              border: InputBorder.none,
              enabledBorder: InputBorder.none,
              focusedBorder: InputBorder.none,
              disabledBorder: InputBorder.none,
              contentPadding: const EdgeInsets.fromLTRB(12, 12, 8, 8),
            ),
          ),
          Row(
            children: [
              if (widget.onAttach != null)
                _ComposerIconButton(
                  icon: PhosphorIconsRegular.plus,
                  tooltip: 'More actions',
                  onPressed: widget.onAttach,
                ),
              const Spacer(),
              if (widget.voiceActive || widget.voiceStarting)
                _ComposerIconButton(
                  icon: PhosphorIconsRegular.stop,
                  tooltip: 'Stop voice',
                  danger: true,
                  onPressed: widget.onVoice,
                )
              else
                AnimatedSwitcher(
                  duration: const Duration(milliseconds: 180),
                  transitionBuilder: (child, animation) =>
                      ScaleTransition(scale: animation, child: child),
                  child: showVoice
                      ? IconButton.filled(
                          key: const ValueKey('voice'),
                          tooltip: 'Voice mode',
                          onPressed: widget.onVoice,
                          icon: const Icon(
                            PhosphorIconsBold.waveform,
                            size: 18,
                          ),
                          style: IconButton.styleFrom(
                            minimumSize: const Size(38, 38),
                            backgroundColor: JarvisColors.ink,
                            foregroundColor: Colors.white,
                          ),
                        )
                      : IconButton.filled(
                          key: const ValueKey('send'),
                          tooltip: 'Send',
                          onPressed: _canSend ? widget.onSend : null,
                          icon: widget.sending
                              ? const SizedBox.square(
                                  dimension: 16,
                                  child: CircularProgressIndicator(
                                    strokeWidth: 2,
                                    color: Colors.white,
                                  ),
                                )
                              : const Icon(PhosphorIconsBold.arrowUp, size: 18),
                          style: IconButton.styleFrom(
                            minimumSize: const Size(38, 38),
                            backgroundColor: JarvisColors.ink,
                            foregroundColor: Colors.white,
                            disabledBackgroundColor: widget.sending
                                ? JarvisColors.ink
                                : JarvisColors.surfaceRaised,
                            disabledForegroundColor: widget.sending
                                ? Colors.white
                                : JarvisColors.muted,
                          ),
                        ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _ComposerIconButton extends StatelessWidget {
  const _ComposerIconButton({
    required this.icon,
    required this.tooltip,
    required this.onPressed,
    this.danger = false,
  });

  final IconData icon;
  final String tooltip;
  final VoidCallback? onPressed;
  final bool danger;

  @override
  Widget build(BuildContext context) => IconButton(
    tooltip: tooltip,
    onPressed: onPressed,
    icon: Icon(icon, size: 19),
    style: IconButton.styleFrom(
      minimumSize: const Size(38, 38),
      foregroundColor: danger ? JarvisColors.danger : JarvisColors.ink,
      backgroundColor: danger ? JarvisColors.dangerSoft : Colors.transparent,
      shape: const CircleBorder(side: BorderSide(color: JarvisColors.outline)),
    ),
  );
}

class SuggestionChips extends StatelessWidget {
  const SuggestionChips({required this.onSelected, super.key});

  final ValueChanged<String>? onSelected;

  static const suggestions = [
    (PhosphorIconsRegular.alarm, 'Remind me to stretch in 20 minutes'),
    (PhosphorIconsRegular.brain, 'What do you know about me?'),
    (PhosphorIconsRegular.calendarBlank, 'What reminders do I have?'),
    (
      PhosphorIconsRegular.globeSimple,
      'Research the best espresso grinders in the background',
    ),
  ];

  @override
  Widget build(BuildContext context) => SizedBox(
    height: 104,
    child: ListView.separated(
      scrollDirection: Axis.horizontal,
      clipBehavior: Clip.none,
      itemCount: suggestions.length,
      separatorBuilder: (_, _) => const SizedBox(width: 10),
      itemBuilder: (context, index) {
        final (icon, text) = suggestions[index];
        return SizedBox(
          width: 196,
          child: _SuggestionRow(
            icon: icon,
            text: text,
            onTap: onSelected == null ? null : () => onSelected!(text),
          ),
        );
      },
    ),
  );
}

class _SuggestionRow extends StatelessWidget {
  const _SuggestionRow({
    required this.icon,
    required this.text,
    required this.onTap,
  });

  final IconData icon;
  final String text;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) => Semantics(
    button: true,
    enabled: onTap != null,
    child: SurfaceCard(
      onTap: onTap,
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 18, color: JarvisColors.inkSoft),
          const Spacer(),
          Text(
            text,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: const TextStyle(
              fontSize: 14,
              height: 1.3,
              fontWeight: FontWeight.w500,
              letterSpacing: -.15,
              color: JarvisColors.ink,
            ),
          ),
        ],
      ),
    ),
  );
}
