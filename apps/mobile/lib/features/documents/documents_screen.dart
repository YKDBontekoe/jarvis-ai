import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'document_screen.dart';

/// Drafts you can edit, save and export. Creating one sends nothing.
class DocumentsScreen extends StatefulWidget {
  const DocumentsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<DocumentsScreen> createState() => _DocumentsScreenState();
}

class _DocumentsScreenState extends State<DocumentsScreen> {
  List<Map<String, dynamic>> _drafts = const [];
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/documents');
      if (!mounted) return;
      setState(() {
        _drafts = [
          for (final item in jsonMaps(response.data))
            if (jsonString(item, 'id') != null) item,
        ];
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your drafts.';
      });
    }
  }

  Future<void> _open(String id) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => DocumentScreen(http: widget.http, documentId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create() async {
    var projects = <Map<String, dynamic>>[];
    try {
      projects = jsonMaps(
        (await widget.http.get<dynamic>('/api/v1/projects')).data,
      );
    } on DioException {
      // Without projects there is nothing to update.
    }
    if (!mounted) return;
    final draft = await showModalBottomSheet<Map<String, Object?>>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _NewDocumentSheet(projects: projects),
    );
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/documents',
        data: draft,
      );
      final id = jsonString(
        jsonObject(jsonObject(response.data)?['draft']) ?? const {},
        'id',
      );
      if (id != null && mounted) await _open(id);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.maybeOf(context)
        ?..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'Could not start the draft.',
            ),
          ),
        );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Documents'),
        actions: [
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: _create,
          ),
        ],
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: _drafts.isEmpty
                      ? Padding(
                          padding: const EdgeInsets.only(top: 24),
                          child: EmptyState(
                            icon: PhosphorIconsRegular.notePencil,
                            title: 'Turn what you know into something to share',
                            message:
                                'Start a one-page project update from your own '
                                'records. You edit it, nothing is sent.',
                            action: PillAction(
                              key: const Key('document-start'),
                              primary: true,
                              icon: PhosphorIconsRegular.notePencil,
                              label: 'Start a project update',
                              onPressed: _create,
                            ),
                          ),
                        )
                      : Column(
                          crossAxisAlignment: CrossAxisAlignment.stretch,
                          children: [
                            for (var i = 0; i < _drafts.length; i++)
                              FeatureRow(
                                key: Key('document-${_drafts[i]['id']}'),
                                index: i,
                                icon: PhosphorIconsRegular.fileText,
                                caption: 'Project update',
                                title:
                                    asJsonString(_drafts[i]['title']) ??
                                    'Draft',
                                subtitle:
                                    'Version ${asJsonInt(_drafts[i]['version'])}',
                                trailing:
                                    asJsonInt(_drafts[i]['savedVersion']) ==
                                        asJsonInt(_drafts[i]['version'])
                                    ? StatusPill(
                                        label: 'Saved',
                                        color: JarvisColors.of(context).accent,
                                      )
                                    : null,
                                onTap: () => unawaited(
                                  _open(_drafts[i]['id'] as String),
                                ),
                              ),
                          ],
                        ),
                ),
              ],
            ),
    );
  }
}

class _NewDocumentSheet extends StatefulWidget {
  const _NewDocumentSheet({required this.projects});

  final List<Map<String, dynamic>> projects;

  @override
  State<_NewDocumentSheet> createState() => _NewDocumentSheetState();
}

class _NewDocumentSheetState extends State<_NewDocumentSheet> {
  String? _project;
  String _audience = 'team';
  String _language = 'en';

  @override
  Widget build(BuildContext context) => SafeArea(
    child: SingleChildScrollView(
      padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Text(
            'Project update',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 12),
          if (widget.projects.isEmpty)
            const Text('Create a project first; the update is built from it.')
          else
            DropdownButtonFormField<String?>(
              key: const Key('document-project'),
              initialValue: _project,
              decoration: const InputDecoration(labelText: 'Which project?'),
              items: [
                for (final project in widget.projects)
                  if (jsonString(project, 'id') != null)
                    DropdownMenuItem(
                      value: jsonString(project, 'id'),
                      child: Text(asJsonString(project['name']) ?? 'Project'),
                    ),
              ],
              onChanged: (value) => setState(() => _project = value),
            ),
          const SizedBox(height: 12),
          Wrap(
            spacing: 8,
            children: [
              for (final audience in const ['team', 'client', 'self'])
                ChoiceChip(
                  key: Key('document-audience-$audience'),
                  label: Text(audience),
                  selected: _audience == audience,
                  onSelected: (_) => setState(() => _audience = audience),
                ),
            ],
          ),
          Wrap(
            spacing: 8,
            children: [
              for (final language in const ['en', 'nl'])
                ChoiceChip(
                  key: Key('document-language-$language'),
                  label: Text(language == 'en' ? 'English' : 'Nederlands'),
                  selected: _language == language,
                  onSelected: (_) => setState(() => _language = language),
                ),
            ],
          ),
          const SizedBox(height: 14),
          FilledButton(
            key: const Key('document-create'),
            onPressed: _project == null
                ? null
                : () => Navigator.pop<Map<String, Object?>>(context, {
                    'kind': 'project_update',
                    'projectId': _project,
                    'audience': _audience,
                    'language': _language,
                  }),
            child: const Text('Draft it'),
          ),
        ],
      ),
    ),
  );
}
