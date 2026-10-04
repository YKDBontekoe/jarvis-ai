import 'dart:async';

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../tiles/tile_card.dart';
import '../tiles/tile_controller.dart';
import '../tiles/tile_grid.dart';
import '../tiles/tile_models.dart';
import '../tiles/tile_registry.dart';

/// Every feature as a tile, by category. Tapping one previews it at each size
/// and pins it to Home.
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
                spec.category.label.toLowerCase().contains(needle)))
          spec,
    ];
  }

  void _preview(TileSpec spec) {
    unawaited(
      showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
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
                  style: JarvisType.displayOf(context).copyWith(fontSize: 28),
                ),
                const SizedBox(height: 14),
                TextField(
                  key: const Key('everything-search'),
                  controller: _query,
                  onChanged: (_) => setState(() {}),
                  textInputAction: TextInputAction.search,
                  decoration: InputDecoration(
                    hintText: 'Search',
                    prefixIcon: Icon(
                      PhosphorIconsRegular.magnifyingGlass,
                      size: 18,
                      color: colors.muted,
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
                    final sections = [
                      for (final category in TileCategory.values)
                        if (_matches(category) case final specs
                            when specs.isNotEmpty)
                          (category, specs),
                    ];
                    if (sections.isEmpty) {
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
                        for (final (category, specs) in sections) ...[
                          Padding(
                            padding: const EdgeInsets.fromLTRB(2, 22, 2, 10),
                            child: Text(
                              category.label,
                              style: TextStyle(
                                fontSize: 13,
                                fontWeight: FontWeight.w500,
                                color: colors.muted,
                              ),
                            ),
                          ),
                          _IconGrid(
                            specs: specs,
                            pinned: widget.layout.contains,
                            onTap: _preview,
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

class _IconGrid extends StatelessWidget {
  const _IconGrid({
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
    return LayoutBuilder(
      builder: (context, box) {
        const columns = TileGrid.columns;
        const gap = TileGrid.gap;
        final cell = (box.maxWidth - gap * (columns - 1)) / columns;
        return Wrap(
          spacing: gap,
          runSpacing: gap,
          children: [
            for (final spec in specs)
              SizedBox.square(
                dimension: cell,
                child: Semantics(
                  button: true,
                  label: pinned(spec.id) ? '${spec.name}, on Home' : spec.name,
                  child: InkWell(
                    key: Key('everything-${spec.id}'),
                    borderRadius: BorderRadius.circular(20),
                    onTap: () => onTap(spec),
                    child: Stack(
                      children: [
                        Positioned.fill(
                          child: ExcludeSemantics(
                            child: IgnorePointer(
                              child: TileCard(
                                spec: spec,
                                size: TileSize.icon,
                                data: null,
                              ),
                            ),
                          ),
                        ),
                        if (pinned(spec.id))
                          Positioned(
                            top: 7,
                            right: 7,
                            child: Icon(
                              PhosphorIconsRegular.pushPin,
                              size: 12,
                              color: colors.accent,
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
                        spec.category.label,
                        style: TextStyle(fontSize: 13, color: colors.muted),
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
                      child: AnimatedSize(
                        duration: JarvisMotion.of(context, JarvisMotion.base),
                        curve: JarvisMotion.standard,
                        alignment: Alignment.topLeft,
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
                    onSelected: (_) => setState(() => _size = size),
                    showCheckmark: false,
                  ),
              ],
            ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('tile-pin'),
              onPressed: () {
                if (pinnedSize == _size) {
                  widget.layout.unpin(spec.id);
                  widget.onDone('${spec.name} removed from Home');
                } else {
                  widget.layout.pin(spec.id, _size);
                  widget.onDone('${spec.name} added to Home');
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
