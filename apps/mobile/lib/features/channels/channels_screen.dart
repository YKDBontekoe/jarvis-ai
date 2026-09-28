import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

part 'channel_detail_screen.dart';
part 'channel_editor_sheet.dart';

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
      } catch (_) {
        // Ignore a malformed Signal status payload.
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
