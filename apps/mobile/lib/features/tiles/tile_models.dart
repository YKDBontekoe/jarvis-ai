import 'package:dio/dio.dart';
import 'package:flutter/widgets.dart';

/// Sizes a tile can take on the four-column home grid.
enum TileSize {
  icon(1, 1, 'Icon'),
  strip(2, 1, 'Strip'),
  square(2, 2, 'Square'),
  wide(4, 2, 'Wide'),
  large(4, 4, 'Large');

  const TileSize(this.columns, this.rows, this.label);

  final int columns;
  final int rows;
  final String label;

  /// Stable name used when the layout is saved.
  String get code => name;

  static TileSize? parse(Object? value) {
    for (final size in values) {
      if (size.name == value) return size;
    }
    return null;
  }
}

/// How the Everything screen groups features.
enum TileCategory {
  plan('Plan'),
  talk('Talk'),
  know('Know'),
  money('Money'),
  automate('Automate'),
  system('System');

  const TileCategory(this.label);

  final String label;
}

/// One line inside a wide or large tile.
@immutable
class TileRow {
  const TileRow(this.text, {this.meta, this.attention = false, this.target});

  final String text;

  /// Right-aligned detail, such as a time or a status.
  final String? meta;

  /// Marks the row as needing the person, which draws it in the accent colour.
  final bool attention;

  /// Opens something specific instead of the tile's own page, such as
  /// `chat:<id>`. Null opens the tile's page.
  final String? target;
}

/// What a tile shows right now. Every field is optional so a tile degrades to
/// its name and subtitle when a server has nothing to report.
@immutable
class TileData {
  const TileData({
    this.stat,
    this.unit,
    this.subtitle,
    this.rows = const [],
    this.attention = false,
    this.progress,
  });

  /// The big number or word, such as `4`, `€412` or `2/3`.
  final String? stat;
  final String? unit;
  final String? subtitle;
  final List<TileRow> rows;

  /// Something here needs the person; shown as a dot on small tiles.
  final bool attention;

  /// 0 to 1, drawn as a thin bar under the number.
  final double? progress;

  static const empty = TileData();
}

/// What a tile loader may use. [briefing] is shared so tiles that read the
/// home briefing cause one request, not one each.
class TileEnv {
  const TileEnv({required this.http, required this.briefing, this.now});

  final Dio http;
  final Future<Map<String, dynamic>?> Function() briefing;
  final DateTime? now;

  DateTime get clock => now ?? DateTime.now();
}

typedef TileLoader = Future<TileData?> Function(TileEnv env);

/// A feature that can sit on Home. [destination] is where tapping it goes; the
/// app resolves it the same way it resolves sidebar and search destinations.
@immutable
class TileSpec {
  const TileSpec({
    required this.id,
    required this.name,
    required this.icon,
    required this.category,
    required this.sizes,
    required this.destination,
    this.load,
    this.fallback = '',
  });

  final String id;
  final String name;
  final IconData icon;
  final TileCategory category;
  final List<TileSize> sizes;
  final String destination;

  /// Null for tiles that read live app state instead of the server.
  final TileLoader? load;

  /// Shown under the name while there is no data.
  final String fallback;

  bool supports(TileSize size) => sizes.contains(size);

  /// [size] if this tile supports it, else the nearest size it does.
  TileSize nearest(TileSize size) {
    if (supports(size)) return size;
    var best = sizes.first;
    var gap = (best.columns * best.rows - size.columns * size.rows).abs();
    for (final candidate in sizes) {
      final next =
          (candidate.columns * candidate.rows - size.columns * size.rows).abs();
      if (next < gap) {
        best = candidate;
        gap = next;
      }
    }
    return best;
  }
}
