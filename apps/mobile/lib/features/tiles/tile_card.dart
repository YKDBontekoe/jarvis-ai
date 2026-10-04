import 'package:flutter/material.dart';

import '../../theme.dart';
import 'tile_models.dart';

/// One tile at one size. It only draws; the grid decides where it sits and
/// what tapping it does.
class TileCard extends StatelessWidget {
  const TileCard({
    required this.spec,
    required this.size,
    required this.data,
    this.onRowTap,
    super.key,
  });

  final TileSpec spec;
  final TileSize size;

  /// Null until the first load finishes or when the feature has no data.
  final TileData? data;
  final ValueChanged<TileRow>? onRowTap;

  static const _rowHeight = 30.0;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final info = data ?? TileData(subtitle: spec.fallback);
    // Tiles keep their grid size, so their text grows only a little with the
    // device's text size, as widgets on the home screen do.
    return MediaQuery.withClampedTextScaling(
      maxScaleFactor: 1.15,
      child: _frame(colors, info),
    );
  }

  Widget _frame(JarvisColors colors, TileData info) {
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(20),
        border: Border.all(color: colors.outline),
      ),
      child: switch (size) {
        TileSize.icon => _IconTile(spec: spec, info: info),
        TileSize.strip => _StripTile(spec: spec, info: info),
        TileSize.square => _SquareTile(spec: spec, info: info),
        TileSize.wide || TileSize.large => _ListTile(
          spec: spec,
          info: info,
          large: size == TileSize.large,
          onRowTap: onRowTap,
        ),
      },
    );
  }
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
              Icon(spec.icon, size: 24, color: colors.inkSoft),
              const SizedBox(height: 7),
              Padding(
                padding: const EdgeInsets.symmetric(horizontal: 4),
                child: Text(
                  spec.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 11,
                    fontWeight: FontWeight.w500,
                    color: colors.inkSoft,
                    height: 1,
                  ),
                ),
              ),
            ],
          ),
        ),
        if (info.attention) const Positioned(top: 9, right: 9, child: _Dot()),
      ],
    );
  }
}

class _StripTile extends StatelessWidget {
  const _StripTile({required this.spec, required this.info});

  final TileSpec spec;
  final TileData info;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final headline = info.stat == null
        ? spec.name
        : '${info.stat}${info.unit == null ? '' : ' ${info.unit}'}';
    return Padding(
      padding: const EdgeInsets.symmetric(horizontal: 14),
      child: Row(
        children: [
          Icon(spec.icon, size: 22, color: colors.inkSoft),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              mainAxisAlignment: MainAxisAlignment.center,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  headline,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(
                    fontSize: 13.5,
                    fontWeight: FontWeight.w600,
                    letterSpacing: -.1,
                    color: colors.ink,
                  ),
                ),
                const SizedBox(height: 2),
                Text(
                  info.subtitle ?? spec.name,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: TextStyle(fontSize: 12, color: colors.muted),
                ),
              ],
            ),
          ),
          if (info.attention) const _Dot(),
        ],
      ),
    );
  }
}

class _Label extends StatelessWidget {
  const _Label({required this.spec, required this.info});

  final TileSpec spec;
  final TileData info;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Row(
      children: [
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
        if (info.attention) const _Dot(),
      ],
    );
  }
}

class _SquareTile extends StatelessWidget {
  const _SquareTile({required this.spec, required this.info});

  final TileSpec spec;
  final TileData info;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Padding(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          _Label(spec: spec, info: info),
          const Spacer(),
          if (info.stat != null)
            FittedBox(
              fit: BoxFit.scaleDown,
              alignment: Alignment.centerLeft,
              child: Text.rich(
                TextSpan(
                  text: info.stat,
                  style: _stat(colors),
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
                maxLines: 1,
              ),
            ),
          if (info.subtitle != null) ...[
            const SizedBox(height: 5),
            Text(
              info.subtitle!,
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
      ),
    );
  }
}

class _ListTile extends StatelessWidget {
  const _ListTile({
    required this.spec,
    required this.info,
    required this.large,
    required this.onRowTap,
  });

  final TileSpec spec;
  final TileData info;
  final bool large;
  final ValueChanged<TileRow>? onRowTap;

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
          final used = 18 + 8 + (showHeadline ? 46 + 8 : 0);
          final fit = ((box.maxHeight - used) / rowHeight).floor().clamp(0, 12);
          final rows = info.rows.take(fit).toList();
          return Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              _Label(spec: spec, info: info),
              if (showHeadline) ...[
                const SizedBox(height: 10),
                FittedBox(
                  fit: BoxFit.scaleDown,
                  alignment: Alignment.centerLeft,
                  child: Text.rich(
                    maxLines: 1,
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
                  ),
                ),
              ],
              const SizedBox(height: 8),
              if (rows.isEmpty)
                Expanded(
                  child: Align(
                    alignment: Alignment.topLeft,
                    child: Text(
                      info.subtitle ?? spec.fallback,
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
                  SizedBox(
                    height: rowHeight,
                    child: InkWell(
                      borderRadius: BorderRadius.circular(8),
                      onTap: onRowTap == null || row.target == null
                          ? null
                          : () => onRowTap!(row),
                      child: Row(
                        children: [
                          Expanded(
                            child: Text(
                              row.text,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: TextStyle(
                                fontSize: 13.5,
                                color: colors.inkSoft,
                              ),
                            ),
                          ),
                          if (row.meta != null) ...[
                            const SizedBox(width: 10),
                            Text(
                              row.meta!,
                              style: TextStyle(
                                fontSize: 12,
                                fontWeight: row.attention
                                    ? FontWeight.w600
                                    : FontWeight.w400,
                                color: row.attention
                                    ? colors.accent
                                    : colors.muted,
                                fontFeatures: const [
                                  FontFeature.tabularFigures(),
                                ],
                              ),
                            ),
                          ],
                        ],
                      ),
                    ),
                  ),
            ],
          );
        },
      ),
    );
  }
}
