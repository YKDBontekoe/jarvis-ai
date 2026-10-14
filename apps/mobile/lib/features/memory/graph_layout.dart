import 'dart:math' as math;
import 'dart:ui';

/// Deterministic force-directed layout (Fruchterman–Reingold) for small graphs.
///
/// Positions are normalized to 0..1 in both axes. [pinned] nodes start and stay at the center, which keeps the
/// user's own node as the visual anchor. Well-connected nodes push harder so hubs get room around them, a gentle
/// pull toward the center keeps stray nodes from piling up against the edges, and nodes sharing a [groups] index
/// attract each other so circles of friends, colleagues, or places read as visible clusters.
Map<String, Offset> layoutGraph(
  List<String> nodes,
  List<(String, String)> edges, {
  String? pinned,
  Map<String, int> groups = const {},
  int iterations = 260,
}) {
  if (nodes.isEmpty) return const {};
  final positions = <String, Offset>{};
  final known = nodes.toSet();
  final valid = edges
      .where((e) => e.$1 != e.$2)
      .where((e) => known.contains(e.$1) && known.contains(e.$2))
      .toList();
  final degree = {for (final node in nodes) node: 0};
  for (final (from, to) in valid) {
    degree[from] = degree[from]! + 1;
    degree[to] = degree[to]! + 1;
  }
  for (final (index, node) in nodes.indexed) {
    final angle = (index / nodes.length) * math.pi * 2 + _hash(node) % 7 * .1;
    final radius = .18 + (_hash(node) % 100) / 600;
    positions[node] = node == pinned
        ? const Offset(.5, .5)
        : Offset(.5 + math.cos(angle) * radius, .5 + math.sin(angle) * radius);
  }
  final ideal = math.sqrt(1 / nodes.length) * .9;
  double charge(String node) =>
      1 + math.sqrt(math.min(degree[node]!, 30).toDouble()) * .35;
  var temperature = .1;
  for (var step = 0; step < iterations; step++) {
    final displacement = {for (final node in nodes) node: Offset.zero};
    for (var i = 0; i < nodes.length; i++) {
      for (var j = i + 1; j < nodes.length; j++) {
        final a = nodes[i];
        final b = nodes[j];
        var delta = positions[a]! - positions[b]!;
        var distance = delta.distance;
        if (distance < 1e-4) {
          delta = Offset((_hash(a) % 3 - 1) * 1e-3, (_hash(b) % 3 - 1) * 1e-3);
          distance = 1e-3;
        }
        final force = ideal * ideal / distance * charge(a) * charge(b) * .6;
        final push = delta / distance * force;
        displacement[a] = displacement[a]! + push;
        displacement[b] = displacement[b]! - push;
      }
    }
    for (final (from, to) in valid) {
      final delta = positions[from]! - positions[to]!;
      final distance = math.max(delta.distance, 1e-4);
      // Hub edges are slightly weaker so leaves fan out instead of collapsing onto the hub.
      final weight =
          1 / math.sqrt(math.max(1, math.max(degree[from]!, degree[to]!)));
      final pull = delta / distance * (distance * distance / ideal) * weight;
      displacement[from] = displacement[from]! - pull;
      displacement[to] = displacement[to]! + pull;
    }
    final centroids = <int, Offset>{};
    final sizes = <int, int>{};
    for (final MapEntry(key: node, value: group) in groups.entries) {
      final at = positions[node];
      if (at == null) continue;
      centroids[group] = (centroids[group] ?? Offset.zero) + at;
      sizes[group] = (sizes[group] ?? 0) + 1;
    }
    for (final node in nodes) {
      if (node == pinned) continue;
      final toCenter = const Offset(.5, .5) - positions[node]!;
      final lonely = degree[node] == 0 ? 2.2 : 1.0;
      var move = displacement[node]! + toCenter * (ideal * 1.6 * lonely);
      final group = groups[node];
      if (group != null && sizes[group]! > 1) {
        final centroid = centroids[group]! / sizes[group]!.toDouble();
        move += (centroid - positions[node]!) * (ideal * 1.3);
      }
      final length = move.distance;
      if (length == 0) continue;
      move = move / length * math.min(length, temperature);
      positions[node] = positions[node]! + move;
    }
    temperature = math.max(.003, temperature * .975);
  }
  return _normalize(positions, pinned);
}

/// Scales the layout uniformly into 0.06..0.94, centered on [pinned] when present.
Map<String, Offset> _normalize(Map<String, Offset> positions, String? pinned) {
  final pinnedAt = pinned == null ? null : positions[pinned];
  Offset center;
  if (pinnedAt != null) {
    center = pinnedAt;
  } else {
    var minX = double.infinity, minY = double.infinity;
    var maxX = -double.infinity, maxY = -double.infinity;
    for (final point in positions.values) {
      minX = math.min(minX, point.dx);
      minY = math.min(minY, point.dy);
      maxX = math.max(maxX, point.dx);
      maxY = math.max(maxY, point.dy);
    }
    center = Offset((minX + maxX) / 2, (minY + maxY) / 2);
  }
  // Scale to the 90th percentile so one straggler doesn't shrink everyone else; stragglers are clamped to the edge.
  final reaches = [
    for (final point in positions.values)
      math.max((point.dx - center.dx).abs(), (point.dy - center.dy).abs()),
  ]..sort();
  final reach = math.max(1e-6, reaches[((reaches.length - 1) * .9).round()]);
  final scale = .4 / reach;
  return {
    for (final MapEntry(key: id, value: point) in positions.entries)
      id: id == pinned
          ? const Offset(.5, .5)
          : Offset(
              (.5 + (point.dx - center.dx) * scale).clamp(.03, .97),
              (.5 + (point.dy - center.dy) * scale).clamp(.03, .97),
            ),
  };
}

int _hash(String value) {
  var hash = 17;
  for (final unit in value.codeUnits) {
    hash = (hash * 31 + unit) & 0x7fffffff;
  }
  return hash;
}
