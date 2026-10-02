import 'dart:async';
import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../whatsapp/read_along_screen.dart';

part 'channel_detail_screen.dart';
part 'channel_allowed_senders_sheet.dart';
part 'channel_editor_sheet.dart';
part 'channel_link_sheet.dart';
part 'channel_notifications_sheet.dart';
part 'channel_thread_screen.dart';

({String label, IconData icon, Color color}) channelKind(String kind) =>
    switch (kind) {
      'whatsapp' || 'whatsapp_linked' => (
        label: 'WhatsApp',
        icon: PhosphorIconsRegular.whatsappLogo,
        color: const Color(0xff16a34a),
      ),
      'signal' => (
        label: 'Signal',
        icon: PhosphorIconsRegular.chatsCircle,
        color: JarvisColors.light.info,
      ),
      _ => (
        label: kind,
        icon: PhosphorIconsRegular.broadcast,
        color: JarvisColors.light.muted,
      ),
    };

/// Link WhatsApp or Signal by scanning a QR code (or connect the WhatsApp Cloud
/// API), restrict who can talk to Jarvis, and review recent messages.
class ChannelsScreen extends StatefulWidget {
  const ChannelsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ChannelsScreen> createState() => _ChannelsScreenState();
}

class _ChannelsScreenState extends State<ChannelsScreen> {
  List<Map<String, dynamic>> _channels = const [];
  Map<String, dynamic> _signal = const {};
  Map<String, dynamic> _providers = const {};
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
      } catch (_) {
        // Ignore a malformed Signal status payload.
      }
      Map<String, dynamic> providers = const {};
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/channels/providers',
        );
        providers = jsonObject(response.data) ?? const {};
      } on DioException {
        // Older servers do not report providers; assume linking is unavailable.
      } catch (_) {
        // Ignore a malformed providers payload.
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _channels = jsonMaps(channels.data);
        _signal = signal;
        _providers = providers;
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
    final id = asJsonString(channel['id']);
    if (id == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => ChannelDetailScreen(http: widget.http, channelId: id),
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

  Future<void> _link(
    String kind, {
    String? channelId,
    bool readAlong = false,
  }) async {
    final linked = await showModalBottomSheet<Map<String, dynamic>>(
      context: context,
      isScrollControlled: true,
      builder: (_) => ChannelLinkSheet(
        http: widget.http,
        kind: kind,
        channelId: channelId,
        readAlong: readAlong,
      ),
    );
    if (linked == null || !mounted) return;
    unawaited(_load());
    if (readAlong) {
      final id = asJsonString(linked['channelId']);
      if (id != null) {
        await Navigator.of(context).push<void>(
          MaterialPageRoute<void>(
            builder: (_) => ReadAlongScreen(http: widget.http, channelId: id),
          ),
        );
        if (mounted) unawaited(_load());
      }
      return;
    }
    final label = channelKind(kind).label;
    final phone = asJsonString(linked['phone']);
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(
          '$label linked${phone == null ? '' : ' as $phone'}. '
          'Message yourself in $label to chat with Jarvis.',
        ),
      ),
    );
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
        isEmpty: false,
        onRetry: () => unawaited(_load()),
        empty: const SizedBox.shrink(),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          children: [
            ContentWidth(
              child: SurfaceCard(
                child: ListTile(
                  key: const Key('connect-personal-whatsapp'),
                  leading: const Icon(PhosphorIconsRegular.whatsappLogo),
                  title: const Text('Your WhatsApp · Read along'),
                  subtitle: Text(
                    asJsonBool(_providers['whatsAppLink'])
                        ? 'Connect your own account alongside Jarvis. Choose chats for reply drafts, questions and reminders.'
                        : 'Connecting your own account needs the WhatsApp bridge on the server.',
                  ),
                  enabled: asJsonBool(_providers['whatsAppLink']),
                  trailing: const Icon(PhosphorIconsRegular.plus),
                  onTap: () =>
                      unawaited(_link('whatsapp_linked', readAlong: true)),
                ),
              ),
            ),
            const SizedBox(height: 12),
            if (_channels.isEmpty)
              EmptyState(
                icon: PhosphorIconsRegular.chatsCircle,
                title: 'No messaging channels yet',
                message:
                    'Connect a channel to talk to Jarvis from WhatsApp or Signal.',
                action: FilledButton.icon(
                  onPressed: () => unawaited(_pickKind()),
                  icon: const Icon(PhosphorIconsRegular.plus),
                  label: const Text('Connect a channel'),
                ),
              ),
            if (!signalReady)
              ContentWidth(
                child: InlineNotice(
                  message:
                      'Signal needs signal-cli on the server. WhatsApp links by QR code when the WhatsApp bridge is running, or through a Meta Cloud API app.',
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
            if (asJsonString(channel['kind']) == 'whatsapp_linked' &&
                senders.isEmpty)
              'Read along',
            asJsonString(channel['account']) ?? '',
            if (senders.isNotEmpty)
              '${senders.length} allowed number${senders.length == 1 ? '' : 's'}',
            if (!enabled) 'Paused',
            if (error != null) 'Last error: $error',
          ].where((part) => part.isNotEmpty).join(' · '),
        ),
        isThreeLine: error != null,
        trailing: Icon(
          PhosphorIconsRegular.caretRight,
          size: 16,
          color: JarvisColors.of(context).muted,
        ),
        onTap: () => unawaited(_open(channel)),
      ),
    );
  }

  Future<void> _pickKind() async {
    final whatsappLink = asJsonBool(_providers['whatsAppLink']);
    final signalLink = asJsonBool(_providers['signal']);
    final choice = await showModalBottomSheet<String>(
      context: context,
      builder: (context) => SafeArea(
        child: Padding(
          padding: const EdgeInsets.fromLTRB(8, 8, 8, 16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              ListTile(
                key: const Key('connect-whatsapp-read-along'),
                enabled: whatsappLink,
                leading: const Icon(PhosphorIconsRegular.whatsappLogo),
                title: const Text('Your WhatsApp · Read along'),
                subtitle: Text(
                  whatsappLink
                      ? 'Connect your own account and choose chats Jarvis may read'
                      : 'Needs the WhatsApp bridge on the server',
                ),
                onTap: () => Navigator.pop(context, 'whatsapp_read_along'),
              ),
              ListTile(
                key: const Key('connect-whatsapp-qr'),
                enabled: whatsappLink,
                leading: IconBadge(
                  icon: PhosphorIconsRegular.whatsappLogo,
                  color: const Color(0xff16a34a),
                ),
                title: const Text('Jarvis WhatsApp channel'),
                subtitle: Text(
                  whatsappLink
                      ? 'Scan a QR code — no Meta account needed'
                      : 'Needs the WhatsApp bridge on the server',
                ),
                onTap: () => Navigator.pop(context, 'whatsapp_linked'),
              ),
              ListTile(
                key: const Key('connect-signal'),
                enabled: signalLink,
                leading: IconBadge(
                  icon: PhosphorIconsRegular.chatsCircle,
                  color: JarvisColors.of(context).info,
                ),
                title: const Text('Signal'),
                subtitle: Text(
                  signalLink
                      ? 'Scan a QR code to link this server'
                      : 'Needs signal-cli on the server',
                ),
                onTap: () => Navigator.pop(context, 'signal'),
              ),
              ListTile(
                key: const Key('connect-whatsapp'),
                leading: IconBadge(
                  icon: PhosphorIconsRegular.gearSix,
                  color: JarvisColors.of(context).muted,
                ),
                title: const Text('WhatsApp Business API'),
                subtitle: const Text('Advanced: Meta Cloud API with a webhook'),
                onTap: () => Navigator.pop(context, 'whatsapp'),
              ),
            ],
          ),
        ),
      ),
    );
    if (choice == null || !mounted) return;
    if (choice == 'whatsapp_read_along') {
      unawaited(_link('whatsapp_linked', readAlong: true));
    } else if (choice == 'whatsapp') {
      unawaited(_create(choice));
    } else {
      unawaited(_link(choice));
    }
  }
}
