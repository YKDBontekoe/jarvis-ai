import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'json_maps.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

const _memoryKinds = [
  'preference',
  'fact',
  'decision',
  'project',
  'event',
  'relationship',
  'technical',
  'routine',
  'other',
];

class MemoryScreen extends StatefulWidget {
  const MemoryScreen({required this.http, super.key});

  final Dio http;

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
          ? await widget.http.get<List<dynamic>>(
              '/api/v1/memory',
              queryParameters: {
                if (_selectedKind != null) 'kind': _selectedKind,
              },
            )
          : await widget.http.get<List<dynamic>>(
              '/api/v1/memory/search',
              queryParameters: {
                'query': query,
                if (_selectedKind != null) 'kind': _selectedKind,
              },
            );
      final records = response.data ?? [];
      final entries = <Map<String, dynamic>>[];
      for (final item in records) {
        if (item is! Map) continue;
        final record = Map<String, dynamic>.from(item);
        if (_searching) {
          final memory = record['memory'];
          if (memory is Map) {
            entries.add(Map<String, dynamic>.from(memory));
          }
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
    final formKey = GlobalKey<FormState>();
    final content = TextEditingController();
    var kind = 'fact';
    var pinned = false;
    final created = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('Add a memory'),
          content: Form(
            key: formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  initialValue: kind,
                  decoration: const InputDecoration(labelText: 'Type'),
                  items: _memoryKinds
                      .map(
                        (value) =>
                            DropdownMenuItem(value: value, child: Text(value)),
                      )
                      .toList(),
                  onChanged: (value) =>
                      setDialogState(() => kind = value ?? 'fact'),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: content,
                  autofocus: true,
                  minLines: 2,
                  maxLines: 5,
                  maxLength: 8000,
                  decoration: const InputDecoration(
                    labelText: 'What should Jarvis remember?',
                    alignLabelWithHint: true,
                  ),
                  validator: (value) => value == null || value.trim().isEmpty
                      ? 'Enter something to remember.'
                      : null,
                ),
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Pin this memory'),
                  value: pinned,
                  onChanged: (value) => setDialogState(() => pinned = value),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                if (formKey.currentState?.validate() ?? false) {
                  Navigator.pop(dialogContext, true);
                }
              },
              child: const Text('Save'),
            ),
          ],
        ),
      ),
    );
    if (created != true) {
      content.dispose();
      return;
    }
    if (!mounted) {
      content.dispose();
      return;
    }
    try {
      await widget.http.post(
        '/api/v1/memory',
        data: {
          'kind': kind,
          'content': content.text.trim(),
          'importance': pinned ? 0.9 : 0.5,
          'confidence': 1.0,
          'isPinned': pinned,
        },
      );
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not save this memory.');
    } finally {
      content.dispose();
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
    try {
      await widget.http.delete('/api/v1/memory/${memory['id']}');
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not delete this memory.');
    }
  }

  Future<void> _editMemory(Map<String, dynamic> memory) async {
    final formKey = GlobalKey<FormState>();
    final content = TextEditingController(
      text: asJsonString(memory['content']) ?? '',
    );
    var kind = _memoryKinds.contains(memory['kind'])
        ? asJsonString(memory['kind']) ?? 'other'
        : 'other';
    var pinned = asJsonBool(memory['isPinned']);
    final saved = await showDialog<bool>(
      context: context,
      builder: (dialogContext) => StatefulBuilder(
        builder: (context, setDialogState) => AlertDialog(
          title: const Text('Correct this memory'),
          content: Form(
            key: formKey,
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                DropdownButtonFormField<String>(
                  initialValue: kind,
                  decoration: const InputDecoration(labelText: 'Type'),
                  items: _memoryKinds
                      .map(
                        (value) =>
                            DropdownMenuItem(value: value, child: Text(value)),
                      )
                      .toList(),
                  onChanged: (value) =>
                      setDialogState(() => kind = value ?? 'other'),
                ),
                const SizedBox(height: 12),
                TextFormField(
                  controller: content,
                  autofocus: true,
                  minLines: 2,
                  maxLines: 5,
                  maxLength: 8000,
                  decoration: const InputDecoration(
                    labelText: 'What should Jarvis remember?',
                    alignLabelWithHint: true,
                  ),
                  validator: (value) => value == null || value.trim().isEmpty
                      ? 'Enter something to remember.'
                      : null,
                ),
                SwitchListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Pin this memory'),
                  value: pinned,
                  onChanged: (value) => setDialogState(() => pinned = value),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(dialogContext, false),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () {
                if (formKey.currentState?.validate() ?? false) {
                  Navigator.pop(dialogContext, true);
                }
              },
              child: const Text('Save correction'),
            ),
          ],
        ),
      ),
    );
    if (saved != true) {
      content.dispose();
      return;
    }
    if (!mounted) {
      content.dispose();
      return;
    }
    try {
      await widget.http.put(
        '/api/v1/memory/${memory['id']}',
        data: {
          'kind': kind,
          'content': content.text.trim(),
          'importance': memory['importance'] ?? 0.5,
          'confidence': memory['confidence'] ?? 0.8,
          'validUntil': memory['validUntil'],
          'isPinned': pinned,
        },
      );
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
      if (mounted) _showError('Jarvis could not correct this memory.');
    } finally {
      content.dispose();
    }
  }

  Future<void> _togglePinned(Map<String, dynamic> memory) async {
    final pinned = !asJsonBool(memory['isPinned']);
    try {
      await widget.http.put(
        '/api/v1/memory/${memory['id']}',
        data: {...memory, 'isPinned': pinned},
      );
      if (mounted) await _load(query: _searching ? _query.text.trim() : null);
    } on DioException {
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
                fillColor: JarvisColors.surface,
                enabledBorder: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                  borderSide: const BorderSide(color: JarvisColors.outline),
                ),
                focusedBorder: OutlineInputBorder(
                  borderRadius: BorderRadius.circular(JarvisRadii.md),
                  borderSide: const BorderSide(
                    color: JarvisColors.ink,
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
        if (_error != null)
          ContentWidth(
            child: InlineNotice(
              message: _error!,
              tone: NoticeTone.danger,
              margin: const EdgeInsets.fromLTRB(16, 8, 16, 0),
              actions: [
                TextButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          ),
        Expanded(
          child: _loading
              ? const LoadingState()
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
    final validUntil = DateTime.tryParse(asJsonString(memory['validUntil']) ?? '');
    final isSuperseded =
        validUntil != null && !validUntil.isAfter(DateTime.now());
    final kind = asJsonString(memory['kind']) ?? 'fact';
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 10, 6, 14),
      borderColor: JarvisColors.outline,
      color: isSuperseded ? JarvisColors.canvas : JarvisColors.surface,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              _KindTag(label: kind, icon: _kindIcon(kind)),
              if (isSuperseded) ...[
                const SizedBox(width: 6),
                const StatusPill(
                  label: 'Superseded',
                  color: JarvisColors.muted,
                ),
              ],
              const Spacer(),
              IconButton(
                tooltip: isPinned ? 'Unpin memory' : 'Pin memory',
                onPressed: () => _togglePinned(memory),
                visualDensity: VisualDensity.compact,
                style: IconButton.styleFrom(
                  foregroundColor: isPinned
                      ? JarvisColors.ink
                      : JarvisColors.muted,
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
                color: isSuperseded ? JarvisColors.inkSoft : JarvisColors.ink,
                decoration: isSuperseded ? TextDecoration.lineThrough : null,
                decorationColor: JarvisColors.muted,
              ),
            ),
          ),
          if (memory['sourceType'] == 'conversation')
            const Padding(
              padding: EdgeInsets.only(top: 10),
              child: Row(
                children: [
                  Icon(
                    PhosphorIconsRegular.sparkle,
                    size: 14,
                    color: JarvisColors.muted,
                  ),
                  SizedBox(width: 6),
                  Text(
                    'Learned from a conversation',
                    style: TextStyle(
                      fontSize: 12.5,
                      color: JarvisColors.inkSoft,
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
    _ => PhosphorIconsRegular.notepad,
  };

  Widget _kindFilter({required String label, required String? value}) =>
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 4),
        child: ChoiceChip(
          label: Text(label),
          selected: _selectedKind == value,
          selectedColor: JarvisColors.ink,
          side: BorderSide(
            color: _selectedKind == value
                ? JarvisColors.ink
                : JarvisColors.outline,
          ),
          labelStyle: TextStyle(
            fontSize: 13,
            fontWeight: FontWeight.w500,
            color: _selectedKind == value ? Colors.white : JarvisColors.inkSoft,
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
      Icon(icon, size: 14, color: JarvisColors.muted),
      const SizedBox(width: 6),
      Text(
        label.isEmpty ? 'Other' : label[0].toUpperCase() + label.substring(1),
        style: const TextStyle(
          fontSize: 12.5,
          fontWeight: FontWeight.w500,
          color: JarvisColors.muted,
        ),
      ),
    ],
  );
}
