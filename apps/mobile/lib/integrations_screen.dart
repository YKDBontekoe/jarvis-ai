import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import 'ui/phosphor_icons.dart';

import 'theme.dart';
import 'json_maps.dart';
import 'http_urls.dart';
import 'ui/jarvis_ui.dart';
import 'features/chat/mcp_setup.dart';

part 'integrations_credentials.dart';
part 'integrations_mcp.dart';
part 'integrations_packs.dart';

class IntegrationsScreen extends StatefulWidget {
  const IntegrationsScreen({required this.http, this.onAskInChat, super.key});

  final Dio http;
  final ValueChanged<String>? onAskInChat;

  @override
  State<IntegrationsScreen> createState() => _IntegrationsScreenState();
}

/// Holds integration fields so credential and MCP mixins can share state.
abstract class _IntegrationsController extends State<IntegrationsScreen> {
  List<Map<String, dynamic>> _providers = [];
  List<Map<String, dynamic>> _connections = [];
  List<Map<String, dynamic>> _managedServers = [];
  List<Map<String, dynamic>> _packs = [];
  bool _loading = true;
  bool _credentialsFailed = false;
  bool _connectionsFailed = false;
  bool _serversFailed = false;
  String? _error;
  int _requestRevision = 0;

  Future<void> _load();
  Future<void> _editSecret({String? provider, String? secretName});
  Future<void> _connectOAuth({String? server, String? endpoint});
}

class _IntegrationsScreenState extends _IntegrationsController
    with _IntegrationsCredentials, _IntegrationsMcp, _IntegrationsPacks {
  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    try {
      var credentialsFailed = false;
      var connectionsFailed = false;
      var serversFailed = false;
      String? error;
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/integrations/credentials',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _providers = jsonMaps(response.data));
      } on DioException {
        credentialsFailed = true;
        error = 'Could not load integration credentials.';
      } catch (_) {
        credentialsFailed = true;
        error = 'Could not load integration credentials.';
      }
      try {
        final connectionResponse = await widget.http.get<dynamic>(
          '/api/v1/integrations/connections',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _connections = jsonMaps(connectionResponse.data));
      } on DioException {
        connectionsFailed = true;
        error ??= 'Could not load integration connections.';
      } catch (_) {
        connectionsFailed = true;
        error ??= 'Could not load integration connections.';
      }
      try {
        final serversResponse = await widget.http.get<dynamic>(
          '/api/v1/mcp-servers',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _managedServers = jsonMaps(serversResponse.data));
      } on DioException {
        serversFailed = true;
        error ??= 'Could not load MCP servers.';
      } catch (_) {
        serversFailed = true;
        error ??= 'Could not load MCP servers.';
      }
      try {
        final packsResponse = await widget.http.get<dynamic>(
          '/api/v1/integrations/packs',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _packs = jsonMaps(packsResponse.data));
      } catch (_) {
        if (!mounted || revision != _requestRevision) return;
        setState(() => _packs = []);
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _credentialsFailed = credentialsFailed;
        _connectionsFailed = connectionsFailed;
        _serversFailed = serversFailed;
        _loading = false;
        _error = error;
      });
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() {
          _loading = false;
          _error ??= 'Could not load integration credentials.';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Integrations'),
      actions: [
        if (widget.onAskInChat != null)
          HeaderAction(
            label: 'Ask Jarvis',
            icon: PhosphorIconsRegular.chatCircle,
            collapsesWhenNarrow: true,
            onPressed: () {
              final ask = widget.onAskInChat!;
              Navigator.of(context).pop();
              ask(mcpSetupPrompt);
            },
          ),
        HeaderAction(
          label: 'Add',
          icon: PhosphorIconsRegular.plus,
          onPressed: () => _editSecret(),
        ),
      ],
    ),
    body: _loading
        ? const LoadingState()
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: _content(),
                  ),
                ),
              ],
            ),
          ),
  );

  List<Widget> _content() {
    final providers = _providers
        .where(
          (item) =>
              !(asJsonString(item['provider']) ?? '').startsWith('jarvis-mcp-'),
        )
        .toList();
    return [
      const InlineNotice(
        tone: NoticeTone.info,
        message: 'Add, authorize, pause, and remove MCP servers in chat. This page is the encrypted token vault and a status list.',
      ),
      if (_error != null)
        InlineNotice(
          message: _error!,
          tone: NoticeTone.danger,
          margin: const EdgeInsets.only(top: 12),
        ),
      const SizedBox(height: 24),
      const SectionHeader('Guided packs'),
      if (_packs.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.plugsConnected,
          text: 'Calendar, mail, and contacts packs appear here when the server supports them.',
        )
      else
        for (final pack in _packs) _packCard(pack),
      const SizedBox(height: 28),
      const SectionHeader('Featured'),
      _featuredCard(
        title: 'Home Assistant',
        icon: PhosphorIconsRegular.house,
        description: 'Enable Home Assistant’s MCP Server integration, expose the entities Jarvis may use, and configure HOME_ASSISTANT_MCP_URL on the Jarvis host. Store a long-lived access token here; Jarvis adds the bearer scheme when connecting. Jarvis asks for approval before every Home Assistant action.',
        provider: 'home-assistant',
      ),
      const SizedBox(height: 12),
      _featuredCard(
        title: 'GitHub',
        icon: PhosphorIconsRegular.code,
        description: 'Enable the GitHub MCP Compose overlay, then store a least-privilege personal access token here. Jarvis exposes repository, issue, and pull request tools. In chat it can pause GitHub or narrow those tools. Each operation asks for approval.',
        provider: 'github',
      ),
      const SizedBox(height: 28),
      const SectionHeader('MCP connections'),
      for (final server in _managedServers) _managedServerCard(server),
      if ((_connectionsFailed || _serversFailed) &&
          _connections.isEmpty &&
          _managedServers.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.cloudSlash,
          text: 'Could not load MCP servers.',
        )
      else if (_connections.isEmpty && _managedServers.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.cloudSlash,
          text: 'No MCP servers yet. Ask Jarvis in chat to connect one.',
        ),
      for (final connection in _connections) _connectionCard(connection),
      const SizedBox(height: 28),
      const SectionHeader('Stored credentials'),
      if (_credentialsFailed)
        const _MutedLine(
          icon: PhosphorIconsRegular.key,
          text: 'Could not load integration credentials.',
        )
      else if (providers.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.key,
          text: 'No integration credentials yet.',
        ),
      for (final item in providers) _providerCard(item),
    ];
  }
}
