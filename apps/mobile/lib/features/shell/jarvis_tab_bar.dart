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
    super.key,
  });

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
                    child: Row(
                      children: [
                        tab(JarvisTab.home),
                        tab(JarvisTab.chats),
                        Expanded(
                          child: JarvisOrbButton(
                            onPressed: onJarvis,
                            busy: jarvisBusy,
                          ),
                        ),
                        tab(JarvisTab.everything),
                        tab(JarvisTab.you),
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

class JarvisOrbButton extends StatelessWidget {
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
  Widget build(BuildContext context) {
    void activate() {
      unawaited(HapticFeedback.lightImpact());
      onPressed();
    }

    final label = busy ? 'Jarvis is replying. Open chat' : 'Ask Jarvis';
    return Semantics(
      button: true,
      label: label,
      onTap: activate,
      excludeSemantics: true,
      child: Tooltip(
        message: label,
        excludeFromSemantics: true,
        child: PressFeedback(
          scale: .92,
          builder: (context, highlight) => InkResponse(
            key: const Key('tab-jarvis'),
            onTap: activate,
            onHighlightChanged: highlight,
            radius: 26,
            child: SizedBox(
              height: 52,
              width: 52,
              child: Center(
                child: JarvisOrb(size: size, glow: false, animate: busy),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _TabButton extends StatelessWidget {
  const _TabButton({
    required this.tab,
    required this.selected,
    required this.attention,
    required this.onTap,
  });

  final JarvisTab tab;
  final bool selected;
  final bool attention;
  final VoidCallback onTap;

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
                  color: selected
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
                        duration: JarvisMotion.of(context, JarvisMotion.fast),
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
