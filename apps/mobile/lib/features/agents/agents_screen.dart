import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../memory/graph_model.dart';

/// Agent2Agent peers Jarvis can delegate to, plus inbound tokens other agents use.
class AgentsScreen extends StatefulWidget {
  const AgentsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<AgentsScreen> createState() => _AgentsScreenState();
}

class _AgentsScreenState extends State<AgentsScreen> {
  List<Map<String, dynamic>> _agents = const [];
  List<Map<String, dynamic>> _tokens = const [];
  bool _loading = true;
  String? _error;
  String? _newToken;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final agents = await widget.http.get<dynamic>('/api/v1/agents');
      final tokens = await widget.http.get<dynamic>('/api/v1/a2a/tokens');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _agents = jsonMaps(agents.data);
        _tokens = jsonMaps(tokens.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load Agent2Agent settings.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load Agent2Agent settings.';
      });
    }
  }

  Future<void> _addAgent() async {
    final name = TextEditingController();
    final url = TextEditingController();
    final token = TextEditingController();
    try {
      final saved = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: const Text('Add a remote agent'),
          content: Column(
            mainAxisSize: MainAxisSize.min,
            children: [
              TextField(
                controller: name,
                decoration: const InputDecoration(labelText: 'Name'),
              ),
              TextField(
                controller: url,
                decoration: const InputDecoration(
                  labelText: 'Agent2Agent URL',
                  hintText: 'https://assistant.example/a2a',
                ),
              ),
              TextField(
                controller: token,
                obscureText: true,
                decoration: const InputDecoration(
                  labelText: 'Bearer token (optional)',
                ),
              ),
            ],
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Add'),
            ),
          ],
        ),
      );
      if (saved != true || !mounted) return;
      try {
        await widget.http.post<void>(
          '/api/v1/agents',
          data: {
            'name': name.text.trim(),
            'url': url.text.trim(),
            'enabled': true,
            if (token.text.trim().isNotEmpty) 'token': token.text.trim(),
          },
        );
        unawaited(_load());
      } on DioException catch (error) {
        if (!mounted) return;
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Could not add that agent.',
        );
      }
    } finally {
      name.dispose();
      url.dispose();
      token.dispose();
    }
  }

  Future<void> _createToken() async {
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/a2a/tokens',
        data: {
          'name': 'App ${DateTime.now().toIso8601String().substring(0, 10)}',
        },
      );
      if (!mounted) return;
      setState(
        () => _newToken = asJsonString(jsonObject(response.data)?['token']),
      );
      unawaited(_load());
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not create a token.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not create a token.');
    }
  }

  Future<void> _copyNewToken() async {
    final token = _newToken;
    if (token == null) return;
    setState(() => _newToken = null);
    try {
      await Clipboard.setData(ClipboardData(text: token));
    } catch (_) {
      // The secret is already off-screen even if the clipboard write fails.
    }
    if (!mounted) return;
    ScaffoldMessenger.of(context).showSnackBar(
      const SnackBar(
        content: Text('Token copied. It will not be shown again.'),
      ),
    );
  }

  Future<void> _removeAgent(String? id) async {
    if (id == null || id.isEmpty) return;
    try {
      await widget.http.delete('/api/v1/agents/$id');
      unawaited(_load());
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not remove that agent.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not remove that agent.');
    }
  }

  Future<void> _removeToken(String? id) async {
    if (id == null || id.isEmpty) return;
    try {
      await widget.http.delete('/api/v1/a2a/tokens/$id');
      unawaited(_load());
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not remove that token.',
      );
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Could not remove that token.');
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Agents'),
      actions: [
        HeaderAction(
          label: 'Add',
          icon: PhosphorIconsRegular.plus,
          onPressed: () => unawaited(_addAgent()),
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
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                FadeSlideIn(
                  child: _section(
                    title: 'Remote agents',
                    description:
                        'Add another assistant’s Agent2Agent URL. Jarvis will ask before delegating.',
                    children: [
                      if (_agents.isEmpty)
                        const _EmptyRow(
                          icon: PhosphorIconsRegular.robot,
                          message: 'No remote agents yet.',
                        ),
                      for (final agent in _agents) _agentCard(agent),
                    ],
                  ),
                ),
                const SizedBox(height: 28),
                FadeSlideIn(
                  index: 1,
                  child: _section(
                    title: 'Inbound tokens',
                    description:
                        'Other agents use these bearer tokens at POST /a2a. Copy a token when you create it; Jarvis only stores a hash.',
                    action: OutlinedButton.icon(
                      onPressed: () => unawaited(_createToken()),
                      icon: const Icon(PhosphorIconsRegular.key, size: 18),
                      label: const Text('Create token'),
                    ),
                    children: [
                      if (_newToken != null)
                        InlineNotice(
                          message: 'Copy this token now: $_newToken',
                          tone: NoticeTone.info,
                          margin: const EdgeInsets.only(bottom: 10),
                          actions: [
                            TextButton(
                              onPressed: () => unawaited(_copyNewToken()),
                              child: const Text('Copy'),
                            ),
                          ],
                        ),
                      if (_tokens.isEmpty && _newToken == null)
                        const _EmptyRow(
                          icon: PhosphorIconsRegular.key,
                          message: 'No tokens yet.',
                        ),
                      for (final token in _tokens) _tokenCard(token),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ],
      ),
    ),
  );

  Widget _section({
    required String title,
    required String description,
    required List<Widget> children,
    Widget? action,
  }) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [
      SectionHeader(title, padding: const EdgeInsets.fromLTRB(4, 0, 0, 4)),
      Padding(
        padding: const EdgeInsets.fromLTRB(4, 0, 4, 12),
        child: Text(
          description,
          style: Theme.of(context).textTheme.bodySmall?.copyWith(height: 1.45),
        ),
      ),
      ...children,
      if (action != null)
        Align(alignment: Alignment.centerLeft, child: action),
    ],
  );

  Widget _agentCard(Map<String, dynamic> agent) => SurfaceCard(
    margin: const EdgeInsets.only(bottom: 10),
    padding: const EdgeInsets.fromLTRB(14, 10, 6, 10),
    child: Row(
      children: [
        const IconBadge(icon: PhosphorIconsRegular.robot),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                asJsonString(agent['name']) ?? 'Agent',
                style: Theme.of(context).textTheme.titleSmall,
              ),
              const SizedBox(height: 2),
              Text(
                asJsonString(agent['url']) ?? '',
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: Theme.of(context).textTheme.bodySmall,
              ),
            ],
          ),
        ),
        IconButton(
          tooltip: 'Remove',
          onPressed: () => unawaited(_removeAgent(asJsonString(agent['id']))),
          icon: const Icon(PhosphorIconsRegular.trash, size: 20),
        ),
      ],
    ),
  );

  Widget _tokenCard(Map<String, dynamic> token) {
    final created = jsonDate(token['createdAt'], local: true);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(14, 10, 6, 10),
      child: Row(
        children: [
          const IconBadge(icon: PhosphorIconsRegular.key),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  asJsonString(token['name']) ?? 'Token',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                if (created != null) ...[
                  const SizedBox(height: 2),
                  Text(
                    'Created ${formatGraphDay(created)}',
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ],
              ],
            ),
          ),
          IconButton(
            tooltip: 'Remove token',
            onPressed: () => unawaited(_removeToken(asJsonString(token['id']))),
            icon: const Icon(PhosphorIconsRegular.trash, size: 20),
          ),
        ],
      ),
    );
  }
}

/// A quiet placeholder inside a section that has nothing to list yet.
class _EmptyRow extends StatelessWidget {
  const _EmptyRow({required this.icon, required this.message});

  final IconData icon;
  final String message;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 16),
      decoration: BoxDecoration(
        borderRadius: BorderRadius.circular(JarvisRadii.lg),
        border: Border.all(color: colors.outline),
      ),
      child: Row(
        children: [
          Icon(icon, size: 20, color: colors.muted),
          const SizedBox(width: 12),
          Text(
            message,
            style: Theme.of(context).textTheme.bodyMedium?.copyWith(
              color: colors.inkSoft,
            ),
          ),
        ],
      ),
    );
  }
}
