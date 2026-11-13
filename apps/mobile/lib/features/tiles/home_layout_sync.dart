import '../../json_maps.dart';
import 'tile_layout.dart';
import 'tile_models.dart';

/// The cards above the tiles that can be put away, by the owner in Customize
/// Home or by Jarvis when asked. Needs you and the Right now card always show.
/// The ids match `HomeCards` on the server.
abstract final class HomeCardIds {
  static const ongoing = 'ongoing';
  static const brief = 'brief';
  static const noticed = 'noticed';
  static const evening = 'evening';
  static const learning = 'learning';
  static const suggestions = 'suggestions';

  /// In the order Home shows them, with the name Customize Home uses.
  static const all = <String, String>{
    ongoing: 'Ongoing work',
    suggestions: 'Ask suggestions',
    noticed: 'Jarvis noticed',
    evening: 'Evening recap',
    brief: 'Morning brief',
    learning: 'Jarvis is learning',
  };
}

/// Who made the latest change to Home, as `/home/layout` says it.
const homeLayoutByJarvis = 'jarvis';

/// `GET /api/v1/home/layout`: Home as stored on the account, shared by every
/// device and by Jarvis.
class RemoteHomeLayout {
  const RemoteHomeLayout({
    required this.tiles,
    required this.hiddenCards,
    required this.revision,
    required this.changedBy,
    required this.stored,
    this.note,
    this.canUndo = false,
  });

  final List<TilePlacement> tiles;
  final Set<String> hiddenCards;
  final int revision;
  final String changedBy;

  /// False while the account never saved a layout; the device then sends its own.
  final bool stored;

  /// Jarvis's one line on what it changed and why.
  final String? note;
  final bool canUndo;

  static RemoteHomeLayout? fromJson(Object? data) {
    final json = jsonObject(data);
    if (json == null || json['tiles'] is! List) return null;
    return RemoteHomeLayout(
      tiles: normalizeTileLayout([
        for (final item in json['tiles'] as List)
          if (jsonObject(item) case final tile?
              when tile['id'] is String && TileSize.parse(tile['size']) != null)
            TilePlacement(tile['id'] as String, TileSize.parse(tile['size'])!),
      ]),
      hiddenCards: {
        for (final card
            in json['hiddenCards'] is List
                ? json['hiddenCards'] as List
                : const [])
          if (card is String && HomeCardIds.all.containsKey(card)) card,
      },
      revision: asJsonInt(json['revision'], 0),
      changedBy: asJsonString(json['changedBy']) ?? 'app',
      stored: json['stored'] != false,
      note: asJsonString(json['note'])?.trim(),
      canUndo: asJsonBool(json['canUndo']),
    );
  }
}

/// A change Jarvis made to Home that this device has not acknowledged yet.
class JarvisHomeChange {
  const JarvisHomeChange({
    required this.revision,
    this.note,
    this.canUndo = true,
  });

  final int revision;
  final String? note;
  final bool canUndo;
}
