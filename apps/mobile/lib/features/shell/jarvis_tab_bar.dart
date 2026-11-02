import 'dart:async';
import 'dart:ui' show ImageFilter;

import 'package:flutter/material.dart';
import 'package:flutter/rendering.dart' show ScrollDirection;
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import 'tab_icons.dart';
import '../../l10n/l10n.dart';

/// The main destinations. Names remain available to VoiceOver and tooltips.
enum JarvisTab {
  home('Home', TabGlyph.home),
  chats('Chats', TabGlyph.chats),
  everything('Everything', TabGlyph.everything),
  you('You', TabGlyph.you);

  const JarvisTab(this.label, this.glyph);

  final String label;
  final TabGlyph glyph;

  /// [label] in the app's language.
  String localizedLabel(AppLocalizations strings) => switch (this) {
    JarvisTab.home => strings.tabHome,
    JarvisTab.chats => strings.tabChats,
    JarvisTab.everything => strings.tabEverything,
    JarvisTab.you => strings.tabYou,
  };
}

/// Whether the dock should be folded after the owner scrolls: scrolling down
/// folds it, scrolling back up or reaching the top unfolds it, and stopping
/// leaves it as it was.
bool dockCompactAfterScroll({
  required ScrollDirection direction,
  required double pixels,
  required bool current,
}) {
  final atTop = pixels <= 24;
  return switch (direction) {
    ScrollDirection.reverse => !atTop,
    ScrollDirection.forward => false,
    ScrollDirection.idle => current && !atTop,
  };
}

/// The approved two-piece dock: a glass capsule for Home, Chats, Everything
/// and You, and a separate glass orb button for Jarvis. The open tab is a solid
/// pill that names itself; the others are icons. While a list scrolls the
/// capsule folds to the open tab ([compact]) and unfolds on a tap or when the
/// list scrolls back. An optional [strip] sits above both pieces.
class JarvisTabBar extends StatelessWidget {
  const JarvisTabBar({
    required this.selected,
    required this.onSelect,
    required this.onJarvis,
    this.chatsAttention = false,
    this.jarvisBusy = false,
    this.compact = false,
    this.onUnfold,
    this.strip,
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

  /// Show only the open tab; a tap on it calls [onUnfold].
  final bool compact;
  final VoidCallback? onUnfold;

  /// One contextual line above the dock, or nothing.
  final Widget? strip;

  /// Space a tab needs to be comfortable to hit.
  static const minTarget = 44.0;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SafeArea(
      top: false,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(12, 6, 12, 6),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            if (strip != null) ...[strip!, const SizedBox(height: 8)],
            Row(
              crossAxisAlignment: CrossAxisAlignment.center,
              children: [
                Expanded(
                  child: LayoutBuilder(
                    builder: (context, box) {
                      // If the open tab's name does not fit beside three
                      // icons (a narrow phone, large text), its pill keeps
                      // the icon alone; the name stays on its label.
                      final label = _labelWidth(
                        context,
                        selected.localizedLabel(context.l10n),
                      );
                      // Three icons, then the open pill: icon, gap, name, padding.
                      final needed = 3 * minTarget + 24 + 8 + 28 + label + 12;
                      final named = box.maxWidth >= needed;
                      return Align(
                        alignment: Alignment.centerLeft,
                        child: _Glass(
                          key: const Key('dock-capsule'),
                          radius: 30,
                          child: AnimatedSize(
                            duration: JarvisMotion.of(
                              context,
                              const Duration(milliseconds: 320),
                            ),
                            curve: JarvisMotion.standard,
                            alignment: Alignment.centerLeft,
                            child: compact
                                ? _compactTab(context, colors)
                                : _fullCapsule(named),
                          ),
                        ),
                      );
                    },
                  ),
                ),
                const SizedBox(width: 8),
                _Glass(
                  key: const Key('dock-orb'),
                  radius: 28,
                  child: SizedBox.square(
                    dimension: 56,
                    child: JarvisOrbButton(
                      key: orbKey,
                      onPressed: onJarvis,
                      busy: jarvisBusy,
                      size: 34,
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  /// The folded capsule: just the open tab, tapping it brings the rest back.
  Widget _compactTab(BuildContext context, JarvisColors colors) => _DockTab(
    tab: selected,
    selected: true,
    named: true,
    attention: selected == JarvisTab.chats && chatsAttention,
    onTap: onUnfold ?? () {},
    semanticsHint: 'Show all tabs',
  );

  Widget _fullCapsule(bool named) => Padding(
    padding: const EdgeInsets.all(4),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        for (final tab in JarvisTab.values)
          _DockTab(
            tab: tab,
            selected: tab == selected,
            named: tab == selected && named,
            attention: tab == JarvisTab.chats && chatsAttention,
            onTap: () => onSelect(tab),
          ),
      ],
    ),
  );

  static double _labelWidth(BuildContext context, String text) {
    final painter = TextPainter(
      text: TextSpan(text: text, style: _DockTab.labelStyle(context)),
      textDirection: TextDirection.ltr,
      textScaler: _DockTab.scaler(context),
      maxLines: 1,
    )..layout();
    final width = painter.width;
    painter.dispose();
    return width;
  }
}

/// A floating glass piece of the dock: blurred backdrop, soft edge and shadow.
class _Glass extends StatelessWidget {
  const _Glass({required this.radius, required this.child, super.key});

  final double radius;
  final Widget child;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(radius),
        boxShadow: JarvisShadows.soft(colors.brightness),
      ),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(radius),
        child: BackdropFilter(
          filter: ImageFilter.blur(sigmaX: 18, sigmaY: 18),
          child: DecoratedBox(
            decoration: BoxDecoration(
              color: colors.surface.withValues(alpha: .86),
              borderRadius: BorderRadius.circular(radius),
              border: Border.all(
                color: colors.outlineStrong.withValues(
                  alpha: colors.isDark ? .45 : .3,
                ),
                width: .5,
              ),
            ),
            child: Material(type: MaterialType.transparency, child: child),
          ),
        ),
      ),
    );
  }
}

/// One place in the capsule. The open one is a solid pill that names itself.
class _DockTab extends StatelessWidget {
  const _DockTab({
    required this.tab,
    required this.selected,
    required this.named,
    required this.attention,
    required this.onTap,
    this.semanticsHint,
  });

  final JarvisTab tab;
  final bool selected;

  /// Writes the tab's name beside its icon (only the open tab ever does).
  final bool named;
  final bool attention;
  final VoidCallback onTap;
  final String? semanticsHint;

  /// Labels stay readable at large text without outgrowing the dock.
  static TextScaler scaler(BuildContext context) =>
      MediaQuery.textScalerOf(context).clamp(maxScaleFactor: 1.4);

  static TextStyle labelStyle(BuildContext context) => TextStyle(
    fontFamily: 'Geist',
    fontSize: 13.5,
    fontWeight: FontWeight.w600,
    color: JarvisColors.of(context).ink,
  );

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final name = tab.localizedLabel(context.l10n);
    void activate() {
      if (!selected) unawaited(HapticFeedback.selectionClick());
      onTap();
    }

    final icon = Stack(
      clipBehavior: Clip.none,
      children: [
        TabIcon(
          key: ValueKey((tab.glyph, selected)),
          glyph: tab.glyph,
          color: selected ? colors.accent : colors.inkSoft,
          selected: selected,
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
                border: Border.all(color: colors.surface, width: 1.5),
              ),
            ),
          ),
      ],
    );
    return Semantics(
      button: true,
      selected: selected,
      label: attention ? context.l10n.tabUnread(name) : name,
      hint: semanticsHint,
      onTap: activate,
      excludeSemantics: true,
      child: Tooltip(
        message: name,
        excludeFromSemantics: true,
        child: PressFeedback(
          scale: .96,
          builder: (context, highlight) => InkResponse(
            key: Key('tab-${tab.name}'),
            onTap: activate,
            onHighlightChanged: highlight,
            radius: 28,
            child: AnimatedContainer(
              duration: JarvisMotion.of(context, JarvisMotion.fast),
              curve: JarvisMotion.standard,
              constraints: const BoxConstraints(
                minWidth: JarvisTabBar.minTarget,
                minHeight: JarvisTabBar.minTarget + 4,
              ),
              padding: EdgeInsets.symmetric(horizontal: named ? 14 : 4),
              decoration: BoxDecoration(
                color: selected
                    ? (colors.isDark
                          ? colors.surfaceRaised
                          : colors.surfaceMuted)
                    : Colors.transparent,
                borderRadius: BorderRadius.circular(26),
                boxShadow: selected
                    ? [
                        BoxShadow(
                          color: colors.accent.withValues(
                            alpha: colors.isDark ? .18 : .10,
                          ),
                          blurRadius: 12,
                          offset: const Offset(0, 3),
                        ),
                      ]
                    : null,
              ),
              child: Row(
                mainAxisSize: MainAxisSize.min,
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  icon,
                  if (named) ...[
                    const SizedBox(width: 8),
                    Text(
                      name,
                      key: const Key('tab-selected-label'),
                      maxLines: 1,
                      softWrap: false,
                      overflow: TextOverflow.ellipsis,
                      textScaler: scaler(context),
                      style: labelStyle(context),
                    ),
                  ],
                ],
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
      label: attention
          ? context.l10n.tabUnread(tab.localizedLabel(context.l10n))
          : tab.localizedLabel(context.l10n),
      onTap: activate,
      excludeSemantics: true,
      child: Tooltip(
        message: tab.localizedLabel(context.l10n),
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
