import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'home_context.dart';
import 'next_up.dart' show clockTime;

/// What a button on the Right now card asked for.
enum HeroAction { done, snooze, open, plan, brief, ok }

/// One slot, many moments: the next plan, an unread brief, a low battery, or
/// tomorrow. The content morphs when the moment changes; the card keeps its
/// place and says why it is there.
class RightNowCard extends StatelessWidget {
  const RightNowCard({
    required this.hero,
    required this.now,
    required this.onAction,
    required this.onDismiss,
    super.key,
  });

  final HomeHero hero;
  final DateTime now;
  final ValueChanged<HeroAction> onAction;

  /// Puts the card away; [mute] hides cards of this kind.
  final void Function({required bool mute}) onDismiss;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final look = _Look.of(hero.kind, colors);
    final duration = JarvisMotion.of(context, JarvisMotion.slow);
    return AnimatedContainer(
      key: const Key('home-right-now'),
      duration: duration,
      curve: JarvisMotion.emphasized,
      clipBehavior: Clip.antiAlias,
      decoration: BoxDecoration(
        color: look.background,
        borderRadius: BorderRadius.circular(28),
        border: look.border == null ? null : Border.all(color: look.border!),
        boxShadow: [
          BoxShadow(
            color: look.background.withValues(alpha: colors.isDark ? .3 : .28),
            blurRadius: 34,
            offset: const Offset(0, 16),
            spreadRadius: -18,
          ),
        ],
      ),
      child: Stack(
        children: [
          if (look.glow != null)
            Positioned(
              right: -70,
              top: -90,
              child: IgnorePointer(
                child: Container(
                  width: 260,
                  height: 260,
                  decoration: BoxDecoration(
                    shape: BoxShape.circle,
                    gradient: RadialGradient(
                      colors: [look.glow!, look.glow!.withValues(alpha: 0)],
                    ),
                  ),
                ),
              ),
            ),
          Padding(
            padding: const EdgeInsets.fromLTRB(22, 20, 14, 18),
            child: MotionSwitcher(
              resize: true,
              child: Column(
                key: ValueKey(hero.id),
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          look.label.toUpperCase(),
                          style: JarvisType.capsOf(
                            context,
                          ).copyWith(color: look.label2),
                        ),
                      ),
                      Semantics(
                        button: true,
                        label: 'Why am I seeing this?',
                        excludeSemantics: true,
                        child: InkWell(
                          key: const Key('home-right-now-why'),
                          borderRadius: BorderRadius.circular(999),
                          onTap: () => unawaited(_why(context)),
                          child: Padding(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 10,
                              vertical: 8,
                            ),
                            child: Row(
                              mainAxisSize: MainAxisSize.min,
                              children: [
                                Icon(
                                  PhosphorIconsRegular.info,
                                  size: 14,
                                  color: look.label2,
                                ),
                                const SizedBox(width: 4),
                                Text(
                                  'Why',
                                  style: TextStyle(
                                    fontSize: 12.5,
                                    fontWeight: FontWeight.w600,
                                    color: look.label2,
                                  ),
                                ),
                              ],
                            ),
                          ),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  _Title(hero: hero, now: now, look: look),
                  if (hero.detail case final detail?) ...[
                    const SizedBox(height: 8),
                    Text(
                      detail,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 15,
                        height: 1.4,
                        color: look.soft,
                      ),
                    ),
                  ],
                  const SizedBox(height: 18),
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: Row(
                      children: [
                        for (final (index, (action, label))
                            in _actions().indexed) ...[
                          if (index > 0) const SizedBox(width: 8),
                          Expanded(
                            child: _HeroButton(
                              key: Key('home-right-now-${action.name}'),
                              label: label,
                              primary: index == 0,
                              look: look,
                              onTap: () {
                                unawaited(HapticFeedback.lightImpact());
                                if (action == HeroAction.ok) {
                                  onDismiss(mute: false);
                                } else {
                                  onAction(action);
                                }
                              },
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: 12),
                  Row(
                    children: [
                      Icon(
                        PhosphorIconsRegular.sparkle,
                        size: 12,
                        color: look.faint,
                      ),
                      const SizedBox(width: 6),
                      Expanded(
                        child: Text(
                          hero.reason,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                          style: TextStyle(fontSize: 12, color: look.faint),
                        ),
                      ),
                    ],
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  List<(HeroAction, String)> _actions() => switch (hero.kind) {
    'soon' when hero.reminderId != null => [
      (HeroAction.done, 'Done'),
      (HeroAction.snooze, '+10 min'),
    ],
    'soon' => [(HeroAction.open, 'Open my day'), (HeroAction.plan, 'Prepare')],
    'brief' => [
      (HeroAction.brief, 'Read it'),
      (HeroAction.plan, 'Ask about it'),
    ],
    'battery' => [(HeroAction.ok, 'Got it')],
    'wind_down' => [
      (HeroAction.plan, 'Plan tomorrow'),
      (HeroAction.ok, 'Not now'),
    ],
    _ => [(HeroAction.ok, 'OK')],
  };

  Future<void> _why(BuildContext context) async {
    final choice = await showWhySheet(context, hero);
    if (choice == null) return;
    onDismiss(mute: choice == WhyChoice.fewer);
  }
}

class _Title extends StatelessWidget {
  const _Title({required this.hero, required this.now, required this.look});

  final HomeHero hero;
  final DateTime now;
  final _Look look;

  @override
  Widget build(BuildContext context) {
    final style = TextStyle(
      fontFamily: 'Geist',
      fontSize: 30,
      height: 1.12,
      fontWeight: FontWeight.w700,
      letterSpacing: -1.1,
      color: look.ink,
    );
    final start = hero.startsAt;
    if (hero.kind != 'soon' || start == null) {
      // The label already says "Tomorrow".
      final title = hero.kind == 'wind_down'
          ? hero.title.replaceFirst(RegExp(r'^Tomorrow:\s*'), '')
          : hero.title;
      return Text(title, key: const Key('home-right-now-title'), style: style);
    }
    final minutes = start.difference(now).inMinutes;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          hero.title,
          key: const Key('home-right-now-title'),
          maxLines: 2,
          overflow: TextOverflow.ellipsis,
          style: style,
        ),
        const SizedBox(height: 4),
        // Wraps rather than overflows at large text sizes.
        Wrap(
          crossAxisAlignment: WrapCrossAlignment.end,
          children: [
            if (minutes <= 0)
              Text(
                'Now',
                style: style.copyWith(fontSize: 22, color: look.accent),
              )
            else ...[
              Text(
                'in ',
                style: style.copyWith(fontSize: 22, color: look.soft),
              ),
              RollingNumber(
                '$minutes',
                key: const Key('home-right-now-minutes'),
                style: style.copyWith(
                  fontSize: 22,
                  color: look.accent,
                  fontFeatures: const [FontFeature.tabularFigures()],
                ),
              ),
              Text(
                ' min',
                style: style.copyWith(fontSize: 22, color: look.accent),
              ),
            ],
            Text(
              '  ·  ${clockTime(start)}',
              style: style.copyWith(fontSize: 18, color: look.soft),
            ),
          ],
        ),
      ],
    );
  }
}

class _HeroButton extends StatelessWidget {
  const _HeroButton({
    required this.label,
    required this.primary,
    required this.look,
    required this.onTap,
    super.key,
  });

  final String label;
  final bool primary;
  final _Look look;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    return PressFeedback(
      scale: .97,
      builder: (context, highlight) => Material(
        color: primary ? look.button : look.button2,
        borderRadius: BorderRadius.circular(14),
        child: InkWell(
          onTap: onTap,
          onHighlightChanged: highlight,
          borderRadius: BorderRadius.circular(14),
          child: SizedBox(
            height: 46,
            child: Center(
              child: Text(
                label,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 15,
                  fontWeight: FontWeight.w600,
                  color: primary ? look.buttonInk : look.button2Ink,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// The colours of one moment.
class _Look {
  const _Look({
    required this.label,
    required this.background,
    required this.ink,
    required this.soft,
    required this.faint,
    required this.label2,
    required this.accent,
    required this.button,
    required this.buttonInk,
    required this.button2,
    required this.button2Ink,
    this.glow,
    this.border,
  });

  final String label;
  final Color background;
  final Color ink;
  final Color soft;
  final Color faint;
  final Color label2;
  final Color accent;
  final Color button;
  final Color buttonInk;
  final Color button2;
  final Color button2Ink;
  final Color? glow;
  final Color? border;

  static _Look of(String kind, JarvisColors colors) {
    const white = Colors.white;
    switch (kind) {
      case 'brief':
        return _Look(
          label: 'Morning brief',
          background: colors.surface,
          border: colors.outline,
          ink: colors.ink,
          soft: colors.inkSoft,
          faint: colors.muted,
          label2: colors.accent,
          accent: colors.accent,
          button: colors.ink,
          buttonInk: colors.onInk,
          button2: colors.surfaceMuted,
          button2Ink: colors.ink,
        );
      case 'battery':
        return _Look(
          label: 'Before you go',
          background: colors.warningSoft,
          ink: colors.isDark
              ? const Color(0xfffde7c3)
              : const Color(0xff3d2208),
          soft: colors.isDark
              ? const Color(0xffe8c594)
              : const Color(0xff6b4318),
          faint: colors.isDark
              ? const Color(0xffc9a06a)
              : const Color(0xff8a6233),
          label2: colors.warning,
          accent: colors.warning,
          button: colors.isDark
              ? const Color(0xfffde7c3)
              : const Color(0xff3d2208),
          buttonInk: colors.isDark
              ? const Color(0xff3a2a12)
              : const Color(0xfffbf1e2),
          button2: colors.surface,
          button2Ink: colors.ink,
        );
      case 'wind_down':
        return const _Look(
          label: 'Tomorrow',
          background: Color(0xff141a33),
          ink: Color(0xffeef0ff),
          soft: Color(0xc0eef0ff),
          faint: Color(0x80eef0ff),
          label2: Color(0xff9fb4ff),
          accent: Color(0xff9fb4ff),
          button: Color(0xffeef0ff),
          buttonInk: Color(0xff141a33),
          button2: Color(0x1affffff),
          button2Ink: Color(0xffeef0ff),
          glow: Color(0x597aa2ff),
        );
      default:
        return _Look(
          label: 'Right now',
          background: colors.isDark
              ? colors.surfaceRaised
              : const Color(0xff111116),
          ink: white,
          soft: white.withValues(alpha: .78),
          faint: white.withValues(alpha: .5),
          label2: white.withValues(alpha: .62),
          accent: const Color(0xffb3abff),
          button: white,
          buttonInk: const Color(0xff111116),
          button2: white.withValues(alpha: .12),
          button2Ink: white,
          glow: const Color(0x8c7c6cff),
        );
    }
  }
}

/// What the person chose in the why sheet.
enum WhyChoice { notNow, fewer }

/// Lists what the card is based on, with ways to see less of it.
Future<WhyChoice?> showWhySheet(BuildContext context, HomeHero hero) {
  return showModalBottomSheet<WhyChoice>(
    context: context,
    showDragHandle: true,
    useSafeArea: true,
    isScrollControlled: true,
    builder: (sheet) {
      final colors = JarvisColors.of(sheet);
      return SingleChildScrollView(
        child: Padding(
          key: const Key('home-why-sheet'),
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text('WHY YOU SEE THIS', style: JarvisType.capsOf(sheet)),
              const SizedBox(height: 8),
              Text(
                hero.title,
                style: JarvisType.displayOf(
                  sheet,
                ).copyWith(fontSize: 24, fontWeight: FontWeight.w700),
              ),
              const SizedBox(height: 6),
              Text(
                hero.reason,
                style: TextStyle(fontSize: 14, color: colors.inkSoft),
              ),
              const SizedBox(height: 14),
              for (final fact in hero.facts)
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Row(
                    children: [
                      Container(
                        width: 32,
                        height: 32,
                        decoration: BoxDecoration(
                          color: colors.accentSoft,
                          borderRadius: BorderRadius.circular(10),
                        ),
                        child: Icon(
                          PhosphorIconsRegular.check,
                          size: 16,
                          color: colors.accent,
                        ),
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Text(
                          fact,
                          style: TextStyle(fontSize: 15, color: colors.ink),
                        ),
                      ),
                    ],
                  ),
                ),
              const SizedBox(height: 14),
              Material(
                color: colors.surfaceMuted,
                borderRadius: BorderRadius.circular(18),
                clipBehavior: Clip.antiAlias,
                child: Column(
                  children: [
                    Semantics(
                      button: true,
                      label: 'Not now',
                      excludeSemantics: true,
                      onTap: () => Navigator.of(sheet).pop(WhyChoice.notNow),
                      child: ListTile(
                        key: const Key('home-why-not-now'),
                        title: const Text('Not now'),
                        subtitle: const Text('Hide this card for a week'),
                        onTap: () => Navigator.of(sheet).pop(WhyChoice.notNow),
                      ),
                    ),
                    Divider(height: 1, color: colors.outline),
                    Semantics(
                      button: true,
                      label: 'Fewer cards like this',
                      excludeSemantics: true,
                      onTap: () => Navigator.of(sheet).pop(WhyChoice.fewer),
                      child: ListTile(
                        key: const Key('home-why-fewer'),
                        title: const Text('Fewer cards like this'),
                        subtitle: const Text('Stop showing this kind of card'),
                        onTap: () => Navigator.of(sheet).pop(WhyChoice.fewer),
                      ),
                    ),
                  ],
                ),
              ),
            ],
          ),
        ),
      );
    },
  );
}
