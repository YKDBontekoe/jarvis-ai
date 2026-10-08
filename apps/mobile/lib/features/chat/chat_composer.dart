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
    this.focusRequests,
    this.photos = const [],
    this.onRemovePhoto,
    this.onPhoto,
    this.onImages,
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

  /// Each notification moves keyboard focus into the text field.
  final Listenable? focusRequests;

  /// Photos picked for the next message.
  final List<PendingPhoto> photos;
  final ValueChanged<PendingPhoto>? onRemovePhoto;

  /// Opens the camera or photo library; hidden when null.
  final VoidCallback? onPhoto;

  /// Photos pasted, dropped, or inserted from the keyboard.
  final ValueChanged<List<IncomingPhoto>>? onImages;

  @override
  State<ChatComposer> createState() => _ChatComposerState();
}

class _ChatComposerState extends State<ChatComposer> {
  late final FocusNode _focus = FocusNode(onKeyEvent: _onKey)
    ..addListener(_changed);
  late final _paste = ImagePasteListener(
    shouldHandlePaste: () => _canAcceptImages && !_anotherTextFieldHasFocus(),
    shouldHandleDrop: () => _canAcceptImages,
    onImages: (images) => widget.onImages?.call(images),
  );

  @override
  void initState() {
    super.initState();
    widget.controller.addListener(_changed);
    widget.focusRequests?.addListener(_requestFocus);
    _paste.attach();
  }

  @override
  void didUpdateWidget(ChatComposer oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.controller != widget.controller) {
      oldWidget.controller.removeListener(_changed);
      widget.controller.addListener(_changed);
    }
    if (oldWidget.focusRequests != widget.focusRequests) {
      oldWidget.focusRequests?.removeListener(_requestFocus);
      widget.focusRequests?.addListener(_requestFocus);
    }
  }

  void _requestFocus() => _focus.requestFocus();

  void _changed() => setState(() {});

  KeyEventResult _onKey(FocusNode node, KeyEvent event) {
    if (event is KeyDownEvent &&
        event.logicalKey == LogicalKeyboardKey.enter &&
        !HardwareKeyboard.instance.isShiftPressed) {
      if (_canSend) _send();
      return KeyEventResult.handled;
    }
    return KeyEventResult.ignored;
  }

  bool get _inputEnabled => !widget.voiceActive && !widget.voiceStarting;

  bool get _canAcceptImages =>
      widget.onImages != null &&
      _inputEnabled &&
      !widget.sending &&
      !widget.awaitingApproval;

  bool _anotherTextFieldHasFocus() {
    if (_focus.hasFocus) return false;
    final primary = FocusManager.instance.primaryFocus;
    if (primary == null || !primary.hasFocus) return false;
    final ctx = primary.context;
    if (ctx == null) return false;
    if (ctx.widget is EditableText) return true;
    return ctx.findAncestorWidgetOfExactType<EditableText>() != null;
  }

  void _acceptInsertedContent(KeyboardInsertedContent content) {
    if (!_canAcceptImages) return;
    final data = content.data;
    if (data == null || data.isEmpty) return;
    widget.onImages!([
      IncomingPhoto(bytes: data, name: content.uri, mimeType: content.mimeType),
    ]);
  }

  bool get _photosReady =>
      widget.photos.isNotEmpty &&
      widget.photos.every((photo) => photo.fileId != null);

  bool get _canSend =>
      _inputEnabled &&
      !widget.sending &&
      !widget.awaitingApproval &&
      !widget.photos.any((photo) => photo.uploading) &&
      (widget.controller.text.trim().isNotEmpty || _photosReady);

  @override
  void dispose() {
    widget.controller.removeListener(_changed);
    widget.focusRequests?.removeListener(_requestFocus);
    _paste.detach();
    _focus.dispose();
    super.dispose();
  }

  /// Send, voice, or stop: one slot, so the button morphs instead of jumping.
  Widget _trailingAction(BuildContext context, bool showVoice) {
    final colors = JarvisColors.of(context);
    if (widget.sending && widget.onCancel != null) {
      return _ComposerIconButton(
        key: const ValueKey('stop'),
        icon: PhosphorIconsRegular.stop,
        tooltip: 'Stop',
        danger: true,
        onPressed: widget.onCancel,
      );
    }
    if (widget.voiceActive || widget.voiceStarting) {
      return _ComposerIconButton(
        key: const ValueKey('stop-voice'),
        icon: PhosphorIconsRegular.stop,
        tooltip: 'Stop voice',
        danger: true,
        onPressed: widget.onVoice,
      );
    }
    if (showVoice) {
      return IconButton.filled(
        key: const ValueKey('voice'),
        tooltip: 'Voice mode',
        onPressed: widget.onVoice,
        icon: const Icon(PhosphorIconsBold.waveform, size: 18),
        style: IconButton.styleFrom(
          minimumSize: const Size(44, 44),
          backgroundColor: colors.ink,
          foregroundColor: colors.onInk,
        ),
      );
    }
    final enabled = _canSend || widget.sending;
    return AnimatedContainer(
      key: const ValueKey('send'),
      duration: JarvisMotion.of(context, JarvisMotion.base),
      curve: JarvisMotion.standard,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        gradient: enabled ? colors.accentGradient : null,
        color: enabled ? null : colors.surfaceRaised,
        boxShadow: enabled
            ? [
                BoxShadow(
                  color: colors.accent.withValues(alpha: .28),
                  blurRadius: 12,
                  offset: const Offset(0, 4),
                ),
              ]
            : null,
      ),
      child: IconButton(
        tooltip: 'Send',
        onPressed: _canSend ? _send : null,
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
          minimumSize: const Size(44, 44),
          foregroundColor: Colors.white,
          disabledForegroundColor: widget.sending ? Colors.white : colors.muted,
        ),
      ),
    );
  }

  void _send() {
    unawaited(HapticFeedback.lightImpact());
    widget.onSend();
  }

  @override
  Widget build(BuildContext context) {
    final focused = _focus.hasFocus;
    final hasText =
        widget.controller.text.trim().isNotEmpty || widget.photos.isNotEmpty;
    final showVoice =
        widget.onVoice != null &&
        !hasText &&
        !widget.sending &&
        !widget.awaitingApproval &&
        !widget.voiceActive;
    return AnimatedContainer(
      duration: JarvisMotion.of(context, JarvisMotion.base),
      curve: JarvisMotion.standard,
      padding: const EdgeInsets.fromLTRB(6, 4, 8, 8),
      decoration: BoxDecoration(
        color: JarvisColors.of(context).surface,
        borderRadius: BorderRadius.circular(26),
        border: Border.all(
          color: focused
              ? JarvisColors.of(context).accent.withValues(alpha: .45)
              : JarvisColors.of(context).outline,
        ),
        boxShadow: [
          ...JarvisShadows.floating(JarvisColors.of(context).brightness),
          if (focused)
            BoxShadow(
              color: JarvisColors.of(context).accent.withValues(alpha: .10),
              blurRadius: 0,
              spreadRadius: 3,
            ),
        ],
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
          if (widget.photos.isNotEmpty)
            Align(
              alignment: Alignment.centerLeft,
              child: ComposerPhotoStrip(
                photos: widget.photos,
                onRemove: widget.onRemovePhoto ?? (_) {},
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
            contentInsertionConfiguration: widget.onImages == null
                ? null
                : ContentInsertionConfiguration(
                    allowedMimeTypes: const [
                      'image/png',
                      'image/jpeg',
                      'image/jpg',
                      'image/webp',
                    ],
                    onContentInserted: _acceptInsertedContent,
                  ),
            style: TextStyle(fontSize: 16, color: JarvisColors.of(context).ink),
            decoration: InputDecoration(
              hintText: widget.voiceActive
                  ? 'Listening…'
                  : widget.awaitingApproval
                  ? 'Approve or decline above to continue'
                  : widget.hint,
              hintStyle: TextStyle(
                fontSize: 16,
                color: JarvisColors.of(context).muted,
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
              if (widget.onPhoto != null) ...[
                const SizedBox(width: 6),
                _ComposerIconButton(
                  icon: PhosphorIconsRegular.camera,
                  tooltip: 'Add a photo',
                  onPressed: _inputEnabled && !widget.sending
                      ? widget.onPhoto
                      : null,
                ),
              ],
              const Spacer(),
              AnimatedSwitcher(
                duration: JarvisMotion.of(
                  context,
                  const Duration(milliseconds: 420),
                ),
                reverseDuration: JarvisMotion.of(context, JarvisMotion.fast),
                transitionBuilder: JarvisMotion.morph,
                child: _trailingAction(context, showVoice),
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
    super.key,
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
      minimumSize: const Size(40, 40),
      fixedSize: const Size(40, 40),
      foregroundColor: danger
          ? JarvisColors.of(context).danger
          : JarvisColors.of(context).inkSoft,
      backgroundColor: danger
          ? JarvisColors.of(context).dangerSoft
          : JarvisColors.of(context).surfaceMuted,
      disabledBackgroundColor: JarvisColors.of(context).surfaceMuted,
      shape: const CircleBorder(),
    ),
  );
}

class SuggestionChips extends StatelessWidget {
  const SuggestionChips({required this.onSelected, super.key});

  final ValueChanged<String>? onSelected;

  static const suggestions = [
    (PhosphorIconsRegular.alarm, 'Remind me to stretch in 20 minutes'),
    (PhosphorIconsRegular.brain, 'What do you know about me?'),
    (PhosphorIconsRegular.plugsConnected, mcpSetupPrompt),
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
          Icon(icon, size: 18, color: JarvisColors.of(context).inkSoft),
          const Spacer(),
          Text(
            text,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: 14,
              height: 1.3,
              fontWeight: FontWeight.w500,
              letterSpacing: -.15,
              color: JarvisColors.of(context).ink,
            ),
          ),
        ],
      ),
    ),
  );
}
