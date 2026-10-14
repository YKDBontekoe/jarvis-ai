import 'dart:math' as math;
import 'dart:typed_data';
import 'dart:ui';

/// Deterministic force-directed layout (Fruchterman–Reingold).
///
/// Positions are normalized to 0..1 in both axes. [pinned] nodes start and stay at the center, which keeps the
/// user's own node as the visual anchor. Well-connected nodes push harder so hubs get room around them, a gentle
/// pull toward the center keeps stray nodes from piling up against the edges, and nodes sharing a [groups] index
/// attract each other so circles of friends, colleagues, or places read as visible clusters.
///
/// Above [exactLimit] nodes, repulsion only acts between nodes in neighbouring grid cells (the grid variant of
/// Fruchterman–Reingold), which keeps each step linear so a thousand-node graph lays out in well under a second.
Map<String, Offset> layoutGraph(
  List<String> nodes,
  List<(String, String)> edges, {
  String? pinned,
  Map<String, int> groups = const {},
  int iterations = 260,
  int exactLimit = 160,
}) {
  if (nodes.isEmpty) return const {};
  // Index-based typed arrays: string-keyed maps dominate the cost at a thousand nodes.
  final n = nodes.length;
  final indexOf = {for (final (index, node) in nodes.indexed) node: index};
  final pin = pinned == null ? -1 : (indexOf[pinned] ?? -1);
  final from = <int>[];
  final to = <int>[];
  for (final (a, b) in edges) {
    final i = indexOf[a];
    final j = indexOf[b];
    if (i == null || j == null || i == j) continue;
    from.add(i);
    to.add(j);
  }
  final degree = Int32List(n);
  for (var e = 0; e < from.length; e++) {
    degree[from[e]]++;
    degree[to[e]]++;
  }
  final group = Int32List(n)..fillRange(0, n, -1);
  var groupCount = 0;
  for (var i = 0; i < n; i++) {
    final value = groups[nodes[i]];
    if (value == null || value < 0) continue;
    group[i] = value;
    groupCount = math.max(groupCount, value + 1);
  }
  final x = Float64List(n);
  final y = Float64List(n);
  for (var i = 0; i < n; i++) {
    final hash = _hash(nodes[i]);
    final angle = (i / n) * math.pi * 2 + hash % 7 * .1;
    final radius = .18 + (hash % 100) / 600;
    x[i] = i == pin ? .5 : .5 + math.cos(angle) * radius;
    y[i] = i == pin ? .5 : .5 + math.sin(angle) * radius;
  }
  final charge = Float64List(n);
  for (var i = 0; i < n; i++) {
    charge[i] = 1 + math.sqrt(math.min(degree[i], 30).toDouble()) * .35;
  }
  final ideal = math.sqrt(1 / n) * .9;
  final ideal2 = ideal * ideal * .6;
  final dx = Float64List(n);
  final dy = Float64List(n);
  final sumX = Float64List(groupCount);
  final sumY = Float64List(groupCount);
  final sizes = Int32List(groupCount);
  final next = Int32List(n);

  var cutoff = double.infinity;
  void repel(int a, int b) {
    var ex = x[a] - x[b];
    var ey = y[a] - y[b];
    var distance = math.sqrt(ex * ex + ey * ey);
    if (distance > cutoff) return;
    if (distance < 1e-4) {
      ex = (_hash(nodes[a]) % 3 - 1) * 1e-3;
      ey = (_hash(nodes[b]) % 3 - 1) * 1e-3;
      distance = 1e-3;
    }
    final force = ideal2 / distance * charge[a] * charge[b] / distance;
    dx[a] += ex * force;
    dy[a] += ey * force;
    dx[b] -= ex * force;
    dy[b] -= ey * force;
  }

  var temperature = .1;
  for (var step = 0; step < iterations; step++) {
    dx.fillRange(0, n, 0);
    dy.fillRange(0, n, 0);
    if (n <= exactLimit) {
      for (var i = 0; i < n; i++) {
        for (var j = i + 1; j < n; j++) {
          repel(i, j);
        }
      }
    } else {
      // Bucket nodes into cells of a few ideal lengths; only neighbouring cells interact.
      var minX = double.infinity, minY = double.infinity;
      var maxX = -double.infinity, maxY = -double.infinity;
      for (var i = 0; i < n; i++) {
        minX = math.min(minX, x[i]);
        minY = math.min(minY, y[i]);
        maxX = math.max(maxX, x[i]);
        maxY = math.max(maxY, y[i]);
      }
      var cell = ideal * 2.5;
      while (((maxX - minX) / cell + 2) * ((maxY - minY) / cell + 2) > n * 4) {
        cell *= 1.5;
      }
      // A hard, round cutoff keeps the force the same in every direction, and shifting the grid each step stops nodes
      // from lining up along fixed cell borders.
      cutoff = cell;
      minX -= cell * ((step * 0.618034) % 1);
      minY -= cell * ((step * 0.414214) % 1);
      final cols = ((maxX - minX) / cell).floor() + 1;
      final rows = ((maxY - minY) / cell).floor() + 1;
      final head = Int32List(cols * rows)..fillRange(0, cols * rows, -1);
      final cellX = Int32List(n);
      final cellY = Int32List(n);
      for (var i = 0; i < n; i++) {
        final cx = ((x[i] - minX) / cell).floor();
        final cy = ((y[i] - minY) / cell).floor();
        cellX[i] = cx;
        cellY[i] = cy;
        next[i] = head[cy * cols + cx];
        head[cy * cols + cx] = i;
      }
      for (var a = 0; a < n; a++) {
        for (var oy = -1; oy <= 1; oy++) {
          final cy = cellY[a] + oy;
          if (cy < 0 || cy >= rows) continue;
          for (var ox = -1; ox <= 1; ox++) {
            final cx = cellX[a] + ox;
            if (cx < 0 || cx >= cols) continue;
            for (var b = head[cy * cols + cx]; b != -1; b = next[b]) {
              if (b > a) repel(a, b);
            }
          }
        }
      }
    }
    for (var e = 0; e < from.length; e++) {
      final a = from[e];
      final b = to[e];
      final ex = x[a] - x[b];
      final ey = y[a] - y[b];
      final distance = math.max(math.sqrt(ex * ex + ey * ey), 1e-4);
      // Hub edges are weaker so leaves fan out instead of collapsing onto the hub.
      final weight = 1 / math.sqrt(math.max(1, math.max(degree[a], degree[b])));
      final pull = distance / ideal * weight;
      dx[a] -= ex * pull;
      dy[a] -= ey * pull;
      dx[b] += ex * pull;
      dy[b] += ey * pull;
    }
    sumX.fillRange(0, groupCount, 0);
    sumY.fillRange(0, groupCount, 0);
    sizes.fillRange(0, groupCount, 0);
    for (var i = 0; i < n; i++) {
      final g = group[i];
      if (g < 0) continue;
      sumX[g] += x[i];
      sumY[g] += y[i];
      sizes[g]++;
    }
    for (var i = 0; i < n; i++) {
      if (i == pin) continue;
      final lonely = degree[i] == 0 ? 2.2 : 1.0;
      var mx = dx[i] + (.5 - x[i]) * ideal * 1.6 * lonely;
      var my = dy[i] + (.5 - y[i]) * ideal * 1.6 * lonely;
      final g = group[i];
      if (g >= 0 && sizes[g] > 1) {
        mx += (sumX[g] / sizes[g] - x[i]) * ideal * 1.3;
        my += (sumY[g] / sizes[g] - y[i]) * ideal * 1.3;
      }
      final length = math.sqrt(mx * mx + my * my);
      if (length == 0) continue;
      final scale = math.min(length, temperature) / length;
      x[i] += mx * scale;
      y[i] += my * scale;
    }
    temperature = math.max(.003, temperature * .975);
  }
  return _normalize({
    for (var i = 0; i < n; i++) nodes[i]: Offset(x[i], y[i]),
  }, pinned);
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

/// Inputs for [layoutGraphJob], so a big layout can run on a background isolate.
typedef GraphLayoutJob = ({
  List<String> nodes,
  List<(String, String)> edges,
  String? pinned,
  Map<String, int> groups,
});

Map<String, Offset> layoutGraphJob(GraphLayoutJob job) =>
    layoutGraph(job.nodes, job.edges, pinned: job.pinned, groups: job.groups);
