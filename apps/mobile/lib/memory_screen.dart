import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import 'ui/phosphor_icons.dart';

import 'features/memory/knowledge_graph_screen.dart';
import 'json_maps.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

part 'memory_editor.dart';

const _memoryKinds = [
  'preference',
  'fact',
  'decision',
  'project',
  'event',
  'relationship',
  'technical',
  'routine',
  'journal',
  'other',
];

class MemoryScreen extends StatefulWidget {
  const MemoryScreen({
    required this.http,
    this.startCreating = false,
    super.key,
  });

  final Dio http;

  /// Opens the "new" editor as soon as the page has settled.
  final bool startCreating;

  @override
  State<MemoryScreen> createState() => _MemoryScreenState();
}

class _MemoryScreenState extends State<MemoryScreen> {
  final _query = TextEditingController();
  List<Map<String, dynamic>> _memories = [];
  bool _loading = true;
  String? _error;
  bool _searching = false;
  String? _selectedKind;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.startCreating) afterRouteSettles(this, _createMemory);
  }

  Future<void> _load({String? query}) async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
      _searching = query?.isNotEmpty ?? false;
    });
    try {
      final response = query == null || query.isEmpty
          ? await widget.http.get<dynamic>(
              '/api/v1/memory',
              queryParameters: {
                if (_selectedKind != null) 'kind': _selectedKind,
              },
            )
          : await widget.http.get<dynamic>(
              '/api/v1/memory/search',
              queryParameters: {
                'query': query,
                if (_selectedKind != null) 'kind': _selectedKind,
              },
            );
      final records = jsonMaps(response.data);
      final entries = <Map<String, dynamic>>[];
      for (final record in records) {
        if (_searching) {
          final memory = jsonObject(record['memory']);
          if (memory != null) entries.add(memory);
        } else {
          entries.add(record);
        }
      }
      if (mounted && revision == _requestRevision) {
        setState(() => _memories = entries);
      }
    } on DioException catch (error) {
      if (mounted && revision == _requestRevision) {
        setState(
          () => _error = error.response?.statusCode == 401
              ? 'Your sign-in has expired. Sign in again to manage memory.'
              : 'Jarvis could not load memory. Check the API connection and try again.',
        );
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(
          () => _error =
              'Jarvis could not load memory. Check the API connection and try again.',
        );
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _createMemory() async {
    final draft = await showDialog<_MemoryDraft>(
      context: context,
      builder: (_) =>
          const _MemoryEditorDialog(title: 'Add a memory', saveLabel: 'Save'),
    );
    if (draft == null || !mounted) return;
    try {
      await widget.http.post(
        '/api/v1/memory',
        data: {
          'kind': draft.kind,
          'content': draft.content,
          'importance': draft.pinned ? 0.9 : 0.5,
          'confidence': 1.0,
          'isPinned': draft.pinned,
        },
      );
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not save this memory.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not save this memory.');
    }
  }

  Future<void> _deleteMemory(Map<String, dynamic> memory) async {
    final delete = await showJarvisConfirm(
      context,
      title: 'Delete memory?',
      message: 'Jarvis will forget “${memory['content']}”.',
      cancelLabel: 'Keep',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!delete) return;
    if (!mounted) return;
    final id = jsonId(memory);
    if (id == null) return;
    try {
      await widget.http.delete('/api/v1/memory/$id');
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not delete this memory.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not delete this memory.');
    }
  }

  Future<void> _editMemory(Map<String, dynamic> memory) async {
    final draft = await showDialog<_MemoryDraft>(
      context: context,
      builder: (_) => _MemoryEditorDialog(
        title: 'Correct this memory',
        saveLabel: 'Save correction',
        kind: asJsonString(memory['kind']),
        content: asJsonString(memory['content']) ?? '',
        pinned: asJsonBool(memory['isPinned']),
        kindFallback: 'other',
      ),
    );
    if (draft == null || !mounted) return;
    final id = jsonId(memory);
    if (id == null) return;
    try {
      await widget.http.put(
        '/api/v1/memory/$id',
        data: {
          'kind': draft.kind,
          'content': draft.content,
          'importance': memory['importance'] ?? 0.5,
          'confidence': memory['confidence'] ?? 0.8,
          'validUntil': activeValidUntil(memory),
          'isPinned': draft.pinned,
        },
      );
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not correct this memory.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not correct this memory.');
    }
  }

  Future<void> _togglePinned(Map<String, dynamic> memory) async {
    final id = jsonId(memory);
    if (id == null) return;
    final pinned = !asJsonBool(memory['isPinned']);
    try {
      await widget.http.put(
        '/api/v1/memory/$id',
        data: {
          'kind': memory['kind'],
          'content': memory['content'],
          'importance': memory['importance'] ?? 0.5,
          'confidence': memory['confidence'] ?? 0.8,
          'validUntil': activeValidUntil(memory),
          'isPinned': pinned,
        },
      );
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not update this memory.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not update this memory.');
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  void dispose() {
    _query.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Memory'),
      actions: [
        IconButton(
          tooltip: 'Knowledge graph',
          onPressed: () => Navigator.of(context).push<void>(
            MaterialPageRoute(
              builder: (_) => KnowledgeGraphScreen(http: widget.http),
            ),
          ),
          icon: const Icon(PhosphorIconsRegular.graph),
        ),
        HeaderAction(
          label: 'Add',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createMemory,
        ),
      ],
    ),
    body: Column(
      children: [
        ContentWidth(
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 12),
            child: TextField(
              controller: _query,
              textInputAction: TextInputAction.search,
              onSubmitted: (value) => _load(query: value.trim()),
              decoration: InputDecoration(
                hintText: 'Search what Jarvis remembers',
                fillColor: JarvisColors.of(context).surface,
                enabledBorder: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                  borderSide: BorderSide(
                    color: JarvisColors.of(context).outline,
                  ),
                ),
                focusedBorder: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                  borderSide: BorderSide(
                    color: JarvisColors.of(context).ink,
                    width: 1.2,
                  ),
                ),
                prefixIcon: const Icon(PhosphorIconsRegular.magnifyingGlass),
                suffixIcon: IconButton(
                  tooltip: 'Clear search',
                  onPressed: () {
                    _query.clear();
                    _load();
                  },
                  icon: const Icon(PhosphorIconsRegular.x, size: 20),
                ),
              ),
            ),
          ),
        ),
        ContentWidth(
          child: SizedBox(
            height: 44,
            child: ListView(
              scrollDirection: Axis.horizontal,
              padding: const EdgeInsets.symmetric(horizontal: 12),
              children: [
                _kindFilter(label: 'All', value: null),
                ..._memoryKinds.map(
                  (kind) => _kindFilter(
                    label: kind[0].toUpperCase() + kind.substring(1),
                    value: kind,
                  ),
                ),
              ],
            ),
          ),
        ),
        if (_error != null && _memories.isNotEmpty)
          ContentWidth(
            child: InlineNotice(
              message: _error!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              actions: [
                TextButton(
                  onPressed: () =>
                      _load(query: _searching ? _query.text.trim() : null),
                  child: const Text('Retry'),
                ),
              ],
            ),
          ),
        Expanded(
          child: _loading
              ? const LoadingState()
              : _error != null && _memories.isEmpty
              ? ErrorState(
                  message: _error!,
                  onRetry: () =>
                      _load(query: _searching ? _query.text.trim() : null),
                )
              : _memories.isEmpty
              ? EmptyState(
                  icon: _searching
                      ? PhosphorIconsRegular.magnifyingGlass
                      : PhosphorIconsRegular.brain,
                  title: _searching
                      ? 'No matching memories.'
                      : 'No memories yet',
                  message: _searching
                      ? 'Try a different phrase or clear the filter.'
                      : 'No memories yet. Add one to get started.',
                )
              : ListView.builder(
                  padding: EdgeInsets.fromLTRB(
                    16,
                    10,
                    16,
                    32 + MediaQuery.paddingOf(context).bottom,
                  ),
                  itemCount: _memories.length,
                  itemBuilder: (context, index) =>
                      ContentWidth(child: _memoryCard(_memories[index])),
                ),
        ),
      ],
    ),
  );

  Widget _memoryCard(Map<String, dynamic> memory) {
    final isPinned = asJsonBool(memory['isPinned']);
    final validUntil = jsonDate(memory['validUntil']);
    final isSuperseded =
        validUntil != null && !validUntil.isAfter(DateTime.now());
    final kind = asJsonString(memory['kind']) ?? 'fact';
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 10, 6, 14),
      borderColor: JarvisColors.of(context).outline,
      color: isSuperseded
          ? JarvisColors.of(context).canvas
          : JarvisColors.of(context).surface,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _KindTag(label: kind, icon: _kindIcon(kind)),
              if (isSuperseded) ...[
                const SizedBox(width: 6),
                StatusPill(
                  label: 'Superseded',
                  color: JarvisColors.of(context).muted,
                ),
              ],
              const Spacer(),
              IconButton(
                tooltip: isPinned ? 'Unpin memory' : 'Pin memory',
                onPressed: () => _togglePinned(memory),
                visualDensity: VisualDensity.compact,
                style: IconButton.styleFrom(
                  foregroundColor: isPinned
                      ? JarvisColors.of(context).ink
                      : JarvisColors.of(context).muted,
                ),
                icon: Icon(
                  isPinned
                      ? PhosphorIconsFill.pushPin
                      : PhosphorIconsRegular.pushPin,
                  size: 19,
                ),
              ),
              IconButton(
                tooltip: 'Edit memory',
                onPressed: () => _editMemory(memory),
                visualDensity: VisualDensity.compact,
                icon: const Icon(PhosphorIconsRegular.pencilSimple, size: 19),
              ),
              IconButton(
                tooltip: 'Delete memory',
                onPressed: () => _deleteMemory(memory),
                visualDensity: VisualDensity.compact,
                icon: const Icon(PhosphorIconsRegular.trash, size: 19),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Padding(
            padding: const EdgeInsets.only(right: 12),
            child: Text(
              asJsonString(memory['content']) ?? '',
              style: TextStyle(
                fontSize: 15,
                height: 1.5,
                color: isSuperseded
                    ? JarvisColors.of(context).inkSoft
                    : JarvisColors.of(context).ink,
                decoration: isSuperseded ? TextDecoration.lineThrough : null,
                decorationColor: JarvisColors.of(context).muted,
              ),
            ),
          ),
          if (memory['sourceType'] == 'conversation')
            Padding(
              padding: EdgeInsets.only(top: 10),
              child: Row(
                children: [
                  Icon(
                    PhosphorIconsRegular.sparkle,
                    size: 14,
                    color: JarvisColors.of(context).muted,
                  ),
                  SizedBox(width: 6),
                  Text(
                    'Learned from a conversation',
                    style: TextStyle(
                      fontSize: 12.5,
                      color: JarvisColors.of(context).inkSoft,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  IconData _kindIcon(String kind) => switch (kind) {
    'preference' => PhosphorIconsRegular.heart,
    'fact' => PhosphorIconsRegular.lightbulb,
    'decision' => PhosphorIconsRegular.gavel,
    'project' => PhosphorIconsRegular.folderSimple,
    'event' => PhosphorIconsRegular.calendarBlank,
    'relationship' => PhosphorIconsRegular.users,
    'technical' => PhosphorIconsRegular.code,
    'routine' => PhosphorIconsRegular.repeat,
    'journal' => PhosphorIconsRegular.notebook,
    _ => PhosphorIconsRegular.notepad,
  };

  Widget _kindFilter({required String label, required String? value}) =>
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 4),
        child: ChoiceChip(
          label: Text(label),
          selected: _selectedKind == value,
          selectedColor: JarvisColors.of(context).ink,
          side: BorderSide(
            color: _selectedKind == value
                ? JarvisColors.of(context).ink
                : JarvisColors.of(context).outline,
          ),
          labelStyle: TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w500,
            color: _selectedKind == value
                ? JarvisColors.of(context).onInk
                : JarvisColors.of(context).inkSoft,
          ),
          onSelected: (_) {
            setState(() => _selectedKind = value);
            _load(query: _query.text.trim());
          },
        ),
      );
}

class _KindTag extends StatelessWidget {
  const _KindTag({required this.label, required this.icon});

  final String label;
  final IconData icon;

  @override
  Widget build(BuildContext context) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Icon(icon, size: 14, color: JarvisColors.of(context).muted),
      const SizedBox(width: 6),
      Text(
        label.isEmpty ? 'Other' : label[0].toUpperCase() + label.substring(1),
        style: TextStyle(
          fontSize: 12.5,
          fontWeight: FontWeight.w500,
          color: JarvisColors.of(context).muted,
        ),
      ),
    ],
  );
}
