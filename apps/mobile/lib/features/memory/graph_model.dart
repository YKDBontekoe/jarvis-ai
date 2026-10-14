import 'dart:ui';

import '../../json_maps.dart';

const graphTypeOrder = [
  'person',
  'place',
  'organization',
  'project',
  'event',
  'pet',
  'topic',
  'thing',
];

String humanPredicate(String predicate) =>
    predicate.replaceAll('_', ' ').trim();

String graphTypeLabel(String? type) {
  final value = (type == null || type.isEmpty) ? 'thing' : type;
  return value[0].toUpperCase() + value.substring(1);
}

const _months = [
  'Jan', 'Feb', 'Mar', 'Apr', 'May', 'Jun', //
  'Jul', 'Aug', 'Sep', 'Oct', 'Nov', 'Dec',
];

String formatGraphDay(DateTime time) =>
    '${time.day} ${_months[time.month - 1]} ${time.year}';

/// Formats a fact's validity interval the same way on the map and the timeline.
String formatFactRange(String? fromIso, String? toIso) {
  String day(String? iso) {
    final time = jsonDate(iso, local: true);
    if (time == null) return 'now';
    return formatGraphDay(time);
  }

  final from = day(fromIso);
  if (toIso == null || toIso.isEmpty) return 'Since $from';
  return '$from → ${day(toIso)}';
}

/// Omits a perfect score so confident facts stay quiet.
String? confidenceLabel(double? confidence) {
  if (confidence == null || confidence >= 0.995 || confidence <= 0) return null;
  return '${(confidence * 100).round()}%';
}

double? graphJsonDouble(dynamic value) => switch (value) {
  num number => number.toDouble(),
  String text => double.tryParse(text),
  _ => null,
};

class GraphNode {
  const GraphNode({
    required this.id,
    required this.name,
    required this.type,
    required this.aliases,
    required this.relationCount,
    this.summary,
    this.updatedAt,
  });

  final String id;
  final String name;
  final String type;
  final List<String> aliases;
  final int relationCount;
  final String? summary;
  final String? updatedAt;

  bool get isYou => name == 'You';
}

class GraphLink {
  const GraphLink({
    required this.fromId,
    required this.toId,
    required this.predicate,
    this.confidence,
    this.validFrom,
  });

  final String fromId;
  final String toId;
  final String predicate;
  final double? confidence;
  final String? validFrom;
}

class GraphLiteralFact {
  const GraphLiteralFact({
    required this.entityId,
    required this.predicate,
    required this.value,
    this.confidence,
    this.validFrom,
  });

  final String entityId;
  final String predicate;
  final String value;
  final double? confidence;
  final String? validFrom;
}

class GraphFactView {
  const GraphFactView({
    required this.subjectName,
    required this.predicate,
    required this.objectLabel,
    required this.incoming,
    this.otherId,
    this.confidence,
    this.validFrom,
    this.literal = false,
  });

  final String subjectName;
  final String predicate;
  final String objectLabel;
  final bool incoming;
  final String? otherId;
  final double? confidence;
  final String? validFrom;
  final bool literal;

  String get sentence =>
      '$subjectName ${humanPredicate(predicate)} $objectLabel'.trim();

  String get compact => '${humanPredicate(predicate)} $objectLabel'.trim();
}

class GraphSnapshot {
  const GraphSnapshot({
    required this.nodes,
    required this.links,
    required this.literals,
  });

  final List<GraphNode> nodes;
  final List<GraphLink> links;
  final List<GraphLiteralFact> literals;

  static const empty = GraphSnapshot(nodes: [], links: [], literals: []);

  GraphNode? node(String? id) {
    if (id == null) return null;
    return (_nodeIndex[this] ??= {for (final node in nodes) node.id: node})[id];
  }
}

// Lookups are cached per snapshot so graphs with a thousand entities stay linear instead of quadratic.
final _nodeIndex = Expando<Map<String, GraphNode>>('graphNodeIndex');
final _factIndex = Expando<Map<String, List<GraphFactView>>>('graphFactIndex');
final _searchText = Expando<Map<String, String>>('graphSearchText');

GraphSnapshot parseGraphOverview(Map<String, dynamic> overview) {
  final nodes = <GraphNode>[];
  for (final entity in jsonMaps(overview['entities'])) {
    final id = asJsonString(entity['id']);
    if (id == null || id.isEmpty) continue;
    final summary = asJsonString(entity['summary']);
    nodes.add(
      GraphNode(
        id: id,
        name: asJsonString(entity['name']) ?? 'Unknown',
        type: asJsonString(entity['type']) ?? 'thing',
        aliases: jsonStrings(entity['aliases']),
        relationCount: asJsonInt(entity['relationCount']),
        summary: summary == null || summary.isEmpty ? null : summary,
        updatedAt: asJsonString(entity['updatedAt']),
      ),
    );
  }
  final known = {for (final node in nodes) node.id};
  final links = <GraphLink>[];
  for (final edge in jsonMaps(overview['edges'])) {
    final from = asJsonString(edge['from']);
    final to = asJsonString(edge['to']);
    final predicate = asJsonString(edge['predicate']);
    if (from == null ||
        to == null ||
        predicate == null ||
        predicate.isEmpty ||
        !known.contains(from) ||
        !known.contains(to) ||
        from == to) {
      continue;
    }
    links.add(
      GraphLink(
        fromId: from,
        toId: to,
        predicate: predicate,
        confidence: graphJsonDouble(edge['confidence']),
        validFrom: asJsonString(edge['validFrom']),
      ),
    );
  }
  final literals = <GraphLiteralFact>[];
  for (final item in jsonMaps(overview['literals'])) {
    final entityId = asJsonString(item['entityId']);
    final predicate = asJsonString(item['predicate']);
    final value = asJsonString(item['value']);
    if (entityId == null ||
        predicate == null ||
        value == null ||
        predicate.isEmpty ||
        value.isEmpty ||
        !known.contains(entityId)) {
      continue;
    }
    literals.add(
      GraphLiteralFact(
        entityId: entityId,
        predicate: predicate,
        value: value,
        confidence: graphJsonDouble(item['confidence']),
        validFrom: asJsonString(item['validFrom']),
      ),
    );
  }
  return GraphSnapshot(nodes: nodes, links: links, literals: literals);
}

List<GraphNode> sortedGraphNodes(List<GraphNode> nodes) {
  final copy = [...nodes];
  copy.sort((a, b) {
    if (a.isYou != b.isYou) return a.isYou ? -1 : 1;
    final byCount = b.relationCount.compareTo(a.relationCount);
    if (byCount != 0) return byCount;
    return a.name.toLowerCase().compareTo(b.name.toLowerCase());
  });
  return copy;
}

List<String> graphTypesPresent(List<GraphNode> nodes) {
  final present = nodes.map((node) => node.type).toSet();
  return [
    for (final type in graphTypeOrder)
      if (present.contains(type)) type,
    for (final type in present)
      if (!graphTypeOrder.contains(type)) type,
  ];
}

List<GraphFactView> factsFor(String id, GraphSnapshot snapshot) =>
    (_factIndex[snapshot] ??= _indexFacts(snapshot))[id] ?? const [];

Map<String, List<GraphFactView>> _indexFacts(GraphSnapshot snapshot) {
  final index = <String, List<GraphFactView>>{};
  void add(String id, GraphFactView fact) =>
      (index[id] ??= <GraphFactView>[]).add(fact);
  for (final link in snapshot.links) {
    final from = snapshot.node(link.fromId);
    final to = snapshot.node(link.toId);
    if (from != null) {
      add(
        link.fromId,
        GraphFactView(
          subjectName: from.name,
          predicate: link.predicate,
          objectLabel: to?.name ?? 'Unknown',
          incoming: false,
          otherId: link.toId,
          confidence: link.confidence,
          validFrom: link.validFrom,
        ),
      );
    }
    if (to != null) {
      add(
        link.toId,
        GraphFactView(
          subjectName: from?.name ?? 'Unknown',
          predicate: link.predicate,
          objectLabel: to.name,
          incoming: true,
          otherId: link.fromId,
          confidence: link.confidence,
          validFrom: link.validFrom,
        ),
      );
    }
  }
  for (final literal in snapshot.literals) {
    final self = snapshot.node(literal.entityId);
    if (self == null) continue;
    add(
      literal.entityId,
      GraphFactView(
        subjectName: self.name,
        predicate: literal.predicate,
        objectLabel: literal.value,
        incoming: false,
        confidence: literal.confidence,
        validFrom: literal.validFrom,
        literal: true,
      ),
    );
  }
  for (final facts in index.values) {
    facts.sort((a, b) {
      if (a.incoming != b.incoming) return a.incoming ? 1 : -1;
      final byConfidence = (b.confidence ?? 0).compareTo(a.confidence ?? 0);
      if (byConfidence != 0) return byConfidence;
      return a.compact.compareTo(b.compact);
    });
  }
  return index;
}

String factPreview(List<GraphFactView> facts, {int limit = 2}) => facts
    .take(limit)
    .map(
      (fact) => fact.incoming
          ? '${fact.subjectName} ${humanPredicate(fact.predicate)}'
          : fact.compact,
    )
    .where((line) => line.isNotEmpty)
    .join(' · ');

bool nodeMatchesQuery(GraphNode node, GraphSnapshot snapshot, String query) {
  final needle = query.trim().toLowerCase();
  if (needle.isEmpty) return true;
  final haystacks = _searchText[snapshot] ??= {};
  final text = haystacks[node.id] ??= _searchableText(node, snapshot);
  return text.contains(needle);
}

/// Everything a search can hit for [node], lower-cased once. Fields are joined with a separator no query contains,
/// so a match never spans two fields.
String _searchableText(GraphNode node, GraphSnapshot snapshot) => [
  node.name,
  node.type,
  graphTypeLabel(node.type),
  ?node.summary,
  ...node.aliases,
  for (final fact in factsFor(node.id, snapshot)) ...[
    fact.predicate,
    fact.sentence,
  ],
].map((value) => value.toLowerCase()).join('\u0000');

String graphStatsLabel({
  required int entities,
  required int links,
  required int details,
}) {
  String noun(int count, String one, String many) =>
      '$count ${count == 1 ? one : many}';
  return [
    noun(entities, 'entity', 'entities'),
    noun(links, 'link', 'links'),
    if (details > 0) noun(details, 'detail', 'details'),
  ].join(' · ');
}

/// Lane index for each edge so parallel facts between the same pair don't share one line.
List<({int lane, int lanes})> edgeLanes(List<GraphLink> links) {
  String key(GraphLink link) => link.fromId.compareTo(link.toId) <= 0
      ? '${link.fromId}\u0000${link.toId}'
      : '${link.toId}\u0000${link.fromId}';
  final counts = <String, int>{};
  for (final link in links) {
    final id = key(link);
    counts[id] = (counts[id] ?? 0) + 1;
  }
  final seen = <String, int>{};
  final lanes = <({int lane, int lanes})>[];
  for (final link in links) {
    final id = key(link);
    final lane = seen[id] ?? 0;
    seen[id] = lane + 1;
    lanes.add((lane: lane, lanes: counts[id] ?? 1));
  }
  return lanes;
}

/// Keeps the most important names readable by skipping labels that would sit on top of each other, or on top of
/// [obstacles] such as other nodes.
Set<String> chooseLabelIds(
  List<({String id, Offset anchor, bool force})> anchors, {
  double labelWidth = 104,
  double labelHeight = 18,
  double gap = 6,
  List<Rect> obstacles = const [],
  Map<String, double> widths = const {},
  int? maxLabels,
}) {
  final occupied = <Rect>[];
  final chosen = <String>{};
  var optional = 0;
  for (final anchor in anchors) {
    if (!anchor.force && maxLabels != null && optional >= maxLabels) continue;
    final rect = Rect.fromCenter(
      center: anchor.anchor + const Offset(0, 24),
      width: widths[anchor.id] ?? labelWidth,
      height: labelHeight,
    ).inflate(gap / 2);
    final blocked =
        occupied.any(rect.overlaps) ||
        obstacles.any((obstacle) => obstacle.deflate(2).overlaps(rect));
    if (!anchor.force && blocked) continue;
    if (!anchor.force) optional++;
    chosen.add(anchor.id);
    occupied.add(rect);
  }
  return chosen;
}

/// When a node first appears: its earliest dated fact, else its last update.
Map<String, DateTime> nodeFirstSeen(GraphSnapshot snapshot) {
  final seen = <String, DateTime>{};
  void note(String id, String? iso) {
    final time = jsonDate(iso);
    if (time == null) return;
    final current = seen[id];
    if (current == null || time.isBefore(current)) seen[id] = time;
  }

  for (final link in snapshot.links) {
    note(link.fromId, link.validFrom);
    note(link.toId, link.validFrom);
  }
  for (final literal in snapshot.literals) {
    note(literal.entityId, literal.validFrom);
  }
  for (final node in snapshot.nodes) {
    if (!seen.containsKey(node.id)) note(node.id, node.updatedAt);
  }
  return seen;
}

/// Earliest and latest fact dates, or null when nothing is dated.
({DateTime first, DateTime last})? graphTimeRange(GraphSnapshot snapshot) {
  DateTime? first;
  DateTime? last;
  void note(String? iso) {
    final time = jsonDate(iso);
    if (time == null) return;
    if (first == null || time.isBefore(first!)) first = time;
    if (last == null || time.isAfter(last!)) last = time;
  }

  for (final link in snapshot.links) {
    note(link.validFrom);
  }
  for (final literal in snapshot.literals) {
    note(literal.validFrom);
  }
  if (first == null || last == null || !last!.isAfter(first!)) return null;
  return (first: first!, last: last!);
}

/// The graph as Jarvis knew it at [time]: facts dated later are hidden, undated ones always show.
GraphSnapshot snapshotAsOf(GraphSnapshot snapshot, DateTime time) {
  bool known(String? iso) {
    final when = jsonDate(iso);
    return when == null || !when.isAfter(time);
  }

  final firstSeen = nodeFirstSeen(snapshot);
  final nodes = [
    for (final node in snapshot.nodes)
      if (firstSeen[node.id] == null || !firstSeen[node.id]!.isAfter(time))
        node,
  ];
  final ids = {for (final node in nodes) node.id};
  return GraphSnapshot(
    nodes: nodes,
    links: [
      for (final link in snapshot.links)
        if (ids.contains(link.fromId) &&
            ids.contains(link.toId) &&
            known(link.validFrom))
          link,
    ],
    literals: [
      for (final literal in snapshot.literals)
        if (ids.contains(literal.entityId) && known(literal.validFrom)) literal,
    ],
  );
}

/// Links plus details per node, so hubs can be drawn larger.
Map<String, int> graphDegrees(GraphSnapshot snapshot) {
  final degrees = {for (final node in snapshot.nodes) node.id: 0};
  for (final link in snapshot.links) {
    degrees[link.fromId] = (degrees[link.fromId] ?? 0) + 1;
    degrees[link.toId] = (degrees[link.toId] ?? 0) + 1;
  }
  for (final literal in snapshot.literals) {
    degrees[literal.entityId] = (degrees[literal.entityId] ?? 0) + 1;
  }
  return degrees;
}

/// Hop distance from [id] to every node reachable within [maxHops].
///
/// Paths never continue through [barriers] (other than [id] itself): everything is two steps away via "You", which
/// says nothing.
Map<String, int> graphNeighborhood(
  String id,
  List<GraphLink> links, {
  int maxHops = 2,
  Set<String> barriers = const {},
}) {
  final adjacent = <String, Set<String>>{};
  for (final link in links) {
    adjacent.putIfAbsent(link.fromId, () => {}).add(link.toId);
    adjacent.putIfAbsent(link.toId, () => {}).add(link.fromId);
  }
  final distance = {id: 0};
  var frontier = [id];
  for (var hop = 1; hop <= maxHops && frontier.isNotEmpty; hop++) {
    final next = <String>[];
    for (final current in frontier) {
      if (current != id && barriers.contains(current)) continue;
      for (final other in adjacent[current] ?? const <String>{}) {
        if (distance.containsKey(other)) continue;
        distance[other] = hop;
        next.add(other);
      }
    }
    frontier = next;
  }
  return distance;
}

/// Groups nodes that know each other, ignoring [hubs] (usually "You", who links to everything).
///
/// Deterministic label propagation: each node repeatedly adopts the label most common among its neighbours, keeping
/// its own label on a tie. Returns a group index per node; nodes without links outside [hubs] get none.
Map<String, int> graphCommunities(
  List<String> nodes,
  List<(String, String)> edges, {
  Set<String> hubs = const {},
}) {
  final adjacent = <String, List<String>>{};
  for (final (from, to) in edges) {
    if (hubs.contains(from) || hubs.contains(to) || from == to) continue;
    adjacent.putIfAbsent(from, () => []).add(to);
    adjacent.putIfAbsent(to, () => []).add(from);
  }
  final ordered = [
    for (final node in nodes)
      if (adjacent.containsKey(node)) node,
  ];
  final label = {for (final (index, node) in ordered.indexed) node: index};
  for (var round = 0; round < 12; round++) {
    var changed = false;
    for (final node in ordered) {
      final counts = <int, int>{};
      for (final other in adjacent[node]!) {
        final value = label[other]!;
        counts[value] = (counts[value] ?? 0) + 1;
      }
      // Keeping the current label on ties stops one label from snowballing across a large, loosely knit graph.
      var best = label[node]!;
      var bestCount = counts[best] ?? 0;
      for (final MapEntry(key: value, value: count) in counts.entries) {
        if (count > bestCount ||
            (count == bestCount && value < best && bestCount == 0)) {
          best = value;
          bestCount = count;
        }
      }
      if (best != label[node]) {
        label[node] = best;
        changed = true;
      }
    }
    if (!changed) break;
  }
  final renumbered = <int, int>{};
  return {
    for (final node in ordered)
      node: renumbered.putIfAbsent(label[node]!, () => renumbered.length),
  };
}

class GraphCluster {
  const GraphCluster({required this.index, required this.memberIds});

  final int index;
  final List<String> memberIds;
}

/// Groups of three or more, largest first. A group holding more than [maxShare] of all grouped nodes says nothing
/// ("everyone knows everyone") and is dropped.
List<GraphCluster> graphClusters(
  Map<String, int> communities, {
  double maxShare = .5,
}) {
  final members = <int, List<String>>{};
  for (final MapEntry(key: id, value: group) in communities.entries) {
    members.putIfAbsent(group, () => []).add(id);
  }
  final clusters = [
    for (final MapEntry(key: index, value: ids) in members.entries)
      if (ids.length >= 3 &&
          (communities.length < 12 ||
              ids.length <= communities.length * maxShare))
        GraphCluster(index: index, memberIds: ids),
  ];
  clusters.sort((a, b) {
    final bySize = b.memberIds.length.compareTo(a.memberIds.length);
    return bySize != 0 ? bySize : a.index.compareTo(b.index);
  });
  return clusters;
}

class GraphRecentFact {
  const GraphRecentFact({
    required this.fact,
    required this.entityId,
    required this.at,
  });

  final GraphFactView fact;
  final String entityId;
  final DateTime at;
}

/// Newest dated facts, links and details alike.
List<GraphRecentFact> recentGraphFacts(
  GraphSnapshot snapshot, {
  int limit = 4,
}) {
  final items = <GraphRecentFact>[];
  for (final link in snapshot.links) {
    final at = jsonDate(link.validFrom);
    final subject = snapshot.node(link.fromId);
    if (at == null || subject == null) continue;
    items.add(
      GraphRecentFact(
        entityId: link.fromId,
        at: at,
        fact: GraphFactView(
          subjectName: subject.name,
          predicate: link.predicate,
          objectLabel: snapshot.node(link.toId)?.name ?? 'Unknown',
          incoming: false,
          otherId: link.toId,
          confidence: link.confidence,
          validFrom: link.validFrom,
        ),
      ),
    );
  }
  for (final literal in snapshot.literals) {
    final at = jsonDate(literal.validFrom);
    final subject = snapshot.node(literal.entityId);
    if (at == null || subject == null) continue;
    items.add(
      GraphRecentFact(
        entityId: literal.entityId,
        at: at,
        fact: GraphFactView(
          subjectName: subject.name,
          predicate: literal.predicate,
          objectLabel: literal.value,
          incoming: false,
          confidence: literal.confidence,
          validFrom: literal.validFrom,
          literal: true,
        ),
      ),
    );
  }
  items.sort((a, b) {
    final byTime = b.at.compareTo(a.at);
    return byTime != 0 ? byTime : a.fact.sentence.compareTo(b.fact.sentence);
  });
  return items.take(limit).toList(growable: false);
}

/// Linked neighbours of [id] counted by type, most common first.
List<({String type, int count})> neighborTypeMix(
  String id,
  GraphSnapshot snapshot,
) {
  final counts = <String, int>{};
  final seen = <String>{};
  for (final link in snapshot.links) {
    final other = link.fromId == id
        ? link.toId
        : (link.toId == id ? link.fromId : null);
    if (other == null || !seen.add(other)) continue;
    final type = snapshot.node(other)?.type ?? 'thing';
    counts[type] = (counts[type] ?? 0) + 1;
  }
  final mix = [
    for (final MapEntry(key: type, value: count) in counts.entries)
      (type: type, count: count),
  ];
  mix.sort((a, b) {
    final byCount = b.count.compareTo(a.count);
    return byCount != 0 ? byCount : a.type.compareTo(b.type);
  });
  return mix;
}

/// What the server answered about the graph, with the entities and links to light up on the map.
class GraphAnswerView {
  const GraphAnswerView({
    required this.question,
    required this.answer,
    required this.entityIds,
    required this.links,
    required this.facts,
    required this.followUps,
    required this.grounded,
  });

  final String question;
  final String answer;
  final Set<String> entityIds;

  /// Unordered "a|b" keys so a link matches whichever way it is stored.
  final Set<String> links;
  final List<String> facts;
  final List<String> followUps;

  /// False when the model was unavailable and the answer only lists facts.
  final bool grounded;

  bool coversLink(String fromId, String toId) =>
      links.contains(graphPairKey(fromId, toId));
}

String graphPairKey(String a, String b) =>
    a.compareTo(b) <= 0 ? '$a|$b' : '$b|$a';

GraphAnswerView parseGraphAnswer(String question, Map<String, dynamic> json) =>
    GraphAnswerView(
      question: question,
      answer: asJsonString(json['answer'])?.trim() ?? '',
      entityIds: jsonStrings(json['entityIds']).toSet(),
      links: {
        for (final link in jsonMaps(json['links']))
          if (asJsonString(link['from']) case final from?)
            if (asJsonString(link['to']) case final to?) graphPairKey(from, to),
      },
      facts: jsonStrings(json['facts']),
      followUps: jsonStrings(json['followUps']),
      grounded: json['grounded'] == true,
    );

/// Questions worth asking about this graph right now, built from real names so they always have an answer.
List<String> graphQuestionSuggestions(
  GraphSnapshot snapshot,
  Map<String, int> degrees, {
  GraphNode? selected,
}) {
  if (selected != null && !selected.isYou) {
    return [
      'Tell me about ${selected.name}',
      'How is ${selected.name} connected to me?',
      'What changed about ${selected.name} lately?',
    ];
  }
  final hubs = [
    for (final node in snapshot.nodes)
      if (!node.isYou) node,
  ]..sort((a, b) => (degrees[b.id] ?? 0).compareTo(degrees[a.id] ?? 0));
  final people = hubs.where((node) => node.type == 'person').take(2).toList();
  final organization = hubs
      .where((node) => node.type == 'organization')
      .firstOrNull;
  return [
    'Who matters most in my life?',
    if (people.length == 2)
      'How is ${people[0].name} connected to ${people[1].name}?'
    else if (hubs.isNotEmpty)
      'Tell me about ${hubs.first.name}',
    if (organization != null) 'Who do I know at ${organization.name}?',
    'What did I tell you recently?',
  ];
}
