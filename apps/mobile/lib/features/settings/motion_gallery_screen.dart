import 'dart:async';
import 'dart:math' as math;

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/chat_widgets.dart' show StreamingMarkdown;

/// Every effect Jarvis uses, on one page, so they can be tried (and kept
/// consistent). Nothing here talks to the server.
class MotionGalleryScreen extends StatefulWidget {
  const MotionGalleryScreen({super.key});

  @override
  State<MotionGalleryScreen> createState() => _MotionGalleryScreenState();
}

enum _OrbMode { calm, thinking, listening, voice }

class _MotionGalleryScreenState extends State<MotionGalleryScreen> {
  _OrbMode _orb = _OrbMode.calm;
  double _voice = .5;
  int _bloom = 0;
  int _replay = 0;
  bool _done = false;
  int _number = 128;
  int _page = 0;
  String _reply = '';
  Timer? _stream;
  final _random = math.Random();

  static const _story =
      'Your Friday is clear after four. Dinner with Sanne is at 19:45 at '
      'Café Loetje, and it is a twelve minute walk, so leave by half past '
      'seven. I moved the dentist reminder to Monday morning.';

  @override
  void dispose() {
    _stream?.cancel();
    super.dispose();
  }

  void _streamReply() {
    _stream?.cancel();
    final words = _story.split(' ');
    var shown = 0;
    setState(() => _reply = '');
    // Text arrives in uneven bursts, as it does from the server.
    _stream = Timer.periodic(const Duration(milliseconds: 260), (timer) {
      shown = math.min(words.length, shown + 1 + _random.nextInt(5));
      setState(() => _reply = words.take(shown).join(' '));
      if (shown >= words.length) timer.cancel();
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final reduced = JarvisMotion.reduced(context);
    Widget section(String title, String note, Widget child) => Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(title, style: Theme.of(context).textTheme.titleMedium),
            const SizedBox(height: 4),
            Text(note, style: Theme.of(context).textTheme.bodySmall),
            const SizedBox(height: 16),
            child,
          ],
        ),
      ),
    );

    return Scaffold(
      appBar: AppBar(title: const Text('Preview effects')),
      body: SafeArea(
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
          children: [
            if (reduced)
              const InlineNotice(
                message:
                    'Motion is reduced, so effects show in place. Change it '
                    'under Appearance → Motion.',
                margin: EdgeInsets.only(bottom: 16),
              ),
            section(
              'The orb',
              'Calm, thinking, listening, or following a voice. Bloom plays '
                  'when a reply lands.',
              Column(
                children: [
                  SizedBox(
                    height: 150,
                    child: Center(
                      child: JarvisOrb(
                        size: 72,
                        animate: _orb == _OrbMode.calm,
                        thinking: _orb == _OrbMode.thinking,
                        listening: _orb == _OrbMode.listening,
                        level: _orb == _OrbMode.voice ? () => _voice : null,
                        pulse: _bloom == 0 ? null : _bloom,
                      ),
                    ),
                  ),
                  SegmentedButton<_OrbMode>(
                    segments: const [
                      ButtonSegment(value: _OrbMode.calm, label: Text('Calm')),
                      ButtonSegment(
                        value: _OrbMode.thinking,
                        label: Text('Thinking'),
                      ),
                      ButtonSegment(
                        value: _OrbMode.listening,
                        label: Text('Listen'),
                      ),
                      ButtonSegment(
                        value: _OrbMode.voice,
                        label: Text('Voice'),
                      ),
                    ],
                    selected: {_orb},
                    showSelectedIcon: false,
                    onSelectionChanged: (value) =>
                        setState(() => _orb = value.single),
                  ),
                  if (_orb == _OrbMode.voice)
                    Slider(
                      value: _voice,
                      onChanged: (value) => setState(() => _voice = value),
                    ),
                  const SizedBox(height: 8),
                  TextButton.icon(
                    onPressed: () => setState(() => _bloom++),
                    icon: const Icon(PhosphorIconsRegular.sparkle, size: 16),
                    label: const Text('Bloom'),
                  ),
                ],
              ),
            ),
            section(
              'Arrivals',
              'Content blurs into focus, pops in on a spring, and catches '
                  'the light.',
              Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  KeyedSubtree(
                    key: ValueKey(_replay),
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        BlurIn(
                          child: Text(
                            'Good evening',
                            style: JarvisType.displayOf(
                              context,
                            ).copyWith(fontSize: 30),
                          ),
                        ),
                        const SizedBox(height: 12),
                        Row(
                          children: [
                            for (var i = 0; i < 4; i++)
                              Padding(
                                padding: const EdgeInsets.only(right: 10),
                                child: PopIn(
                                  delay: Duration(milliseconds: 90 * i),
                                  child: IconBadge(
                                    icon: const [
                                      PhosphorIconsRegular.bell,
                                      PhosphorIconsRegular.calendarCheck,
                                      PhosphorIconsRegular.checkCircle,
                                      PhosphorIconsRegular.sparkle,
                                    ][i],
                                  ),
                                ),
                              ),
                          ],
                        ),
                        const SizedBox(height: 12),
                        Sheen(
                          borderRadius: BorderRadius.circular(JarvisRadii.lg),
                          child: Container(
                            height: 64,
                            decoration: BoxDecoration(
                              gradient: colors.litSurface(colors.accent),
                              borderRadius: BorderRadius.circular(
                                JarvisRadii.lg,
                              ),
                            ),
                          ),
                        ),
                      ],
                    ),
                  ),
                  const SizedBox(height: 8),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      key: const Key('gallery-replay'),
                      onPressed: () => setState(() => _replay++),
                      icon: const Icon(PhosphorIconsRegular.play, size: 16),
                      label: const Text('Play again'),
                    ),
                  ),
                ],
              ),
            ),
            section(
              'Finishing things',
              'Checks spring in with confetti; numbers roll to their value.',
              Row(
                children: [
                  CelebrationBurst(
                    trigger: _done,
                    child: IconButton.filled(
                      key: const Key('gallery-check'),
                      onPressed: () => setState(() => _done = !_done),
                      style: IconButton.styleFrom(
                        backgroundColor: _done
                            ? colors.success
                            : colors.surfaceMuted,
                        foregroundColor: _done ? Colors.white : colors.muted,
                        minimumSize: const Size(52, 52),
                      ),
                      icon: const Icon(PhosphorIconsRegular.check),
                    ),
                  ),
                  const SizedBox(width: 24),
                  Expanded(
                    child: RollingNumber(
                      _formatThousands(_number),
                      style: JarvisType.displayOf(
                        context,
                      ).copyWith(fontSize: 34),
                    ),
                  ),
                  TextButton(
                    onPressed: () =>
                        setState(() => _number = _random.nextInt(20000)),
                    child: const Text('Roll'),
                  ),
                ],
              ),
            ),
            section(
              'Touch',
              'Cards tilt toward your finger while you hold them.',
              TiltOnPress(
                child: SurfaceCard(
                  elevated: true,
                  onTap: () {},
                  gradient: colors.litSurface(colors.violet, strength: .2),
                  child: const SizedBox(
                    height: 96,
                    child: Center(child: Text('Press and drag')),
                  ),
                ),
              ),
            ),
            section(
              'Pages',
              'Sibling pages slide toward the side you move to.',
              Column(
                children: [
                  SegmentedButton<int>(
                    segments: const [
                      ButtonSegment(value: 0, label: Text('One')),
                      ButtonSegment(value: 1, label: Text('Two')),
                      ButtonSegment(value: 2, label: Text('Three')),
                    ],
                    selected: {_page},
                    showSelectedIcon: false,
                    onSelectionChanged: (value) =>
                        setState(() => _page = value.single),
                  ),
                  const SizedBox(height: 12),
                  ClipRect(
                    child: SizedBox(
                      height: 90,
                      child: PageSwitcher(
                        index: _page,
                        child: Container(
                          key: ValueKey(_page),
                          alignment: Alignment.center,
                          decoration: BoxDecoration(
                            color: [
                              colors.accentSoft,
                              colors.successSoft,
                              colors.warningSoft,
                            ][_page],
                            borderRadius: BorderRadius.circular(JarvisRadii.md),
                          ),
                          child: Text('Page ${_page + 1}'),
                        ),
                      ),
                    ),
                  ),
                ],
              ),
            ),
            section(
              'Streaming replies',
              'Bursts of text flow in a few words at a time.',
              Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  ConstrainedBox(
                    constraints: const BoxConstraints(minHeight: 100),
                    child: StreamingMarkdown(data: _reply),
                  ),
                  Align(
                    alignment: Alignment.centerLeft,
                    child: TextButton.icon(
                      key: const Key('gallery-stream'),
                      onPressed: _streamReply,
                      icon: const Icon(PhosphorIconsRegular.play, size: 16),
                      label: const Text('Stream a reply'),
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  static String _formatThousands(int value) {
    final digits = '$value';
    final out = StringBuffer();
    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) out.write(',');
      out.write(digits[i]);
    }
    return out.toString();
  }
}
