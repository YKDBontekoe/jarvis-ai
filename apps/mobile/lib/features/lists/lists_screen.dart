import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Plain lists such as shopping or packing. Jarvis can add to them from chat;
/// here the owner ticks items off, adds more and clears the ticked ones.
class ListsScreen extends StatefulWidget {
  const ListsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ListsScreen> createState() => _ListsScreenState();
}

class _ListsScreenState extends State<ListsScreen> {
  List<Map<String, dynamic>> _lists = const [];
  bool _loading = true;
  String? _error;
  int _revision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_revision;
    try {
      final response = await widget.http.get<dynamic>('/api/v1/lists');
      if (!mounted || revision != _revision) return;
      setState(() {
        _lists = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your lists.';
      });
    }
  }

  /// Runs a change and swaps in the list the server sends back, so the screen
  /// always shows what is stored.
  Future<void> _change(String path, Map<String, Object?> body) async {
    try {
      await widget.http.post<dynamic>('/api/v1/lists/$path', data: body);
      await _load();
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text(
              firstProblemMessage(error.response?.data) ??
                  'That did not work. Try again.',
            ),
          ),
        );
    }
  }

  /// Removes an item and offers Undo, which puts it back (ticked again if
  /// it was ticked).
  Future<void> _removeWithUndo(
    Map<String, dynamic> list,
    String text,
    bool wasDone,
  ) async {
    final name = list['name'];
    final messenger = ScaffoldMessenger.of(context);
    await _change('remove', {
      'list': name,
      'items': [text],
    });
    if (!mounted) return;
    messenger
      ..hideCurrentSnackBar()
      ..showSnackBar(
        SnackBar(
          content: Text('Removed “$text”'),
          action: SnackBarAction(
            label: 'Undo',
            onPressed: () => unawaited(() async {
              await _change('items', {
                'list': name,
                'items': [text],
              });
              if (wasDone) {
                await _change('done', {
                  'list': name,
                  'items': [text],
                  'done': true,
                });
              }
            }()),
          ),
        ),
      );
  }

  Future<void> _newList() async {
    final name = await _askText(
      context,
      title: 'New list',
      hint: 'Shopping',
      label: 'List name',
    );
    if (name == null || !mounted) return;
    final item = await _askText(
      context,
      title: 'First item',
      hint: 'Milk',
      label: 'Item',
    );
    if (item == null) return;
    await _change('items', {
      'list': name,
      'items': [item],
    });
  }

  Future<void> _rename(Map<String, dynamic> list) async {
    final name = await _askText(
      context,
      title: 'Rename list',
      hint: asJsonString(list['name']) ?? '',
      label: 'New name',
    );
    if (name == null) return;
    await _change('rename', {'list': list['name'], 'newName': name});
  }

  Future<void> _delete(Map<String, dynamic> list) async {
    final name = asJsonString(list['name']) ?? 'this list';
    final ok = await showJarvisConfirm(
      context,
      title: 'Delete “$name”?',
      message: 'The list and its items are removed.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!ok) return;
    try {
      await widget.http.delete<dynamic>('/api/v1/lists/${list['id']}');
      await _load();
    } on DioException {
      if (mounted) {
        ScaffoldMessenger.of(context)
          ..hideCurrentSnackBar()
          ..showSnackBar(
            const SnackBar(content: Text('Could not delete the list.')),
          );
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Lists'),
      actions: [
        HeaderAction(
          label: 'New list',
          icon: PhosphorIconsRegular.plus,
          onPressed: () => unawaited(_newList()),
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _lists.isEmpty,
      onRetry: _load,
      empty: EmptyState(
        icon: PhosphorIconsRegular.listChecks,
        title: 'No lists yet',
        message:
            'Start a shopping or packing list here, or tell Jarvis '
            '“add milk to my shopping list”.',
        action: FilledButton.icon(
          key: const Key('lists-new'),
          onPressed: () => unawaited(_newList()),
          icon: const Icon(PhosphorIconsRegular.plus, size: 18),
          label: const Text('New list'),
        ),
      ),
      child: OrbRefresh(
        onRefresh: _load,
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            for (final list in _lists)
              ContentWidth(
                child: _ListCard(
                  key: ValueKey(list['id']),
                  list: list,
                  onAdd: (text) => _change('items', {
                    'list': list['name'],
                    'items': [text],
                  }),
                  onToggle: (text, done) => _change('done', {
                    'list': list['name'],
                    'items': [text],
                    'done': done,
                  }),
                  onClear: () => _change('clear-done', {'list': list['name']}),
                  onRemoveItem: (text) {
                    final wasDone = jsonMaps(list['items']).any(
                      (item) =>
                          asJsonString(item['text']) == text &&
                          asJsonBool(item['done']),
                    );
                    // A swiped-away row must leave the tree at once, so take
                    // it out locally first; the reload that follows confirms.
                    setState(() {
                      _lists = [
                        for (final entry in _lists)
                          if (entry['id'] == list['id'])
                            {
                              ...entry,
                              'items': [
                                for (final item in jsonMaps(entry['items']))
                                  if (asJsonString(item['text']) != text) item,
                              ],
                            }
                          else
                            entry,
                      ];
                    });
                    unawaited(_removeWithUndo(list, text, wasDone));
                  },
                  onDelete: () => unawaited(_delete(list)),
                  onRename: () => unawaited(_rename(list)),
                ),
              ),
          ],
        ),
      ),
    ),
  );
}

Future<String?> _askText(
  BuildContext context, {
  required String title,
  required String hint,
  required String label,
}) async {
  final result = await showJarvisDialog<String>(
    context: context,
    builder: (dialogContext) =>
        _AskTextDialog(title: title, hint: hint, label: label),
  );
  return result == null || result.isEmpty ? null : result;
}

/// Owns its text controller, so it is only disposed once the dialog has fully
/// left the screen (disposing it right after the route pops breaks the
/// closing animation).
class _AskTextDialog extends StatefulWidget {
  const _AskTextDialog({
    required this.title,
    required this.hint,
    required this.label,
  });

  final String title;
  final String hint;
  final String label;

  @override
  State<_AskTextDialog> createState() => _AskTextDialogState();
}

class _AskTextDialogState extends State<_AskTextDialog> {
  final _controller = TextEditingController();

  @override
  void dispose() {
    _controller.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: TextField(
      controller: _controller,
      autofocus: true,
      maxLength: 60,
      buildCounter: quietCounter,
      textCapitalization: TextCapitalization.sentences,
      decoration: InputDecoration(
        hintText: widget.hint,
        labelText: widget.label,
      ),
      onSubmitted: (value) => Navigator.pop(context, value.trim()),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: const Text('Add'),
      ),
    ],
  );
}

class _ListCard extends StatefulWidget {
  const _ListCard({
    required this.list,
    required this.onAdd,
    required this.onToggle,
    required this.onClear,
    required this.onDelete,
    required this.onRename,
    required this.onRemoveItem,
    super.key,
  });

  final Map<String, dynamic> list;
  final ValueChanged<String> onAdd;
  final void Function(String text, bool done) onToggle;
  final VoidCallback onClear;
  final VoidCallback onDelete;
  final VoidCallback onRename;
  final ValueChanged<String> onRemoveItem;

  @override
  State<_ListCard> createState() => _ListCardState();
}

class _ListCardState extends State<_ListCard> {
  final _controller = TextEditingController();
  final _focus = FocusNode();

  @override
  void dispose() {
    _controller.dispose();
    _focus.dispose();
    super.dispose();
  }

  /// Adds the item and keeps the keyboard up, so a shopping list can be
  /// typed in one go. An empty submit puts the keyboard away.
  void _submit() {
    final text = _controller.text.trim();
    if (text.isEmpty) {
      _focus.unfocus();
      return;
    }
    HapticFeedback.selectionClick();
    _controller.clear();
    widget.onAdd(text);
    _focus.requestFocus();
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final name = asJsonString(widget.list['name']) ?? 'List';
    // Open items first, ticked ones sink to the bottom; each group keeps
    // the order it was added in.
    final all = jsonMaps(widget.list['items']);
    final items = [
      ...all.where((i) => !asJsonBool(i['done'])),
      ...all.where((i) => asJsonBool(i['done'])),
    ];
    final ticked =
        items.length - all.where((i) => !asJsonBool(i['done'])).length;
    final open = items.length - ticked;
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 14),
      padding: const EdgeInsets.fromLTRB(16, 12, 8, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Expanded(
                child: InkWell(
                  key: Key('list-title-${widget.list['id']}'),
                  onLongPress: widget.onRename,
                  child: Semantics(
                    hint: 'Long press to rename',
                    child: Text(
                      name,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                  ),
                ),
              ),
              Text(
                items.isNotEmpty && open == 0 ? 'All done' : '$open open',
                style: TextStyle(
                  fontSize: 13,
                  color: items.isNotEmpty && open == 0
                      ? colors.accent
                      : colors.inkSoft,
                  fontWeight: items.isNotEmpty && open == 0
                      ? FontWeight.w600
                      : FontWeight.w400,
                  fontFeatures: const [FontFeature.tabularFigures()],
                ),
              ),
              IconButton(
                tooltip: 'Delete $name',
                onPressed: widget.onDelete,
                icon: Icon(
                  PhosphorIconsRegular.trash,
                  size: 20,
                  color: colors.muted,
                ),
              ),
            ],
          ),
          for (final item in items)
            Dismissible(
              key: ValueKey('dismiss-${item['id']}'),
              direction: DismissDirection.endToStart,
              onDismissed: (_) =>
                  widget.onRemoveItem(asJsonString(item['text']) ?? ''),
              background: Container(
                alignment: Alignment.centerRight,
                padding: const EdgeInsets.only(right: 16),
                decoration: BoxDecoration(
                  color: colors.dangerSoft,
                  borderRadius: BorderRadius.circular(JarvisRadii.sm),
                ),
                child: Icon(
                  PhosphorIconsRegular.trash,
                  color: colors.danger,
                  semanticLabel: 'Remove',
                ),
              ),
              child: CheckboxListTile(
                key: ValueKey(item['id']),
                contentPadding: EdgeInsets.zero,
                controlAffinity: ListTileControlAffinity.leading,
                value: asJsonBool(item['done']),
                onChanged: (value) {
                  HapticFeedback.selectionClick();
                  widget.onToggle(
                    asJsonString(item['text']) ?? '',
                    value ?? false,
                  );
                },
                title: AnimatedDefaultTextStyle(
                  duration: JarvisMotion.of(context, JarvisMotion.base),
                  curve: JarvisMotion.standard,
                  style: DefaultTextStyle.of(context).style.merge(
                    TextStyle(
                      fontFamily: 'Geist',
                      fontSize: 15,
                      color: asJsonBool(item['done'])
                          ? colors.muted
                          : colors.ink,
                      decoration: asJsonBool(item['done'])
                          ? TextDecoration.lineThrough
                          : TextDecoration.none,
                      decorationColor: colors.muted,
                    ),
                  ),
                  child: Text(asJsonString(item['text']) ?? ''),
                ),
              ),
            ),
          Padding(
            padding: const EdgeInsets.only(right: 8),
            child: TextField(
              controller: _controller,
              focusNode: _focus,
              textInputAction: TextInputAction.done,
              // Handled in _submit so adding an item keeps the keyboard up.
              onEditingComplete: () {},
              textCapitalization: TextCapitalization.sentences,
              onSubmitted: (_) => _submit(),
              decoration: InputDecoration(
                hintText: 'Add an item',
                suffixIcon: IconButton(
                  tooltip: 'Add to $name',
                  onPressed: _submit,
                  icon: const Icon(PhosphorIconsRegular.plus, size: 20),
                ),
              ),
            ),
          ),
          if (ticked > 0)
            Padding(
              padding: const EdgeInsets.only(top: 10, bottom: 4),
              child: Align(
                alignment: Alignment.centerLeft,
                child: PillAction(
                  compact: true,
                  icon: PhosphorIconsRegular.broom,
                  label: 'Clear $ticked ticked',
                  onPressed: widget.onClear,
                ),
              ),
            ),
        ],
      ),
    );
  }
}
