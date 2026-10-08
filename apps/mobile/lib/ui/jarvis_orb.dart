part of 'jarvis_ui.dart';

/// The Jarvis mark: an iridescent sphere. It is static unless [animate],
/// [listening], [thinking] or [level] is set, so screens that settle (and
/// widget tests using pumpAndSettle) stay idle.
class JarvisOrb extends StatefulWidget {
  const JarvisOrb({
    required this.size,
    this.animate = false,
    this.listening = false,
    this.thinking = false,
    this.level,
    this.pulse,
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

  /// Jarvis is working: the orb turns faster inside a sweeping ring of light.
  final bool thinking;

  /// Reads the live audio level (0 to 1) every frame; the orb swells and
  /// glows with it. Implies [animate].
  final ValueGetter<double>? level;

  /// Each new value blooms the orb once, such as when a reply has landed.
  final Object? pulse;
  final bool glow;

  @override
  State<JarvisOrb> createState() => _JarvisOrbState();
}

class _JarvisOrbState extends State<JarvisOrb> with TickerProviderStateMixin {
  static const _calm = Duration(seconds: 6);
  static const _busy = Duration(milliseconds: 2400);

  late final AnimationController _controller = AnimationController(
    vsync: this,
    duration: _calm,
  );

  /// One-shot bloom for [JarvisOrb.pulse].
  late final AnimationController _bloom = AnimationController(
    vsync: this,
    duration: const Duration(milliseconds: 900),
  );

  bool _reducedMotion = false;

  /// Smoothed [JarvisOrb.level], so the orb glides rather than jitters.
  double _level = 0;

  bool get _running =>
      !_reducedMotion &&
      (widget.animate ||
          widget.listening ||
          widget.thinking ||
          widget.level != null);

  @override
  void didChangeDependencies() {
    super.didChangeDependencies();
    _reducedMotion = JarvisMotion.reduced(context);
    _syncAnimation();
  }

  @override
  void didUpdateWidget(JarvisOrb oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.pulse != widget.pulse &&
        widget.pulse != null &&
        !_reducedMotion) {
      _bloom.forward(from: 0);
    }
    _syncAnimation();
  }

  void _syncAnimation() {
    final duration = widget.thinking ? _busy : _calm;
    if (_controller.duration != duration) {
      _controller.duration = duration;
      if (_controller.isAnimating) _controller.repeat();
    }
    if (_running && !_controller.isAnimating) {
      _controller.repeat();
    } else if (!_running && _controller.isAnimating) {
      _controller.stop();
    }
  }

  @override
  void dispose() {
    _controller.dispose();
    _bloom.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final size = widget.size;
    final extent = widget.listening || widget.level != null
        ? size * 1.9
        : widget.thinking
        ? size * 1.3
        : size;
    final orb = ExcludeSemantics(
      child: SizedBox.square(
        dimension: extent,
        child: AnimatedBuilder(
          animation: Listenable.merge([_controller, _bloom]),
          builder: (context, _) {
            final t = _controller.value;
            final sample = _running ? (widget.level?.call() ?? 0) : 0.0;
            // Rise quickly with the voice, fall back gently.
            _level +=
                (sample.clamp(0, 1) - _level) * (sample > _level ? .5 : .12);
            final bloom = Curves.easeOutCubic.transform(_bloom.value);
            final bloomFade = _bloom.isAnimating ? 1 - bloom : 0.0;
            final breathe = _running
                ? 1 + (widget.thinking ? .05 : .035) * math.sin(t * math.pi * 4)
                : 1.0;
            final scale =
                breathe *
                (1 + .22 * _level) *
                (1 + .08 * math.sin(bloom * math.pi));
            return Stack(
              alignment: Alignment.center,
              children: [
                if (widget.listening)
                  for (var i = 0; i < 3; i++) _halo(size, (t * 2 + i / 3) % 1),
                if (widget.level != null && _level > .02)
                  _voiceGlow(size, _level),
                if (widget.thinking && _running) _thinkingRing(size, t),
                if (bloomFade > 0) _bloomRing(size, bloom, bloomFade),
                Transform.scale(
                  scale: scale,
                  child: _sphere(
                    size,
                    t * math.pi * 2 * (widget.thinking ? 2 : 1),
                  ),
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

  /// A comet of light circling the orb while Jarvis thinks.
  Widget _thinkingRing(double size, double t) {
    final colors = JarvisColors.of(context);
    return IgnorePointer(
      child: CustomPaint(
        size: Size.square(size * 1.22),
        painter: _CometPainter(
          turn: t,
          head: colors.sky,
          tail: colors.violet,
          width: math.max(1.5, size * .045),
        ),
      ),
    );
  }

  Widget _voiceGlow(double size, double level) {
    final colors = JarvisColors.of(context);
    final extent = size * (1.15 + .7 * level);
    return IgnorePointer(
      child: Container(
        width: extent,
        height: extent,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: [
              colors.violet.withValues(alpha: .35 * level),
              colors.sky.withValues(alpha: .18 * level),
              colors.accent.withValues(alpha: 0),
            ],
            stops: const [.45, .7, 1],
          ),
        ),
      ),
    );
  }

  Widget _bloomRing(double size, double progress, double fade) {
    final colors = JarvisColors.of(context);
    final extent = size * (1 + .6 * progress);
    return IgnorePointer(
      child: Container(
        width: extent,
        height: extent,
        decoration: BoxDecoration(
          shape: BoxShape.circle,
          gradient: RadialGradient(
            colors: [
              colors.accent.withValues(alpha: .28 * fade),
              colors.violet.withValues(alpha: .12 * fade),
              colors.violet.withValues(alpha: 0),
            ],
          ),
        ),
      ),
    );
  }

  Widget _halo(double size, double progress) => IgnorePointer(
    child: Container(
      width: size * (1 + .9 * progress),
      height: size * (1 + .9 * progress),
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        border: Border.all(
          color: JarvisColors.of(
            context,
          ).accent.withValues(alpha: .28 * (1 - progress)),
          width: 1.5,
        ),
        color: JarvisColors.of(
          context,
        ).violet.withValues(alpha: .06 * (1 - progress)),
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
                color: JarvisColors.of(context).accent.withValues(alpha: .16),
                blurRadius: size * .4,
                offset: Offset(0, size * .14),
              ),
            ]
          : null,
    ),
    child: Transform.rotate(
      angle: _running ? .06 * math.sin(rotation) : 0,
      child: Image.asset(
        'assets/brand/jarvis-orb-v1.png',
        fit: BoxFit.contain,
        filterQuality: FilterQuality.high,
        cacheWidth: (size * MediaQuery.devicePixelRatioOf(context)).ceil(),
        // Keep the original mark available if the image cannot be decoded.
        errorBuilder: (context, error, trace) =>
            _fallbackSphere(size, rotation),
      ),
    ),
  );

  Widget _fallbackSphere(double size, double rotation) => ClipOval(
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
              colors: [Color(0xd9ffffff), Color(0x33ffffff), Color(0x00ffffff)],
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
  );
}

class _CometPainter extends CustomPainter {
  _CometPainter({
    required this.turn,
    required this.head,
    required this.tail,
    required this.width,
  });

  final double turn;
  final Color head;
  final Color tail;
  final double width;

  @override
  void paint(Canvas canvas, Size size) {
    final rect = (Offset.zero & size).deflate(width);
    final paint = Paint()
      ..style = PaintingStyle.stroke
      ..strokeCap = StrokeCap.round
      ..strokeWidth = width
      ..shader = SweepGradient(
        transform: GradientRotation(turn * math.pi * 2),
        colors: [
          tail.withValues(alpha: 0),
          tail.withValues(alpha: .25),
          head.withValues(alpha: .9),
        ],
        stops: const [0, .5, .8],
      ).createShader(rect);
    canvas.drawArc(rect, turn * math.pi * 2, math.pi * 1.6, false, paint);
  }

  @override
  bool shouldRepaint(_CometPainter old) => old.turn != turn;
}
