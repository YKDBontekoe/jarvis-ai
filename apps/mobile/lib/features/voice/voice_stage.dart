import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Full-screen voice session. The phase comes from the LiveKit room so the
/// page can show listening, thinking, and speaking without guessing from text.
class VoiceStage extends StatelessWidget {
  const VoiceStage({
    required this.phase,
    required this.voiceName,
    required this.handsFree,
    required this.captions,
    required this.canStart,
    required this.onPrimary,
    this.caption,
    this.captionRole,
    this.muted = false,
    this.error,
    this.onToggleMute,
    super.key,
  });

  final String phase;
  final String voiceName;
  final bool handsFree;
  final bool captions;
  final bool canStart;
  final VoidCallback onPrimary;
  final String? caption;
  final String? captionRole;
  final bool muted;
  final String? error;
  final VoidCallback? onToggleMute;

  bool get _live => phase != 'idle';

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final speaking = phase == 'speaking';
    final listening = phase == 'listening' || speaking;
    return Stack(
      children: [
        const Positioned.fill(child: _VoiceBackdrop()),
        SafeArea(
          child: Center(
            child: SingleChildScrollView(
              padding: const EdgeInsets.fromLTRB(24, 20, 24, 28),
              child: ConstrainedBox(
                constraints: const BoxConstraints(maxWidth: 440),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    _VoicePill(voiceName: voiceName),
                    const SizedBox(height: 28),
                    SizedBox.square(
                      dimension: 240,
                      child: Center(
                        child: JarvisOrb(
                          size: speaking ? 148 : 128,
                          animate: phase == 'connecting' || speaking,
                          listening: listening && phase != 'connecting',
                        ),
                      ),
                    ),
                    const SizedBox(height: 8),
                    AnimatedSwitcher(
                      duration: const Duration(milliseconds: 240),
                      child: Text(
                        _title,
                        key: ValueKey(phase),
                        textAlign: TextAlign.center,
                        style: JarvisType.serif.copyWith(fontSize: 40),
                      ),
                    ),
                    const SizedBox(height: 10),
                    Text(
                      _subtitle,
                      textAlign: TextAlign.center,
                      style: theme.textTheme.bodyLarge?.copyWith(
                        color: JarvisColors.inkSoft,
                        height: 1.4,
                      ),
                    ),
                    if (captions && caption != null && caption!.isNotEmpty) ...[
                      const SizedBox(height: 22),
                      _CaptionCard(role: captionRole, text: caption!),
                    ],
                    if (error != null)
                      InlineNotice(
                        message: error!,
                        tone: NoticeTone.danger,
                        margin: const EdgeInsets.only(top: 20),
                      ),
                    const SizedBox(height: 28),
                    FilledButton.icon(
                      key: const Key('voice-primary'),
                      onPressed: _live || canStart ? onPrimary : null,
                      style: FilledButton.styleFrom(
                        backgroundColor: _live
                            ? JarvisColors.danger
                            : JarvisColors.ink,
                        minimumSize: const Size(220, 56),
                        shape: RoundedRectangleBorder(
                          borderRadius: BorderRadius.circular(40),
                        ),
                      ),
                      icon: phase == 'connecting'
                          ? const SizedBox.square(
                              dimension: 18,
                              child: CircularProgressIndicator(strokeWidth: 2),
                            )
                          : Icon(
                              _live
                                  ? PhosphorIconsRegular.stop
                                  : PhosphorIconsRegular.microphone,
                            ),
                      label: Text(_live ? 'End voice chat' : 'Start voice chat'),
                    ),
                    if (onToggleMute != null) ...[
                      const SizedBox(height: 12),
                      TextButton.icon(
                        key: const Key('voice-mute'),
                        onPressed: onToggleMute,
                        icon: Icon(
                          muted
                              ? PhosphorIconsRegular.microphoneSlash
                              : PhosphorIconsRegular.microphone,
                        ),
                        label: Text(muted ? 'Unmute microphone' : 'Mute microphone'),
                      ),
                    ],
                    if (!_live) ...[
                      const SizedBox(height: 28),
                      const Wrap(
                        alignment: WrapAlignment.center,
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          _VoiceHint(
                            icon: PhosphorIconsRegular.waveform,
                            label: 'Continuous voice',
                          ),
                          _VoiceHint(
                            icon: PhosphorIconsRegular.ear,
                            label: 'Interrupt by speaking',
                          ),
                          _VoiceHint(
                            icon: PhosphorIconsRegular.brain,
                            label: 'Uses your memory',
                          ),
                        ],
                      ),
                    ],
                  ],
                ),
              ),
            ),
          ),
        ),
      ],
    );
  }

  String get _title => switch (phase) {
    'connecting' => 'Connecting',
    'listening' => muted ? 'Muted' : 'Listening',
    'thinking' => 'One moment',
    'speaking' => 'Speaking',
    _ => 'Talk to Jarvis',
  };

  String get _subtitle {
    if (phase == 'connecting') return 'Opening a ChatGPT voice session.';
    if (phase == 'thinking') return 'Jarvis is working on that.';
    if (phase == 'speaking') {
      return '$voiceName is speaking. Talk when you want to interrupt.';
    }
    if (phase == 'listening') {
      return muted
          ? 'The microphone is off. Unmute when you want to speak.'
          : handsFree
          ? 'Speak naturally. $voiceName keeps going until you actually interrupt.'
          : 'Unmute the microphone when you want to speak.';
    }
    return '$voiceName answers in one continuous ChatGPT voice, using this conversation and its memory.';
  }
}

class _VoicePill extends StatelessWidget {
  const _VoicePill({required this.voiceName});

  final String voiceName;

  @override
  Widget build(BuildContext context) => Container(
    key: const Key('voice-name'),
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 7),
    decoration: BoxDecoration(
      color: JarvisColors.surface.withValues(alpha: .9),
      borderRadius: BorderRadius.circular(40),
      border: Border.all(color: JarvisColors.outline),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        const Icon(PhosphorIconsRegular.waveform, size: 15, color: JarvisColors.inkSoft),
        const SizedBox(width: 6),
        Text(
          'ChatGPT voice · $voiceName',
          style: const TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w600,
            color: JarvisColors.inkSoft,
          ),
        ),
      ],
    ),
  );
}

class _CaptionCard extends StatelessWidget {
  const _CaptionCard({required this.role, required this.text});

  final String? role;
  final String text;

  @override
  Widget build(BuildContext context) {
    final assistant = role == 'assistant';
    return SurfaceCard(
      key: const Key('voice-caption'),
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 14),
      child: Column(
        children: [
          Text(
            assistant ? 'Jarvis' : 'You',
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: JarvisColors.muted,
              letterSpacing: .4,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            text,
            textAlign: TextAlign.center,
            maxLines: 6,
            overflow: TextOverflow.ellipsis,
            style: Theme.of(context).textTheme.bodyLarge?.copyWith(height: 1.35),
          ),
        ],
      ),
    );
  }
}

class _VoiceHint extends StatelessWidget {
  const _VoiceHint({required this.icon, required this.label});

  final IconData icon;
  final String label;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 12, vertical: 8),
    decoration: BoxDecoration(
      color: JarvisColors.surface.withValues(alpha: .8),
      borderRadius: BorderRadius.circular(40),
      border: Border.all(color: JarvisColors.outline),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 15, color: JarvisColors.inkSoft),
        const SizedBox(width: 6),
        Text(
          label,
          style: const TextStyle(
            fontSize: 12.5,
            fontWeight: FontWeight.w500,
            color: JarvisColors.inkSoft,
          ),
        ),
      ],
    ),
  );
}

class _VoiceBackdrop extends StatelessWidget {
  const _VoiceBackdrop();

  @override
  Widget build(BuildContext context) => const IgnorePointer(
    child: Stack(
      children: [
        Positioned(top: -120, left: -80, child: _Blob(size: 360, color: Color(0x1c7c6cff))),
        Positioned(bottom: -140, right: -100, child: _Blob(size: 420, color: Color(0x1638bdf8))),
        Positioned(top: 180, right: -60, child: _Blob(size: 220, color: Color(0x12f472b6))),
      ],
    ),
  );
}

class _Blob extends StatelessWidget {
  const _Blob({required this.size, required this.color});

  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      gradient: RadialGradient(colors: [color, color.withValues(alpha: 0)]),
    ),
  );
}
