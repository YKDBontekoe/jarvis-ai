part of 'knowledge_graph_screen.dart';

/// Camera distance from the origin in layout units; nodes sit roughly inside the unit sphere.
const _cameraDistance = 3.2;

/// How strongly something is shown: 1 in focus, fading toward 0.
typedef _Emphasis = double Function(GraphNode node);

/// The knowledge graph as a slowly turning 3D constellation: drag to rotate, pinch to zoom, two fingers to pan, tap a
/// star to focus it. Everything is drawn in one painter so a thousand nodes stay smooth.
class _Graph3DView extends StatefulWidget {
  const _Graph3DView({
    required this.nodes,
    required this.points,
    required this.links,
    required this.degrees,
    required this.maxDegree,
    required this.selectedId,
    required this.emphasis,
    required this.highlighted,
    required this.labelPriority,
    required this.flow,
    required this.insets,
    required this.onSelect,
    required this.onClear,
    super.key,
  });

  final List<GraphNode> nodes;
  final Map<String, GraphPoint3> points;
  final List<GraphLink> links;
  final Map<String, int> degrees;
  final int maxDegree;
  final String? selectedId;
  final _Emphasis emphasis;

  /// Whether a link is part of the current focus (the selection or an answer).
  final bool Function(GraphLink link) highlighted;

  /// Lower ranks get labels first; null means "only if there is room".
  final int? Function(GraphNode node) labelPriority;
  final Animation<double> flow;

  /// Space covered by overlays, so the graph centres in what is actually visible.
  final EdgeInsets insets;
  final ValueChanged<String> onSelect;
  final VoidCallback onClear;

  @override
  State<_Graph3DView> createState() => _Graph3DViewState();
}

class _Camera {
  _Camera({
    required this.yaw,
    required this.pitch,
    required this.zoom,
    required this.pan,
  });

  double yaw;
  double pitch;
  double zoom;
  Offset pan;

  /// The point the camera orbits: the origin ("You"), or the focused node.
  GraphPoint3 target = (x: 0, y: 0, z: 0);
}

class _Projected {
  const _Projected(this.screen, this.depth, this.scale);

  final Offset screen;

  /// Toward the camera is positive.
  final double depth;

  /// Perspective factor: nearer is bigger.
  final double scale;
}

class _Graph3DViewState extends State<_Graph3DView>
    with TickerProviderStateMixin {
  final _camera = _Camera(yaw: .6, pitch: -.25, zoom: 1.1, pan: Offset.zero);
  final _repaint = ValueNotifier<int>(0);
  late final Ticker _spin;
  late final AnimationController _fly;
  Duration _lastTick = Duration.zero;
  DateTime _idleSince = DateTime.fromMillisecondsSinceEpoch(0);
  bool _interacting = false;
  double _startZoom = 1;
  Size _size = Size.zero;
  final _labels = <String, TextPainter>{};
  Brightness? _labelBrightness;

  @override
  void initState() {
    super.initState();
    _spin = createTicker(_onTick);
    _fly = AnimationController(
      vsync: this,
      duration: const Duration(milliseconds: 700),
    );
    WidgetsBinding.instance.addPostFrameCallback((_) {
      if (!mounted) return;
      _updateSpin();
      if (widget.selectedId case final id?) _flyTo(id, animate: false);
    });
  }

  @override
  void didUpdateWidget(_Graph3DView oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (widget.selectedId != oldWidget.selectedId) {
      if (widget.selectedId case final id?) {
        _flyTo(id);
      } else {
        _flyHome();
      }
    }
    if (!identical(widget.nodes, oldWidget.nodes)) _labels.clear();
    _updateSpin();
    _repaint.value++;
  }

  @override
  void dispose() {
    _spin.dispose();
    _fly.dispose();
    _repaint.dispose();
    for (final painter in _labels.values) {
      painter.dispose();
    }
    super.dispose();
  }

  /// Turns slowly while nothing is focused and nobody is touching it.
  bool get _shouldSpin =>
      widget.selectedId == null &&
      !MediaQuery.disableAnimationsOf(context) &&
      !_interacting;

  void _updateSpin() {
    if (!mounted) return;
    if (_shouldSpin && !_spin.isActive) {
      _lastTick = Duration.zero;
      unawaited(_spin.start());
    } else if (!_shouldSpin && _spin.isActive) {
      _spin.stop();
    }
  }

  void _onTick(Duration elapsed) {
    final delta = _lastTick == Duration.zero
        ? Duration.zero
        : elapsed - _lastTick;
    _lastTick = elapsed;
    if (DateTime.now().difference(_idleSince) < const Duration(seconds: 3)) {
      return;
    }
    _camera.yaw += delta.inMicroseconds / 1e6 * .14;
    _repaint.value++;
  }

  /// Glides the orbit centre to [id] and zooms in, so dragging then turns the graph around that node.
  void _flyTo(String id, {bool animate = true}) {
    final point = widget.points[id];
    if (point == null) return;
    _glide(point, math.max(_camera.zoom, 1.6), animate: animate);
  }

  /// Back to orbiting the whole graph.
  void _flyHome() => _glide((x: 0, y: 0, z: 0), 1.1);

  void _glide(GraphPoint3 target, double zoom, {bool animate = true}) {
    final from = (target: _camera.target, zoom: _camera.zoom, pan: _camera.pan);
    void apply(double t) {
      final eased = Curves.easeInOutCubic.transform(t);
      _camera
        ..target = (
          x: from.target.x + (target.x - from.target.x) * eased,
          y: from.target.y + (target.y - from.target.y) * eased,
          z: from.target.z + (target.z - from.target.z) * eased,
        )
        ..zoom = from.zoom + (zoom - from.zoom) * eased
        ..pan = Offset.lerp(from.pan, Offset.zero, eased)!;
      _repaint.value++;
    }

    if (!animate || MediaQuery.disableAnimationsOf(context)) {
      apply(1);
      return;
    }
    _fly
      ..stop()
      ..reset();
    void listener() => apply(_fly.value);
    _fly.addListener(listener);
    unawaited(_fly.forward().whenComplete(() => _fly.removeListener(listener)));
  }

  _Projected _project(GraphPoint3 point) {
    final target = _camera.target;
    final px = point.x - target.x;
    final py = point.y - target.y;
    final pz = point.z - target.z;
    final cy = math.cos(_camera.yaw), sy = math.sin(_camera.yaw);
    final x1 = px * cy + pz * sy;
    final z1 = -px * sy + pz * cy;
    final cp = math.cos(_camera.pitch), sp = math.sin(_camera.pitch);
    final y2 = py * cp - z1 * sp;
    final z2 = py * sp + z1 * cp;
    final perspective = _cameraDistance / math.max(.4, _cameraDistance - z2);
    final insets = widget.insets;
    final visible = Rect.fromLTRB(
      insets.left,
      insets.top,
      _size.width - insets.right,
      _size.height - insets.bottom,
    );
    final radius = math.min(visible.width, visible.height) * .44 * _camera.zoom;
    return _Projected(
      visible.center + _camera.pan + Offset(x1, y2) * radius * perspective,
      z2,
      perspective,
    );
  }

  double _radius(GraphNode node, _Projected projected) {
    final density = math.sqrt(120 / math.max(120, widget.nodes.length));
    if (node.isYou) return 11 * projected.scale * math.sqrt(_camera.zoom);
    final degree = widget.degrees[node.id] ?? 0;
    final weight = math.sqrt(degree / widget.maxDegree).clamp(0.0, 1.0);
    return (2.6 + weight * 7.5) *
        projected.scale *
        math.sqrt(_camera.zoom) *
        density.clamp(.5, 1.0);
  }

  void _onTap(Offset position) {
    String? best;
    var bestScore = double.infinity;
    for (final node in widget.nodes) {
      final point = widget.points[node.id];
      if (point == null) continue;
      final projected = _project(point);
      final distance = (projected.screen - position).distance;
      final reach = _radius(node, projected) + 12;
      if (distance > reach) continue;
      // Prefer the nearer star when two overlap on screen.
      final score = distance - projected.depth * 6;
      if (score < bestScore) {
        bestScore = score;
        best = node.id;
      }
    }
    if (best == null) {
      widget.onClear();
    } else {
      widget.onSelect(best);
    }
  }

  TextPainter _label(GraphNode node, JarvisColors colors, bool strong) {
    if (_labelBrightness != colors.brightness) {
      _labels.clear();
      _labelBrightness = colors.brightness;
    }
    final key = '${node.id}|$strong';
    return _labels[key] ??= TextPainter(
      text: TextSpan(
        text: node.name,
        style: TextStyle(
          fontFamily: 'Geist',
          fontSize: strong ? 12 : 11,
          fontWeight: strong ? FontWeight.w700 : FontWeight.w600,
          color: colors.ink,
        ),
      ),
      maxLines: 1,
      ellipsis: '…',
      textDirection: TextDirection.ltr,
    )..layout(maxWidth: 120);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return LayoutBuilder(
      builder: (context, constraints) {
        _size = constraints.biggest;
        return GestureDetector(
          key: const Key('graph-3d'),
          behavior: HitTestBehavior.opaque,
          onTapUp: (details) => _onTap(details.localPosition),
          onScaleStart: (details) {
            _interacting = true;
            _fly.stop();
            _startZoom = _camera.zoom;
            _updateSpin();
          },
          onScaleUpdate: (details) {
            if (details.pointerCount >= 2) {
              _camera
                ..zoom = (_startZoom * details.scale).clamp(.45, 5.0)
                ..pan += details.focalPointDelta;
            } else {
              _camera
                ..yaw += details.focalPointDelta.dx * .008
                ..pitch = (_camera.pitch - details.focalPointDelta.dy * .008)
                    .clamp(-1.4, 1.4);
            }
            _repaint.value++;
          },
          onScaleEnd: (_) {
            _interacting = false;
            _idleSince = DateTime.now();
            _updateSpin();
          },
          child: Semantics(
            label: 'Knowledge graph in 3D. Drag to rotate, pinch to zoom.',
            child: CustomPaint(
              size: Size.infinite,
              painter: _Graph3DPainter(
                state: this,
                colors: colors,
                repaint: Listenable.merge([_repaint, widget.flow]),
              ),
            ),
          ),
        );
      },
    );
  }

  /// Zoom controls for the floating buttons.
  void zoomBy(double factor) {
    _camera.zoom = (_camera.zoom * factor).clamp(.45, 5.0);
    _idleSince = DateTime.now();
    _repaint.value++;
  }

  void reset() {
    _camera
      ..zoom = 1.1
      ..pan = Offset.zero
      ..target = (x: 0, y: 0, z: 0)
      ..pitch = -.25;
    _idleSince = DateTime.fromMillisecondsSinceEpoch(0);
    _repaint.value++;
  }
}

class _Graph3DPainter extends CustomPainter {
  _Graph3DPainter({
    required this.state,
    required this.colors,
    required Listenable repaint,
  }) : super(repaint: repaint);

  final _Graph3DViewState state;
  final JarvisColors colors;

  @override
  void paint(Canvas canvas, Size size) {
    final view = state.widget;
    state._size = size;
    final center = Rect.fromLTRB(
      view.insets.left,
      view.insets.top,
      size.width - view.insets.right,
      size.height - view.insets.bottom,
    ).center;
    // A faint glow behind the centre gives the space some depth.
    canvas.drawCircle(
      center + state._camera.pan,
      math.min(size.width, size.height) * .55 * state._camera.zoom,
      Paint()
        ..shader = ui.Gradient.radial(
          center + state._camera.pan,
          math.min(size.width, size.height) * .55 * state._camera.zoom,
          [
            colors.accent.withValues(alpha: colors.isDark ? .14 : .08),
            colors.accent.withValues(alpha: 0),
          ],
        ),
    );

    final projected = <String, _Projected>{};
    for (final node in view.nodes) {
      final point = view.points[node.id];
      if (point != null) projected[node.id] = state._project(point);
    }
    double fog(double depth) => (.55 + depth * .35).clamp(.18, 1.0);

    // Plain edges in a few alpha bands, one path each, so thousands of edges cost a handful of draw calls.
    const bands = 4;
    final paths = List.generate(bands, (_) => Path());
    final focus = <GraphLink>[];
    for (final link in view.links) {
      final a = projected[link.fromId];
      final b = projected[link.toId];
      if (a == null || b == null) continue;
      if (view.highlighted(link)) {
        focus.add(link);
        continue;
      }
      final from = view.nodes.isEmpty ? null : _nodeOf(link.fromId);
      final to = _nodeOf(link.toId);
      if (from == null || to == null) continue;
      final emphasis = math.min(view.emphasis(from), view.emphasis(to));
      final alpha = fog((a.depth + b.depth) / 2) * emphasis;
      final band = (alpha * bands).floor().clamp(0, bands - 1);
      paths[band]
        ..moveTo(a.screen.dx, a.screen.dy)
        ..lineTo(b.screen.dx, b.screen.dy);
    }
    for (var band = 0; band < bands; band++) {
      canvas.drawPath(
        paths[band],
        Paint()
          ..style = PaintingStyle.stroke
          ..strokeWidth = .8
          ..color = colors.inkSoft.withValues(
            alpha: (colors.isDark ? .34 : .4) * (band + 1) / bands,
          ),
      );
    }

    final flowing = view.flow.value < 1;
    for (final link in focus) {
      final a = projected[link.fromId]!;
      final b = projected[link.toId]!;
      final fromColor = graphTypeColor(
        _nodeOf(link.fromId)?.type,
        null,
        colors,
      );
      final toColor = graphTypeColor(_nodeOf(link.toId)?.type, null, colors);
      canvas.drawLine(
        a.screen,
        b.screen,
        Paint()
          ..strokeWidth = 2.2
          ..strokeCap = StrokeCap.round
          ..shader = ui.Gradient.linear(a.screen, b.screen, [
            fromColor.withValues(alpha: .95),
            toColor.withValues(alpha: .95),
          ]),
      );
      if (flowing) {
        final fade = 1 - Curves.easeIn.transform(view.flow.value);
        for (var index = 0; index < 3; index++) {
          final t = (view.flow.value * 2.4 + index / 3) % 1;
          canvas.drawCircle(
            Offset.lerp(a.screen, b.screen, t)!,
            2.6,
            Paint()..color = toColor.withValues(alpha: .9 * fade),
          );
        }
      }
    }

    // Far to near, so nearer stars cover farther ones.
    final order = [
      for (final node in view.nodes)
        if (projected.containsKey(node.id)) node,
    ]..sort((a, b) => projected[a.id]!.depth.compareTo(projected[b.id]!.depth));
    final fill = Paint();
    final shine = Paint();
    final rim = Paint()
      ..style = PaintingStyle.stroke
      ..strokeWidth = 1;
    for (final node in order) {
      final p = projected[node.id]!;
      final radius = state._radius(node, p);
      final alpha = fog(p.depth) * view.emphasis(node);
      final color = graphTypeColor(node.type, null, colors);
      final selected = node.id == view.selectedId;
      if (selected || node.isYou) {
        canvas.drawCircle(
          p.screen,
          radius * 2.4,
          Paint()
            ..color = color.withValues(alpha: .22 * alpha)
            ..maskFilter = const MaskFilter.blur(BlurStyle.normal, 8),
        );
      }
      if (selected && flowing) {
        final t = (view.flow.value * 3) % 1;
        canvas.drawCircle(
          p.screen,
          radius * (1.4 + t * 2),
          Paint()
            ..style = PaintingStyle.stroke
            ..strokeWidth = 2
            ..color = color.withValues(
              alpha: (1 - t) * .5 * (1 - view.flow.value),
            ),
        );
      }
      fill.color = color.withValues(alpha: alpha);
      canvas.drawCircle(p.screen, radius, fill);
      if (radius > 3) {
        // A soft highlight up and to the left makes each dot read as a sphere.
        shine.color = Colors.white.withValues(alpha: .38 * alpha);
        canvas.drawCircle(
          p.screen + Offset(-radius * .32, -radius * .36),
          radius * .42,
          shine,
        );
      }
      if (selected) {
        rim
          ..color = colors.surface.withValues(alpha: alpha)
          ..strokeWidth = 2;
        canvas.drawCircle(p.screen, radius + 1.5, rim);
      }
    }

    _paintLabels(canvas, size, projected);
  }

  GraphNode? _nodeOf(String id) => state._nodeIndex[id];

  void _paintLabels(
    Canvas canvas,
    Size size,
    Map<String, _Projected> projected,
  ) {
    final view = state.widget;
    final candidates = <(GraphNode, double)>[];
    for (final node in view.nodes) {
      final p = projected[node.id];
      if (p == null || view.emphasis(node) < .5) continue;
      if (p.screen.dx < -40 ||
          p.screen.dy < -20 ||
          p.screen.dx > size.width + 40 ||
          p.screen.dy > size.height + 20) {
        continue;
      }
      final priority = view.labelPriority(node);
      final degree = view.degrees[node.id] ?? 0;
      // Forced labels first, then big, near stars.
      final score = priority != null
          ? -1000.0 + priority
          : -(degree + 1) * p.scale * (1 + p.depth);
      candidates.add((node, score));
    }
    candidates.sort((a, b) => a.$2.compareTo(b.$2));
    // Forced labels (the focus, an answer, "You") are placed first so the rest avoid them, and painted last so they
    // stay on top; everything else only gets a label where there is room.
    final occupied = <Rect>[];
    final placed = <(Rect, TextPainter, double, bool)>[];
    var optional = 0;
    final budget = (14 * state._camera.zoom).round().clamp(8, 40);
    for (final (node, score) in candidates.take(300)) {
      final forced = score < -998.5;
      if (!forced && optional >= budget) break;
      final p = projected[node.id]!;
      final painter = state._label(node, colors, forced);
      final top = p.screen.dy + state._radius(node, p) + 4;
      final rect = Rect.fromLTWH(
        p.screen.dx - painter.width / 2 - 6,
        top,
        painter.width + 12,
        painter.height + 4,
      );
      if (!forced && occupied.any(rect.inflate(2).overlaps)) continue;
      if (!forced) optional++;
      occupied.add(rect);
      final alpha = forced ? 1.0 : (.55 + p.depth * .3).clamp(.45, 1.0);
      placed.add((rect, painter, alpha, forced));
    }
    placed.sort((a, b) => (a.$4 ? 1 : 0).compareTo(b.$4 ? 1 : 0));
    for (final (rect, painter, alpha, _) in placed) {
      canvas.drawRRect(
        RRect.fromRectAndRadius(rect, const Radius.circular(6)),
        Paint()..color = colors.surface.withValues(alpha: .86 * alpha),
      );
      canvas.saveLayer(
        rect,
        Paint()..color = Colors.black.withValues(alpha: alpha),
      );
      painter.paint(canvas, Offset(rect.left + 6, rect.top + 2));
      canvas.restore();
    }
  }

  @override
  bool shouldRepaint(_Graph3DPainter oldDelegate) =>
      oldDelegate.state != state || oldDelegate.colors != colors;
}

extension on _Graph3DViewState {
  Map<String, GraphNode> get _nodeIndex => _cachedIndex(widget.nodes);
}

final _graph3dIndex = Expando<Map<String, GraphNode>>('graph3dIndex');

Map<String, GraphNode> _cachedIndex(List<GraphNode> nodes) =>
    _graph3dIndex[nodes] ??= {for (final node in nodes) node.id: node};
