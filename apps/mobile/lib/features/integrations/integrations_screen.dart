import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../ui/phosphor_icons.dart';

import '../../theme.dart';
import '../../json_maps.dart';
import '../../http_urls.dart';
import '../../ui/jarvis_ui.dart';
import '../chat/mcp_setup.dart';
import 'app_catalog_screen.dart';
import 'app_presentation.dart';
import 'server_detail_screen.dart';

part 'integrations_apps.dart';
part 'integrations_credentials.dart';
part 'integrations_mcp.dart';
part 'integrations_packs.dart';

class IntegrationsScreen extends StatefulWidget {
  const IntegrationsScreen({
    required this.http,
    this.onAskInChat,
    this.startBrowsing = false,
    super.key,
  });

  final Dio http;
  final ValueChanged<String>? onAskInChat;

  /// Opens app search as soon as the page shows.
  final bool startBrowsing;

  @override
  State<IntegrationsScreen> createState() => _IntegrationsScreenState();
}

/// Holds integration fields so credential and MCP mixins can share state.
abstract class _IntegrationsController extends State<IntegrationsScreen> {
  List<Map<String, dynamic>> _providers = [];
  List<Map<String, dynamic>> _connections = [];
  List<Map<String, dynamic>> _managedServers = [];
  List<Map<String, dynamic>> _packs = [];
  List<Map<String, dynamic>> _customServers = [];
  bool _loading = true;
  bool _credentialsFailed = false;
  bool _connectionsFailed = false;
  bool _serversFailed = false;
  String? _error;
  int _requestRevision = 0;

  Future<void> _load();
  Future<void> _editSecret({String? provider, String? secretName});
  Future<void> _connectOAuth({String? server, String? endpoint});
  void _askInChat(String prompt);
  Widget _featuredRow({required String provider, required String steps});
}

class _IntegrationsScreenState extends _IntegrationsController
    with _IntegrationsCredentials, _IntegrationsMcp, _IntegrationsPacks {
  @override
  void initState() {
    super.initState();
    _load();
    if (widget.startBrowsing) afterRouteSettles(this, _openCatalog);
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
        error = 'Could not load your saved keys.';
      } catch (_) {
        credentialsFailed = true;
        error = 'Could not load your saved keys.';
      }
      try {
        final connectionResponse = await widget.http.get<dynamic>(
          '/api/v1/integrations/connections',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _connections = jsonMaps(connectionResponse.data));
      } on DioException {
        connectionsFailed = true;
        error ??= 'Could not check how your apps are doing.';
      } catch (_) {
        connectionsFailed = true;
        error ??= 'Could not check how your apps are doing.';
      }
      try {
        final serversResponse = await widget.http.get<dynamic>(
          '/api/v1/mcp-servers',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _managedServers = jsonMaps(serversResponse.data));
      } on DioException {
        serversFailed = true;
        error ??= 'Could not load your apps.';
      } catch (_) {
        serversFailed = true;
        error ??= 'Could not load your apps.';
      }
      try {
        final customResponse = await widget.http.get<dynamic>(
          '/api/v1/custom-mcp-servers',
        );
        if (!mounted || revision != _requestRevision) return;
        setState(() => _customServers = jsonMaps(customResponse.data));
      } catch (_) {
        // Older servers have no self-built apps; the list just stays empty.
        if (!mounted || revision != _requestRevision) return;
        setState(() => _customServers = []);
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
          _error ??= 'Could not load your saved keys.';
        });
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Connected apps'),
      actions: [
        HeaderAction(
          label: 'Add',
          icon: PhosphorIconsRegular.plus,
          onPressed: _showAddSheet,
        ),
      ],
    ),
    body: _loading
        ? const SkeletonList()
        : OrbRefresh(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 40),
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

  /// One place to start anything new: find an app by name, paste its
  /// address, let Jarvis guide it in chat, or add a key for an app the server
  /// already knows about.
  Future<void> _showAddSheet() async {
    final ask = widget.onAskInChat;
    final choice = await showModalBottomSheet<String>(
      context: context,
      showDragHandle: true,
      isScrollControlled: true,
      builder: (sheetContext) => SafeArea(
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(8, 0, 8, 16),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
                child: Text(
                  'Connect an app',
                  style: Theme.of(sheetContext).textTheme.titleLarge,
                ),
              ),
              ListTile(
                key: const Key('add-browse'),
                leading: const IconBadge(
                  icon: PhosphorIconsRegular.magnifyingGlass,
                ),
                title: const Text('Browse apps'),
                subtitle: const Text(
                  'Search by name, like Notion or web search, and connect in one tap',
                ),
                onTap: () => Navigator.pop(sheetContext, 'browse'),
              ),
              ListTile(
                key: const Key('add-address'),
                leading: const IconBadge(icon: PhosphorIconsRegular.linkSimple),
                title: const Text('Paste an address'),
                subtitle: const Text('When the app gave you a link to use'),
                onTap: () => Navigator.pop(sheetContext, 'address'),
              ),
              if (ask != null)
                ListTile(
                  key: const Key('add-build'),
                  leading: const IconBadge(
                    icon: PhosphorIconsRegular.magicWand,
                  ),
                  title: const Text('Have Jarvis build one'),
                  subtitle: const Text(
                    'For a service with no ready-made app: Jarvis writes it, tests it, and asks before switching it on',
                  ),
                  onTap: () => Navigator.pop(sheetContext, 'build'),
                ),
              if (ask != null)
                ListTile(
                  leading: const IconBadge(
                    icon: PhosphorIconsRegular.chatCircle,
                  ),
                  title: const Text('Ask Jarvis to set it up'),
                  subtitle: const Text(
                    'Describe what you want and Jarvis finds the right app in chat',
                  ),
                  onTap: () => Navigator.pop(sheetContext, 'chat'),
                ),
              ListTile(
                leading: const IconBadge(icon: PhosphorIconsRegular.key),
                title: const Text('Paste an access key'),
                subtitle: const Text(
                  'For an app that is already set up on your Jarvis server',
                ),
                onTap: () => Navigator.pop(sheetContext, 'key'),
              ),
            ],
          ),
        ),
      ),
    );
    if (!mounted) return;
    switch (choice) {
      case 'browse':
        await _openCatalog();
      case 'address':
        await _openCatalog(address: true);
      case 'chat':
        _askInChat(mcpSetupPrompt);
      case 'build':
        _askInChat(mcpBuildAnyPrompt);
      case 'key':
        await _editSecret();
    }
  }

  Future<void> _openCatalog({bool address = false, String? query}) async {
    final outcome = await Navigator.of(context).push<AppInstallOutcome>(
      MaterialPageRoute(
        builder: (_) => AppCatalogScreen(
          http: widget.http,
          startWithAddress: address,
          initialQuery: query,
          canBuild: widget.onAskInChat != null,
        ),
      ),
    );
    if (outcome == null || !mounted) return;
    if (outcome.nextStep == 'build') {
      _askInChat(mcpBuildPrompt(outcome.name));
      return;
    }
    if (outcome.nextStep == 'open') {
      await _openDetail(serverId: outcome.serverId);
      return;
    }
    await _load();
    if (!mounted) return;
    final message = switch (outcome.nextStep) {
      'sign_in' => 'Added ${outcome.name}. Sign in to finish.',
      'secrets' => 'Added ${outcome.name}. Add its key to finish.',
      _ => 'Added ${outcome.name}.',
    };
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
    if (outcome.nextStep == 'sign_in') {
      await _connectOAuth(server: outcome.serverId, endpoint: outcome.endpoint);
    }
  }

  @override
  void _askInChat(String prompt) {
    final ask = widget.onAskInChat;
    if (ask == null) return;
    Navigator.of(context).pop();
    ask(prompt);
  }

  List<Widget> _content() {
    final keys = _providers
        .where(
          (item) =>
              !(asJsonString(item['provider']) ?? '').startsWith('jarvis-mcp-'),
        )
        .toList();
    final connections = _connectionCards();
    final attention = _needsAttentionCount();
    return [
      Padding(
        padding: const EdgeInsets.fromLTRB(4, 4, 4, 0),
        child: Text(
          'Connect the apps you already use, so Jarvis can help with them. '
          'Jarvis asks for your OK before it acts in them, unless you allowed that action in advance.',
          style: TextStyle(
            fontSize: 14.5,
            height: 1.45,
            color: JarvisColors.of(context).inkSoft,
          ),
        ),
      ),
      const SizedBox(height: 16),
      _AppSearchEntry(onSearch: (query) => _openCatalog(query: query)),
      if (_error != null)
        InlineNotice(
          message: _error!,
          tone: NoticeTone.danger,
          margin: const EdgeInsets.only(top: 16),
        ),
      const SizedBox(height: 28),
      SectionHeader(
        'Your apps',
        trailing: attention == 0
            ? null
            : StatusPill(
                label: attention == 1 ? '1 needs you' : '$attention need you',
                color: JarvisColors.of(context).warning,
              ),
      ),
      if ((_connectionsFailed || _serversFailed) && connections.isEmpty)
        _MutedLine(
          icon: PhosphorIconsRegular.cloudSlash,
          text: 'Could not load your apps. Pull down to try again.',
        )
      else if (connections.isEmpty)
        const _MutedLine(
          icon: PhosphorIconsRegular.plugsConnected,
          text: 'Nothing connected yet. Pick an app below to get started.',
        )
      else
        ...connections,
      const SizedBox(height: 28),
      const SectionHeader('Add an app'),
      _suggestions(),
      const SizedBox(height: 28),
      _savedKeys(keys),
    ];
  }
}

/// Search the app directory without first choosing "Add". Typing and
/// pressing search opens the directory with the results.
class _AppSearchEntry extends StatefulWidget {
  const _AppSearchEntry({required this.onSearch});

  final ValueChanged<String?> onSearch;

  @override
  State<_AppSearchEntry> createState() => _AppSearchEntryState();
}

class _AppSearchEntryState extends State<_AppSearchEntry> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  void _submit([String? value]) {
    final query = (value ?? _controller.text).trim();
    widget.onSearch(query.isEmpty ? null : query);
    _controller.clear();
  }

  @override
  Widget build(BuildContext context) => TextField(
    key: const Key('integrations-search'),
    controller: _controller,
    textInputAction: TextInputAction.search,
    onSubmitted: _submit,
    decoration: InputDecoration(
      prefixIcon: const Icon(PhosphorIconsRegular.magnifyingGlass),
      hintText: 'Find an app to connect, like Notion or weather',
      suffixIcon: IconButton(
        tooltip: 'Browse apps',
        onPressed: _submit,
        icon: const Icon(PhosphorIconsRegular.caretRight),
      ),
    ),
  );
}
