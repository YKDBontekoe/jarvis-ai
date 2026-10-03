import '../../json_maps.dart';

String libraryKindLabel(String kind) => switch (kind) {
  'web' => 'Web page',
  'report' => 'Research report',
  _ => 'Note',
};

class LibraryItemData {
  const LibraryItemData({
    required this.id,
    required this.kind,
    required this.title,
    required this.summary,
    required this.tags,
    required this.keyPoints,
    this.url,
    this.content,
  });

  final String id;
  final String kind;
  final String title;
  final String summary;
  final List<String> tags;
  final List<String> keyPoints;
  final String? url;
  final String? content;

  String? get host => url == null ? null : Uri.tryParse(url!)?.host;

  static LibraryItemData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final title = map == null ? null : jsonString(map, 'title');
    if (map == null || id == null || title == null) return null;
    return LibraryItemData(
      id: id,
      kind: jsonString(map, 'kind') ?? 'note',
      title: title,
      summary: jsonString(map, 'summary') ?? '',
      tags: jsonStrings(map['tags']),
      keyPoints: jsonStrings(map['keyPoints']),
      url: jsonString(map, 'url'),
      content: jsonString(map, 'content'),
    );
  }
}

class FlashcardData {
  const FlashcardData({
    required this.id,
    required this.front,
    required this.back,
  });

  final String id;
  final String front;
  final String back;

  static FlashcardData? fromJson(dynamic json) {
    final map = jsonObject(json);
    final id = map == null ? null : jsonString(map, 'id');
    final front = map == null ? null : jsonString(map, 'front');
    final back = map == null ? null : jsonString(map, 'back');
    if (id == null || front == null || back == null) return null;
    return FlashcardData(id: id, front: front, back: back);
  }
}

class CardStatsData {
  const CardStatsData({this.total = 0, this.due = 0});

  final int total;
  final int due;

  static CardStatsData fromJson(dynamic json) {
    final map = jsonObject(json);
    return map == null
        ? const CardStatsData()
        : CardStatsData(
            total: asJsonInt(map['total']),
            due: asJsonInt(map['due']),
          );
  }
}
