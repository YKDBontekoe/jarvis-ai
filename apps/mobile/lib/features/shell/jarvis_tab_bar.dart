import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import 'tab_icons.dart';

/// The main destinations. Names remain available to VoiceOver and tooltips.
enum JarvisTab {
  home('Home', TabGlyph.home),
  chats('Chats', TabGlyph.chats),
  everything('Everything', TabGlyph.everything),
  you('You', TabGlyph.you);

  const JarvisTab(this.label, this.glyph);

  final String label;
  final TabGlyph glyph;
}

/// A quiet, floating navigation surface. The central orb opens Jarvis.
class JarvisTabBar extends StatelessWidget {
  const JarvisTabBar({
    required this.selected,
    required this.onSelect,
    required this.onJarvis,
    this.chatsAttention = false,
    this.jarvisBusy = false,
    this.orbKey,
    super.key,
  });

  /// Lets the shell find the orb, to fly it into the chat it opens.
  final GlobalKey? orbKey;

  final JarvisTab selected;
  final ValueChanged<JarvisTab> onSelect;
  final VoidCallback onJarvis;
  final bool chatsAttention;
  final bool jarvisBusy;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget tab(JarvisTab value) => Expanded(
      child: _TabButton(
        tab: value,
        selected: value == selected,
        attention: value == JarvisTab.chats && chatsAttention,
        onTap: () => onSelect(value),
        pill: false,
      ),
    );
    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
        child: DecoratedBox(
          decoration: BoxDecoration(
            borderRadius: BorderRadius.circular(32),
            boxShadow: JarvisShadows.soft(colors.brightness),
          ),
          child: ClipRRect(
            borderRadius: BorderRadius.circular(32),
            child: BackdropFilter(
              filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18),
              child: DecoratedBox(
                decoration: BoxDecoration(
                  color: colors.surface.withValues(alpha: .92),
                  borderRadius: BorderRadius.circular(32),
                  border: Border.all(
                    color: colors.outlineStrong.withValues(
                      alpha: colors.isDark ? .45 : .3,
                    ),
                    width: .5,
                  ),
                ),
                child: Material(
                  type: MaterialType.transparency,
                  child: SizedBox(
                    height: 60,
                    child: Stack(
                      children: [
                        // One pill glides between tabs on a spring instead of
                        // each tab fading its own background in and out.
                        Positioned.fill(
                          child: LayoutBuilder(
                            builder: (context, box) {
                              final slot = box.maxWidth / 5;
                              final index = switch (selected) {
                                JarvisTab.home => 0,
                                JarvisTab.chats => 1,
                                JarvisTab.everything => 3,
                                JarvisTab.you => 4,
                              };
                              return Stack(
                                children: [
                                  AnimatedPositioned(
                                    key: const Key('tab-indicator'),
                                    duration: JarvisMotion.of(
                                      context,
                                      const Duration(milliseconds: 560),
                                    ),
                                    curve: JarvisSprings.soft,
                                    left: slot * index + (slot - 48) / 2,
                                    top: 8,
                                    width: 48,
                                    height: 44,
                                    child: _SelectionPill(colors: colors),
                                  ),
                                ],
                              );
                            },
                          ),
                        ),
                        Positioned.fill(
                          child: Row(
                            children: [
                              tab(JarvisTab.home),
                              tab(JarvisTab.chats),
                              Expanded(
                                child: JarvisOrbButton(
                                  key: orbKey,
                                  onPressed: onJarvis,
                                  busy: jarvisBusy,
                                ),
                              ),
                              tab(JarvisTab.everything),
                              tab(JarvisTab.you),
                            ],
                          ),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class JarvisOrbButton extends StatefulWidget {
  const JarvisOrbButton({
    required this.onPressed,
    this.size = 40,
    this.busy = false,
    super.key,
  });

  final VoidCallback onPressed;
  final double size;
  final bool busy;

  @override
  State<JarvisOrbButton> createState() => _JarvisOrbButtonState();
}

class _JarvisOrbButtonState extends State<JarvisOrbButton> {
  int _taps = 0;

  /// Counts finished replies, so the orb blooms when Jarvis is done.
  int _replies = 0;

  @override
  void didUpdateWidget(JarvisOrbButton oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.busy && !widget.busy) _replies++;
  }

  @override
  Widget build(BuildContext context) {
    void activate() {
      unawaited(HapticFeedback.lightImpact());
      setState(() => _taps++);
      widget.onPressed();
    }

    final label = widget.busy ? 'Jarvis is replying. Open chat' : 'Ask Jarvis';
    return Semantics(
      button: true,
      label: label,
      onTap: activate,
      excludeSemantics: true,
      child: Tooltip(
        message: label,
        excludeFromSemantics: true,
        child: PressFeedback(
          scale: .88,
          builder: (context, highlight) => InkResponse(
            key: const Key('tab-jarvis'),
            onTap: activate,
            onHighlightChanged: highlight,
            radius: 26,
            child: SizedBox(
              height: 52,
              width: 52,
              child: Center(
                child: Shockwave(
                  trigger: _taps,
                  size: widget.size,
                  child: JarvisOrb(
                    size: widget.size,
                    glow: false,
                    thinking: widget.busy,
                    pulse: _replies == 0 ? null : _replies,
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The selected-tab background: a soft tinted capsule with a faint top light.
class _SelectionPill extends StatelessWidget {
  const _SelectionPill({required this.colors});

  final JarvisColors colors;

  @override
  Widget build(BuildContext context) => DecoratedBox(
    decoration: BoxDecoration(
      borderRadius: BorderRadius.circular(24),
      gradient: LinearGradient(
        begin: Alignment.topCenter,
        end: Alignment.bottomCenter,
        colors: colors.isDark
            ? [
                colors.surfaceRaised,
                Color.lerp(colors.surfaceRaised, colors.accent, .08)!,
              ]
            : [
                Color.lerp(colors.surfaceMuted, colors.accent, .05)!,
                colors.surfaceMuted,
              ],
      ),
      boxShadow: [
        BoxShadow(
          color: colors.accent.withValues(alpha: colors.isDark ? .18 : .10),
          blurRadius: 14,
          offset: const Offset(0, 4),
        ),
      ],
    ),
  );
}

class _TabButton extends StatelessWidget {
  const _TabButton({
    required this.tab,
    required this.selected,
    required this.attention,
    required this.onTap,
    this.pill = true,
  });

  final JarvisTab tab;
  final bool selected;
  final bool attention;
  final VoidCallback onTap;

  /// Draws its own selected background; off when the bar's gliding pill does.
  final bool pill;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    void activate() {
      if (!selected) unawaited(HapticFeedback.selectionClick());
      onTap();
    }

    return Semantics(
      button: true,
      selected: selected,
      label: attention ? '${tab.label}, unread' : tab.label,
      onTap: activate,
      excludeSemantics: true,
      child: Tooltip(
        message: tab.label,
        excludeFromSemantics: true,
        child: PressFeedback(
          scale: .96,
          builder: (context, highlight) => InkResponse(
            key: Key('tab-${tab.name}'),
            onTap: activate,
            onHighlightChanged: highlight,
            radius: 26,
            child: Center(
              child: AnimatedContainer(
                duration: JarvisMotion.of(context, JarvisMotion.fast),
                curve: JarvisMotion.standard,
                width: 48,
                height: 44,
                decoration: BoxDecoration(
                  color: selected && pill
                      ? (colors.isDark
                            ? colors.surfaceRaised
                            : colors.surfaceMuted)
                      : Colors.transparent,
                  borderRadius: BorderRadius.circular(24),
                ),
                child: Center(
                  child: Stack(
                    clipBehavior: Clip.none,
                    children: [
                      AnimatedSwitcher(
                        duration: JarvisMotion.of(
                          context,
                          const Duration(milliseconds: 460),
                        ),
                        reverseDuration: JarvisMotion.of(
                          context,
                          JarvisMotion.fast,
                        ),
                        transitionBuilder: (child, animation) => FadeTransition(
                          opacity: animation,
                          child: ScaleTransition(
                            scale: Tween(begin: .55, end: 1.0).animate(
                              CurvedAnimation(
                                parent: animation,
                                curve: JarvisSprings.pop,
                                reverseCurve: Curves.easeIn,
                              ),
                            ),
                            child: child,
                          ),
                        ),
                        child: TabIcon(
                          key: ValueKey((tab.glyph, selected)),
                          glyph: tab.glyph,
                          color: selected ? colors.accent : colors.inkSoft,
                          selected: selected,
                        ),
                      ),
                      if (attention)
                        Positioned(
                          top: -2,
                          right: -4,
                          child: PopIn(
                            child: Container(
                              key: const Key('tab-attention'),
                              width: 7,
                              height: 7,
                              decoration: BoxDecoration(
                                color: colors.accent,
                                shape: BoxShape.circle,
                                border: Border.all(
                                  color: colors.surface,
                                  width: 1.5,
                                ),
                              ),
                            ),
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The same icon destinations for wide layouts.
class JarvisNavRail extends StatelessWidget {
  const JarvisNavRail({
    required this.selected,
    required this.onSelect,
    required this.onJarvis,
    this.chatsAttention = false,
    this.jarvisBusy = false,
    super.key,
  });

  final JarvisTab? selected;
  final ValueChanged<JarvisTab> onSelect;
  final VoidCallback onJarvis;
  final bool chatsAttention;
  final bool jarvisBusy;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget tab(JarvisTab value) => SizedBox(
      height: 64,
      child: _TabButton(
        tab: value,
        selected: value == selected,
        attention: value == JarvisTab.chats && chatsAttention,
        onTap: () => onSelect(value),
      ),
    );
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.canvas,
        border: Border(right: BorderSide(color: colors.outline)),
      ),
      child: SafeArea(
        right: false,
        child: SizedBox(
          width: 84,
          child: Column(
            children: [
              const SizedBox(height: 18),
              SizedBox(
                height: 64,
                child: JarvisOrbButton(onPressed: onJarvis, busy: jarvisBusy),
              ),
              const SizedBox(height: 14),
              tab(JarvisTab.home),
              tab(JarvisTab.chats),
              tab(JarvisTab.everything),
              const Spacer(),
              tab(JarvisTab.you),
              const SizedBox(height: 12),
            ],
          ),
        ),
      ),
    );
  }
}
