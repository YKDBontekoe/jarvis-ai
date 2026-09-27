import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

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

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final agents = await widget.http.get<List<dynamic>>('/api/v1/agents');
      final tokens = await widget.http.get<List<dynamic>>('/api/v1/a2a/tokens');
      if (!mounted) return;
      setState(() {
        _agents = jsonMaps(agents.data);
        _tokens = jsonMaps(tokens.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load Agent2Agent settings.';
      });
    }
  }

  Future<void> _addAgent() async {
    final name = TextEditingController();
    final url = TextEditingController();
    final token = TextEditingController();
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
  }

  Future<void> _createToken() async {
    try {
      final response = await widget.http.post<Map<String, dynamic>>(
        '/api/v1/a2a/tokens',
        data: {'name': 'App ${DateTime.now().toIso8601String().substring(0, 10)}'},
      );
      if (!mounted) return;
      setState(() => _newToken = asJsonString(response.data?['token']));
      unawaited(_load());
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Could not create a token.',
      );
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
                Text(
                  'REMOTE AGENTS',
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: JarvisColors.muted,
                    letterSpacing: .8,
                  ),
                ),
                const SizedBox(height: 8),
                if (_agents.isEmpty)
                  const Text(
                    'Add another assistant’s Agent2Agent URL. Jarvis will ask before delegating.',
                  )
                else
                  for (final agent in _agents)
                    SurfaceCard(
                      child: ListTile(
                        leading: const IconBadge(
                          icon: PhosphorIconsRegular.robot,
                        ),
                        title: Text(asJsonString(agent['name']) ?? 'Agent'),
                        subtitle: Text(asJsonString(agent['url']) ?? ''),
                        trailing: IconButton(
                          tooltip: 'Remove',
                          onPressed: () async {
                            await widget.http.delete(
                              '/api/v1/agents/${agent['id']}',
                            );
                            unawaited(_load());
                          },
                          icon: const Icon(PhosphorIconsRegular.trash),
                        ),
                      ),
                    ),
                const SizedBox(height: 24),
                Text(
                  'INBOUND TOKENS',
                  style: Theme.of(context).textTheme.labelSmall?.copyWith(
                    color: JarvisColors.muted,
                    letterSpacing: .8,
                  ),
                ),
                const SizedBox(height: 8),
                const Text(
                  'Other agents use these bearer tokens at POST /a2a. Copy a token when you create it; Jarvis only stores a hash.',
                ),
                const SizedBox(height: 8),
                FilledButton.icon(
                  onPressed: () => unawaited(_createToken()),
                  icon: const Icon(PhosphorIconsRegular.key),
                  label: const Text('Create token'),
                ),
                if (_newToken != null)
                  InlineNotice(
                    message: 'Copy this token now: $_newToken',
                    tone: NoticeTone.info,
                    margin: const EdgeInsets.only(top: 12),
                    actions: [
                      TextButton(
                        onPressed: () => Clipboard.setData(
                          ClipboardData(text: _newToken!),
                        ),
                        child: const Text('Copy'),
                      ),
                    ],
                  ),
                const SizedBox(height: 8),
                for (final token in _tokens)
                  ListTile(
                    leading: const Icon(PhosphorIconsRegular.key),
                    title: Text(asJsonString(token['name']) ?? 'Token'),
                    subtitle: Text(
                      asJsonString(token['createdAt']) ?? '',
                    ),
                    trailing: IconButton(
                      onPressed: () async {
                        await widget.http.delete(
                          '/api/v1/a2a/tokens/${token['id']}',
                        );
                        unawaited(_load());
                      },
                      icon: const Icon(PhosphorIconsRegular.trash),
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    ),
  );
}
