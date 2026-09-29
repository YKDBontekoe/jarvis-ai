import '../../json_maps.dart';

class SearchRouteTarget {
  const SearchRouteTarget({required this.kind, required this.parameters});

  final String kind;
  final Map<String, String> parameters;

  static SearchRouteTarget? fromJson(Object? raw) {
    final map = jsonObject(raw);
    final kind = asJsonString(map?['kind']);
    if (kind == null || kind.isEmpty) return null;
    final params = <String, String>{};
    final parameters = map?['parameters'];
    if (parameters is Map) {
      for (final entry in parameters.entries) {
        final key = entry.key.toString();
        final value = asJsonString(entry.value);
        if (value != null) params[key] = value;
      }
    }
    return SearchRouteTarget(kind: kind, parameters: params);
  }
}

class FederatedSearchHit {
  const FederatedSearchHit({
    required this.kind,
    required this.id,
    required this.title,
    required this.summary,
    required this.timestamp,
    required this.route,
    required this.relevance,
    required this.isPinned,
  });

  final String kind;
  final String id;
  final String? summary;
  final String title;
  final DateTime? timestamp;
  final SearchRouteTarget route;
  final double relevance;
  final bool isPinned;

  static FederatedSearchHit? fromJson(Map<String, dynamic> json) {
    final route = SearchRouteTarget.fromJson(json['route']);
    if (route == null) return null;
    return FederatedSearchHit(
      kind: asJsonString(json['kind']) ?? '',
      id: asJsonString(json['id']) ?? '',
      title: asJsonString(json['title']) ?? '',
      summary: asJsonString(json['summary']),
      timestamp: jsonDate(json['timestamp'], local: true),
      route: route,
      relevance: (json['relevance'] as num?)?.toDouble() ?? 0,
      isPinned: json['isPinned'] == true,
    );
  }
}

const searchKindLabels = <String, String>{
  'conversation': 'Conversations',
  'memory': 'Memories',
  'file': 'Files',
  'task': 'Tasks',
  'reminder': 'Reminders',
  'skill': 'Skills',
  'graph_entity': 'Knowledge graph',
  'channel_thread': 'Channels',
  'coding_run': 'Coding runs',
};
