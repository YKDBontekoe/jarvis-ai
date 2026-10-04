import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/motion.dart';
import '../home/next_up.dart' show clockTime;
import 'tile_models.dart';

/// A progress ring that eases to its new value when the data changes.
class TileRing extends StatelessWidget {
  const TileRing({
    required this.progress,
    required this.size,
    this.child,
    this.stroke = 6,
    super.key,
  });

  final double progress;
  final double size;
  final double stroke;
  final Widget? child;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return TweenAnimationBuilder<double>(
      tween: Tween(end: progress.clamp(0.0, 1.0)),
      duration: JarvisMotion.of(context, JarvisMotion.slow),
      curve: JarvisMotion.standard,
      builder: (context, value, _) => SizedBox.square(
        dimension: size,
        child: CustomPaint(
          painter: _RingPainter(
            value,
            colors.surfaceMuted,
            colors.accent,
            stroke,
          ),
          child: Center(child: child),
        ),
      ),
    );
  }
}

class _RingPainter extends CustomPainter {
  _RingPainter(this.value, this.track, this.fill, this.stroke);

  final double value;
  final Color track;
  final Color fill;
  final double stroke;

  @override
  void paint(Canvas canvas, Size size) {
    final rect = Offset.zero & size;
    final arc = rect.deflate(stroke / 2);
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = stroke
      ..strokeCap = StrokeCap.round;
    canvas.drawArc(arc, 0, math.pi * 2, false, paint..color = track);
    if (value > 0) {
      canvas.drawArc(
        arc,
        -math.pi / 2,
        math.pi * 2 * value,
        false,
        paint..color = fill,
      );
    }
  }

  @override
  bool shouldRepaint(_RingPainter old) =>
      old.value != value || old.track != track || old.fill != fill;
}

/// Seven small bars. Touch one to read its value; touch it again to let go.
/// The last bar is today and is drawn in the accent colour.
class TileBarChart extends StatefulWidget {
  const TileBarChart({required this.bars, this.idleCaption, super.key});

  final List<TileBar> bars;

  /// Shown above the bars until one is touched.
  final String? idleCaption;

  @override
  State<TileBarChart> createState() => _TileBarChartState();
}

class _TileBarChartState extends State<TileBarChart> {
  int? _selected;

  @override
  void didUpdateWidget(TileBarChart old) {
    super.didUpdateWidget(old);
    if (_selected != null && _selected! >= widget.bars.length) {
      _selected = null;
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final bars = widget.bars;
    final peak = bars.fold<double>(0, (m, bar) => math.max(m, bar.value));
    final caption = _selected == null
        ? widget.idleCaption
        : bars[_selected!].text;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: 15,
          child: Align(
            alignment: Alignment.centerLeft,
            child: Text(
              caption ?? '',
              key: const Key('bars-caption'),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 12,
                fontWeight: _selected == null
                    ? FontWeight.w400
                    : FontWeight.w600,
                color: _selected == null ? colors.inkSoft : colors.ink,
              ),
            ),
          ),
        ),
        Expanded(
          child: LayoutBuilder(
            builder: (context, box) {
              const labelHeight = 14.0;
              final room = math.max(0.0, box.maxHeight - labelHeight - 2);
              return Row(
                crossAxisAlignment: CrossAxisAlignment.end,
                children: [
                  for (var i = 0; i < bars.length; i++)
                    Expanded(
                      child: Semantics(
                        button: true,
                        label: bars[i].text,
                        excludeSemantics: true,
                        onTap: () => setState(
                          () => _selected = _selected == i ? null : i,
                        ),
                        child: GestureDetector(
                          behavior: HitTestBehavior.opaque,
                          onTap: () => setState(
                            () => _selected = _selected == i ? null : i,
                          ),
                          child: Column(
                            mainAxisAlignment: MainAxisAlignment.end,
                            children: [
                              AnimatedContainer(
                                key: Key('bar-$i'),
                                duration: JarvisMotion.of(
                                  context,
                                  JarvisMotion.base,
                                ),
                                curve: JarvisMotion.standard,
                                margin: const EdgeInsets.symmetric(
                                  horizontal: 3,
                                ),
                                height: peak <= 0
                                    ? 2
                                    : math.max(
                                        2.0,
                                        room * (bars[i].value / peak),
                                      ),
                                decoration: BoxDecoration(
                                  color: _selected == i
                                      ? colors.ink
                                      : i == bars.length - 1
                                      ? colors.accent
                                      : colors.surfaceRaised,
                                  borderRadius: BorderRadius.circular(3),
                                ),
                              ),
                              SizedBox(
                                height: labelHeight + 2,
                                child: Center(
                                  child: Text(
                                    bars[i].label,
                                    style: TextStyle(
                                      fontSize: 9.5,
                                      fontWeight: i == bars.length - 1
                                          ? FontWeight.w600
                                          : FontWeight.w400,
                                      color: i == bars.length - 1
                                          ? colors.ink
                                          : colors.muted,
                                    ),
                                  ),
                                ),
                              ),
                            ],
                          ),
                        ),
                      ),
                    ),
                ],
              );
            },
          ),
        ),
      ],
    );
  }
}

/// The day as a strip: what is on it and where the clock is. Touch an item to
/// read what it is.
class TileTimelineStrip extends StatefulWidget {
  const TileTimelineStrip({required this.timeline, super.key});

  final TileTimeline timeline;

  @override
  State<TileTimelineStrip> createState() => _TileTimelineStripState();
}

class _TileTimelineStripState extends State<TileTimelineStrip> {
  int? _selected;

  String _time(int minute) =>
      clockTime(DateTime(2000, 1, 1, minute ~/ 60, minute % 60));

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final line = widget.timeline;
    final length = math.max(1, line.endMinute - line.startMinute);
    final selected = _selected != null && _selected! < line.spans.length
        ? line.spans[_selected!]
        : null;
    final upcoming = [
      for (final span in line.spans)
        if (span.endMinute >= line.nowMinute) span,
    ];
    final next = upcoming.isEmpty ? null : upcoming.first;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          height: 15,
          child: Align(
            alignment: Alignment.centerLeft,
            child: Text(
              selected == null
                  ? (line.spans.isEmpty ? 'Nothing planned' : '')
                  : '${_time(selected.startMinute)}  ${selected.label}',
              key: const Key('timeline-caption'),
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                fontSize: 12,
                fontWeight: FontWeight.w600,
                color: selected == null ? colors.muted : colors.ink,
              ),
            ),
          ),
        ),
        SizedBox(
          height: 22,
          child: LayoutBuilder(
            builder: (context, box) {
              double x(int minute) =>
                  box.maxWidth *
                  ((minute - line.startMinute) / length).clamp(0.0, 1.0);
              final nowX = x(line.nowMinute);
              return Stack(
                clipBehavior: Clip.none,
                children: [
                  Positioned.fill(
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        color: colors.surfaceMuted,
                        borderRadius: BorderRadius.circular(8),
                      ),
                    ),
                  ),
                  Positioned(
                    left: 0,
                    top: 0,
                    bottom: 0,
                    width: nowX,
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        color: colors.surfaceRaised.withValues(alpha: .6),
                        borderRadius: const BorderRadius.horizontal(
                          left: Radius.circular(8),
                        ),
                      ),
                    ),
                  ),
                  for (var i = 0; i < line.spans.length; i++)
                    Builder(
                      builder: (context) {
                        final span = line.spans[i];
                        final left = x(span.startMinute);
                        final width = math.max(
                          span.reminder ? 8.0 : 10.0,
                          x(span.endMinute) - left,
                        );
                        final isNext = identical(span, next);
                        final past = span.endMinute < line.nowMinute;
                        return Positioned(
                          left: math.min(left, box.maxWidth - width),
                          top: 4,
                          bottom: 4,
                          width: width,
                          child: Semantics(
                            button: true,
                            label: '${_time(span.startMinute)} ${span.label}',
                            excludeSemantics: true,
                            onTap: () => setState(
                              () => _selected = _selected == i ? null : i,
                            ),
                            child: GestureDetector(
                              key: Key('span-$i'),
                              behavior: HitTestBehavior.opaque,
                              onTap: () => setState(
                                () => _selected = _selected == i ? null : i,
                              ),
                              child: DecoratedBox(
                                decoration: BoxDecoration(
                                  color: _selected == i
                                      ? colors.ink
                                      : past
                                      ? colors.outlineStrong
                                      : isNext
                                      ? colors.accent
                                      : colors.accent.withValues(alpha: .45),
                                  borderRadius: BorderRadius.circular(
                                    span.reminder ? 4 : 6,
                                  ),
                                ),
                              ),
                            ),
                          ),
                        );
                      },
                    ),
                  Positioned(
                    left: nowX - 1,
                    top: 0,
                    bottom: -3,
                    width: 2,
                    child: DecoratedBox(
                      key: const Key('timeline-now'),
                      decoration: BoxDecoration(
                        color: colors.ink,
                        borderRadius: BorderRadius.circular(1),
                      ),
                    ),
                  ),
                ],
              );
            },
          ),
        ),
        const SizedBox(height: 3),
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              _time(line.startMinute),
              style: TextStyle(fontSize: 10, color: colors.muted),
            ),
            Text(
              _time(line.endMinute % 1440),
              style: TextStyle(fontSize: 10, color: colors.muted),
            ),
          ],
        ),
      ],
    );
  }
}

/// Five bars that move while Jarvis is listening. Still when motion is reduced.
class TileWaveform extends StatefulWidget {
  const TileWaveform({required this.color, this.height = 24, super.key});

  final Color color;
  final double height;

  @override
  State<TileWaveform> createState() => _TileWaveformState();
}

class _TileWaveformState extends State<TileWaveform>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 1100),
  );
  bool? _animating;

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    final animate = !JarvisMotion.reduced(context);
    if (animate == _animating) return;
    _animating = animate;
    if (animate) {
      _controller.repeat();
    } else {
      _controller.stop();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => SizedBox(
    key: const Key('tile-waveform'),
    height: widget.height,
    width: widget.height * 1.1,
    child: AnimatedBuilder(
      animation: _controller,
      builder: (context, _) => Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        crossAxisAlignment: CrossAxisAlignment.center,
        children: [
          for (var i = 0; i < 5; i++)
            Container(
              width: widget.height * .12,
              height:
                  widget.height *
                  (.3 +
                      .7 *
                          (.5 +
                              .5 *
                                  math.sin(
                                    _controller.value * math.pi * 2 + i * 1.3,
                                  ))),
              decoration: BoxDecoration(
                color: widget.color,
                borderRadius: BorderRadius.circular(widget.height),
              ),
            ),
        ],
      ),
    ),
  );
}

/// Grey placeholder shapes while a tile's first data is on its way.
class TileSkeleton extends StatelessWidget {
  const TileSkeleton({required this.size, super.key});

  final TileSize size;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget bar(double width, double height) => Container(
      width: width,
      height: height,
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(height / 2),
      ),
    );
    if (size == TileSize.icon) {
      return Center(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          children: [bar(24, 24), const SizedBox(height: 8), bar(34, 8)],
        ),
      );
    }
    if (size == TileSize.strip) {
      return Padding(
        padding: const EdgeInsets.symmetric(horizontal: 14),
        child: Row(
          children: [
            bar(22, 22),
            const SizedBox(width: 10),
            Expanded(
              child: Column(
                mainAxisAlignment: MainAxisAlignment.center,
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [bar(70, 10), const SizedBox(height: 7), bar(100, 8)],
              ),
            ),
          ],
        ),
      );
    }
    final lines = size == TileSize.square ? 1 : (size == TileSize.wide ? 3 : 6);
    return Padding(
      padding: const EdgeInsets.all(14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          bar(56, 10),
          const SizedBox(height: 16),
          if (size == TileSize.square) const Spacer(),
          for (var i = 0; i < lines; i++) ...[
            bar(i.isEven ? 140 : 100, size == TileSize.square ? 24 : 10),
            const SizedBox(height: 14),
          ],
        ],
      ),
    );
  }
}
