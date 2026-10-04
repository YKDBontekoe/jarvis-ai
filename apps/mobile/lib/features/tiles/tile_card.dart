import 'dart:math' as math;
import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/motion.dart';
import '../../ui/phosphor_icons.dart';
import '../home/next_up.dart' show countdownLabel;
import 'tile_models.dart';
import 'tile_visuals.dart';

/// Asked to run [action] on the item with [itemId] (null for the tile itself).
typedef TileActionCallback = void Function(TileAction action, String? itemId);

/// One tile at one size. It draws what the data says and reports taps; the grid
/// decides where it sits and what tapping it does.
class TileCard extends StatelessWidget {
  const TileCard({
    required this.spec,
    required this.size,
    required this.data,
    this.onRowTap,
    this.onAction,
    this.now,
    this.loading = false,
    this.pending = const {},
    super.key,
  });

  final TileSpec spec;
  final TileSize size;

  /// Null until the first load finishes or when the feature has no data.
  final TileData? data;
  final ValueChanged<TileRow>? onRowTap;

  /// Quick actions drawn on the tile. Buttons are inert while this is null.
  final TileActionCallback? onAction;

  /// The clock live countdowns read; the wall clock when null.
  final DateTime? now;

  /// The first load is still on its way: show placeholders, not the fallback.
  final bool loading;

  /// Ids of items an action is running on; their rows are dimmed.
  final Set<String> pending;

  static const _rowHeight = 30.0;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    // Tiles keep their grid size, so their text grows only a little with the
    // device's text size, as widgets on the home screen do.
    return MediaQuery.withClampedTextScaling(
      maxScaleFactor: 1.15,
      child: DecoratedBox(
        decoration: BoxDecoration(
          color: colors.surface,
          borderRadius: BorderRadius.circular(20),
          border: Border.all(color: colors.outline),
        ),
        child: _content(),
      ),
    );
  }

  Widget _content() {
    if (data == null && loading) return TileSkeleton(size: size);
    final info = data ?? TileData(subtitle: spec.fallback);
    final clock = now ?? DateTime.now();
    return switch (size) {
      TileSize.icon => _IconTile(spec: spec, info: info),
      TileSize.strip => _StripTile(
        spec: spec,
        info: info,
        clock: clock,
        onAction: onAction,
      ),
      TileSize.square => _SquareTile(
        spec: spec,
        info: info,
        clock: clock,
        onAction: onAction,
      ),
      TileSize.wide || TileSize.large => _ListTile(
        spec: spec,
        info: info,
        clock: clock,
        large: size == TileSize.large,
        onRowTap: onRowTap,
        onAction: onAction,
        pending: pending,
      ),
    };
  }
}

/// The subtitle, or "Next item · in 24 min" when the tile counts down.
String? _subtitle(TileData info, DateTime clock) {
  final to = info.countdownTo;
  final label = info.focusLabel;
  if (to != null && label != null) {
    return '$label · ${countdownLabel(to, clock)}';
  }
  return info.subtitle;
}

TextStyle _stat(JarvisColors colors, {double size = 30}) => TextStyle(
  fontFamily: 'Geist',
  fontSize: size,
  fontWeight: FontWeight.w600,
  letterSpacing: -size * .05,
  height: 1,
  color: colors.ink,
  fontFeatures: const [FontFeature.tabularFigures()],
);

/// A number that slides to its new value when it changes.
class _AnimatedText extends StatelessWidget {
  const _AnimatedText(this.span, {required this.keyText});

  final InlineSpan span;
  final String keyText;

  @override
  Widget build(BuildContext context) => AnimatedSwitcher(
    duration: JarvisMotion.of(context, JarvisMotion.base),
    switchInCurve: JarvisMotion.standard,
    switchOutCurve: JarvisMotion.exit,
    transitionBuilder: (child, animation) => FadeTransition(
      opacity: animation,
      child: SlideTransition(
        position: Tween(
          begin: const Offset(0, .25),
          end: Offset.zero,
        ).animate(animation),
        child: child,
      ),
    ),
    layoutBuilder: (current, previous) => Stack(
      alignment: Alignment.centerLeft,
      children: [...previous, ?current],
    ),
    child: Text.rich(span, key: ValueKey(keyText), maxLines: 1),
  );
}

class _Dot extends StatelessWidget {
  const _Dot();

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('tile-attention'),
    width: 8,
    height: 8,
    decoration: BoxDecoration(
      color: JarvisColors.of(context).accent,
      shape: BoxShape.circle,
    ),
  );
}

class _IconTile extends StatelessWidget {
  const _IconTile({required this.spec, required this.info});

  final TileSpec spec;
  final TileData info;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Stack(
      children: [
        Center(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              if (info.visual == TileVisual.waveform)
                TileWaveform(color: colors.accent)
              else
                Icon(spec.icon, size: 24, color: colors.inkSoft),
              const SizedBox(height: 7),
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 4),
                child: Text(
                  info.visual == TileVisual.waveform
                      ? (info.subtitle ?? spec.name)
                      : spec.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w500,
                    color: info.visual == TileVisual.waveform
                        ? colors.accent
                        : colors.inkSoft,
                    height: 1,
                  ),
                ),
              ),
            ],
          ),
        ),
        if (info.attention && info.visual != TileVisual.waveform)
          const Positioned(top: 9, right: 9, child: _Dot()),
      ],
    );
  }
}

class _StripTile extends StatelessWidget {
  const _StripTile({
    required this.spec,
    required this.info,
    required this.clock,
    required this.onAction,
  });

  final TileSpec spec;
  final TileData info;
  final DateTime clock;
  final TileActionCallback? onAction;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final live = info.visual == TileVisual.waveform;
    final headline = info.stat == null
        ? spec.name
        : '${info.stat}${info.unit == null ? '' : ' ${info.unit}'}';
    final quick = _quickAction(info);
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14),
      child: Row(
        children: [
          if (live)
            TileWaveform(color: colors.accent, height: 22)
          else
            Icon(spec.icon, size: 22, color: colors.inkSoft),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                _AnimatedText(
                  TextSpan(
                    text: headline,
                    style: TextStyle(
                      fontSize: 13.5,
                      fontWeight: FontWeight.w600,
                      letterSpacing: -.1,
                      color: colors.ink,
                    ),
                  ),
                  keyText: headline,
                ),
                const SizedBox(height: 2),
                Text(
                  _subtitle(info, clock) ?? spec.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 12,
                    color: live ? colors.accent : colors.muted,
                  ),
                ),
              ],
            ),
          ),
          if (quick != null && info.focusId != null)
            _RoundButton(
              action: quick,
              onTap: onAction == null
                  ? null
                  : () => onAction!(quick, info.focusId),
            )
          else if (info.attention)
            const _Dot(),
        ],
      ),
    );
  }
}

/// The one action a strip can offer as a round button.
TileAction? _quickAction(TileData info) {
  for (final action in info.actions) {
    if (action.primary && action.icon != null) return action;
  }
  return null;
}

class _Label extends StatelessWidget {
  const _Label({required this.spec, required this.info, this.trailing});

  final TileSpec spec;
  final TileData info;

  /// Small detail on the right, such as the count.
  final String? trailing;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return LayoutBuilder(
      builder: (context, box) => Row(
        children: [
          Icon(spec.icon, size: 15, color: colors.muted),
          const SizedBox(width: 6),
          Expanded(
            child: Text(
              spec.name,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 12.5,
                fontWeight: FontWeight.w500,
                color: colors.muted,
              ),
            ),
          ),
          if (trailing != null)
            ConstrainedBox(
              // Room for a count; a long unit gives way to the name.
              constraints: BoxConstraints(maxWidth: box.maxWidth * .55),
              child: Text(
                trailing!,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: TextStyle(
                  fontSize: 12,
                  fontWeight: FontWeight.w600,
                  color: info.attention ? colors.accent : colors.muted,
                ),
              ),
            )
          else if (info.attention)
            const _Dot(),
        ],
      ),
    );
  }
}

/// A round button with an action's icon.
class _RoundButton extends StatelessWidget {
  const _RoundButton({required this.action, required this.onTap});

  final TileAction action;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: action.label,
      excludeSemantics: true,
      onTap: onTap,
      child: Material(
        color: colors.surfaceMuted,
        shape: const CircleBorder(),
        child: InkWell(
          key: Key('tile-action-${action.id}'),
          customBorder: const CircleBorder(),
          onTap: onTap,
          child: SizedBox.square(
            dimension: 32,
            child: Icon(
              action.icon ?? PhosphorIconsRegular.check,
              size: 16,
              color: colors.ink,
            ),
          ),
        ),
      ),
    );
  }
}

/// Up to two labelled buttons side by side along the bottom of a square tile.
class _ActionRow extends StatelessWidget {
  const _ActionRow({
    required this.actions,
    required this.onAction,
    this.itemId,
  });

  final List<TileAction> actions;
  final TileActionCallback? onAction;
  final String? itemId;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final shown = actions.take(2).toList();
    return Row(
      children: [
        for (final (i, action) in shown.indexed) ...[
          if (i > 0) const SizedBox(width: 6),
          Expanded(
            child: Material(
              color: action.primary ? colors.ink : colors.surfaceMuted,
              borderRadius: BorderRadius.circular(15),
              child: InkWell(
                key: Key('tile-action-${action.id}'),
                borderRadius: BorderRadius.circular(15),
                onTap: onAction == null
                    ? null
                    : () => onAction!(action, itemId),
                child: SizedBox(
                  height: 30,
                  child: Center(
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        if (action.icon != null && shown.length == 1) ...[
                          Icon(
                            action.icon,
                            size: 14,
                            color: action.primary ? colors.onInk : colors.ink,
                          ),
                          const SizedBox(width: 5),
                        ],
                        Flexible(
                          child: Text(
                            action.label,
                            maxLines: 1,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              fontSize: 12.5,
                              fontWeight: FontWeight.w600,
                              color: action.primary ? colors.onInk : colors.ink,
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
        ],
      ],
    );
  }
}

class _SquareTile extends StatelessWidget {
  const _SquareTile({
    required this.spec,
    required this.info,
    required this.clock,
    required this.onAction,
  });

  final TileSpec spec;
  final TileData info;
  final DateTime clock;
  final TileActionCallback? onAction;

  String? get _count => info.stat == null
      ? null
      : '${info.stat}${info.unit == null ? '' : ' ${info.unit}'}';

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final hasFocus = info.actions.isNotEmpty && info.focusId != null;
    Widget body;
    if (info.visual == TileVisual.ring && info.progress != null) {
      body = _ringBody(colors, hasFocus);
    } else if (info.visual == TileVisual.bars && info.bars.isNotEmpty) {
      body = _barsBody(colors);
    } else if (hasFocus) {
      body = _focusBody(colors);
    } else {
      body = _plainBody(colors);
    }
    return Padding(padding: const EdgeInsets.all(14), child: body);
  }

  Widget _ringBody(JarvisColors colors, bool hasFocus) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      _Label(spec: spec, info: info),
      Expanded(
        child: LayoutBuilder(
          builder: (context, box) {
            final size = math.min(72.0, math.min(box.maxWidth, box.maxHeight));
            if (size < 28) return const SizedBox.shrink();
            return Center(
              child: TileRing(
                progress: info.progress!,
                size: size,
                stroke: math.max(4, size * .09),
                child: _AnimatedText(
                  TextSpan(
                    text: info.stat ?? '',
                    style: _stat(colors, size: math.max(11, size * .26)),
                  ),
                  keyText: info.stat ?? '',
                ),
              ),
            );
          },
        ),
      ),
      const SizedBox(height: 6),
      if (hasFocus)
        _ActionRow(
          actions: info.actions,
          onAction: onAction,
          itemId: info.focusId,
        )
      else
        Text(
          _subtitle(info, clock) ?? '',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 12.5, color: colors.inkSoft),
        ),
    ],
  );

  Widget _barsBody(JarvisColors colors) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      _Label(spec: spec, info: info),
      const SizedBox(height: 4),
      Expanded(
        child: TileBarChart(bars: info.bars, idleCaption: info.subtitle),
      ),
      const SizedBox(height: 4),
      FittedBox(
        fit: BoxFit.scaleDown,
        alignment: Alignment.centerLeft,
        child: _AnimatedText(
          TextSpan(
            text: info.stat,
            style: _stat(colors, size: 26),
            children: [
              if (info.unit != null)
                TextSpan(
                  text: ' ${info.unit}',
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: FontWeight.w500,
                    letterSpacing: 0,
                    color: colors.muted,
                  ),
                ),
            ],
          ),
          keyText: '${info.stat}${info.unit}',
        ),
      ),
    ],
  );

  Widget _focusBody(JarvisColors colors) {
    final countdown = info.countdownTo == null
        ? null
        : countdownLabel(info.countdownTo!, clock);
    final sub = countdown ?? _count ?? '';
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _Label(
          spec: spec,
          info: info,
          trailing: countdown == null ? null : _count,
        ),
        const Spacer(),
        Text(
          info.focusLabel ?? '',
          maxLines: 1,
          overflow: TextOverflow.ellipsis,
          style: TextStyle(
            fontSize: 15,
            fontWeight: FontWeight.w600,
            letterSpacing: -.2,
            color: colors.ink,
          ),
        ),
        const SizedBox(height: 2),
        _AnimatedText(
          TextSpan(
            text: sub,
            style: TextStyle(
              fontSize: 12.5,
              fontWeight: FontWeight.w500,
              color: info.attention ? colors.accent : colors.inkSoft,
            ),
          ),
          keyText: sub,
        ),
        const SizedBox(height: 8),
        _ActionRow(
          actions: info.actions,
          onAction: onAction,
          itemId: info.focusId,
        ),
      ],
    );
  }

  Widget _plainBody(JarvisColors colors) {
    final subtitle = _subtitle(info, clock);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        _Label(spec: spec, info: info),
        // The number sits under the name, so neighbouring tiles line up; the
        // detail settles at the bottom.
        if (info.stat != null) const SizedBox(height: 12) else const Spacer(),
        if (info.stat != null)
          FittedBox(
            fit: BoxFit.scaleDown,
            alignment: Alignment.centerLeft,
            child: _AnimatedText(
              TextSpan(
                text: info.stat,
                style: _stat(colors, size: 34),
                children: [
                  if (info.unit != null)
                    TextSpan(
                      text: ' ${info.unit}',
                      style: TextStyle(
                        fontSize: 13,
                        fontWeight: FontWeight.w500,
                        letterSpacing: 0,
                        color: colors.muted,
                      ),
                    ),
                ],
              ),
              keyText: '${info.stat}${info.unit}',
            ),
          ),
        if (info.stat != null) const Spacer(),
        if (info.visual == TileVisual.waveform)
          _LiveStatus(text: subtitle ?? '')
        else if (subtitle != null) ...[
          if (info.stat == null) const SizedBox(height: 5),
          Text(
            subtitle,
            maxLines: info.stat == null
                ? 3
                : info.progress != null
                ? 1
                : 2,
            overflow: TextOverflow.ellipsis,
            style: TextStyle(
              fontSize: info.stat == null ? 15 : 12.5,
              fontWeight: info.stat == null ? FontWeight.w600 : null,
              height: 1.3,
              color: info.stat == null ? colors.ink : colors.inkSoft,
            ),
          ),
        ],
        if (info.progress != null) ...[
          const SizedBox(height: 8),
          ClipRRect(
            borderRadius: BorderRadius.circular(2),
            child: LinearProgressIndicator(
              value: info.progress!.clamp(0, 1),
              minHeight: 4,
              backgroundColor: colors.surfaceMuted,
              valueColor: AlwaysStoppedAnimation(colors.accent),
            ),
          ),
        ],
      ],
    );
  }
}

class _ListTile extends StatelessWidget {
  const _ListTile({
    required this.spec,
    required this.info,
    required this.clock,
    required this.large,
    required this.onRowTap,
    required this.onAction,
    required this.pending,
  });

  final TileSpec spec;
  final TileData info;
  final DateTime clock;
  final bool large;
  final ValueChanged<TileRow>? onRowTap;
  final TileActionCallback? onAction;
  final Set<String> pending;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final scale = MediaQuery.textScalerOf(context).scale(1).clamp(1.0, 1.6);
    final rowHeight = TileCard._rowHeight * scale;
    return Padding(
      padding: const EdgeInsets.fromLTRB(14, 14, 14, 6),
      child: LayoutBuilder(
        builder: (context, box) {
          final showHeadline = large && info.stat != null;
          final picture = switch (info.visual) {
            TileVisual.timeline when info.timeline != null => 60.0,
            TileVisual.bars when info.bars.isNotEmpty => large ? 84.0 : 60.0,
            _ => 0.0,
          };
          final live = info.visual == TileVisual.waveform;
          final status = live ? 26.0 : 0.0;
          final room =
              box.maxHeight - 18 - 8 - status - (showHeadline ? 56 : 0);
          // A picture is worth more than rows once there is room for both.
          final showPicture = picture > 0 && room >= picture + 4;
          final used =
              status +
              18 +
              8 +
              (showHeadline ? 56 : 0) +
              (showPicture ? picture + 6 : 0);
          final fit = ((box.maxHeight - used) / rowHeight).floor().clamp(0, 12);
          final rows = info.rows.take(fit).toList();
          // When the rows fill the tile, they share what is left over instead
          // of leaving a gap under the last one.
          final spread = rows.isNotEmpty && rows.length == fit
              ? math.min(rowHeight * 1.25, (box.maxHeight - used) / fit)
              : rowHeight;
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Label(
                spec: spec,
                info: info,
                trailing: !showHeadline && info.attention && info.stat != null
                    ? '${info.stat}${info.unit == null ? '' : ' ${info.unit}'}'
                    : null,
              ),
              if (live) _LiveStatus(text: info.subtitle ?? ''),
              if (showHeadline) ...[
                const SizedBox(height: 10),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  alignment: Alignment.centerLeft,
                  child: _AnimatedText(
                    TextSpan(
                      text: info.stat,
                      style: _stat(colors, size: 34),
                      children: [
                        if (info.unit != null)
                          TextSpan(
                            text: ' ${info.unit}',
                            style: TextStyle(
                              fontSize: 14,
                              fontWeight: FontWeight.w500,
                              letterSpacing: 0,
                              color: colors.muted,
                            ),
                          ),
                      ],
                    ),
                    keyText: '${info.stat}${info.unit}',
                  ),
                ),
              ],
              const SizedBox(height: 8),
              if (showPicture) ...[
                SizedBox(
                  height: picture,
                  child: info.visual == TileVisual.timeline
                      ? TileTimelineStrip(timeline: info.timeline!)
                      : TileBarChart(
                          bars: info.bars,
                          idleCaption: info.subtitle,
                        ),
                ),
                const SizedBox(height: 6),
              ],
              if (rows.isEmpty && !showPicture)
                Expanded(
                  child: Align(
                    alignment: Alignment.topLeft,
                    child: Text(
                      _subtitle(info, clock) ?? spec.fallback,
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        fontSize: 15,
                        fontWeight: FontWeight.w500,
                        height: 1.3,
                        color: colors.inkSoft,
                      ),
                    ),
                  ),
                )
              else
                for (final row in rows)
                  _RowView(
                    row: row,
                    height: spread,
                    busy: row.id != null && pending.contains(row.id),
                    onTap: onRowTap == null || row.target == null
                        ? null
                        : () => onRowTap!(row),
                    onAction: onAction,
                  ),
            ],
          );
        },
      ),
    );
  }
}

/// "Jarvis is replying…" with moving bars, under a tile's name.
class _LiveStatus extends StatelessWidget {
  const _LiveStatus({required this.text});

  final String text;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Row(
        children: [
          TileWaveform(color: colors.accent, height: 14),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              text,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 12.5,
                fontWeight: FontWeight.w500,
                color: colors.accent,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _RowView extends StatelessWidget {
  const _RowView({
    required this.row,
    required this.height,
    required this.busy,
    required this.onTap,
    required this.onAction,
  });

  final TileRow row;
  final double height;
  final bool busy;
  final VoidCallback? onTap;
  final TileActionCallback? onAction;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final action = row.actions.isEmpty ? null : row.actions.first;
    final struck = busy || row.done;
    return AnimatedOpacity(
      duration: JarvisMotion.of(context, JarvisMotion.base),
      opacity: busy ? .45 : 1,
      child: SizedBox(
        height: height,
        child: InkWell(
          borderRadius: BorderRadius.circular(8),
          onTap: onTap,
          child: Row(
            children: [
              if (action != null) ...[
                _RowCheck(
                  key: Key('row-action-${row.id}-${action.id}'),
                  row: row,
                  action: action,
                  onTap: busy || onAction == null
                      ? null
                      : () => onAction!(action, row.id),
                ),
                const SizedBox(width: 10),
              ],
              Expanded(
                child: Text(
                  row.text,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 13.5,
                    fontWeight: row.attention && !struck
                        ? FontWeight.w600
                        : FontWeight.w400,
                    color: struck
                        ? colors.muted
                        : row.attention
                        ? colors.ink
                        : colors.inkSoft,
                    decoration: struck && (busy || row.done)
                        ? TextDecoration.lineThrough
                        : null,
                    decorationColor: colors.muted,
                  ),
                ),
              ),
              if (row.meta != null) ...[
                const SizedBox(width: 10),
                Text(
                  row.meta!,
                  style: TextStyle(
                    fontSize: 12,
                    fontWeight: row.attention && !row.done
                        ? FontWeight.w600
                        : FontWeight.w400,
                    color: row.attention && !row.done
                        ? colors.accent
                        : colors.muted,
                    fontFeatures: const [FontFeature.tabularFigures()],
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

/// The round control in front of a row: a checkbox for things you finish, a
/// small icon button for the rest.
class _RowCheck extends StatelessWidget {
  const _RowCheck({
    required this.row,
    required this.action,
    required this.onTap,
    super.key,
  });

  final TileRow row;
  final TileAction action;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final checkbox =
        action.id == 'done' || action.id == 'check' || action.id == 'uncheck';
    final filled = checkbox && row.done;
    return Semantics(
      button: true,
      label: '${action.label}: ${row.text}',
      excludeSemantics: true,
      onTap: onTap,
      child: InkResponse(
        onTap: onTap,
        radius: 20,
        child: SizedBox.square(
          dimension: 28,
          child: Center(
            child: AnimatedContainer(
              duration: JarvisMotion.of(context, JarvisMotion.fast),
              width: 22,
              height: 22,
              decoration: BoxDecoration(
                shape: BoxShape.circle,
                color: filled ? colors.accent : Colors.transparent,
                border: Border.all(
                  color: filled ? colors.accent : colors.outlineStrong,
                  width: 1.6,
                ),
              ),
              child: filled || !checkbox
                  ? Icon(
                      checkbox
                          ? PhosphorIconsRegular.check
                          : (action.icon ?? PhosphorIconsRegular.x),
                      size: 13,
                      color: filled
                          ? (colors.isDark ? colors.canvas : Colors.white)
                          : colors.muted,
                    )
                  : null,
            ),
          ),
        ),
      ),
    );
  }
}
