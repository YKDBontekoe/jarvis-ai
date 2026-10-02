import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../channels/channels_screen.dart';
import 'read_along_screen.dart';
import 'whatsapp_models.dart';

/// Everyday entry to the owner's linked WhatsApp accounts and read-along chats.
class WhatsAppScreen extends StatefulWidget {
  const WhatsAppScreen({required this.http, super.key});
  final Dio http;

  @override
  State<WhatsAppScreen> createState() => _WhatsAppScreenState();
}

class _WhatsAppScreenState extends State<WhatsAppScreen> {
  List<Map<String, dynamic>> _accounts = const [];
  String? _selectedId;
  String? _error;
  bool _loading = true;
  bool _canLink = false;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/channels');
      var canLink = false;
      try {
        final providers = await widget.http.get<dynamic>(
          '/api/v1/channels/providers',
        );
        canLink = asJsonBool(jsonObject(providers.data)?['whatsAppLink']);
      } catch (_) {
        // Existing accounts still work when an older server does not advertise linking.
      }
      final accounts = jsonMaps(response.data)
          .where(
            (account) =>
                asJsonString(account['kind']) == 'whatsapp_linked' &&
                asJsonString(account['id']) != null,
          )
          .toList();
      // Prefer the personal account over a channel configured for automatic replies.
      accounts.sort(
        (a, b) => jsonStrings(
          a['allowedSenders'],
        ).length.compareTo(jsonStrings(b['allowedSenders']).length),
      );
      if (!mounted) return;
      setState(() {
        _accounts = accounts;
        if (!accounts.any(
          (account) => asJsonString(account['id']) == _selectedId,
        )) {
          _selectedId = accounts.isEmpty
              ? null
              : asJsonString(accounts.first['id']);
        }
        _canLink = canLink;
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error =
              firstProblemMessage(error.response?.data) ??
              'Could not load your WhatsApp accounts.';
        });
      }
    } catch (_) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = 'Could not load your WhatsApp accounts.';
        });
      }
    }
  }

  Future<void> _connect() async {
    final linked = await showModalBottomSheet<Map<String, dynamic>>(
      context: context,
      isScrollControlled: true,
      builder: (_) => ChannelLinkSheet(
        http: widget.http,
        kind: 'whatsapp_linked',
        readAlong: true,
      ),
    );
    if (linked == null || !mounted) return;
    _selectedId = asJsonString(linked['channelId']);
    await _load();
  }

  Future<void> _manage() async {
    final id = _selectedId;
    if (id == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => ChannelDetailScreen(http: widget.http, channelId: id),
      ),
    );
    if (mounted) await _load();
  }

  @override
  Widget build(BuildContext context) {
    final id = _selectedId;
    if (!_loading && _error == null && id != null) {
      final account = _accounts.firstWhere((a) => asJsonString(a['id']) == id);
      return ReadAlongScreen(
        key: ValueKey(id),
        http: widget.http,
        channelId: id,
        title: 'WhatsApp',
        selecting: false,
        account: asJsonString(account['account']),
        onManageAccount: () => unawaited(_manage()),
        onConnect: _canLink ? () => unawaited(_connect()) : null,
        accountPickerBuilder: _accounts.length < 2
            ? null
            : (context, phone, state) => PopupMenuButton<String>(
                key: const Key('whatsapp-account-picker'),
                tooltip: 'Switch WhatsApp account',
                initialValue: id,
                onSelected: (value) => setState(() => _selectedId = value),
                itemBuilder: (_) => [
                  for (final item in _accounts)
                    PopupMenuItem(
                      value: asJsonString(item['id']),
                      child: Text(
                        '${asJsonString(item['displayName']) ?? 'WhatsApp'} · ${asJsonString(item['account']) ?? ''}',
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                      ),
                    ),
                ],
                child: Row(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Flexible(
                      child: Text(
                        '$phone · ${whatsAppConnectionLabel(state)}',
                        key: const Key('whatsapp-account-status'),
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ),
                    const SizedBox(width: 4),
                    Icon(
                      PhosphorIconsRegular.caretDown,
                      size: 12,
                      color: JarvisColors.of(context).muted,
                    ),
                  ],
                ),
              ),
      );
    }
    return Scaffold(
      appBar: AppBar(title: const Text('WhatsApp')),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: true,
        onRetry: () => unawaited(_load()),
        empty: EmptyState(
          icon: PhosphorIconsRegular.whatsappLogo,
          title: 'Your WhatsApp, with Jarvis',
          message: _canLink
              ? 'Connect your personal account, then choose which chats Jarvis may read. You control every message sent.'
              : 'Your server needs the WhatsApp bridge to connect a personal account.',
          action: FilledButton.icon(
            key: const Key('whatsapp-connect-account'),
            onPressed: _canLink ? () => unawaited(_connect()) : null,
            icon: const Icon(PhosphorIconsRegular.plus),
            label: const Text('Connect your WhatsApp'),
          ),
        ),
        child: const SizedBox.shrink(),
      ),
    );
  }
}
