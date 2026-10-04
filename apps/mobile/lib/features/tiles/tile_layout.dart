import 'dart:convert';

import 'package:shared_preferences/shared_preferences.dart';

import 'tile_models.dart';
import 'tile_registry.dart';

/// A tile pinned to Home at a size.
class TilePlacement {
  const TilePlacement(this.id, this.size);

  final String id;
  final TileSize size;

  TilePlacement withSize(TileSize next) => TilePlacement(id, next);

  Map<String, Object?> toJson() => {'id': id, 'size': size.code};
}

/// Where a placement sits on the grid.
class PlacedTile {
  const PlacedTile({
    required this.index,
    required this.column,
    required this.row,
    required this.columns,
    required this.rows,
  });

  final int index;
  final int column;
  final int row;
  final int columns;
  final int rows;

  bool contains(int c, int r) =>
      c >= column && c < column + columns && r >= row && r < row + rows;
}

/// Packs [items] into a grid [columns] wide, in order, each into the first free
/// spot that fits (reading left to right, top to bottom), so a small tile can
/// fill a gap beside a taller one.
List<PlacedTile> packTiles(List<TilePlacement> items, {int columns = 4}) {
  final taken = <(int, int)>{};
  final placed = <PlacedTile>[];
  for (var index = 0; index < items.length; index++) {
    final size = items[index].size;
    final width = size.columns.clamp(1, columns);
    var row = 0;
    PlacedTile? spot;
    while (spot == null) {
      for (var column = 0; column + width <= columns; column++) {
        var free = true;
        for (var r = row; r < row + size.rows && free; r++) {
          for (var c = column; c < column + width; c++) {
            if (taken.contains((c, r))) {
              free = false;
              break;
            }
          }
        }
        if (free) {
          spot = PlacedTile(
            index: index,
            column: column,
            row: row,
            columns: width,
            rows: size.rows,
          );
          break;
        }
      }
      row++;
    }
    for (var r = spot.row; r < spot.row + spot.rows; r++) {
      for (var c = spot.column; c < spot.column + spot.columns; c++) {
        taken.add((c, r));
      }
    }
    placed.add(spot);
  }
  return placed;
}

/// Rows needed to show [placed].
int gridRows(List<PlacedTile> placed) => placed.fold(
  0,
  (rows, tile) => tile.row + tile.rows > rows ? tile.row + tile.rows : rows,
);

/// What a new account sees on Home.
const defaultTileLayout = [
  TilePlacement('chats', TileSize.wide),
  TilePlacement('today', TileSize.square),
  TilePlacement('tasks', TileSize.square),
  TilePlacement('voice', TileSize.icon),
  TilePlacement('memory', TileSize.icon),
  TilePlacement('journal', TileSize.icon),
  TilePlacement('people', TileSize.icon),
  TilePlacement('expenses', TileSize.strip),
  TilePlacement('habits', TileSize.strip),
];

/// Drops unknown and repeated tiles and moves unsupported sizes to the nearest
/// one a tile offers, so an old or edited layout never breaks Home.
List<TilePlacement> normalizeTileLayout(Iterable<TilePlacement> items) {
  final seen = <String>{};
  final result = <TilePlacement>[];
  for (final item in items) {
    final spec = tileSpecFor(item.id);
    if (spec == null || !seen.add(item.id)) continue;
    result.add(TilePlacement(item.id, spec.nearest(item.size)));
  }
  return result;
}

List<TilePlacement>? decodeTileLayout(String? raw) {
  if (raw == null) return null;
  try {
    final data = jsonDecode(raw);
    if (data is! List) return null;
    return normalizeTileLayout([
      for (final item in data)
        if (item is Map &&
            item['id'] is String &&
            TileSize.parse(item['size']) != null)
          TilePlacement(item['id'] as String, TileSize.parse(item['size'])!),
    ]);
  } on FormatException {
    return null;
  }
}

/// Saves the home layout on this device.
class TileLayoutStore {
  TileLayoutStore._(this._prefs);

  static const _key = 'home.tiles.v1';

  final SharedPreferences? _prefs;

  /// In-memory store for tests and while storage is opening.
  TileLayoutStore.memory() : _prefs = null;

  static Future<TileLayoutStore> open() async {
    try {
      return TileLayoutStore._(await SharedPreferences.getInstance());
    } catch (_) {
      return TileLayoutStore.memory();
    }
  }

  List<TilePlacement> read() =>
      decodeTileLayout(_prefs?.getString(_key)) ?? defaultTileLayout;

  Future<void> write(List<TilePlacement> layout) async {
    await _prefs?.setString(
      _key,
      jsonEncode([for (final item in layout) item.toJson()]),
    );
  }

  Future<void> clear() async => _prefs?.remove(_key);

  /// Forgets the saved layout of everyone on this device (sign out).
  static Future<void> clearAll() async {
    try {
      await (await SharedPreferences.getInstance()).remove(_key);
    } catch (_) {
      // Nothing was stored.
    }
  }
}
