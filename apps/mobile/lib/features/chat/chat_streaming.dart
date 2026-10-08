part of 'chat_widgets.dart';

/// A reply that is still being written. Text that arrives in bursts is let
/// out a few words at a time, and each new group of words fades in over
/// the words already shown, so the reply flows instead of jumping.
class StreamingMarkdown extends StatefulWidget {
  const StreamingMarkdown({required this.data, super.key});

  final String data;

  /// How long each group of words takes to fade in.
  static const step = Duration(milliseconds: 140);

  /// Next reveal point: [words] more words after [from] in [text], or the
  /// end of the text.
  @visibleForTesting
  static int advance(String text, int from, int words) {
    var index = from;
    var seen = 0;
    // Skip leading whitespace, then take whole words with their trailing space.
    while (index < text.length && text[index].trim().isEmpty) {
      index++;
    }
    while (index < text.length && seen < words) {
      while (index < text.length && text[index].trim().isNotEmpty) {
        index++;
      }
      while (index < text.length && text[index].trim().isEmpty) {
        index++;
      }
      seen++;
    }
    return index;
  }

  /// Words in [text] after [from].
  static int wordsAfter(String text, int from) => from >= text.length
      ? 0
      : text
            .substring(from)
            .split(RegExp(r'\s+'))
            .where((word) => word.isNotEmpty)
            .length;

  @override
  State<StreamingMarkdown> createState() => _StreamingMarkdownState();
}

class _StreamingMarkdownState extends State<StreamingMarkdown>
    with SingleTickerProviderStateMixin {
  late final AnimationController _fade = AnimationController(
    vsync: this,
    duration: StreamingMarkdown.step,
  )..addStatusListener(_stepDone);

  /// Fully shown text.
  late String _settled = widget.data;

  /// Text shown, including the group still fading in.
  late String _shown = widget.data;

  @override
  void didUpdateWidget(StreamingMarkdown oldWidget) {
    super.didUpdateWidget(oldWidget);
    final target = widget.data;
    if (JarvisMotion.reduced(context) || !target.startsWith(_shown)) {
      // Reduced motion, or the text was rewritten: show it as it is.
      _fade.stop();
      _settled = _shown = target;
      return;
    }
    if (!_fade.isAnimating) _next();
  }

  void _stepDone(AnimationStatus status) {
    if (status != AnimationStatus.completed || !mounted) return;
    setState(() => _settled = _shown);
    _next();
  }

  void _next() {
    final target = widget.data;
    if (_shown.length >= target.length) return;
    // Catch up within about three steps however far behind the text is.
    final backlog = StreamingMarkdown.wordsAfter(target, _shown.length);
    final words = math.max(1, (backlog / 3).ceil());
    final end = StreamingMarkdown.advance(target, _shown.length, words);
    setState(() {
      _settled = _shown;
      _shown = target.substring(0, end);
    });
    _fade.forward(from: 0);
  }

  @override
  void dispose() {
    _fade.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    if (_settled == _shown) return JarvisMarkdown(data: _shown);
    // The new words fade in on a layer above the words already settled; the
    // settled text is drawn on both layers, so it stays solid throughout.
    return Stack(
      children: [
        ExcludeSemantics(child: JarvisMarkdown(data: _settled)),
        FadeTransition(
          opacity: CurvedAnimation(parent: _fade, curve: Curves.easeOut),
          child: JarvisMarkdown(data: _shown),
        ),
      ],
    );
  }
}
