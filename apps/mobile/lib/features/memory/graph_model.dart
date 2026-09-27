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
    final time = DateTime.tryParse(iso ?? '')?.toLocal();
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
    for (final node in nodes) {
      if (node.id == id) return node;
    }
    return null;
  }
}

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

List<GraphFactView> factsFor(String id, GraphSnapshot snapshot) {
  final self = snapshot.node(id);
  if (self == null) return const [];
  final facts = <GraphFactView>[];
  for (final link in snapshot.links) {
    if (link.fromId == id) {
      facts.add(
        GraphFactView(
          subjectName: self.name,
          predicate: link.predicate,
          objectLabel: snapshot.node(link.toId)?.name ?? 'Unknown',
          incoming: false,
          otherId: link.toId,
          confidence: link.confidence,
          validFrom: link.validFrom,
        ),
      );
    } else if (link.toId == id) {
      final subject = snapshot.node(link.fromId);
      facts.add(
        GraphFactView(
          subjectName: subject?.name ?? 'Unknown',
          predicate: link.predicate,
          objectLabel: self.name,
          incoming: true,
          otherId: link.fromId,
          confidence: link.confidence,
          validFrom: link.validFrom,
        ),
      );
    }
  }
  for (final literal in snapshot.literals) {
    if (literal.entityId != id) continue;
    facts.add(
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
  facts.sort((a, b) {
    if (a.incoming != b.incoming) return a.incoming ? 1 : -1;
    final byConfidence = (b.confidence ?? 0).compareTo(a.confidence ?? 0);
    if (byConfidence != 0) return byConfidence;
    return a.compact.compareTo(b.compact);
  });
  return facts;
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
  bool has(String? value) =>
      value != null && value.toLowerCase().contains(needle);
  if (has(node.name) ||
      has(node.type) ||
      has(graphTypeLabel(node.type)) ||
      has(node.summary)) {
    return true;
  }
  if (node.aliases.any((alias) => alias.toLowerCase().contains(needle))) {
    return true;
  }
  for (final fact in factsFor(node.id, snapshot)) {
    if (has(fact.predicate) ||
        has(humanPredicate(fact.predicate)) ||
        has(fact.objectLabel) ||
        has(fact.subjectName) ||
        has(fact.sentence)) {
      return true;
    }
  }
  return false;
}

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

/// Keeps the most important names readable by skipping labels that would sit on top of each other.
Set<String> chooseLabelIds(
  List<({String id, Offset anchor, bool force})> anchors, {
  double labelWidth = 104,
  double labelHeight = 18,
  double gap = 6,
}) {
  final occupied = <Rect>[];
  final chosen = <String>{};
  for (final anchor in anchors) {
    final rect = Rect.fromCenter(
      center: anchor.anchor + const Offset(0, 24),
      width: labelWidth,
      height: labelHeight,
    ).inflate(gap / 2);
    final blocked = occupied.any(rect.overlaps);
    if (!anchor.force && blocked) continue;
    chosen.add(anchor.id);
    occupied.add(rect);
  }
  return chosen;
}
