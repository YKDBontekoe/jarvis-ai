import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart';
import '../tiles/tile_controller.dart';
import '../tiles/tile_grid.dart';
import '../tiles/tile_models.dart';
import '../tiles/tile_registry.dart';

/// Compact feature launchers, grouped by category and what is pinned to Home.
/// The existing preview sheet keeps sizing and pinning in one place.
class EverythingScreen extends StatefulWidget {
  const EverythingScreen({
    required this.source,
    required this.layout,
    required this.onOpen,
    super.key,
  });

  final TileDataSource source;
  final TileLayoutController layout;

  /// Opens a destination, as on Home.
  final ValueChanged<String> onOpen;

  @override
  State<EverythingScreen> createState() => _EverythingScreenState();
}

class _EverythingScreenState extends State<EverythingScreen> {
  final _query = TextEditingController();

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  List<TileSpec> _matches(TileCategory category) {
    final needle = _query.text.trim().toLowerCase();
    return [
      for (final spec in tileSpecs)
        if (spec.category == category &&
            (needle.isEmpty ||
                spec.name.toLowerCase().contains(needle) ||
                spec.description.toLowerCase().contains(needle) ||
                spec.category.label.toLowerCase().contains(needle)))
          spec,
    ];
  }

  void _preview(TileSpec spec) {
    FocusManager.instance.primaryFocus?.unfocus();
    unawaited(HapticFeedback.selectionClick());
    unawaited(
      showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        sheetAnimationStyle: AnimationStyle(
          duration: JarvisMotion.of(context, JarvisMotion.slow),
          reverseDuration: JarvisMotion.of(context, JarvisMotion.base),
        ),
        builder: (sheet) => _PreviewSheet(
          spec: spec,
          source: widget.source,
          layout: widget.layout,
          onOpen: (destination) {
            Navigator.of(sheet).pop();
            widget.onOpen(destination);
          },
          onDone: (message) {
            Navigator.of(sheet).pop();
            ScaffoldMessenger.maybeOf(context)
              ?..hideCurrentSnackBar()
              ..showSnackBar(SnackBar(content: Text(message)));
          },
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return ListView(
      key: const Key('everything-list'),
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 28),
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      children: [
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 640),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                const SizedBox(height: 8),
                Text(
                  'Everything',
                  style: JarvisType.displayOf(context).copyWith(fontSize: 32),
                ),
                const SizedBox(height: 8),
                Text(
                  'Explore features and choose what goes on Home.',
                  style: TextStyle(
                    fontSize: 14,
                    height: 1.4,
                    color: colors.inkSoft,
                  ),
                ),
                const SizedBox(height: 20),
                TextField(
                  key: const Key('everything-search'),
                  controller: _query,
                  onChanged: (_) => setState(() {}),
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Find a feature',
                    hintStyle: TextStyle(color: colors.inkSoft),
                    prefixIcon: Icon(
                      PhosphorIconsRegular.magnifyingGlass,
                      size: 18,
                      color: colors.muted,
                    ),
                    suffixIcon: _query.text.isEmpty
                        ? null
                        : IconButton(
                            key: const Key('everything-clear-search'),
                            tooltip: 'Clear feature search',
                            onPressed: () => setState(_query.clear),
                            icon: Icon(
                              PhosphorIconsRegular.x,
                              size: 16,
                              color: colors.inkSoft,
                            ),
                          ),
                    filled: true,
                    fillColor: colors.surfaceMuted,
                    contentPadding: const EdgeInsets.symmetric(vertical: 12),
                    border: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: BorderSide.none,
                    ),
                    enabledBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: BorderSide.none,
                    ),
                    focusedBorder: OutlineInputBorder(
                      borderRadius: BorderRadius.circular(12),
                      borderSide: BorderSide.none,
                    ),
                  ),
                ),
                ListenableBuilder(
                  listenable: widget.layout,
                  builder: (context, _) {
                    final favorites = [
                      for (final item in widget.layout.layout)
                        if (tileSpecFor(item.id) case final spec?)
                          if (_matches(spec.category).contains(spec)) spec,
                    ];
                    final sections = [
                      for (final category in TileCategory.values)
                        if (_matches(category) case final specs
                            when specs.isNotEmpty)
                          (category, specs),
                    ];
                    if (sections.isEmpty && favorites.isEmpty) {
                      return Padding(
                        padding: const EdgeInsets.symmetric(vertical: 48),
                        child: Center(
                          child: Text(
                            'Nothing matches “${_query.text.trim()}”',
                            style: TextStyle(color: colors.muted),
                          ),
                        ),
                      );
                    }
                    return Column(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        if (favorites.isNotEmpty &&
                            _query.text.trim().isEmpty) ...[
                          Padding(
                            padding: const EdgeInsets.fromLTRB(2, 24, 2, 8),
                            child: Text(
                              'On Home',
                              style: JarvisType.sectionOf(context),
                            ),
                          ),
                          SingleChildScrollView(
                            scrollDirection: Axis.horizontal,
                            child: IntrinsicHeight(
                              child: Row(
                                crossAxisAlignment: CrossAxisAlignment.stretch,
                                children: [
                                  for (final spec in favorites)
                                    _FavoriteLauncher(
                                      spec: spec,
                                      onTap: () => _preview(spec),
                                    ),
                                ],
                              ),
                            ),
                          ),
                        ],
                        for (final (category, specs) in sections) ...[
                          Padding(
                            padding: const EdgeInsets.fromLTRB(2, 26, 2, 12),
                            child: Text(
                              category.label,
                              style: JarvisType.displayOf(
                                context,
                              ).copyWith(fontSize: 20, letterSpacing: -.4),
                            ),
                          ),
                          SurfaceCard(
                            radius: JarvisRadii.lg,
                            padding: EdgeInsets.zero,
                            color: colors.surface,
                            borderColor: colors.outline.withValues(
                              alpha: colors.isDark ? .5 : .25,
                            ),
                            child: _FeatureList(
                              specs: specs,
                              pinned: widget.layout.contains,
                              onTap: _preview,
                            ),
                          ),
                        ],
                      ],
                    );
                  },
                ),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// A familiar shortcut strip; the feature directory below explains each tool.
class _FavoriteLauncher extends StatelessWidget {
  const _FavoriteLauncher({required this.spec, required this.onTap});

  final TileSpec spec;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final width = MediaQuery.textScalerOf(context).scale(90).clamp(90.0, 180.0);
    return Semantics(
      button: true,
      label: '${spec.name}, on Home. ${spec.description}',
      onTap: onTap,
      excludeSemantics: true,
      child: SizedBox(
        width: width,
        child: Material(
          type: MaterialType.transparency,
          child: PressFeedback(
            scale: .96,
            builder: (context, highlight) => InkWell(
              key: Key('favorite-${spec.id}'),
              borderRadius: BorderRadius.circular(JarvisRadii.md),
              onTap: onTap,
              onHighlightChanged: highlight,
              child: Padding(
                padding: const EdgeInsets.fromLTRB(4, 8, 8, 8),
                child: Column(
                  children: [
                    TileIconChip(spec: spec, size: 46),
                    const SizedBox(height: 8),
                    Text(
                      spec.name,
                      textAlign: TextAlign.center,
                      style: TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.w500,
                        color: colors.ink,
                        height: 1.25,
                      ),
                    ),
                  ],
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Grouped iOS-style rows leave room for the purpose of every feature.
class _FeatureList extends StatelessWidget {
  const _FeatureList({
    required this.specs,
    required this.pinned,
    required this.onTap,
  });

  final List<TileSpec> specs;
  final bool Function(String id) pinned;
  final ValueChanged<TileSpec> onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Column(
      children: [
        for (var index = 0; index < specs.length; index++) ...[
          if (index > 0)
            Divider(
              height: .5,
              thickness: .5,
              indent: 64,
              endIndent: 16,
              color: colors.outlineStrong.withValues(alpha: .45),
            ),
          _row(context, specs[index], colors),
        ],
      ],
    );
  }

  Widget _row(BuildContext context, TileSpec spec, JarvisColors colors) {
    final isPinned = pinned(spec.id);
    return Semantics(
      button: true,
      label: '${spec.name}${isPinned ? ', on Home' : ''}. ${spec.description}',
      excludeSemantics: true,
      child: PressFeedback(
        scale: .99,
        builder: (context, highlight) => InkWell(
          key: Key('everything-${spec.id}'),
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          onTap: () => onTap(spec),
          onHighlightChanged: highlight,
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 14),
            child: Row(
              children: [
                TileIconChip(spec: spec, size: 36),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        spec.name,
                        style: TextStyle(
                          fontSize: 16,
                          fontWeight: FontWeight.w500,
                          height: 1.25,
                          color: colors.ink,
                        ),
                      ),
                      const SizedBox(height: 4),
                      Text(
                        spec.description,
                        style: TextStyle(
                          fontSize: 13,
                          height: 1.35,
                          color: colors.inkSoft,
                        ),
                      ),
                    ],
                  ),
                ),
                const SizedBox(width: 12),
                if (isPinned) ...[
                  Icon(
                    PhosphorIconsRegular.pushPin,
                    size: 12,
                    color: colors.inkSoft,
                  ),
                  const SizedBox(width: 8),
                ],
                Icon(
                  PhosphorIconsRegular.caretRight,
                  size: 12,
                  color: colors.inkSoft,
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _PreviewSheet extends StatefulWidget {
  const _PreviewSheet({
    required this.spec,
    required this.source,
    required this.layout,
    required this.onOpen,
    required this.onDone,
  });

  final TileSpec spec;
  final TileDataSource source;
  final TileLayoutController layout;
  final ValueChanged<String> onOpen;
  final ValueChanged<String> onDone;

  @override
  State<_PreviewSheet> createState() => _PreviewSheetState();
}

class _PreviewSheetState extends State<_PreviewSheet> {
  late TileSize _size =
      widget.layout.sizeOf(widget.spec.id) ??
      (widget.spec.supports(TileSize.square)
          ? TileSize.square
          : widget.spec.sizes.last);
  TileData? _data;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final data = await widget.source.load(widget.spec);
    if (mounted) setState(() => _data = data);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final spec = widget.spec;
    final pinnedSize = widget.layout.sizeOf(spec.id);
    final primary = pinnedSize == null
        ? 'Add to Home'
        : pinnedSize == _size
        ? 'Remove from Home'
        : 'Update size';
    return SafeArea(
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(16, 0, 16, 16),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                Icon(spec.icon, size: 24, color: colors.inkSoft),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        spec.name,
                        style: JarvisType.displayOf(
                          context,
                        ).copyWith(fontSize: 22),
                      ),
                      Text(
                        spec.description,
                        style: TextStyle(
                          fontSize: 14,
                          height: 1.4,
                          color: colors.inkSoft,
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
            const SizedBox(height: 16),
            DecoratedBox(
              decoration: BoxDecoration(
                color: colors.surfaceMuted,
                borderRadius: BorderRadius.circular(22),
              ),
              child: Padding(
                padding: const EdgeInsets.all(12),
                child: LayoutBuilder(
                  builder: (context, box) {
                    const gap = TileGrid.gap;
                    final cell =
                        (box.maxWidth - gap * (TileGrid.columns - 1)) /
                        TileGrid.columns;
                    return Align(
                      alignment: Alignment.topLeft,
                      child: MotionSize(
                        child: SizedBox(
                          key: const Key('tile-preview'),
                          width:
                              _size.columns * cell + (_size.columns - 1) * gap,
                          height: _size.rows * cell + (_size.rows - 1) * gap,
                          child: TileCard(spec: spec, size: _size, data: _data),
                        ),
                      ),
                    );
                  },
                ),
              ),
            ),
            const SizedBox(height: 14),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final size in spec.sizes)
                  ChoiceChip(
                    key: Key('size-${size.name}'),
                    label: Text(size.label),
                    selected: size == _size,
                    onSelected: (_) {
                      if (_size == size) return;
                      unawaited(HapticFeedback.selectionClick());
                      setState(() => _size = size);
                    },
                    showCheckmark: false,
                  ),
              ],
            ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('tile-pin'),
              onPressed: () {
                unawaited(HapticFeedback.lightImpact());
                if (pinnedSize == _size) {
                  widget.layout.unpin(spec.id);
                  widget.onDone('${spec.name} removed from Home');
                } else {
                  widget.layout.pin(spec.id, _size);
                  widget.onDone(
                    pinnedSize == null
                        ? '${spec.name} added to Home'
                        : '${spec.name} size updated',
                  );
                }
              },
              style: FilledButton.styleFrom(
                minimumSize: const Size.fromHeight(50),
                backgroundColor: pinnedSize == _size
                    ? colors.surfaceMuted
                    : colors.ink,
                foregroundColor: pinnedSize == _size
                    ? colors.ink
                    : colors.onInk,
                shape: RoundedRectangleBorder(
                  borderRadius: BorderRadius.circular(14),
                ),
              ),
              child: Text(primary),
            ),
            const SizedBox(height: 8),
            TextButton(
              key: const Key('tile-open'),
              onPressed: () => widget.onOpen(spec.destination),
              style: TextButton.styleFrom(
                minimumSize: const Size.fromHeight(46),
                foregroundColor: colors.inkSoft,
              ),
              child: Text('Open ${spec.name}'),
            ),
          ],
        ),
      ),
    );
  }
}
