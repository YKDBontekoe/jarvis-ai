part of 'integrations_screen.dart';

mixin _IntegrationsPacks on _IntegrationsController {
  Widget _packCard(Map<String, dynamic> status) {
    final pack = jsonObject(status['pack']) ?? status;
    final id = asJsonString(pack['id']) ?? '';
    final name = asJsonString(pack['name']) ?? 'Pack';
    final installed = asJsonBool(status['installed']);
    final description = asJsonString(pack['description']) ?? '';
    final icon = switch (asJsonString(pack['category'])) {
      'calendar' => PhosphorIconsRegular.calendarBlank,
      'mail' => PhosphorIconsRegular.paperPlaneTilt,
      'contacts' => PhosphorIconsRegular.addressBook,
      _ => PhosphorIconsRegular.plugsConnected,
    };
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              IconBadge(icon: icon),
              const SizedBox(width: 12),
              Expanded(
                child: Text(name, style: Theme.of(context).textTheme.titleMedium),
              ),
              StatusPill(
                label: installed ? 'Connected' : 'Not connected',
                color: installed ? JarvisColors.success : JarvisColors.muted,
              ),
            ],
          ),
          if (description.isNotEmpty) ...[
            const SizedBox(height: 10),
            Text(
              description,
              style: const TextStyle(color: JarvisColors.inkSoft, height: 1.4),
            ),
          ],
          const SizedBox(height: 8),
          Wrap(
            spacing: 4,
            children: [
              TextButton.icon(
                onPressed: id.isEmpty ? null : () => _installPack(status),
                icon: const Icon(PhosphorIconsRegular.plugsConnected, size: 18),
                label: Text(installed ? 'Update pack' : 'Set up'),
              ),
              if (asJsonString(pack['authKind']) == 'oauth' ||
                  asJsonString(status['mcpServerId']) != null)
                TextButton.icon(
                  onPressed: () => _connectOAuth(
                    server: asJsonString(status['mcpServerId']),
                    endpoint: asJsonString(pack['suggestedEndpoint']),
                  ),
                  icon: const Icon(PhosphorIconsRegular.lockSimple, size: 18),
                  label: const Text('Connect with OAuth'),
                ),
            ],
          ),
        ],
      ),
    );
  }

  Future<void> _installPack(Map<String, dynamic> status) async {
    final pack = jsonObject(status['pack']) ?? status;
    final id = asJsonString(pack['id']);
    if (id == null) return;
    final supportsIcs = asJsonBool(pack['supportsIcs']);
    final ics = TextEditingController();
    final token = TextEditingController();
    final endpoint = TextEditingController(
      text: asJsonString(pack['suggestedEndpoint']) ?? '',
    );
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: Text('Set up ${asJsonString(pack['name']) ?? 'pack'}'),
        content: SizedBox(
          width: 480,
          child: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                if (supportsIcs) ...[
                  TextFormField(
                    controller: ics,
                    keyboardType: TextInputType.url,
                    decoration: const InputDecoration(
                      labelText: 'ICS/iCal HTTPS URL',
                      hintText: 'https://calendar.google.com/.../basic.ics',
                    ),
                  ),
                  const SizedBox(height: 8),
                  TextFormField(
                    controller: token,
                    obscureText: true,
                    decoration: const InputDecoration(
                      labelText: 'Optional feed token',
                    ),
                  ),
                  const SizedBox(height: 8),
                ],
                TextFormField(
                  controller: endpoint,
                  keyboardType: TextInputType.url,
                  decoration: InputDecoration(
                    labelText: supportsIcs
                        ? 'Optional MCP HTTPS endpoint'
                        : 'MCP HTTPS endpoint (or leave empty for stdio)',
                  ),
                ),
                const SizedBox(height: 12),
                Text(
                  supportsIcs
                      ? 'An ICS URL is enough for today’s events on Home. Add an MCP endpoint only if Jarvis should create events too.'
                      : 'Jarvis can start the suggested stdio MCP server, or you can paste a public HTTPS MCP endpoint. Authorize with OAuth next — never paste tokens in chat.',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ],
            ),
          ),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext, false),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, true),
            child: const Text('Save'),
          ),
        ],
      ),
    );
    final icsUrl = ics.text.trim();
    final icsToken = token.text.trim();
    final mcpEndpoint = endpoint.text.trim();
    ics.dispose();
    token.dispose();
    endpoint.dispose();
    if (saved != true || !mounted) return;
    if (supportsIcs &&
        icsUrl.isNotEmpty &&
        parsePublicHttpsUrl(icsUrl) == null) {
      setState(() => _error = 'Calendar feeds need a public HTTPS ICS URL.');
      return;
    }
    try {
      await widget.http.post<void>(
        '/api/v1/integrations/packs/$id',
        data: {
          if (icsUrl.isNotEmpty) 'icsUrl': icsUrl,
          if (icsToken.isNotEmpty) 'icsToken': icsToken,
          if (mcpEndpoint.isNotEmpty) 'endpoint': mcpEndpoint,
        },
      );
      if (mounted) await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not install that pack.',
        );
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not install that pack.');
    }
  }

  Future<void> _connectOAuth({String? server, String? endpoint}) async {
    final target = (server ?? '').trim();
    final url = (endpoint ?? '').trim();
    if (target.isEmpty && url.isEmpty) {
      setState(
        () => _error =
            'OAuth needs an MCP server or a public HTTPS endpoint.',
      );
      return;
    }
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/integrations/oauth/sessions',
        data: {
          if (target.isNotEmpty) 'server': target,
          if (target.isEmpty && url.isNotEmpty) 'endpoint': url,
        },
      );
      final body = jsonObject(response.data) ?? const {};
      final status = asJsonString(body['status']);
      final authorize = parseHttpUrl(asJsonString(body['authorizationUrl']));
      if (status == 'needs_token') {
        await _editSecret(
          provider: target.isNotEmpty ? target : 'jarvis-mcp-discovery',
          secretName: 'token',
        );
        return;
      }
      if (authorize == null) {
        setState(
          () => _error =
              'This server did not return an authorization URL. Store a token instead.',
        );
        return;
      }
      final opened = await launchHttpUrl(authorize);
      if (!opened && mounted) {
        setState(() => _error = 'Could not open the authorization page.');
        return;
      }
      final id = asJsonString(body['id']);
      if (id == null || id.isEmpty || id == '00000000-0000-0000-0000-000000000000') {
        if (mounted) await _load();
        return;
      }
      for (var i = 0; i < 45; i++) {
        await Future<void>.delayed(const Duration(seconds: 2));
        if (!mounted) return;
        try {
          final poll = await widget.http.get<dynamic>(
            '/api/v1/integrations/oauth/sessions/$id',
          );
          final session = jsonObject(poll.data);
          final state = asJsonString(session?['status']);
          if (state == 'completed') {
            if (mounted) {
              ScaffoldMessenger.of(context).showSnackBar(
                const SnackBar(content: Text('Authorization finished.')),
              );
              await _load();
            }
            return;
          }
          if (state == 'failed') {
            setState(
              () => _error =
                  asJsonString(session?['error']) ??
                  'Authorization did not finish. You can store a token instead.',
            );
            return;
          }
        } catch (_) {
          break;
        }
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not start authorization.',
        );
      }
    } catch (_) {
      if (mounted) setState(() => _error = 'Could not start authorization.');
    }
  }
}
