import 'package:flutter/semantics.dart' show CustomSemanticsAction;

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'tile_card.dart';
import 'tile_layout.dart';
import 'tile_models.dart';
import 'tile_registry.dart';

/// The home grid: tiles packed four columns wide. In edit mode tiles can be
/// dragged to reorder, resized with the corner button and removed.
class TileGrid extends StatelessWidget {
  const TileGrid({
    required this.layout,
    required this.data,
    required this.onOpen,
    this.onRowTap,
    this.editing = false,
    this.onRemove,
    this.onResize,
    this.onReorder,
    this.onEdit,
    this.now,
    this.isLoading,
    this.pending = const {},
    this.onAction,
    this.onMenu,
    super.key,
  });

  static const columns = 4;
  static const gap = 10.0;

  final List<TilePlacement> layout;
  final Map<String, TileData?> data;
  final ValueChanged<TileSpec> onOpen;
  final void Function(TileSpec spec, TileRow row)? onRowTap;
  final bool editing;
  final ValueChanged<TilePlacement>? onRemove;
  final ValueChanged<TilePlacement>? onResize;
  final void Function(int from, int to)? onReorder;

  /// Long-press on a tile outside edit mode, when there is no [onMenu].
  final VoidCallback? onEdit;

  /// The clock live countdowns on tiles read; the wall clock when null.
  final DateTime? now;

  /// Whether a tile's first data is still on its way.
  final bool Function(String id)? isLoading;

  /// Ids of items an action is running on.
  final Set<String> pending;

  /// A quick action on a tile was pressed; [itemId] is null for the tile itself.
  final void Function(TileSpec spec, TileAction action, String? itemId)?
  onAction;

  /// Long-press on a tile outside edit mode: show its menu at the touch point.
  final void Function(TileSpec spec, Offset position)? onMenu;

  @override
  Widget build(BuildContext context) {
    final placed = packTiles(layout, columns: columns);
    final rows = gridRows(placed);
    return LayoutBuilder(
      builder: (context, box) {
        final cell = (box.maxWidth - gap * (columns - 1)) / columns;
        final height = rows == 0 ? 0.0 : rows * cell + (rows - 1) * gap;
        final duration = JarvisMotion.of(context, JarvisMotion.base);
        return SizedBox(
          height: height,
          child: Stack(
            clipBehavior: Clip.none,
            children: [
              for (final spot in placed)
                _Slide(
                  key: ValueKey(layout[spot.index].id),
                  duration: duration,
                  left: spot.column * (cell + gap),
                  top: spot.row * (cell + gap),
                  width: spot.columns * cell + (spot.columns - 1) * gap,
                  height: spot.rows * cell + (spot.rows - 1) * gap,
                  child: _tile(context, layout[spot.index], spot),
                ),
            ],
          ),
        );
      },
    );
  }

  Widget _tile(BuildContext context, TilePlacement placement, PlacedTile spot) {
    final spec = tileSpecFor(placement.id);
    if (spec == null) return const SizedBox.shrink();
    final card = TileCard(
      spec: spec,
      size: placement.size,
      data: data[spec.id],
      onRowTap: editing || onRowTap == null
          ? null
          : (row) => onRowTap!(spec, row),
      onAction: editing || onAction == null
          ? null
          : (action, itemId) => onAction!(spec, action, itemId),
      now: now,
      loading: isLoading?.call(spec.id) ?? false,
      pending: pending,
    );
    if (!editing) {
      return Semantics(
        button: true,
        label: spec.name,
        child: _Pressable(
          onTap: () => onOpen(spec),
          onLongPress: onMenu != null
              ? (position) => onMenu!(spec, position)
              : onEdit == null
              ? null
              : (_) => onEdit!(),
          child: card,
        ),
      );
    }
    return _EditableTile(
      key: ValueKey('edit-${spec.id}'),
      spec: spec,
      placement: placement,
      index: spot.index,
      count: layout.length,
      card: card,
      onRemove: onRemove,
      onResize: onResize,
      onReorder: onReorder,
    );
  }
}

/// Places a tile in the grid and slides it when its position changes. The
/// size changes at once: a tile's content cannot be laid out in a box that is
/// still growing, so it would overflow on the way.
class _Slide extends StatelessWidget {
  const _Slide({
    required this.duration,
    required this.left,
    required this.top,
    required this.width,
    required this.height,
    required this.child,
    super.key,
  });

  final Duration duration;
  final double left;
  final double top;
  final double width;
  final double height;
  final Widget child;

  @override
  Widget build(BuildContext context) => TweenAnimationBuilder<Offset>(
    tween: Tween(end: Offset(left, top)),
    duration: duration,
    curve: JarvisMotion.standard,
    builder: (context, offset, child) => Positioned(
      left: offset.dx,
      top: offset.dy,
      width: width,
      height: height,
      child: child!,
    ),
    child: child,
  );
}

/// Eases down a hair while pressed, like the app's other tappable surfaces.
class _Pressable extends StatefulWidget {
  const _Pressable({
    required this.onTap,
    required this.child,
    this.onLongPress,
  });

  final VoidCallback onTap;
  final ValueChanged<Offset>? onLongPress;
  final Widget child;

  @override
  State<_Pressable> createState() => _PressableState();
}

class _PressableState extends State<_Pressable> {
  var _down = false;

  @override
  Widget build(BuildContext context) => AnimatedScale(
    scale: _down ? JarvisMotion.startScale : 1,
    duration: JarvisMotion.of(context, JarvisMotion.fast),
    curve: JarvisMotion.standard,
    child: GestureDetector(
      onLongPressStart: widget.onLongPress == null
          ? null
          : (details) => widget.onLongPress!(details.globalPosition),
      child: Material(
        type: MaterialType.transparency,
        child: InkWell(
          borderRadius: BorderRadius.circular(20),
          onTap: widget.onTap,
          onHighlightChanged: (value) => setState(() => _down = value),
          child: widget.child,
        ),
      ),
    ),
  );
}

class _EditableTile extends StatelessWidget {
  const _EditableTile({
    required this.spec,
    required this.placement,
    required this.index,
    required this.count,
    required this.card,
    required this.onRemove,
    required this.onResize,
    required this.onReorder,
    super.key,
  });

  final TileSpec spec;
  final TilePlacement placement;
  final int index;
  final int count;
  final Widget card;
  final ValueChanged<TilePlacement>? onRemove;
  final ValueChanged<TilePlacement>? onResize;
  final void Function(int from, int to)? onReorder;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    Widget control(Key key, IconData icon, String label, VoidCallback? onTap) =>
        Tooltip(
          message: label,
          child: Material(
            color: colors.ink,
            shape: const CircleBorder(),
            child: InkWell(
              key: key,
              customBorder: const CircleBorder(),
              onTap: onTap,
              child: SizedBox.square(
                dimension: 26,
                child: Icon(icon, size: 14, color: colors.onInk),
              ),
            ),
          ),
        );

    final body = Stack(
      clipBehavior: Clip.none,
      children: [
        Positioned.fill(
          child: IgnorePointer(
            child: DecoratedBox(
              position: DecorationPosition.foreground,
              decoration: BoxDecoration(
                borderRadius: BorderRadius.circular(20),
                border: Border.all(color: colors.outlineStrong),
              ),
              child: card,
            ),
          ),
        ),
        Positioned(
          top: -7,
          left: -7,
          child: control(
            Key('tile-remove-${spec.id}'),
            PhosphorIconsRegular.x,
            'Remove ${spec.name}',
            onRemove == null ? null : () => onRemove!(placement),
          ),
        ),
        if (spec.sizes.length > 1)
          Positioned(
            bottom: -7,
            right: -7,
            child: control(
              Key('tile-resize-${spec.id}'),
              PhosphorIconsRegular.arrowUpRight,
              'Change size of ${spec.name}',
              onResize == null ? null : () => onResize!(placement),
            ),
          ),
      ],
    );

    final target = DragTarget<int>(
      onWillAcceptWithDetails: (details) {
        if (details.data != index) onReorder?.call(details.data, index);
        return false;
      },
      builder: (context, _, _) => LongPressDraggable<int>(
        data: index,
        delay: const Duration(milliseconds: 150),
        hitTestBehavior: HitTestBehavior.opaque,
        hapticFeedbackOnStart: true,
        dragAnchorStrategy: pointerDragAnchorStrategy,
        feedback: Material(
          type: MaterialType.transparency,
          child: Opacity(
            opacity: .92,
            child: SizedBox(
              width: 150,
              height: 110,
              child: DecoratedBox(
                decoration: BoxDecoration(
                  borderRadius: BorderRadius.circular(20),
                  boxShadow: JarvisShadows.floating(colors.brightness),
                ),
                child: TileCard(spec: spec, size: TileSize.strip, data: null),
              ),
            ),
          ),
        ),
        childWhenDragging: Opacity(opacity: .35, child: body),
        child: body,
      ),
    );

    return Semantics(
      label: '${spec.name}, ${placement.size.label}',
      customSemanticsActions: {
        const CustomSemanticsAction(label: 'Remove'): () =>
            onRemove?.call(placement),
        if (spec.sizes.length > 1)
          const CustomSemanticsAction(label: 'Change size'): () =>
              onResize?.call(placement),
        if (index > 0)
          const CustomSemanticsAction(label: 'Move earlier'): () =>
              onReorder?.call(index, index - 1),
        if (index < count - 1)
          const CustomSemanticsAction(label: 'Move later'): () =>
              onReorder?.call(index, index + 1),
      },
      child: target,
    );
  }
}
