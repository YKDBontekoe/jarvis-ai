import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

({String label, IconData icon, Color color}) channelKind(String kind) =>
    switch (kind) {
      'whatsapp' => (
        label: 'WhatsApp',
        icon: PhosphorIconsRegular.whatsappLogo,
        color: const Color(0xff16a34a),
      ),
      'signal' => (
        label: 'Signal',
        icon: PhosphorIconsRegular.chatsCircle,
        color: JarvisColors.info,
      ),
      _ => (
        label: kind,
        icon: PhosphorIconsRegular.broadcast,
        color: JarvisColors.muted,
      ),
    };

/// Connect WhatsApp Cloud API or Signal, restrict who can talk to Jarvis, and
/// review recent messages.
class ChannelsScreen extends StatefulWidget {
  const ChannelsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ChannelsScreen> createState() => _ChannelsScreenState();
}

class _ChannelsScreenState extends State<ChannelsScreen> {
  List<Map<String, dynamic>> _channels = const [];
  Map<String, dynamic> _signal = const {};
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final channels = await widget.http.get<dynamic>('/api/v1/channels');
      Map<String, dynamic> signal = const {};
      try {
        final status = await widget.http.get<dynamic>(
          '/api/v1/channels/signal/status',
        );
        signal = jsonObject(status.data) ?? const {};
      } on DioException {
        // Signal is optional; the list of WhatsApp connections still loads.
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _channels = jsonMaps(channels.data);
        _signal = signal;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load messaging channels.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load messaging channels.';
      });
    }
  }

  Future<void> _open(Map<String, dynamic> channel) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => ChannelDetailScreen(
          http: widget.http,
          channelId: asJsonString(channel['id']) ?? '',
        ),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create(String kind) async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => ChannelEditorSheet(http: widget.http, kind: kind),
    );
    if (created == true && mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    final signalReady = asJsonBool(_signal['configured']);
    return Scaffold(
      appBar: AppBar(
        title: const Text('WhatsApp & Signal'),
        actions: [
          HeaderAction(
            label: 'Connect',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_pickKind()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: _channels.isEmpty,
        onRetry: () => unawaited(_load()),
        empty: EmptyState(
          icon: PhosphorIconsRegular.whatsappLogo,
          title: 'No messaging channels yet',
          message:
              'Connect WhatsApp or Signal so you can talk to Jarvis from your phone. Only numbers you allow can send messages.',
          action: FilledButton.icon(
            onPressed: () => unawaited(_pickKind()),
            icon: const Icon(PhosphorIconsRegular.plus),
            label: const Text('Connect a channel'),
          ),
        ),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            if (!signalReady)
              ContentWidth(
                child: InlineNotice(
                  message:
                      'Signal needs signal-cli on the server. WhatsApp works with a Meta Cloud API app.',
                  tone: NoticeTone.info,
                  margin: const EdgeInsets.only(bottom: 12),
                ),
              ),
            ContentWidth(
              child: Column(
                children: [
                  for (final (index, channel) in _channels.indexed) ...[
                    if (index > 0) const SizedBox(height: 10),
                    _tile(channel),
                  ],
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _tile(Map<String, dynamic> channel) {
    final kind = channelKind(asJsonString(channel['kind']) ?? '');
    final enabled = asJsonBool(channel['enabled'], true);
    final error = asJsonString(channel['lastError']);
    final senders = jsonStrings(channel['allowedSenders']);
    return SurfaceCard(
      child: ListTile(
        key: Key('channel-${asJsonString(channel['id'])}'),
        contentPadding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
        leading: IconBadge(icon: kind.icon, color: kind.color, size: 40),
        title: Text(asJsonString(channel['displayName']) ?? kind.label),
        subtitle: Text(
          [
            kind.label,
            asJsonString(channel['account']) ?? '',
            if (senders.isNotEmpty)
              '${senders.length} allowed number${senders.length == 1 ? '' : 's'}',
            if (!enabled) 'Paused',
            if (error != null) 'Last error: $error',
          ].where((part) => part.isNotEmpty).join(' · '),
        ),
        isThreeLine: error != null,
        trailing: const Icon(
          PhosphorIconsRegular.caretRight,
          size: 16,
          color: JarvisColors.muted,
        ),
        onTap: () => unawaited(_open(channel)),
      ),
    );
  }

  Future<void> _pickKind() async {
    final kind = await showModalBottomSheet<String>(
      context: context,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(8, 8, 8, 16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                key: const Key('connect-whatsapp'),
                leading: IconBadge(
                  icon: PhosphorIconsRegular.whatsappLogo,
                  color: const Color(0xff16a34a),
                ),
                title: const Text('WhatsApp'),
                subtitle: const Text('Official Cloud API with a webhook'),
                onTap: () => Navigator.pop(context, 'whatsapp'),
              ),
              ListTile(
                key: const Key('connect-signal'),
                leading: const IconBadge(
                  icon: PhosphorIconsRegular.chatsCircle,
                  color: JarvisColors.info,
                ),
                title: const Text('Signal'),
                subtitle: const Text('Link through signal-cli on this server'),
                onTap: () => Navigator.pop(context, 'signal'),
              ),
            ],
          ),
        ),
      ),
    );
    if (kind != null && mounted) unawaited(_create(kind));
  }
}

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

  Future<void> _disconnect() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Disconnect this channel?',
      message:
          'Jarvis will stop reading and sending messages on this number. You can connect it again later.',
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
    await Clipboard.setData(ClipboardData(text: url));
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(const SnackBar(content: Text('Webhook URL copied.')));
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
                                  ? JarvisColors.success
                                  : JarvisColors.muted,
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
                  Text(
                    'RECENT MESSAGES',
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: JarvisColors.muted,
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
                                    ?.copyWith(color: JarvisColors.muted),
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

class ChannelEditorSheet extends StatefulWidget {
  const ChannelEditorSheet({required this.http, required this.kind, super.key});

  final Dio http;
  final String kind;

  @override
  State<ChannelEditorSheet> createState() => _ChannelEditorSheetState();
}

class _ChannelEditorSheetState extends State<ChannelEditorSheet> {
  final _name = TextEditingController();
  final _account = TextEditingController();
  final _senders = TextEditingController();
  final _notify = TextEditingController();
  final _accessToken = TextEditingController();
  final _appSecret = TextEditingController();
  final _verifyToken = TextEditingController();
  var _enabled = true;
  var _forward = true;
  var _saving = false;
  String? _error;

  bool get _whatsapp => widget.kind == 'whatsapp';

  @override
  void initState() {
    super.initState();
    _name.text = _whatsapp ? 'WhatsApp' : 'Signal';
  }

  @override
  void dispose() {
    _name.dispose();
    _account.dispose();
    _senders.dispose();
    _notify.dispose();
    _accessToken.dispose();
    _appSecret.dispose();
    _verifyToken.dispose();
    super.dispose();
  }

  Future<void> _save() async {
    final senders = _senders.text
        .split(RegExp(r'[\n,]'))
        .map((value) => value.trim())
        .where((value) => value.isNotEmpty)
        .toList();
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await widget.http.post<dynamic>(
        '/api/v1/channels',
        data: {
          'kind': widget.kind,
          'displayName': _name.text.trim(),
          'account': _account.text.trim(),
          'enabled': _enabled,
          'allowedSenders': senders,
          'forwardNotifications': _forward,
          'notifyRecipient': _notify.text.trim().isEmpty
              ? null
              : _notify.text.trim(),
          if (_whatsapp)
            'secrets': {
              'access_token': _accessToken.text.trim(),
              'app_secret': _appSecret.text.trim(),
              'verify_token': _verifyToken.text.trim(),
            },
        },
      );
      if (mounted) Navigator.of(context).pop(true);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not connect this channel.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _saving = false;
        _error = 'Could not connect this channel.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final kind = channelKind(widget.kind);
    final inset = MediaQuery.viewInsetsOf(context).bottom;
    return Padding(
      padding: EdgeInsets.fromLTRB(20, 16, 20, 20 + inset),
      child: SingleChildScrollView(
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'Connect ${kind.label}',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 6),
            Text(
              _whatsapp
                  ? 'Use the phone number ID, access token, app secret, and verify token from Meta Developer → WhatsApp → API Setup.'
                  : 'Enter the Signal account number that signal-cli is registered as, then allow the phones that may talk to Jarvis.',
              style: Theme.of(
                context,
              ).textTheme.bodySmall?.copyWith(color: JarvisColors.inkSoft),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _name,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(labelText: 'Display name'),
            ),
            const SizedBox(height: 10),
            TextField(
              key: const Key('channel-account'),
              controller: _account,
              keyboardType: TextInputType.phone,
              decoration: InputDecoration(
                labelText: _whatsapp
                    ? 'WhatsApp phone number ID'
                    : 'Signal account number',
                hintText: _whatsapp ? '106540352242922' : '+31612345678',
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              key: const Key('channel-senders'),
              controller: _senders,
              minLines: 2,
              maxLines: 4,
              decoration: const InputDecoration(
                labelText: 'Allowed phone numbers',
                hintText: '+31612345678',
                helperText:
                    'One international number per line. Only these can talk to Jarvis.',
              ),
            ),
            const SizedBox(height: 10),
            TextField(
              controller: _notify,
              keyboardType: TextInputType.phone,
              decoration: const InputDecoration(
                labelText: 'Notification number (optional)',
                helperText: 'Must be one of the allowed numbers.',
              ),
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Enabled'),
              value: _enabled,
              onChanged: (value) => setState(() => _enabled = value),
            ),
            SwitchListTile(
              contentPadding: EdgeInsets.zero,
              title: const Text('Forward Jarvis notifications'),
              value: _forward,
              onChanged: (value) => setState(() => _forward = value),
            ),
            if (_whatsapp) ...[
              TextField(
                key: const Key('channel-access-token'),
                controller: _accessToken,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'Access token'),
              ),
              const SizedBox(height: 10),
              TextField(
                key: const Key('channel-app-secret'),
                controller: _appSecret,
                obscureText: true,
                decoration: const InputDecoration(labelText: 'App secret'),
              ),
              const SizedBox(height: 10),
              TextField(
                key: const Key('channel-verify-token'),
                controller: _verifyToken,
                decoration: const InputDecoration(
                  labelText: 'Webhook verify token',
                ),
              ),
            ],
            if (_error != null)
              InlineNotice(
                message: _error!,
                tone: NoticeTone.danger,
                margin: const EdgeInsets.only(top: 12),
              ),
            const SizedBox(height: 16),
            FilledButton(
              key: const Key('channel-save'),
              onPressed: _saving ? null : () => unawaited(_save()),
              child: _saving
                  ? const SizedBox.square(
                      dimension: 18,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : Text('Connect ${kind.label}'),
            ),
          ],
        ),
      ),
    );
  }
}
