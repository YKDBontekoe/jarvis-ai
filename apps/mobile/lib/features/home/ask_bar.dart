import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'home_insights.dart';

/// The way in from Home: type or talk to Jarvis, or take a suggestion picked
/// for this moment.
class AskBar extends StatefulWidget {
  const AskBar({
    required this.suggestions,
    this.onSubmit,
    this.onVoice,
    this.busy = false,
    this.controller,
    super.key,
  });

  final List<HomeSuggestion> suggestions;

  /// Sends a question to Jarvis. Null while Jarvis cannot take one.
  final ValueChanged<String>? onSubmit;

  /// Starts a voice conversation.
  final VoidCallback? onVoice;

  /// Jarvis is already replying somewhere; the orb shows it.
  final bool busy;

  /// Lets Home put text in the field, such as a follow-up on the brief.
  final TextEditingController? controller;

  @override
  State<AskBar> createState() => _AskBarState();
}

class _AskBarState extends State<AskBar> {
  late final TextEditingController _text =
      widget.controller ?? TextEditingController();
  final _focus = FocusNode();

  @override
  void initState() {
    super.initState();
    _text.addListener(_changed);
  }

  @override
  void dispose() {
    _text.removeListener(_changed);
    if (widget.controller == null) _text.dispose();
    _focus.dispose();
    super.dispose();
  }

  void _changed() => setState(() {});

  void _send([String? value]) {
    final text = (value ?? _text.text).trim();
    final submit = widget.onSubmit;
    if (text.isEmpty || submit == null) return;
    unawaited(HapticFeedback.lightImpact());
    _text.clear();
    _focus.unfocus();
    submit(text);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final hasText = _text.text.trim().isNotEmpty;
    final enabled = widget.onSubmit != null;
    final duration = JarvisMotion.of(context, JarvisMotion.base);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        AnimatedContainer(
          duration: duration,
          curve: JarvisMotion.standard,
          height: 60,
          padding: const EdgeInsets.only(left: 12, right: 8),
          decoration: BoxDecoration(
            color: colors.surface,
            borderRadius: BorderRadius.circular(20),
            border: Border.all(
              color: _focus.hasFocus
                  ? colors.accent.withValues(alpha: .45)
                  : colors.outline,
            ),
            boxShadow: [
              BoxShadow(
                color: colors.accent.withValues(
                  alpha: colors.isDark ? .12 : .08,
                ),
                blurRadius: 30,
                offset: const Offset(0, 10),
                spreadRadius: -12,
              ),
            ],
          ),
          child: Row(
            children: [
              const JarvisOrb(size: 34, glow: false),
              const SizedBox(width: 12),
              Expanded(
                child: Focus(
                  onFocusChange: (_) => setState(() {}),
                  child: TextField(
                    key: const Key('home-ask-field'),
                    controller: _text,
                    focusNode: _focus,
                    enabled: enabled,
                    textInputAction: TextInputAction.send,
                    onSubmitted: _send,
                    style: TextStyle(fontSize: 16, color: colors.ink),
                    decoration: InputDecoration(
                      hintText: enabled ? 'Ask Jarvis' : 'Jarvis is busy…',
                      hintStyle: TextStyle(fontSize: 16, color: colors.muted),
                      border: InputBorder.none,
                      enabledBorder: InputBorder.none,
                      focusedBorder: InputBorder.none,
                      disabledBorder: InputBorder.none,
                      filled: false,
                      isCollapsed: true,
                    ),
                  ),
                ),
              ),
              AnimatedSwitcher(
                duration: duration,
                transitionBuilder: JarvisMotion.fadeScale,
                child: hasText
                    ? IconButton(
                        key: const Key('home-ask-send'),
                        tooltip: 'Send to Jarvis',
                        onPressed: enabled ? _send : null,
                        style: IconButton.styleFrom(
                          backgroundColor: colors.ink,
                          fixedSize: const Size(44, 44),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(14),
                          ),
                        ),
                        icon: Icon(
                          PhosphorIconsRegular.paperPlaneTilt,
                          size: 19,
                          color: colors.onInk,
                        ),
                      )
                    : IconButton(
                        key: const Key('home-ask-voice'),
                        tooltip: 'Talk to Jarvis',
                        onPressed: widget.onVoice,
                        style: IconButton.styleFrom(
                          backgroundColor: colors.surfaceMuted,
                          fixedSize: const Size(44, 44),
                          shape: RoundedRectangleBorder(
                            borderRadius: BorderRadius.circular(14),
                          ),
                        ),
                        icon: Icon(
                          PhosphorIconsRegular.waveform,
                          size: 20,
                          color: colors.ink,
                        ),
                      ),
              ),
            ],
          ),
        ),
        if (widget.suggestions.isNotEmpty) ...[
          const SizedBox(height: 12),
          SizedBox(
            height: 40,
            // The last chip fades out at the edge, so it reads as scrollable.
            child: ShaderMask(
              blendMode: BlendMode.dstIn,
              shaderCallback: (bounds) => const LinearGradient(
                colors: [Colors.black, Colors.black, Colors.transparent],
                stops: [0, .88, 1],
              ).createShader(bounds),
              child: ListView.separated(
                key: const Key('home-ask-suggestions'),
                scrollDirection: Axis.horizontal,
                itemCount: widget.suggestions.length,
                separatorBuilder: (_, _) => const SizedBox(width: 8),
                itemBuilder: (context, index) {
                  final suggestion = widget.suggestions[index];
                  return FadeSlideIn(
                    index: index,
                    offset: 6,
                    child: _SuggestionChip(
                      suggestion: suggestion,
                      onTap: enabled ? () => _send(suggestion.prompt) : null,
                    ),
                  );
                },
              ),
            ),
          ),
        ],
      ],
    );
  }
}

class _SuggestionChip extends StatelessWidget {
  const _SuggestionChip({required this.suggestion, this.onTap});

  final HomeSuggestion suggestion;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: 'Ask Jarvis: ${suggestion.label}',
      excludeSemantics: true,
      child: Material(
        color: colors.surface,
        shape: StadiumBorder(side: BorderSide(color: colors.outline)),
        child: InkWell(
          key: Key('home-suggestion-${suggestion.label}'),
          customBorder: const StadiumBorder(),
          onTap: onTap,
          child: Padding(
            padding: const EdgeInsets.only(left: 12, right: 16),
            child: Row(
              mainAxisSize: MainAxisSize.min,
              children: [
                Icon(suggestion.icon, size: 16, color: colors.accent),
                const SizedBox(width: 8),
                Text(
                  suggestion.label,
                  style: TextStyle(
                    fontSize: 14,
                    fontWeight: FontWeight.w500,
                    color: onTap == null ? colors.muted : colors.ink,
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
