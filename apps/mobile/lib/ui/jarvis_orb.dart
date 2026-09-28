part of 'jarvis_ui.dart';

/// The Jarvis mark: an iridescent sphere. It is static unless [animate] is
/// set, so screens that settle (and widget tests using pumpAndSettle) stay idle.
class JarvisOrb extends StatefulWidget {
  const JarvisOrb({
    required this.size,
    this.animate = false,
    this.listening = false,
    this.glow = true,
    this.semanticLabel,
    super.key,
  });

  final double size;
  final bool animate;

  /// Announced as an image when set; otherwise the orb is decorative.
  final String? semanticLabel;

  /// Adds expanding halo rings; implies [animate].
  final bool listening;
  final bool glow;

  @override
  State<JarvisOrb> createState() => _JarvisOrbState();
}

class _JarvisOrbState extends State<JarvisOrb>
    with SingleTickerProviderStateMixin {
  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: const Duration(seconds: 6),
  );

  bool get _running => widget.animate || widget.listening;

  @override
  void initState() {
    super.initState();
    if (_running) _controller.repeat();
  }

  @override
  void didUpdateWidget(JarvisOrb oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (_running && !_controller.isAnimating) {
      _controller.repeat();
    } else if (!_running && _controller.isAnimating) {
      _controller.stop();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final size = widget.size;
    final extent = widget.listening ? size * 1.9 : size;
    final orb = ExcludeSemantics(
      child: SizedBox.square(
        dimension: extent,
        child: AnimatedBuilder(
          animation: _controller,
          builder: (context, _) {
            final t = _controller.value;
            final breathe = _running
                ? 1 + .035 * math.sin(t * math.pi * 4)
                : 1.0;
            return Stack(
              alignment: Alignment.center,
              children: [
                if (widget.listening)
                  for (var i = 0; i < 3; i++) _halo(size, (t * 2 + i / 3) % 1),
                Transform.scale(
                  scale: breathe,
                  child: _sphere(size, t * math.pi * 2),
                ),
              ],
            );
          },
        ),
      ),
    );
    final label = widget.semanticLabel;
    if (label == null) return orb;
    return Semantics(container: true, image: true, label: label, child: orb);
  }

  Widget _halo(double size, double progress) => IgnorePointer(
    child: Container(
      width: size * (1 + .9 * progress),
      height: size * (1 + .9 * progress),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(
          color: JarvisColors.accent.withValues(alpha: .28 * (1 - progress)),
          width: 1.5,
        ),
        color: JarvisColors.violet.withValues(alpha: .06 * (1 - progress)),
      ),
    ),
  );

  Widget _sphere(double size, double rotation) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      boxShadow: widget.glow
          ? [
              BoxShadow(
                color: JarvisColors.accent.withValues(alpha: .16),
                blurRadius: size * .4,
                offset: Offset(0, size * .14),
              ),
            ]
          : null,
    ),
    child: ClipOval(
      child: Stack(
        fit: StackFit.expand,
        children: [
          ImageFiltered(
            imageFilter: ui.ImageFilter.blur(
              sigmaX: size * .08,
              sigmaY: size * .08,
              tileMode: TileMode.mirror,
            ),
            child: DecoratedBox(
              decoration: BoxDecoration(
                gradient: SweepGradient(
                  transform: GradientRotation(rotation),
                  colors: const [
                    Color(0xff5a52e6),
                    Color(0xff9a8cf5),
                    Color(0xffe9bfe0),
                    Color(0xff9ccdf2),
                    Color(0xff6c63ea),
                    Color(0xff5a52e6),
                  ],
                ),
              ),
            ),
          ),
          const DecoratedBox(
            decoration: BoxDecoration(
              gradient: RadialGradient(
                center: Alignment(-.35, -.5),
                radius: .75,
                colors: [
                  Color(0xd9ffffff),
                  Color(0x33ffffff),
                  Color(0x00ffffff),
                ],
                stops: [0, .45, 1],
              ),
            ),
          ),
          DecoratedBox(
            decoration: BoxDecoration(
              shape: BoxShape.circle,
              border: Border.all(
                color: Colors.white.withValues(alpha: .55),
                width: math.max(1, size * .02),
              ),
            ),
          ),
        ],
      ),
    ),
  );
}
