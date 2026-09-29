part of 'chat_widgets.dart';

class ChatComposer extends StatefulWidget {
  const ChatComposer({
    required this.controller,
    required this.onSend,
    required this.onVoice,
    required this.sending,
    required this.voiceActive,
    required this.voiceStarting,
    this.onCancel,
    this.onAttach,
    this.sources = const [],
    this.onRemoveSource,
    this.awaitingApproval = false,
    this.hint = 'Ask Jarvis anything',
    super.key,
  });

  final TextEditingController controller;
  final VoidCallback onSend;
  final VoidCallback? onCancel;
  final VoidCallback? onVoice;
  final bool sending;
  final bool awaitingApproval;
  final bool voiceActive;
  final bool voiceStarting;

  /// Opens extra actions (files, tasks, reminders); hidden when null.
  final VoidCallback? onAttach;
  final List<ConversationSourceChip> sources;
  final ValueChanged<ConversationSourceChip>? onRemoveSource;
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
      !widget.awaitingApproval &&
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
        !widget.awaitingApproval &&
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
          if (widget.sources.isNotEmpty)
            Padding(
              padding: const EdgeInsets.fromLTRB(8, 6, 8, 0),
              child: Align(
                alignment: Alignment.centerLeft,
                child: Wrap(
                  spacing: 6,
                  runSpacing: 6,
                  children: [
                    for (final source in widget.sources)
                      InputChip(
                        label: Text(
                          source.label,
                          style: const TextStyle(fontSize: 12),
                        ),
                        avatar: Icon(
                          source.kind == 'collection'
                              ? PhosphorIconsRegular.folders
                              : PhosphorIconsRegular.fileText,
                          size: 14,
                        ),
                        onDeleted: widget.onRemoveSource == null
                            ? null
                            : () => widget.onRemoveSource!(source),
                      ),
                  ],
                ),
              ),
            ),
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
              if (widget.sending && widget.onCancel != null)
                _ComposerIconButton(
                  icon: PhosphorIconsRegular.stop,
                  tooltip: 'Stop',
                  danger: true,
                  onPressed: widget.onCancel,
                )
              else if (widget.voiceActive || widget.voiceStarting)
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
