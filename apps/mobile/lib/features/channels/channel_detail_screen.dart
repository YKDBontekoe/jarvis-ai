part of 'channels_screen.dart';

class ChannelDetailScreen extends StatefulWidget {
  const ChannelDetailScreen({
    required this.http,
    required this.channelId,
    super.key,
  });

  final Dio http;
  final String channelId;

  @override
  State<ChannelDetailScreen> createState() => _ChannelDetailScreenState();
}

class _ChannelDetailScreenState extends State<ChannelDetailScreen> {
  Map<String, dynamic>? _channel;
  List<Map<String, dynamic>> _messages = const [];
  List<Map<String, dynamic>> _threads = const [];
  bool _loading = true;
  bool _busy = false;
  String? _error;
  int _requestRevision = 0;

  String get _path => '/api/v1/channels/${widget.channelId}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final list = await widget.http.get<dynamic>('/api/v1/channels');
      final messages = await widget.http.get<dynamic>('$_path/messages');
      var threads = <Map<String, dynamic>>[];
      try {
        final threadResponse = await widget.http.get<dynamic>('$_path/threads');
        threads = jsonMaps(threadResponse.data);
      } catch (_) {
        threads = const [];
      }
      if (!mounted || revision != _requestRevision) return;
      final channel = jsonMaps(list.data)
          .cast<Map<String, dynamic>?>()
          .firstWhere(
            (item) => asJsonString(item?['id']) == widget.channelId,
            orElse: () => null,
          );
      setState(() {
        _channel = channel;
        _messages = jsonMaps(messages.data);
        _threads = threads;
        _loading = false;
        _error = channel == null
            ? 'This channel is no longer connected.'
            : null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this channel.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load this channel.';
      });
    }
  }

  Future<void> _openThread(Map<String, dynamic> thread) async {
    final peer = asJsonString(thread['peer']);
    if (peer == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => ChannelThreadScreen(
          http: widget.http,
          channelId: widget.channelId,
          peer: peer,
        ),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _test() async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.post<dynamic>('$_path/test');
      if (!mounted) return;
      final body = jsonObject(response.data);
      final sent = asJsonBool(body?['sent']);
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            sent
                ? 'Test message sent.'
                : asJsonString(body?['error']) ??
                      'Could not send a test message.',
          ),
        ),
      );
      unawaited(_load());
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not send a test message.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not send a test message.');
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _relink() async {
    final linked = await showModalBottomSheet<Map<String, dynamic>>(
      context: context,
      isScrollControlled: true,
      builder: (_) => ChannelLinkSheet(
        http: widget.http,
        kind: 'whatsapp_linked',
        channelId: widget.channelId,
      ),
    );
    if (linked != null && mounted) unawaited(_load());
  }

  Future<void> _disconnect() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Disconnect this channel?',
      message: 'Jarvis will stop reading and sending messages on this number. You can connect it again later.',
      confirmLabel: 'Disconnect',
      destructive: true,
    );
    if (confirmed != true || !mounted) return;
    setState(() => _busy = true);
    try {
      await widget.http.delete<void>(_path);
      if (mounted) Navigator.of(context).pop();
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not disconnect this channel.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _busy = false;
        _error = 'Could not disconnect this channel.';
      });
    }
  }

  Future<void> _copyWebhook() async {
    final url = asJsonString(_channel?['webhookUrl']);
    if (url == null) return;
    try {
      await Clipboard.setData(ClipboardData(text: url));
    } catch (_) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Could not copy the webhook URL.')),
      );
      return;
    }
    if (!mounted) return;
    ScaffoldMessenger.of(context)
        .showSnackBar(const SnackBar(content: Text('Webhook URL copied.')));
  }

  @override
  Widget build(BuildContext context) {
    final channel = _channel;
    final kind = channelKind(asJsonString(channel?['kind']) ?? '');
    return Scaffold(
      appBar: AppBar(
        title: Text(asJsonString(channel?['displayName']) ?? 'Channel'),
        actions: [
          IconButton(
            tooltip: 'Send a test message',
            onPressed: _busy ? null : () => unawaited(_test()),
            icon: const Icon(PhosphorIconsRegular.paperPlaneTilt),
          ),
          IconButton(
            tooltip: 'Disconnect',
            onPressed: _busy ? null : () => unawaited(_disconnect()),
            icon: const Icon(PhosphorIconsRegular.trash),
          ),
        ],
      ),
      body: _loading
          ? const LoadingState()
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                if (_error != null)
                  InlineNotice(
                    message: _error!,
                    tone: NoticeTone.danger,
                    margin: const EdgeInsets.only(bottom: 12),
                  ),
                if (channel != null) ...[
                  SurfaceCard(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        ListTile(
                          contentPadding: EdgeInsets.zero,
                          leading: IconBadge(
                            icon: kind.icon,
                            color: kind.color,
                            size: 40,
                          ),
                          title: Text(kind.label),
                          subtitle: Text(
                            asJsonString(channel['account']) ?? '',
                          ),
                          trailing: Text(
                            asJsonBool(channel['enabled'], true)
                                ? 'On'
                                : 'Paused',
                            style: TextStyle(
                              color: asJsonBool(channel['enabled'], true)
                                  ? JarvisColors.of(context).success
                                  : JarvisColors.of(context).muted,
                              fontWeight: FontWeight.w600,
                            ),
                          ),
                        ),
                        Text(
                          'Allowed senders: ${jsonStrings(channel['allowedSenders']).join(', ')}',
                          style: Theme.of(context).textTheme.bodySmall,
                        ),
                        if (asJsonBool(channel['forwardNotifications']))
                          Padding(
                            padding: const EdgeInsets.only(top: 8),
                            child: Text(
                              'Forwards notifications to ${asJsonString(channel['notifyRecipient']) ?? 'the first allowed number'}.',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ),
                        if (asJsonString(channel['kind']) == 'whatsapp_linked' &&
                            asJsonString(channel['lastError']) != null)
                          Align(
                            alignment: Alignment.centerLeft,
                            child: TextButton.icon(
                              key: const Key('channel-relink'),
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_relink()),
                              icon: const Icon(
                                PhosphorIconsRegular.link,
                                size: 16,
                              ),
                              label: const Text('Link again'),
                            ),
                          ),
                        if (asJsonString(channel['webhookUrl']) != null) ...[
                          const SizedBox(height: 12),
                          const Text(
                            'WhatsApp webhook',
                            style: TextStyle(fontWeight: FontWeight.w600),
                          ),
                          const SizedBox(height: 4),
                          SelectableText(
                            asJsonString(channel['webhookUrl'])!,
                            style: const TextStyle(fontSize: 13),
                          ),
                          TextButton.icon(
                            onPressed: () => unawaited(_copyWebhook()),
                            icon: const Icon(
                              PhosphorIconsRegular.copy,
                              size: 16,
                            ),
                            label: const Text('Copy webhook URL'),
                          ),
                        ],
                      ],
                    ),
                  ),
                  const SizedBox(height: 18),
                  if (_threads.isNotEmpty) ...[
                    Text(
                      'THREADS',
                      style: Theme.of(context).textTheme.labelSmall?.copyWith(
                        color: JarvisColors.of(context).muted,
                        letterSpacing: .8,
                      ),
                    ),
                    const SizedBox(height: 8),
                    for (final thread in _threads)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 8),
                        child: SurfaceCard(
                          key: Key(
                            'channel-thread-${asJsonString(thread['peer'])}',
                          ),
                          onTap: () => unawaited(_openThread(thread)),
                          padding: const EdgeInsets.all(14),
                          child: Row(
                            children: [
                              const IconBadge(
                                icon: PhosphorIconsRegular.chatCircle,
                                size: 32,
                              ),
                              const SizedBox(width: 12),
                              Expanded(
                                child: Column(
                                  crossAxisAlignment: CrossAxisAlignment.start,
                                  children: [
                                    Text(
                                      asJsonString(thread['peer']) ?? 'Unknown',
                                      style: Theme.of(context)
                                          .textTheme
                                          .titleSmall,
                                    ),
                                    Text(
                                      asJsonString(
                                            jsonObject(
                                              thread['lastMessage'],
                                            )?['text'],
                                          ) ??
                                          '${asJsonInt(thread['messageCount'])} messages',
                                      maxLines: 2,
                                      overflow: TextOverflow.ellipsis,
                                      style: Theme.of(context)
                                          .textTheme
                                          .bodySmall,
                                    ),
                                  ],
                                ),
                              ),
                              Icon(
                                PhosphorIconsRegular.caretRight,
                                size: 16,
                                color: JarvisColors.of(context).muted,
                              ),
                            ],
                          ),
                        ),
                      ),
                    const SizedBox(height: 10),
                  ],
                  Text(
                    'RECENT MESSAGES',
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: JarvisColors.of(context).muted,
                      letterSpacing: .8,
                    ),
                  ),
                  const SizedBox(height: 8),
                  if (_messages.isEmpty)
                    const Padding(
                      padding: EdgeInsets.symmetric(vertical: 24),
                      child: Text(
                        'No messages yet. Send a test, or text Jarvis from an allowed number.',
                      ),
                    )
                  else
                    for (final message in _messages)
                      Padding(
                        padding: const EdgeInsets.only(bottom: 8),
                        child: SurfaceCard(
                          padding: const EdgeInsets.all(14),
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                '${asJsonString(message['direction']) == 'out' ? 'Jarvis' : asJsonString(message['peer']) ?? 'Unknown'} · ${asJsonString(message['status']) ?? ''}',
                                style: Theme.of(context).textTheme.labelSmall
                                    ?.copyWith(
                                      color: JarvisColors.of(context).muted,
                                    ),
                              ),
                              const SizedBox(height: 4),
                              Text(asJsonString(message['text']) ?? ''),
                            ],
                          ),
                        ),
                      ),
                ],
              ],
            ),
    );
  }
}
