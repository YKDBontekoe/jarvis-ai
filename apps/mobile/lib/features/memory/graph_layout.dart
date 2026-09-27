import 'dart:math' as math;
import 'dart:ui';

/// Deterministic force-directed layout (Fruchterman–Reingold) for small graphs.
///
/// Positions are normalized to 0..1 in both axes. [pinned] nodes start and stay at the center, which keeps the
/// user's own node as the visual anchor.
Map<String, Offset> layoutGraph(
  List<String> nodes,
  List<(String, String)> edges, {
  String? pinned,
  int iterations = 220,
}) {
  if (nodes.isEmpty) return const {};
  final positions = <String, Offset>{};
  for (final (index, node) in nodes.indexed) {
    final angle = (index / nodes.length) * math.pi * 2 + _hash(node) % 7 * .1;
    final radius = .25 + (_hash(node) % 100) / 500;
    positions[node] = node == pinned
        ? const Offset(.5, .5)
        : Offset(.5 + math.cos(angle) * radius, .5 + math.sin(angle) * radius);
  }
  final ideal = math.sqrt(1 / nodes.length) * .9;
  var temperature = .12;
  final valid = edges
      .where((e) => positions.containsKey(e.$1) && positions.containsKey(e.$2))
      .toList();
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
        final force = ideal * ideal / distance;
        final push = delta / distance * force;
        displacement[a] = displacement[a]! + push;
        displacement[b] = displacement[b]! - push;
      }
    }
    for (final (from, to) in valid) {
      final delta = positions[from]! - positions[to]!;
      final distance = math.max(delta.distance, 1e-4);
      final pull = delta / distance * (distance * distance / ideal);
      displacement[from] = displacement[from]! - pull;
      displacement[to] = displacement[to]! + pull;
    }
    for (final node in nodes) {
      if (node == pinned) continue;
      final move = displacement[node]!;
      final length = move.distance;
      if (length == 0) continue;
      final limited = move / length * math.min(length, temperature);
      final next = positions[node]! + limited;
      positions[node] = Offset(
        next.dx.clamp(.06, .94).toDouble(),
        next.dy.clamp(.06, .94).toDouble(),
      );
    }
    temperature = math.max(.004, temperature * .97);
  }
  return positions;
}

int _hash(String value) {
  var hash = 17;
  for (final unit in value.codeUnits) {
    hash = (hash * 31 + unit) & 0x7fffffff;
  }
  return hash;
}
