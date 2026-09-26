import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load({String? query}) async {
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
      final entries = records.map((item) {
        final record = item as Map<String, dynamic>;
        return _searching ? record['memory'] as Map<String, dynamic> : record;
      }).toList();
      if (mounted) setState(() => _memories = entries);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error = error.response?.statusCode == 401
              ? 'Your sign-in has expired. Sign in again to manage memory.'
              : 'Jarvis could not load memory. Check the API connection and try again.',
        );
      }
    } finally {
      if (mounted) setState(() => _loading = false);
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
    final delete = await showDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Delete memory?'),
        content: Text('Jarvis will forget “${memory['content']}”.'),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Delete'),
          ),
        ],
      ),
    );
    if (delete != true) return;
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
      text: memory['content'] as String? ?? '',
    );
    var kind = _memoryKinds.contains(memory['kind'])
        ? memory['kind'] as String
        : 'other';
    var pinned = memory['isPinned'] as bool? ?? false;
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
    final pinned = !(memory['isPinned'] as bool? ?? false);
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
    appBar: AppBar(title: const Text('Jarvis memory')),
    floatingActionButton: FloatingActionButton.extended(
      onPressed: _createMemory,
      icon: const Icon(Icons.add),
      label: const Text('Add memory'),
    ),
    body: Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 12),
          child: TextField(
            controller: _query,
            textInputAction: TextInputAction.search,
            onSubmitted: (value) => _load(query: value.trim()),
            decoration: InputDecoration(
              hintText: 'Search what Jarvis remembers',
              prefixIcon: const Icon(Icons.search),
              suffixIcon: IconButton(
                tooltip: 'Clear search',
                onPressed: () {
                  _query.clear();
                  _load();
                },
                icon: const Icon(Icons.close),
              ),
            ),
          ),
        ),
        SizedBox(
          height: 48,
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
        if (_error != null)
          MaterialBanner(
            content: Text(_error!),
            leading: const Icon(Icons.info_outline),
            actions: [TextButton(onPressed: _load, child: const Text('Retry'))],
          ),
        Expanded(
          child: _loading
              ? const Center(child: CircularProgressIndicator())
              : _memories.isEmpty
              ? Center(
                  child: Text(
                    _searching
                        ? 'No matching memories.'
                        : 'No memories yet. Add one to get started.',
                  ),
                )
              : ListView.builder(
                  padding: const EdgeInsets.fromLTRB(16, 4, 16, 92),
                  itemCount: _memories.length,
                  itemBuilder: (context, index) {
                    final memory = _memories[index];
                    final isPinned = memory['isPinned'] as bool? ?? false;
                    final validUntil = DateTime.tryParse(
                      memory['validUntil'] as String? ?? '',
                    );
                    final isSuperseded =
                        validUntil != null &&
                        !validUntil.isAfter(DateTime.now());
                    return Card(
                      margin: const EdgeInsets.only(bottom: 10),
                      child: Padding(
                        padding: const EdgeInsets.fromLTRB(16, 12, 4, 12),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Row(
                              children: [
                                Chip(
                                  label: Text(
                                    memory['kind'] as String? ?? 'fact',
                                  ),
                                ),
                                if (isSuperseded)
                                  const Chip(label: Text('Superseded')),
                                if (isPinned)
                                  const Padding(
                                    padding: EdgeInsets.only(left: 4),
                                    child: Icon(Icons.push_pin, size: 17),
                                  ),
                                const Spacer(),
                                IconButton(
                                  tooltip: isPinned
                                      ? 'Unpin memory'
                                      : 'Pin memory',
                                  onPressed: () => _togglePinned(memory),
                                  icon: Icon(
                                    isPinned
                                        ? Icons.push_pin
                                        : Icons.push_pin_outlined,
                                  ),
                                ),
                                IconButton(
                                  tooltip: 'Edit memory',
                                  onPressed: () => _editMemory(memory),
                                  icon: const Icon(Icons.edit_outlined),
                                ),
                                IconButton(
                                  tooltip: 'Delete memory',
                                  onPressed: () => _deleteMemory(memory),
                                  icon: const Icon(Icons.delete_outline),
                                ),
                              ],
                            ),
                            Padding(
                              padding: const EdgeInsets.only(right: 12),
                              child: Text(memory['content'] as String? ?? ''),
                            ),
                            if (memory['sourceType'] == 'conversation')
                              const Padding(
                                padding: EdgeInsets.only(top: 6),
                                child: Text('Learned from a conversation'),
                              ),
                          ],
                        ),
                      ),
                    );
                  },
                ),
        ),
      ],
    ),
  );

  Widget _kindFilter({required String label, required String? value}) =>
      Padding(
        padding: const EdgeInsets.symmetric(horizontal: 4),
        child: ChoiceChip(
          label: Text(label),
          selected: _selectedKind == value,
          onSelected: (_) {
            setState(() => _selectedKind = value);
            _load(query: _query.text.trim());
          },
        ),
      );
}
