part of 'chat_widgets.dart';

class MessageBubble extends StatefulWidget {
  const MessageBubble({
    required this.message,
    this.onRetry,
    this.onRate,
    this.onCitationTap,
    super.key,
  });

  final MessageEntry message;
  final VoidCallback? onRetry;

  /// Rates an assistant reply `up` or `down`; hidden until the reply is stored.
  final ValueChanged<String>? onRate;
  final ValueChanged<MessageCitation>? onCitationTap;

  @override
  State<MessageBubble> createState() => _MessageBubbleState();
}

class _MessageBubbleState extends State<MessageBubble> {
  bool _copied = false;

  Future<void> _copy() async {
    try {
      await Clipboard.setData(ClipboardData(text: widget.message.content));
    } catch (_) {
      return;
    }
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
                if (!message.isUser && message.citations.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 8),
                    child: Wrap(
                      spacing: 6,
                      runSpacing: 6,
                      children: [
                        for (final citation in message.citations)
                          ActionChip(
                            label: Text(
                              citation.pageNumber == null
                                  ? citation.displayName
                                  : '${citation.displayName} · p.${citation.pageNumber}',
                              style: const TextStyle(fontSize: 12),
                            ),
                            onPressed: widget.onCitationTap == null
                                ? null
                                : () => widget.onCitationTap!(citation),
                          ),
                      ],
                    ),
                  ),
                if (!message.pending)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        _CopyButton(copied: _copied, onPressed: _copy),
                        if (widget.onRate != null) ...[
                          _RateButton(
                            rating: 'up',
                            selected: message.rating == 'up',
                            onPressed: () => widget.onRate!('up'),
                          ),
                          _RateButton(
                            rating: 'down',
                            selected: message.rating == 'down',
                            onPressed: () => widget.onRate!('down'),
                          ),
                        ],
                      ],
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
      padding: const EdgeInsets.only(bottom: 18, left: 56),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.end,
        children: [
          Container(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 11),
            decoration: BoxDecoration(
              color: message.failed
                  ? JarvisColors.of(context).dangerSoft
                  : JarvisColors.of(context).surfaceRaised,
              borderRadius: const BorderRadius.only(
                topLeft: Radius.circular(20),
                topRight: Radius.circular(20),
                bottomLeft: Radius.circular(20),
                bottomRight: Radius.circular(6),
              ),
              border: message.failed
                  ? Border.all(
                      color: JarvisColors.of(context).danger
                          .withValues(alpha: .35),
                    )
                  : null,
            ),
            child: SelectableText(
              message.content,
              style: TextStyle(
                fontSize: 15.5,
                height: 1.45,
                color: JarvisColors.of(context).ink,
              ),
            ),
          ),
          if (message.failed)
            Padding(
              padding: const EdgeInsets.only(top: 4),
              child: TextButton.icon(
                onPressed: widget.onRetry,
                style: TextButton.styleFrom(
                  foregroundColor: JarvisColors.of(context).danger,
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

class _RateButton extends StatelessWidget {
  const _RateButton({
    required this.rating,
    required this.selected,
    required this.onPressed,
  });

  final String rating;
  final bool selected;
  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final up = rating == 'up';
    return IconButton(
      tooltip: up ? 'Good reply' : 'Could be better',
      onPressed: onPressed,
      visualDensity: VisualDensity.compact,
      iconSize: 16,
      color: selected
          ? (up
                ? JarvisColors.of(context).success
                : JarvisColors.of(context).danger)
          : JarvisColors.of(context).muted,
      icon: Icon(
        up
            ? (selected
                  ? PhosphorIconsFill.thumbsUp
                  : PhosphorIconsRegular.thumbsUp)
            : (selected
                  ? PhosphorIconsFill.thumbsDown
                  : PhosphorIconsRegular.thumbsDown),
      ),
    );
  }
}

/// Asks what Jarvis should do differently; returns null when cancelled.
Future<String?> showFeedbackNoteDialog(BuildContext context) async {
  final controller = TextEditingController();
  final note = await showDialog<String>(
    context: context,
    builder: (dialogContext) => AlertDialog(
      title: const Text('What should Jarvis do differently?'),
      content: TextField(
        key: const Key('feedback-note'),
        controller: controller,
        autofocus: true,
        maxLines: 3,
        maxLength: 1000,
        decoration: const InputDecoration(
          hintText: 'e.g. Shorter answers, use metric units, skip the intro',
        ),
      ),
      actions: [
        TextButton(
          onPressed: () => Navigator.pop(dialogContext),
          child: const Text('Cancel'),
        ),
        FilledButton(
          onPressed: () => Navigator.pop(dialogContext, controller.text.trim()),
          child: const Text('Send feedback'),
        ),
      ],
    ),
  );
  controller.dispose();
  return note;
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
        foregroundColor: copied
            ? JarvisColors.of(context).success
            : JarvisColors.of(context).muted,
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
    final body = TextStyle(
      fontSize: 15.5,
      height: 1.6,
      color: JarvisColors.of(context).ink,
      letterSpacing: -.1,
    );
    final sheet = MarkdownStyleSheet.fromTheme(theme).copyWith(
      p: body,
      listBullet: body.copyWith(color: JarvisColors.of(context).muted),
      h1: TextStyle(
        fontSize: 22,
        fontWeight: FontWeight.w700,
        height: 1.3,
        letterSpacing: -.5,
        color: JarvisColors.of(context).ink,
      ),
      h2: TextStyle(
        fontSize: 19,
        fontWeight: FontWeight.w700,
        height: 1.3,
        letterSpacing: -.4,
        color: JarvisColors.of(context).ink,
      ),
      h3: TextStyle(
        fontSize: 17,
        fontWeight: FontWeight.w600,
        height: 1.3,
        color: JarvisColors.of(context).ink,
      ),
      strong: const TextStyle(fontWeight: FontWeight.w700),
      a: TextStyle(
        color: JarvisColors.of(context).ink,
        fontWeight: FontWeight.w500,
        decoration: TextDecoration.underline,
        decorationColor: JarvisColors.of(context).outlineStrong,
      ),
      code: TextStyle(
        fontFamily: 'monospace',
        fontSize: 13.5,
        backgroundColor: JarvisColors.of(context).surfaceMuted,
        color: JarvisColors.of(context).ink,
      ),
      codeblockPadding: const EdgeInsets.all(16),
      codeblockDecoration: BoxDecoration(
        color: JarvisColors.of(context).canvas,
        borderRadius: BorderRadius.circular(JarvisRadii.md),
        border: Border.all(color: JarvisColors.of(context).outline),
      ),
      blockquote: TextStyle(
        color: JarvisColors.of(context).inkSoft,
        height: 1.55,
      ),
      blockquotePadding: const EdgeInsets.fromLTRB(14, 8, 12, 8),
      blockquoteDecoration: BoxDecoration(
        border: Border(
          left: BorderSide(
            color: JarvisColors.of(context).outlineStrong,
            width: 2,
          ),
        ),
      ),
      tableHead: TextStyle(
        fontWeight: FontWeight.w600,
        color: JarvisColors.of(context).ink,
      ),
      tableBody: TextStyle(fontSize: 14.5, color: JarvisColors.of(context).ink),
      tableHeadAlign: TextAlign.left,
      tableBorder: TableBorder.all(
        color: JarvisColors.of(context).outline,
        borderRadius: BorderRadius.circular(10),
      ),
      tableCellsDecoration: BoxDecoration(
        color: JarvisColors.of(context).surface,
      ),
      tableCellsPadding: const EdgeInsets.symmetric(
        horizontal: 12,
        vertical: 8,
      ),
      horizontalRuleDecoration: BoxDecoration(
        border: Border(
          top: BorderSide(color: JarvisColors.of(context).outline),
        ),
      ),
    );
    return MarkdownBody(
      data: data,
      selectable: true,
      styleSheet: sheet,
      onTapLink: (text, href, title) {
        final uri = parseHttpUrl(href);
        if (uri == null) return;
        unawaited(launchHttpUrl(uri));
      },
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
            color: JarvisColors.of(context).surface,
            borderRadius: BorderRadius.circular(18),
            border: Border.all(color: JarvisColors.of(context).outline),
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
                          JarvisColors.of(context).outlineStrong,
                          JarvisColors.of(context).inkSoft,
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
