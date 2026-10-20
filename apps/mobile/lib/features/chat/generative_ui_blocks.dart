part of 'generative_ui.dart';

/// The icon for a name from RenderUi's list, or null for anything else.
IconData? surfaceIcon(String? name) => switch (name) {
  'alarm' => PhosphorIconsRegular.alarm,
  'bell' => PhosphorIconsRegular.bell,
  'book' => PhosphorIconsRegular.bookOpen,
  'brain' => PhosphorIconsRegular.brain,
  'calendar' => PhosphorIconsRegular.calendarBlank,
  'car' => PhosphorIconsRegular.car,
  'chart' => PhosphorIconsRegular.chartBar,
  'check' => PhosphorIconsRegular.checkCircle,
  'clock' => PhosphorIconsRegular.clock,
  'fire' => PhosphorIconsRegular.fire,
  'food' => PhosphorIconsRegular.forkKnife,
  'gift' => PhosphorIconsRegular.gift,
  'globe' => PhosphorIconsRegular.globe,
  'heart' => PhosphorIconsRegular.heart,
  'home' => PhosphorIconsRegular.house,
  'idea' => PhosphorIconsRegular.lightbulb,
  'info' => PhosphorIconsRegular.info,
  'key' => PhosphorIconsRegular.key,
  'lightning' => PhosphorIconsRegular.lightning,
  'list' => PhosphorIconsRegular.listChecks,
  'location' => PhosphorIconsRegular.mapPin,
  'lock' => PhosphorIconsRegular.lockSimple,
  'money' => PhosphorIconsRegular.wallet,
  'phone' => PhosphorIconsRegular.phone,
  'plane' => PhosphorIconsRegular.airplaneTilt,
  'receipt' => PhosphorIconsRegular.receipt,
  'rocket' => PhosphorIconsRegular.rocketLaunch,
  'shopping' => PhosphorIconsRegular.shoppingCart,
  'sparkle' => PhosphorIconsRegular.sparkle,
  'star' => PhosphorIconsRegular.star,
  'target' => PhosphorIconsRegular.target,
  'timer' => PhosphorIconsRegular.timer,
  'trophy' => PhosphorIconsRegular.trophy,
  'trend-up' => PhosphorIconsRegular.trendUp,
  'trend-down' => PhosphorIconsRegular.trendDown,
  'user' => PhosphorIconsRegular.user,
  'users' => PhosphorIconsRegular.users,
  'warning' => PhosphorIconsRegular.warningCircle,
  'work' => PhosphorIconsRegular.folderSimple,
  _ => null,
};

/// Foreground and soft background for a block's tone.
(Color, Color) _tone(JarvisColors colors, String? tone) => switch (tone) {
  'success' => (colors.accentDeep, colors.accentSoft),
  'warning' => (colors.warning, colors.warningSoft),
  'danger' => (colors.danger, colors.dangerSoft),
  'info' => (colors.info, colors.infoSoft),
  'neutral' => (colors.inkSoft, colors.surfaceMuted),
  _ => (colors.accent, colors.accentSoft),
};

/// A small rounded tag beside an item ("Due", "New").
class SurfaceTag extends StatelessWidget {
  const SurfaceTag({required this.label, super.key});

  final String label;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: colors.accentSoft,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Text(
        label,
        maxLines: 1,
        overflow: TextOverflow.ellipsis,
        style: TextStyle(
          color: colors.accentDeep,
          fontSize: 12,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}

/// One option of a short choice field, as a pill.
class SurfaceChoiceChip extends StatelessWidget {
  const SurfaceChoiceChip({
    required this.label,
    required this.selected,
    required this.onTap,
    super.key,
  });

  final String label;
  final bool selected;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      selected: selected,
      label: label,
      onTap: onTap,
      excludeSemantics: true,
      child: PressFeedback(
        scale: .96,
        builder: (context, onHighlight) => Material(
          color: selected ? colors.ink : colors.surfaceMuted,
          shape: const StadiumBorder(),
          clipBehavior: Clip.antiAlias,
          child: InkWell(
            onTap: onTap,
            onHighlightChanged: onTap == null ? null : onHighlight,
            child: ConstrainedBox(
              constraints: const BoxConstraints(minHeight: 44),
              child: Padding(
                padding: const EdgeInsets.symmetric(
                  horizontal: 16,
                  vertical: 10,
                ),
                child: Text(
                  label,
                  style: TextStyle(
                    color: selected ? colors.onInk : colors.ink,
                    fontWeight: FontWeight.w600,
                    fontSize: 14.5,
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

/// Draws a card Jarvis composed from blocks: headings, text, stats, progress,
/// bars, steps, pairs, chips, callouts, dividers and columns.
class SurfaceBlocks extends StatelessWidget {
  const SurfaceBlocks({required this.blocks, super.key});

  final List<Map<String, dynamic>> blocks;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      for (var index = 0; index < blocks.length; index++) ...[
        if (index > 0) const SizedBox(height: 14),
        _SurfaceBlock(block: blocks[index]),
      ],
    ],
  );
}

class _SurfaceBlock extends StatelessWidget {
  const _SurfaceBlock({required this.block});

  final Map<String, dynamic> block;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final entries = jsonMaps(block['items']);
    return switch (asString(block['type'])) {
      'heading' => Text(
        asString(block['text']) ?? '',
        style: TextStyle(
          color: colors.ink,
          fontSize: 17,
          fontWeight: FontWeight.w600,
          letterSpacing: -.3,
        ),
      ),
      'text' => Text(
        asString(block['text']) ?? '',
        style: TextStyle(
          color: block['tone'] == null
              ? colors.inkSoft
              : _tone(colors, asString(block['tone'])).$1,
          fontSize: 14.5,
          height: 1.45,
        ),
      ),
      'stats' => _Stats(items: entries),
      'progress' => _Progress(block: block),
      'bars' => _Bars(items: entries, unit: asString(block['unit'])),
      'steps' => _Steps(items: entries),
      'pairs' => _Pairs(items: entries),
      'chips' => Wrap(
        spacing: 6,
        runSpacing: 6,
        children: [
          for (final label in (block['labels'] as List? ?? const []))
            if (label is String)
              Container(
                padding: const EdgeInsets.symmetric(
                  horizontal: 12,
                  vertical: 6,
                ),
                decoration: BoxDecoration(
                  color: colors.surfaceMuted,
                  borderRadius: BorderRadius.circular(999),
                ),
                child: Text(
                  label,
                  style: TextStyle(
                    color: colors.inkSoft,
                    fontSize: 13,
                    fontWeight: FontWeight.w500,
                  ),
                ),
              ),
        ],
      ),
      'callout' => _Callout(block: block),
      'divider' => Divider(height: 1, thickness: 1, color: colors.outline),
      'columns' => _Columns(children: jsonMaps(block['children'])),
      _ => const SizedBox.shrink(),
    };
  }
}

class _Stats extends StatelessWidget {
  const _Stats({required this.items});

  final List<Map<String, dynamic>> items;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final scale = MediaQuery.textScalerOf(context).scale(1);
    return LayoutBuilder(
      builder: (context, constraints) {
        // Two or three tiles a row, fewer when the text is large.
        final perRow = scale >= 1.5
            ? 1
            : constraints.maxWidth >= 420 && items.length >= 3
            ? 3
            : items.length == 1
            ? 1
            : 2;
        const gap = 8.0;
        final width = (constraints.maxWidth - gap * (perRow - 1)) / perRow;
        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [
            for (final (index, item) in items.indexed)
              Container(
                // An odd last tile takes the whole row instead of half.
                width:
                    perRow == 2 &&
                        items.length.isOdd &&
                        index == items.length - 1
                    ? constraints.maxWidth
                    : width,
                padding: const EdgeInsets.fromLTRB(12, 10, 12, 12),
                decoration: BoxDecoration(
                  color: colors.surfaceMuted,
                  borderRadius: BorderRadius.circular(16),
                ),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      asString(item['label']) ?? '',
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(
                        color: colors.muted,
                        fontSize: 12.5,
                        fontWeight: FontWeight.w500,
                      ),
                    ),
                    const SizedBox(height: 4),
                    FittedBox(
                      fit: BoxFit.scaleDown,
                      alignment: Alignment.centerLeft,
                      child: Text(
                        asString(item['value']) ?? '',
                        style: TextStyle(
                          color: colors.ink,
                          fontSize: 24,
                          fontWeight: FontWeight.w700,
                          letterSpacing: -.6,
                          fontFeatures: const [FontFeature.tabularFigures()],
                        ),
                      ),
                    ),
                    if (asString(item['delta']) case final delta?)
                      _Delta(text: delta, trend: asString(item['trend'])),
                  ],
                ),
              ),
          ],
        );
      },
    );
  }
}

class _Delta extends StatelessWidget {
  const _Delta({required this.text, this.trend});

  final String text;
  final String? trend;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final (icon, color) = switch (trend) {
      'up' => (PhosphorIconsRegular.trendUp, colors.accent),
      'down' => (PhosphorIconsRegular.trendDown, colors.warning),
      _ => (null, colors.inkSoft),
    };
    return Padding(
      padding: const EdgeInsets.only(top: 2),
      child: Row(
        children: [
          if (icon != null) ...[
            Icon(icon, size: 14, color: color),
            const SizedBox(width: 4),
          ],
          Flexible(
            child: Text(
              text,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: color,
                fontSize: 12.5,
                fontWeight: FontWeight.w600,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Grows from zero on first show, or appears at once with reduced motion.
class _Grow extends StatelessWidget {
  const _Grow({required this.value, required this.builder});

  final double value;
  final Widget Function(double value) builder;

  @override
  Widget build(BuildContext context) => TweenAnimationBuilder<double>(
    tween: Tween(begin: 0, end: value),
    duration: JarvisMotion.of(context, JarvisMotion.slow),
    curve: JarvisMotion.emphasized,
    builder: (context, value, _) => builder(value),
  );
}

class _Progress extends StatelessWidget {
  const _Progress({required this.block});

  final Map<String, dynamic> block;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final value = (_number(block['value']) ?? 0).clamp(0.0, 1.0).toDouble();
    final (color, track) = _tone(colors, asString(block['tone']));
    final label = asString(block['label']) ?? '';
    final percent = '${(value * 100).round()}%';
    return Semantics(
      label: '$label, $percent',
      excludeSemantics: true,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.baseline,
            textBaseline: TextBaseline.alphabetic,
            children: [
              Expanded(
                child: Text(
                  label,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              const SizedBox(width: 8),
              Text(
                percent,
                style: TextStyle(
                  color: colors.inkSoft,
                  fontWeight: FontWeight.w600,
                  fontFeatures: const [FontFeature.tabularFigures()],
                ),
              ),
            ],
          ),
          const SizedBox(height: 8),
          ClipRRect(
            borderRadius: BorderRadius.circular(999),
            child: SizedBox(
              height: 8,
              child: _Grow(
                value: value,
                builder: (shown) => LinearProgressIndicator(
                  value: shown,
                  minHeight: 8,
                  color: color,
                  backgroundColor: track,
                ),
              ),
            ),
          ),
          if (asString(block['detail']) case final detail?)
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text(
                detail,
                style: TextStyle(color: colors.muted, fontSize: 12.5),
              ),
            ),
        ],
      ),
    );
  }
}

class _Bars extends StatelessWidget {
  const _Bars({required this.items, this.unit});

  final List<Map<String, dynamic>> items;
  final String? unit;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final values = [for (final item in items) _number(item['value']) ?? 0];
    final top = values.fold<double>(
      0,
      (max, value) => value > max ? value : max,
    );
    // Labels sit above their bars when they would not fit beside them.
    final stacked =
        MediaQuery.textScalerOf(context).scale(1) >= 1.5 ||
        items.any((item) => (asString(item['label']) ?? '').length > 10);
    return Column(
      children: [
        for (var index = 0; index < items.length; index++) ...[
          if (index > 0) SizedBox(height: stacked ? 10 : 8),
          _barRow(
            colors,
            asString(items[index]['label']) ?? '',
            values[index],
            top,
            stacked: stacked,
          ),
        ],
      ],
    );
  }

  Widget _barRow(
    JarvisColors colors,
    String label,
    double value,
    double top, {
    required bool stacked,
  }) {
    final shown = [_formatNumber(value), ?unit].join(' ');
    final labelText = Text(
      label,
      maxLines: 1,
      overflow: TextOverflow.ellipsis,
      style: TextStyle(color: colors.inkSoft, fontSize: 13),
    );
    final bar = SizedBox(
      height: 10,
      child: LayoutBuilder(
        builder: (context, constraints) => Align(
          alignment: Alignment.centerLeft,
          child: _Grow(
            value: top == 0 ? 0 : value / top,
            builder: (fraction) => Container(
              // A sliver stays visible for zero so the row still reads.
              width: (constraints.maxWidth * fraction).clamp(
                4.0,
                constraints.maxWidth,
              ),
              decoration: BoxDecoration(
                color: value == top && top > 0
                    ? colors.accent
                    : colors.accent.withValues(alpha: .45),
                borderRadius: BorderRadius.circular(999),
              ),
            ),
          ),
        ),
      ),
    );
    final valueText = Text(
      shown,
      style: TextStyle(
        color: colors.ink,
        fontSize: 13,
        fontWeight: FontWeight.w600,
        fontFeatures: const [FontFeature.tabularFigures()],
      ),
    );
    return Semantics(
      label: '$label, $shown',
      excludeSemantics: true,
      child: stacked
          ? Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(child: labelText),
                    const SizedBox(width: 8),
                    valueText,
                  ],
                ),
                const SizedBox(height: 4),
                bar,
              ],
            )
          : Row(
              children: [
                Expanded(flex: 2, child: labelText),
                const SizedBox(width: 10),
                Expanded(flex: 5, child: bar),
                const SizedBox(width: 10),
                valueText,
              ],
            ),
    );
  }
}

class _Steps extends StatelessWidget {
  const _Steps({required this.items});

  final List<Map<String, dynamic>> items;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Column(
      children: [
        for (var index = 0; index < items.length; index++)
          IntrinsicHeight(
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                SizedBox(
                  width: 22,
                  child: Column(
                    children: [
                      Icon(
                        items[index]['done'] == true
                            ? PhosphorIconsFill.checkCircle
                            : PhosphorIconsRegular.circle,
                        size: 20,
                        color: items[index]['done'] == true
                            ? colors.accent
                            : colors.outlineStrong,
                      ),
                      if (index < items.length - 1)
                        Expanded(
                          child: Container(
                            width: 2,
                            margin: const EdgeInsets.symmetric(vertical: 2),
                            color: colors.outline,
                          ),
                        ),
                    ],
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: Padding(
                    padding: EdgeInsets.only(
                      bottom: index < items.length - 1 ? 12 : 0,
                    ),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          asString(items[index]['title']) ?? '',
                          style: TextStyle(
                            color: items[index]['done'] == true
                                ? colors.inkSoft
                                : colors.ink,
                            fontWeight: FontWeight.w600,
                            decoration: items[index]['done'] == true
                                ? TextDecoration.lineThrough
                                : null,
                            decorationColor: colors.muted,
                          ),
                        ),
                        if (asString(items[index]['detail']) case final detail?)
                          Text(
                            detail,
                            style: TextStyle(
                              color: colors.muted,
                              fontSize: 12.5,
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
      ],
    );
  }
}

class _Pairs extends StatelessWidget {
  const _Pairs({required this.items});

  final List<Map<String, dynamic>> items;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    String text(int index, String key) => asString(items[index][key]) ?? '';
    Widget label(int index) => Text(
      text(index, 'label'),
      style: TextStyle(color: colors.inkSoft, fontSize: 14),
    );
    Widget value(int index, TextAlign align) => Text(
      text(index, 'value'),
      textAlign: align,
      style: TextStyle(
        color: colors.ink,
        fontSize: 14,
        fontWeight: FontWeight.w600,
        fontFeatures: const [FontFeature.tabularFigures()],
      ),
    );
    // One layout for the whole block: side by side when every row is short,
    // otherwise each value goes under its label.
    final stacked =
        MediaQuery.textScalerOf(context).scale(1) >= 1.5 ||
        [
          for (var index = 0; index < items.length; index++)
            text(index, 'label').length + text(index, 'value').length,
        ].any((length) => length > 32);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        for (var index = 0; index < items.length; index++) ...[
          if (index > 0)
            Divider(height: 1, thickness: 1, color: colors.outline),
          Padding(
            padding: const EdgeInsets.symmetric(vertical: 8),
            child: stacked
                ? Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      label(index),
                      const SizedBox(height: 2),
                      value(index, TextAlign.start),
                    ],
                  )
                : Row(
                    crossAxisAlignment: CrossAxisAlignment.baseline,
                    textBaseline: TextBaseline.alphabetic,
                    children: [
                      Expanded(child: label(index)),
                      const SizedBox(width: 12),
                      value(index, TextAlign.end),
                    ],
                  ),
          ),
        ],
      ],
    );
  }
}

class _Callout extends StatelessWidget {
  const _Callout({required this.block});

  final Map<String, dynamic> block;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final tone = asString(block['tone']) ?? 'info';
    final (color, background) = _tone(colors, tone);
    final icon =
        surfaceIcon(asString(block['icon'])) ??
        switch (tone) {
          'warning' || 'danger' => PhosphorIconsRegular.warningCircle,
          'success' => PhosphorIconsRegular.checkCircle,
          _ => PhosphorIconsRegular.info,
        };
    return Container(
      padding: const EdgeInsets.fromLTRB(12, 10, 14, 10),
      decoration: BoxDecoration(
        color: background,
        borderRadius: BorderRadius.circular(14),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Padding(
            padding: const EdgeInsets.only(top: 1),
            child: Icon(icon, size: 18, color: color),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Text(
              asString(block['text']) ?? '',
              style: TextStyle(
                color: tone == 'neutral' ? colors.ink : color,
                fontSize: 14,
                height: 1.4,
                fontWeight: FontWeight.w500,
              ),
            ),
          ),
        ],
      ),
    );
  }
}

class _Columns extends StatelessWidget {
  const _Columns({required this.children});

  final List<Map<String, dynamic>> children;

  @override
  Widget build(BuildContext context) {
    final scale = MediaQuery.textScalerOf(context).scale(1);
    return LayoutBuilder(
      builder: (context, constraints) {
        // Side by side only when each column keeps a readable width.
        final stack =
            scale >= 1.5 ||
            constraints.maxWidth / children.length < 150 ||
            children.length < 2;
        if (stack) return SurfaceBlocks(blocks: children);
        return Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            for (var index = 0; index < children.length; index++) ...[
              if (index > 0) const SizedBox(width: 12),
              Expanded(child: _SurfaceBlock(block: children[index])),
            ],
          ],
        );
      },
    );
  }
}
