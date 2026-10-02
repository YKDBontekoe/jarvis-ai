part of 'channels_screen.dart';

/// Links WhatsApp or Signal by scanning a QR code: start an attempt, show the code,
/// and poll until the phone has scanned it. Pops with the finished link on success.
class ChannelLinkSheet extends StatefulWidget {
  const ChannelLinkSheet({
    required this.http,
    required this.kind,
    this.channelId,
    this.readAlong = false,
    this.pollInterval = const Duration(seconds: 2),
    super.key,
  });

  final Dio http;

  /// `whatsapp_linked` or `signal`.
  final String kind;

  /// Set to re-link an existing channel instead of creating a new one.
  final String? channelId;
  final bool readAlong;
  final Duration pollInterval;

  @override
  State<ChannelLinkSheet> createState() => _ChannelLinkSheetState();
}

class _ChannelLinkSheetState extends State<ChannelLinkSheet> {
  Timer? _timer;
  String? _linkId;
  String? _qrSource;
  Uint8List? _qrBytes;
  String? _error;
  var _polling = false;
  var _finished = false;

  bool get _whatsapp => widget.kind == 'whatsapp_linked';

  @override
  void initState() {
    super.initState();
    unawaited(_start());
  }

  @override
  void dispose() {
    _timer?.cancel();
    super.dispose();
  }

  Future<void> _start() async {
    _timer?.cancel();
    // Reset only on a retry; the first call runs from initState, before the first build.
    if (_error != null || _linkId != null) {
      setState(() {
        _error = null;
        _linkId = null;
        _qrSource = null;
        _qrBytes = null;
      });
    }
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/channels/link',
        data: {
          'kind': widget.kind,
          if (widget.readAlong) 'readAlong': true,
          if (widget.channelId != null) 'channelId': widget.channelId,
        },
      );
      if (!mounted) return;
      _apply(jsonObject(response.data));
      if (_finished || _error != null) return;
      _timer = Timer.periodic(widget.pollInterval, (_) => unawaited(_poll()));
    } on DioException catch (error) {
      _fail(
        firstProblemMessage(error.response?.data) ??
            'Could not start linking. Check that the server is reachable.',
      );
    } catch (_) {
      _fail('Could not start linking.');
    }
  }

  Future<void> _poll() async {
    final id = _linkId;
    if (id == null || _polling || _finished) return;
    _polling = true;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/channels/link/$id',
      );
      if (!mounted) return;
      _apply(jsonObject(response.data));
    } on DioException catch (error) {
      // A missing attempt means the server restarted or it expired; anything
      // else is usually a blip, so keep polling quietly.
      if (error.response?.statusCode == 404) {
        _fail('This code expired. Get a new one.');
      }
    } catch (_) {
      // Ignore a malformed poll response and try again on the next tick.
    } finally {
      _polling = false;
    }
  }

  void _apply(Map<String, dynamic>? link) {
    if (link == null) {
      _fail('Could not read the linking status.');
      return;
    }
    final state = asJsonString(link['state']);
    if (state == 'linked') {
      _timer?.cancel();
      _finished = true;
      Navigator.of(context).pop(link);
      return;
    }
    if (state == 'expired' || state == 'failed') {
      _fail(
        asJsonString(link['message']) ??
            'Linking did not finish. Get a new code.',
      );
      return;
    }
    final qr = asJsonString(link['qrImage']);
    setState(() {
      _linkId = asJsonString(link['linkId']) ?? _linkId;
      if (qr != null && qr != _qrSource) {
        _qrSource = qr;
        _qrBytes = _decode(qr);
      }
    });
  }

  Uint8List? _decode(String dataUrl) {
    try {
      return base64Decode(dataUrl.substring(dataUrl.indexOf(',') + 1));
    } catch (_) {
      return null;
    }
  }

  void _fail(String message) {
    _timer?.cancel();
    if (!mounted) return;
    setState(() => _error = message);
  }

  @override
  Widget build(BuildContext context) {
    final kind = channelKind(widget.kind);
    final colors = JarvisColors.of(context);
    return SafeArea(
      child: Padding(
        padding: const EdgeInsets.fromLTRB(20, 16, 20, 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              widget.readAlong ? 'Connect your WhatsApp' : 'Link ${kind.label}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 6),
            Text(
              _whatsapp
                  ? '${widget.readAlong ? 'Use your personal WhatsApp account. ' : ''}Open WhatsApp → Settings → Linked devices → Link a device, then scan this code.'
                  : 'Open Signal → Settings → Linked devices → Link new device, then scan this code.',
              style: Theme.of(
                context,
              ).textTheme.bodySmall?.copyWith(color: colors.inkSoft),
            ),
            const SizedBox(height: 16),
            if (_error != null) ...[
              InlineNotice(message: _error!, tone: NoticeTone.danger),
              const SizedBox(height: 12),
              FilledButton(
                key: const Key('channel-link-retry'),
                onPressed: () => unawaited(_start()),
                child: const Text('Get a new code'),
              ),
            ] else
              Center(
                child: SizedBox.square(
                  dimension: 240,
                  child: _qrBytes == null
                      ? const Center(child: CircularProgressIndicator())
                      : ClipRRect(
                          borderRadius: BorderRadius.circular(12),
                          child: Image.memory(
                            _qrBytes!,
                            key: const Key('channel-link-qr'),
                            gaplessPlayback: true,
                            fit: BoxFit.contain,
                            filterQuality: FilterQuality.none,
                          ),
                        ),
                ),
              ),
            const SizedBox(height: 12),
            Text(
              widget.readAlong
                  ? 'After linking, choose the chats Jarvis may read. All chats start off. Replies are sent only when you tap Send or approve them.'
                  : widget.channelId == null
                  ? 'Your own number is allowed automatically, so you can message yourself to talk to Jarvis.'
                  : 'Scan with the same phone number to reconnect.',
              textAlign: TextAlign.center,
              style: Theme.of(
                context,
              ).textTheme.bodySmall?.copyWith(color: colors.muted),
            ),
          ],
        ),
      ),
    );
  }
}
