import 'dart:math' as math;
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import '../chat/chat_widgets.dart' show PhotoViewerPage;
import 'whatsapp_models.dart';
import 'whatsapp_visuals.dart' show whatsAppDeepGreen;
import 'whatsapp_open_stub.dart'
    if (dart.library.io) 'whatsapp_open_io.dart'
    if (dart.library.js_interop) 'whatsapp_open_web.dart';

/// The body of one WhatsApp bubble: text, a photo or sticker, or a card for
/// the other things WhatsApp can send.
class WhatsAppMessageContent extends StatelessWidget {
  const WhatsAppMessageContent({
    required this.message,
    required this.http,
    required this.channelId,
    this.selectable = true,
    super.key,
  });

  final WhatsAppMessage message;
  final Dio http;
  final String channelId;

  /// Off where holding the message opens reactions instead of selection.
  final bool selectable;

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    final media = message.media ?? whatsAppLegacyMedia(message.text);
    final quote = message.quote;
    if (media == null && quote == null) return _text(theme, message.text);
    final caption = media == null ? null : whatsAppMediaCaption(message.text);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        if (quote != null) _Quote(quote: quote),
        if (media != null)
          _Element(
            message: message,
            media: media,
            caption: caption,
            http: http,
            channelId: channelId,
          ),
        if (media != null &&
            caption != null &&
            !_captionIsTitle(media, caption)) ...[
          const SizedBox(height: 6),
          _text(theme, caption),
        ],
      ],
    );
  }

  Widget _text(ThemeData theme, String text) {
    final emoji = whatsAppEmojiOnly(text);
    final style = theme.textTheme.bodyMedium?.copyWith(
      fontSize: emoji ? (text.characters.length == 1 ? 48 : 34) : 15.5,
      height: emoji ? 1.15 : 1.45,
    );
    return selectable
        ? SelectableText(text, style: style)
        : Text(text, style: style);
  }

  bool _captionIsTitle(WhatsAppMedia media, String caption) {
    if (media.hasContent &&
        media.kind != 'document' &&
        media.kind != 'audio' &&
        media.kind != 'location' &&
        media.kind != 'contact' &&
        media.kind != 'poll') {
      return false;
    }
    return switch (media.kind) {
      'location' => media.place == null || media.place == caption,
      'contact' => media.contactName == null || media.contactName == caption,
      'document' => media.fileName == null || media.fileName == caption,
      'poll' => true,
      'image' || 'sticker' || 'video' || 'gif' || 'audio' => true,
      _ => true,
    };
  }
}

/// True when the sticker image should sit without a bubble behind it.
bool whatsAppNakedSticker(WhatsAppMessage message) =>
    message.media?.kind == 'sticker' && message.media?.hasContent == true;

class _Element extends StatelessWidget {
  const _Element({
    required this.message,
    required this.media,
    required this.caption,
    required this.http,
    required this.channelId,
  });

  final WhatsAppMessage message;
  final WhatsAppMedia media;
  final String? caption;
  final Dio http;
  final String channelId;

  @override
  Widget build(BuildContext context) {
    final label = _label(message, media);
    if (media.hasContent &&
        (media.kind == 'image' ||
            media.kind == 'sticker' ||
            media.kind == 'video' ||
            media.kind == 'gif')) {
      return _Still(
        http: http,
        channelId: channelId,
        messageId: message.id,
        sticker: media.kind == 'sticker',
        label: label,
        seconds: media.seconds,
      );
    }
    if (media.kind == 'audio' && media.waveform.isNotEmpty) {
      return _VoiceNote(
        media: media,
        label: label,
        mine: message.fromMe,
        onTap: () => _open(context, media, message.id, label),
      );
    }
    final duration = _clock(media.seconds);
    final title = switch (media.kind) {
      'document' => media.fileName ?? caption ?? label,
      'location' => media.place ?? caption ?? label,
      'contact' => media.contactName ?? caption ?? label,
      'poll' => caption ?? label,
      'audio' => label,
      _ => label,
    };
    final subtitle = switch (media.kind) {
      'audio' || 'video' || 'gif' => duration,
      'location' when media.latitude != null && media.longitude != null =>
        '${media.latitude}, ${media.longitude}',
      'image' || 'sticker' => caption,
      _ => null,
    };
    final tappable = media.kind == 'document' ||
        media.kind == 'audio' ||
        (media.kind == 'location' &&
            media.latitude != null &&
            media.longitude != null);
    return _Card(
      icon: _icon(media.kind),
      title: title,
      subtitle: subtitle,
      lines: media.kind == 'poll' ? media.pollOptions : const [],
      semantics: tappable ? 'Open $title' : label,
      onTap: tappable
          ? () => _open(context, media, message.id, title)
          : null,
    );
  }

  Future<void> _open(
    BuildContext context,
    WhatsAppMedia media,
    String messageId,
    String title,
  ) async {
    if (media.kind == 'location') {
      final uri = Uri.parse(
        'https://www.google.com/maps/search/?api=1&query=${media.latitude},${media.longitude}',
      );
      await launchUrl(uri, mode: LaunchMode.externalApplication);
      return;
    }
    if (!media.hasContent) {
      _note(context, 'This $title was not saved.');
      return;
    }
    try {
      final bytes = await WhatsAppMediaCache.load(http, channelId, messageId);
      if (bytes == null) {
        if (context.mounted) _note(context, 'This file is no longer available.');
        return;
      }
      final name = media.fileName ?? _fileName(media, messageId);
      await openWhatsAppBytes(bytes, name);
    } catch (_) {
      if (context.mounted) _note(context, 'Could not open this file.');
    }
  }
}

class _Still extends StatefulWidget {
  const _Still({
    required this.http,
    required this.channelId,
    required this.messageId,
    required this.sticker,
    required this.label,
    required this.seconds,
  });

  final Dio http;
  final String channelId;
  final String messageId;
  final bool sticker;
  final String label;
  final int? seconds;

  @override
  State<_Still> createState() => _StillState();
}

class _StillState extends State<_Still> {
  Uint8List? _bytes;
  var _failed = false;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    final bytes = await WhatsAppMediaCache.load(
      widget.http,
      widget.channelId,
      widget.messageId,
    );
    if (!mounted) return;
    setState(() {
      _bytes = bytes;
      _failed = bytes == null;
    });
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final bytes = _bytes;
    final width = widget.sticker ? 168.0 : 280.0;
    final placeholder = _Card(
      icon: PhosphorIconsRegular.image,
      title: _failed ? '${widget.label} unavailable' : widget.label,
      semantics: widget.label,
    );
    if (bytes == null) {
      return SizedBox(width: widget.sticker ? 168 : null, child: placeholder);
    }
    final duration = _clock(widget.seconds);
    return Semantics(
      label: widget.label,
      button: true,
      child: GestureDetector(
        onTap: () => Navigator.of(context).push(
          MaterialPageRoute<void>(
            builder: (_) => PhotoViewerPage(bytes: bytes, title: widget.label),
          ),
        ),
        child: ClipRRect(
          borderRadius: BorderRadius.circular(widget.sticker ? 8 : 16),
          child: ConstrainedBox(
            constraints: BoxConstraints(maxWidth: width, maxHeight: widget.sticker ? 168 : 360),
            child: Stack(
              alignment: Alignment.center,
              children: [
                Image.memory(
                  bytes,
                  fit: BoxFit.cover,
                  gaplessPlayback: true,
                  errorBuilder: (_, _, _) => placeholder,
                ),
                if (duration != null)
                  Positioned(
                    left: 8,
                    bottom: 8,
                    child: DecoratedBox(
                      decoration: BoxDecoration(
                        color: colors.ink.withValues(alpha: .72),
                        borderRadius: BorderRadius.circular(999),
                      ),
                      child: Padding(
                        padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 3),
                        child: Text(
                          duration,
                          style: const TextStyle(color: Colors.white, fontSize: 12),
                        ),
                      ),
                    ),
                  ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}

class _Card extends StatelessWidget {
  const _Card({
    required this.icon,
    required this.title,
    required this.semantics,
    this.subtitle,
    this.lines = const [],
    this.onTap,
  });

  final IconData icon;
  final String title;
  final String? subtitle;
  final List<String> lines;
  final String semantics;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    final body = Padding(
      padding: const EdgeInsets.symmetric(vertical: 2),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(icon, size: 22, color: colors.accentDeep),
          const SizedBox(width: 10),
          Flexible(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(title, style: theme.textTheme.titleSmall),
                if (subtitle != null)
                  Text(
                    subtitle!,
                    style: theme.textTheme.bodySmall?.copyWith(color: colors.inkSoft),
                  ),
                for (final line in lines)
                  Text(line, style: theme.textTheme.bodyMedium),
              ],
            ),
          ),
        ],
      ),
    );
    return Semantics(
      label: semantics,
      button: onTap != null,
      child: onTap == null ? body : InkWell(onTap: onTap, child: body),
    );
  }
}

/// A voice note drawn as WhatsApp's own loudness bars. The bars grow in once,
/// left to right; tapping opens the recording like any other file.
class _VoiceNote extends StatelessWidget {
  const _VoiceNote({
    required this.media,
    required this.label,
    required this.mine,
    required this.onTap,
  });

  final WhatsAppMedia media;
  final String label;
  final bool mine;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final duration = _clock(media.seconds);
    final tint = mine ? whatsAppDeepGreen : colors.accent;
    final reduced = MediaQuery.maybeDisableAnimationsOf(context) ?? false;
    final samples = media.waveform;
    return Semantics(
      container: true,
      button: true,
      label: ['Open $label', ?duration].join(', '),
      excludeSemantics: true,
      child: InkWell(
        onTap: onTap,
        borderRadius: BorderRadius.circular(12),
        child: Padding(
          padding: const EdgeInsets.symmetric(vertical: 2),
          child: Row(
            mainAxisSize: MainAxisSize.min,
            children: [
              Container(
                width: 36,
                height: 36,
                decoration: BoxDecoration(color: tint, shape: BoxShape.circle),
                child: const Icon(
                  PhosphorIconsRegular.play,
                  size: 16,
                  color: Colors.white,
                ),
              ),
              const SizedBox(width: 10),
              SizedBox(
                width: 168,
                height: 30,
                child: TweenAnimationBuilder<double>(
                  tween: Tween(begin: reduced ? 1 : 0, end: 1),
                  duration: const Duration(milliseconds: 900),
                  curve: Curves.easeOutCubic,
                  builder: (context, t, _) => CustomPaint(
                    painter: _WaveformPainter(
                      samples: samples,
                      progress: t,
                      color: tint.withValues(alpha: .75),
                    ),
                  ),
                ),
              ),
              if (duration != null) ...[
                const SizedBox(width: 8),
                Text(
                  duration,
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: colors.inkSoft,
                    fontFeatures: const [FontFeature.tabularFigures()],
                  ),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _WaveformPainter extends CustomPainter {
  _WaveformPainter({
    required this.samples,
    required this.progress,
    required this.color,
  });

  final List<int> samples;
  final double progress;
  final Color color;

  @override
  void paint(Canvas canvas, Size size) {
    if (samples.isEmpty) return;
    const bars = 32;
    final gap = size.width / bars;
    final paint = Paint()
      ..color = color
      ..strokeCap = StrokeCap.round
      ..strokeWidth = math.max(2, gap * .55);
    final loudest = samples.reduce(math.max).clamp(1, 100);
    for (var i = 0; i < bars; i++) {
      // Average the samples that fall in this bar.
      final from = i * samples.length ~/ bars;
      final to = math.max(from + 1, (i + 1) * samples.length ~/ bars);
      var sum = 0;
      for (var j = from; j < to && j < samples.length; j++) {
        sum += samples[j];
      }
      final level = sum / (to - from) / loudest;
      // Each bar starts growing a little after the one before it.
      final grow = ((progress * 1.6) - i / bars * .6).clamp(0.0, 1.0);
      final height = math.max(3.0, size.height * level * grow);
      final x = gap * (i + .5);
      canvas.drawLine(
        Offset(x, (size.height - height) / 2),
        Offset(x, (size.height + height) / 2),
        paint,
      );
    }
  }

  @override
  bool shouldRepaint(_WaveformPainter old) =>
      old.progress != progress || old.samples != samples || old.color != color;
}

class _Quote extends StatelessWidget {
  const _Quote({required this.quote});

  final WhatsAppQuote quote;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: 6),
      padding: const EdgeInsets.fromLTRB(8, 4, 8, 4),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(8),
        border: Border(left: BorderSide(color: colors.accent, width: 3)),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (quote.author != null)
            Text(
              quote.author!,
              style: theme.textTheme.labelSmall?.copyWith(
                color: colors.accentDeep,
                fontWeight: FontWeight.w600,
              ),
            ),
          Text(
            quote.text,
            maxLines: 3,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodySmall?.copyWith(color: colors.inkSoft),
          ),
        ],
      ),
    );
  }
}

class WhatsAppMediaCache {
  static final _memory = <String, Uint8List?>{};
  static final _loading = <String, Future<Uint8List?>>{};

  static void clear() {
    _memory.clear();
    _loading.clear();
  }

  static Future<Uint8List?> load(Dio http, String channelId, String messageId) {
    final key = '$channelId\n$messageId';
    final cached = _memory[key];
    if (cached != null) return Future.value(cached);
    return _loading[key] ??= _fetch(http, channelId, messageId).then((bytes) {
      if (bytes != null) {
        _memory[key] = bytes;
        while (_memory.length > 40) {
          _memory.remove(_memory.keys.first);
        }
      }
      _loading.remove(key);
      return bytes;
    }, onError: (_) {
      _loading.remove(key);
      return null;
    });
  }

  static Future<Uint8List?> _fetch(
    Dio http,
    String channelId,
    String messageId,
  ) async {
    try {
      final response = await http.get<List<int>>(
        whatsAppChatPath(channelId, action: 'media'),
        queryParameters: {'messageId': messageId},
        options: Options(
          responseType: ResponseType.bytes,
          receiveTimeout: const Duration(seconds: 20),
        ),
      );
      final data = response.data;
      if (data == null || data.isEmpty) return null;
      return Uint8List.fromList(data);
    } on DioException catch (error) {
      if (error.response?.statusCode == 404) return null;
      rethrow;
    }
  }
}

String _label(WhatsAppMessage message, WhatsAppMedia media) {
  final text = message.text.trim();
  if (text.startsWith('[Live location]')) return 'Live location';
  if (text.startsWith('[Contact cards]')) return 'Contacts';
  return media.label;
}

IconData _icon(String kind) => switch (kind) {
  'image' => PhosphorIconsRegular.image,
  'sticker' => PhosphorIconsRegular.image,
  'video' || 'gif' => PhosphorIconsRegular.filmStrip,
  'audio' => PhosphorIconsRegular.microphone,
  'document' => PhosphorIconsRegular.fileText,
  'location' => PhosphorIconsRegular.mapPin,
  'contact' => PhosphorIconsRegular.user,
  'poll' => PhosphorIconsRegular.chartBar,
  _ => PhosphorIconsRegular.paperclip,
};

String? _clock(int? seconds) {
  if (seconds == null || seconds <= 0) return null;
  final minutes = seconds ~/ 60;
  final rest = seconds % 60;
  return '$minutes:${rest.toString().padLeft(2, '0')}';
}

String _fileName(WhatsAppMedia media, String messageId) {
  final ext = switch (media.mime) {
    'application/pdf' => 'pdf',
    'audio/ogg' => 'ogg',
    'audio/mpeg' => 'mp3',
    'audio/mp4' => 'm4a',
    'text/plain' => 'txt',
    _ => 'bin',
  };
  return 'whatsapp-$messageId.$ext';
}

void _note(BuildContext context, String message) {
  ScaffoldMessenger.maybeOf(context)?.showSnackBar(SnackBar(content: Text(message)));
}
