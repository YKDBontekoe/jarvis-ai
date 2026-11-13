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

/// The parts of Home under the greeting and the Ask bar, in an order the owner
/// (or Jarvis, when asked) chooses. [tiles] is the tile grid itself. The ids
/// match `HomeSections` on the server.
abstract final class HomeSectionIds {
  static const needsYou = 'needs-you';
  static const ongoing = HomeCardIds.ongoing;
  static const noticed = HomeCardIds.noticed;
  static const evening = HomeCardIds.evening;
  static const brief = HomeCardIds.brief;
  static const learning = HomeCardIds.learning;
  static const tiles = 'tiles';

  /// How Home always looked, and what a new account gets.
  static const defaults = [
    ongoing,
    needsYou,
    noticed,
    evening,
    brief,
    learning,
    tiles,
  ];

  static const names = <String, String>{
    needsYou: 'Needs you',
    ongoing: 'Ongoing work',
    noticed: 'Jarvis noticed',
    evening: 'Evening recap',
    brief: 'Morning brief',
    learning: 'Jarvis is learning',
    tiles: 'Tiles',
  };

  /// Known sections once each, in the given order, with any left out added
  /// in the default order, so an older or edited list never loses a section.
  static List<String> normalize(Iterable<Object?>? order) {
    final result = <String>[];
    for (final id in order ?? const []) {
      if (id is String && names.containsKey(id) && !result.contains(id)) {
        result.add(id);
      }
    }
    for (final id in defaults) {
      if (!result.contains(id)) result.add(id);
    }
    return result;
  }
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
    this.sections = HomeSectionIds.defaults,
    this.note,
    this.canUndo = false,
  });

  final List<TilePlacement> tiles;
  final Set<String> hiddenCards;
  final int revision;
  final String changedBy;

  /// False while the account never saved a layout; the device then sends its own.
  final bool stored;

  /// Home's sections top to bottom; see [HomeSectionIds].
  final List<String> sections;

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
      sections: HomeSectionIds.normalize(
        json['sections'] is List ? json['sections'] as List : null,
      ),
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
