import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import 'tab_icons.dart';

/// The places Home, Chats, Everything and You, in the order they are shown.
enum JarvisTab {
  home('Home', TabGlyph.home),
  chats('Chats', TabGlyph.chats),
  everything('Everything', TabGlyph.everything),
  you('You', TabGlyph.you);

  const JarvisTab(this.label, this.glyph);

  final String label;
  final TabGlyph glyph;
}

/// The tab bar: Home and Chats, the Jarvis orb in the middle, then Everything
/// and You. The orb opens a conversation instead of switching tabs.
class JarvisTabBar extends StatelessWidget {
  const JarvisTabBar({
    required this.selected,
    required this.onSelect,
    required this.onJarvis,
    this.chatsAttention = false,
    super.key,
  });

  final JarvisTab selected;
  final ValueChanged<JarvisTab> onSelect;
  final VoidCallback onJarvis;

  /// Draws a dot on Chats when something there is unread.
  final bool chatsAttention;

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
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.canvas,
        border: Border(top: BorderSide(color: colors.outline)),
      ),
      child: SafeArea(
        top: false,
        // Labels are short; very large text would not fit the bar.
        child: MediaQuery.withClampedTextScaling(
          maxScaleFactor: 1.2,
          child: SizedBox(
            height: 62,
            child: Row(
              children: [
                tab(JarvisTab.home),
                tab(JarvisTab.chats),
                Expanded(child: JarvisOrbButton(onPressed: onJarvis)),
                tab(JarvisTab.everything),
                tab(JarvisTab.you),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

/// The orb that opens a conversation with Jarvis.
class JarvisOrbButton extends StatelessWidget {
  const JarvisOrbButton({required this.onPressed, this.size = 44, super.key});

  final VoidCallback onPressed;
  final double size;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: 'Ask Jarvis',
      onTap: onPressed,
      excludeSemantics: true,
      child: InkResponse(
        key: const Key('tab-jarvis'),
        onTap: onPressed,
        radius: size * .8,
        child: Center(
          child: DecoratedBox(
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              boxShadow: [
                BoxShadow(
                  color: colors.accent.withValues(alpha: .3),
                  blurRadius: 14,
                  offset: const Offset(0, 4),
                ),
              ],
            ),
            child: JarvisOrb(size: size, glow: false),
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
    final color = selected ? colors.accent : colors.muted;
    return Semantics(
      button: true,
      selected: selected,
      label: attention ? '${tab.label}, unread' : tab.label,
      onTap: onTap,
      excludeSemantics: true,
      child: InkResponse(
        key: Key('tab-${tab.name}'),
        onTap: onTap,
        radius: 36,
        child: Column(
          mainAxisAlignment: MainAxisAlignment.center,
          children: [
            Stack(
              clipBehavior: Clip.none,
              children: [
                TabIcon(
                  glyph: tab.glyph,
                  color: color,
                  fill: selected ? colors.accentSoft : null,
                ),
                if (attention)
                  Positioned(
                    top: -1,
                    right: -4,
                    child: Container(
                      key: const Key('tab-attention'),
                      width: 8,
                      height: 8,
                      decoration: BoxDecoration(
                        color: colors.accent,
                        shape: BoxShape.circle,
                        border: Border.all(color: colors.canvas, width: 1.5),
                      ),
                    ),
                  ),
              ],
            ),
            const SizedBox(height: 4),
            Text(
              tab.label,
              maxLines: 1,
              style: TextStyle(
                fontSize: 10.5,
                fontWeight: selected ? FontWeight.w600 : FontWeight.w500,
                letterSpacing: .1,
                color: color,
              ),
            ),
          ],
        ),
      ),
    );
  }
}

/// The same destinations as a slim column for wide screens.
class JarvisNavRail extends StatelessWidget {
  const JarvisNavRail({
    required this.selected,
    required this.onSelect,
    required this.onJarvis,
    this.chatsAttention = false,
    super.key,
  });

  final JarvisTab? selected;
  final ValueChanged<JarvisTab> onSelect;
  final VoidCallback onJarvis;
  final bool chatsAttention;

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
        child: MediaQuery.withClampedTextScaling(
          maxScaleFactor: 1.2,
          child: SizedBox(
            width: 84,
            child: Column(
              children: [
                const SizedBox(height: 18),
                SizedBox(
                  height: 64,
                  child: JarvisOrbButton(onPressed: onJarvis),
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
      ),
    );
  }
}
