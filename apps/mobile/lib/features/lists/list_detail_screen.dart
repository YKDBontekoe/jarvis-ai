import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'list_models.dart';
import 'lists_screen.dart';

/// One list: tap to tick items off, swipe to remove, and add new items from
/// the field pinned at the bottom.
class ListDetailScreen extends StatefulWidget {
  const ListDetailScreen({required this.http, required this.list, super.key});

  final Dio http;
  final PersonalListData list;

  @override
  State<ListDetailScreen> createState() => _ListDetailScreenState();
}

enum _Menu { rename, clear, delete }

class _ListDetailScreenState extends State<ListDetailScreen>
    with WidgetsBindingObserver {
  late PersonalListData _list = widget.list;
  final _input = TextEditingController();
  final _inputFocus = FocusNode();
  bool _adding = false;
  bool _showDone = true;

  String get _path => '/api/v1/lists/${_list.id}';

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_refresh());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    _input.dispose();
    _inputFocus.dispose();
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_refresh());
  }

  Future<void> _refresh() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      final list = PersonalListData.fromJson(response.data);
      if (mounted && list != null) setState(() => _list = list);
    } on DioException {
      // Keep showing what we have; actions report their own errors.
    }
  }

  void _show(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(context)
      ..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }

  String _problem(DioException error, String fallback) =>
      firstProblemMessage(error.response?.data) ?? fallback;

  Future<void> _add() async {
    // Pasting several lines adds one item per line.
    final texts = _input.text
        .split('\n')
        .map((line) => line.trim())
        .where((line) => line.isNotEmpty)
        .toList();
    if (texts.isEmpty || _adding) return;
    setState(() => _adding = true);
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/items',
        data: {'items': texts},
      );
      final list = PersonalListData.fromJson(response.data);
      if (!mounted) return;
      final before = _list.open.length;
      setState(() {
        if (list != null) _list = list;
        _input.clear();
      });
      if (list != null && list.open.length == before) {
        _show(
          texts.length == 1
              ? '“${texts.first}” is already on the list.'
              : 'Those items are already on the list.',
        );
      }
      _inputFocus.requestFocus();
    } on DioException catch (error) {
      _show(_problem(error, 'Could not add that item.'));
    } finally {
      if (mounted) setState(() => _adding = false);
    }
  }

  void _replaceItem(ListItemData item) => setState(
    () => _list = _list.copyWith(
      items: [for (final x in _list.items) x.id == item.id ? item : x],
    ),
  );

  Future<void> _toggle(ListItemData item) async {
    unawaited(HapticFeedback.selectionClick());
    final next = item.copyWith(done: !item.done);
    _replaceItem(next);
    try {
      await widget.http.patch<dynamic>(
        '$_path/items/${item.id}',
        data: {'done': next.done},
      );
    } on DioException catch (error) {
      if (!mounted) return;
      _replaceItem(item);
      _show(_problem(error, 'Could not update that item.'));
    }
  }

  Future<void> _remove(ListItemData item) async {
    final before = _list;
    setState(
      () => _list = _list.copyWith(
        items: [
          for (final x in _list.items)
            if (x.id != item.id) x,
        ],
      ),
    );
    try {
      await widget.http.delete<dynamic>('$_path/items/${item.id}');
      if (!mounted) return;
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(
          SnackBar(
            content: Text('Removed “${item.text}”'),
            action: SnackBarAction(
              label: 'Undo',
              onPressed: () => unawaited(_restore(item)),
            ),
          ),
        );
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() => _list = before);
      _show(_problem(error, 'Could not remove that item.'));
    }
  }

  Future<void> _restore(ListItemData item) async {
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/items',
        data: {
          'items': [item.text],
        },
      );
      var list = PersonalListData.fromJson(response.data);
      if (list != null && item.done) {
        final restored = list.items.where(
          (x) => x.text == item.text && !x.done,
        );
        if (restored.isNotEmpty) {
          await widget.http.patch<dynamic>(
            '$_path/items/${restored.first.id}',
            data: {'done': true},
          );
          list = list.copyWith(
            items: [
              for (final x in list.items)
                x.id == restored.first.id ? x.copyWith(done: true) : x,
            ],
          );
        }
      }
      if (mounted && list != null) setState(() => _list = list!);
    } on DioException catch (error) {
      _show(_problem(error, 'Could not put that item back.'));
    }
  }

  Future<void> _edit(ListItemData item) async {
    final controller = TextEditingController(text: item.text);
    final text = await showDialog<String>(
      context: context,
      builder: (dialogContext) => AlertDialog(
        title: const Text('Edit item'),
        content: TextField(
          key: const Key('list-item-edit'),
          controller: controller,
          autofocus: true,
          maxLength: 200,
          textCapitalization: TextCapitalization.sentences,
          decoration: const InputDecoration(counterText: ''),
          onSubmitted: (value) => Navigator.pop(dialogContext, value),
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(dialogContext),
            child: const Text('Cancel'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(dialogContext, controller.text),
            child: const Text('Save'),
          ),
        ],
      ),
    );
    controller.dispose();
    final trimmed = text?.trim() ?? '';
    if (trimmed.isEmpty || trimmed == item.text || !mounted) return;
    final next = item.copyWith(text: trimmed);
    _replaceItem(next);
    try {
      await widget.http.patch<dynamic>(
        '$_path/items/${item.id}',
        data: {'text': trimmed},
      );
    } on DioException catch (error) {
      if (!mounted) return;
      _replaceItem(item);
      _show(_problem(error, 'Could not rename that item.'));
    }
  }

  Future<void> _clearDone() async {
    if (_list.done.isEmpty) return;
    try {
      final response = await widget.http.post<dynamic>('$_path/clear-checked');
      final list = PersonalListData.fromJson(response.data);
      if (mounted && list != null) setState(() => _list = list);
    } on DioException catch (error) {
      _show(_problem(error, 'Could not clear checked items.'));
    }
  }

  Future<void> _rename() async {
    final draft = await showListEditor(
      context,
      name: _list.name,
      kind: _list.kind,
      title: 'Edit list',
      confirmLabel: 'Save',
    );
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.put<dynamic>(
        _path,
        data: {'name': draft.name, 'kind': draft.kind},
      );
      final list = PersonalListData.fromJson(response.data);
      if (mounted && list != null) setState(() => _list = list);
    } on DioException catch (error) {
      _show(_problem(error, 'Could not save the list.'));
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Delete “${_list.name}”?',
      message: 'The list and all of its items are removed.',
      confirmLabel: 'Delete',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<dynamic>(_path);
      if (mounted) Navigator.of(context).pop();
    } on DioException catch (error) {
      _show(_problem(error, 'Could not delete the list.'));
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final open = _list.open;
    final done = _list.done;
    final total = _list.items.length;
    final tint = listKindColor(colors, _list.kind);
    return Scaffold(
      appBar: AppBar(
        title: Text(_list.name),
        actions: [
          PopupMenuButton<_Menu>(
            tooltip: 'List options',
            icon: const Icon(PhosphorIconsRegular.dotsThree),
            onSelected: (choice) => unawaited(switch (choice) {
              _Menu.rename => _rename(),
              _Menu.clear => _clearDone(),
              _Menu.delete => _delete(),
            }),
            itemBuilder: (_) => [
              const PopupMenuItem(
                value: _Menu.rename,
                child: ListTile(
                  leading: Icon(PhosphorIconsRegular.pencilSimple),
                  title: Text('Rename'),
                ),
              ),
              PopupMenuItem(
                value: _Menu.clear,
                enabled: done.isNotEmpty,
                child: const ListTile(
                  leading: Icon(PhosphorIconsRegular.broom),
                  title: Text('Clear checked items'),
                ),
              ),
              PopupMenuItem(
                value: _Menu.delete,
                child: ListTile(
                  leading: Icon(
                    PhosphorIconsRegular.trash,
                    color: colors.danger,
                  ),
                  title: Text(
                    'Delete list',
                    style: TextStyle(color: colors.danger),
                  ),
                ),
              ),
            ],
          ),
          const SizedBox(width: 4),
        ],
      ),
      body: Column(
        children: [
          Expanded(
            child: RefreshIndicator(
              onRefresh: _refresh,
              child: ListView(
                physics: const AlwaysScrollableScrollPhysics(),
                padding: const EdgeInsets.fromLTRB(16, 4, 16, 16),
                children: [
                  ContentWidth(
                    child: _Header(
                      kind: _list.kind,
                      tint: tint,
                      open: open.length,
                      total: total,
                    ),
                  ),
                  if (total == 0)
                    ContentWidth(
                      child: Padding(
                        padding: const EdgeInsets.symmetric(vertical: 48),
                        child: Column(
                          children: [
                            Icon(
                              listKindIcon(_list.kind),
                              size: 32,
                              color: colors.muted,
                            ),
                            const SizedBox(height: 12),
                            Text(
                              'Nothing here yet',
                              style: Theme.of(context).textTheme.titleMedium,
                            ),
                            const SizedBox(height: 4),
                            Text(
                              'Add items below, or ask Jarvis to add them.',
                              textAlign: TextAlign.center,
                              style: TextStyle(color: colors.inkSoft),
                            ),
                          ],
                        ),
                      ),
                    ),
                  if (open.isNotEmpty)
                    ContentWidth(
                      child: GroupedSection(
                        dividerIndent: 52,
                        children: [
                          for (final item in open)
                            _ItemRow(
                              key: ValueKey('open-${item.id}'),
                              item: item,
                              tint: tint,
                              onToggle: () => unawaited(_toggle(item)),
                              onEdit: () => unawaited(_edit(item)),
                              onRemove: () => unawaited(_remove(item)),
                            ),
                        ],
                      ),
                    )
                  else if (total > 0)
                    ContentWidth(
                      child: SurfaceCard(
                        child: Row(
                          children: [
                            Icon(
                              PhosphorIconsFill.checkCircle,
                              color: colors.success,
                            ),
                            const SizedBox(width: 12),
                            const Expanded(
                              child: Text('Everything is checked off.'),
                            ),
                          ],
                        ),
                      ),
                    ),
                  if (done.isNotEmpty) ...[
                    const SizedBox(height: 18),
                    ContentWidth(
                      child: SectionHeader(
                        'Checked off · ${done.length}',
                        padding: const EdgeInsets.fromLTRB(4, 0, 0, 6),
                        trailing: Row(
                          mainAxisSize: MainAxisSize.min,
                          children: [
                            TextButton(
                              key: const Key('list-clear-checked'),
                              onPressed: () => unawaited(_clearDone()),
                              child: const Text('Clear'),
                            ),
                            IconButton(
                              tooltip: _showDone ? 'Hide' : 'Show',
                              onPressed: () =>
                                  setState(() => _showDone = !_showDone),
                              icon: AnimatedRotation(
                                turns: _showDone ? 0 : -.25,
                                duration: const Duration(milliseconds: 200),
                                child: const Icon(
                                  PhosphorIconsRegular.caretDown,
                                  size: 16,
                                ),
                              ),
                            ),
                          ],
                        ),
                      ),
                    ),
                    AnimatedSize(
                      duration: const Duration(milliseconds: 220),
                      curve: Curves.easeOutCubic,
                      alignment: Alignment.topCenter,
                      child: _showDone
                          ? ContentWidth(
                              child: GroupedSection(
                                dividerIndent: 52,
                                children: [
                                  for (final item in done)
                                    _ItemRow(
                                      key: ValueKey('done-${item.id}'),
                                      item: item,
                                      tint: tint,
                                      onToggle: () => unawaited(_toggle(item)),
                                      onEdit: () => unawaited(_edit(item)),
                                      onRemove: () => unawaited(_remove(item)),
                                    ),
                                ],
                              ),
                            )
                          : const SizedBox(width: double.infinity),
                    ),
                  ],
                ],
              ),
            ),
          ),
          _AddBar(
            controller: _input,
            focusNode: _inputFocus,
            busy: _adding,
            hint: _list.kind == 'shopping' ? 'Add groceries' : 'Add an item',
            onSubmit: () => unawaited(_add()),
          ),
        ],
      ),
    );
  }
}

class _Header extends StatelessWidget {
  const _Header({
    required this.kind,
    required this.tint,
    required this.open,
    required this.total,
  });

  final String kind;
  final Color tint;
  final int open;
  final int total;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final doneCount = total - open;
    return Padding(
      padding: const EdgeInsets.fromLTRB(4, 4, 4, 16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(listKindIcon(kind), size: 16, color: tint),
              const SizedBox(width: 6),
              Text(
                listKindLabel(kind),
                style: TextStyle(fontSize: 13, color: colors.inkSoft),
              ),
              const Spacer(),
              if (total > 0)
                Text(
                  '$doneCount of $total done',
                  key: const Key('list-progress'),
                  style: TextStyle(fontSize: 13, color: colors.inkSoft),
                ),
            ],
          ),
          if (total > 0) ...[
            const SizedBox(height: 10),
            ClipRRect(
              borderRadius: BorderRadius.circular(3),
              child: TweenAnimationBuilder<double>(
                tween: Tween(end: doneCount / total),
                duration: const Duration(milliseconds: 400),
                curve: Curves.easeOutCubic,
                builder: (context, value, _) => LinearProgressIndicator(
                  value: value,
                  minHeight: 5,
                  color: tint,
                  backgroundColor: colors.surfaceMuted,
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _ItemRow extends StatelessWidget {
  const _ItemRow({
    required this.item,
    required this.tint,
    required this.onToggle,
    required this.onEdit,
    required this.onRemove,
    super.key,
  });

  final ListItemData item;
  final Color tint;
  final VoidCallback onToggle;
  final VoidCallback onEdit;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Dismissible(
      key: ValueKey('dismiss-${item.id}'),
      direction: DismissDirection.endToStart,
      onDismissed: (_) => onRemove(),
      background: ColoredBox(
        color: colors.danger,
        child: Align(
          alignment: Alignment.centerRight,
          child: Padding(
            padding: const EdgeInsets.only(right: 20),
            child: Icon(PhosphorIconsRegular.trash, color: colors.onInk),
          ),
        ),
      ),
      child: Semantics(
        checked: item.done,
        label: item.text,
        excludeSemantics: true,
        onTap: onToggle,
        onLongPress: onEdit,
        child: InkWell(
          onTap: onToggle,
          onLongPress: onEdit,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 52),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
              child: Row(
                children: [
                  _Check(done: item.done, tint: tint),
                  const SizedBox(width: 14),
                  Expanded(
                    child: AnimatedDefaultTextStyle(
                      duration: const Duration(milliseconds: 180),
                      style: TextStyle(
                        fontFamily: 'Inter',
                        fontSize: 15.5,
                        height: 1.35,
                        color: item.done ? colors.muted : colors.ink,
                        decoration: item.done
                            ? TextDecoration.lineThrough
                            : TextDecoration.none,
                        decorationColor: colors.muted,
                      ),
                      child: Text(item.text),
                    ),
                  ),
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }
}

/// Round checkbox that fills with the list's colour when ticked.
class _Check extends StatelessWidget {
  const _Check({required this.done, required this.tint});

  final bool done;
  final Color tint;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return AnimatedContainer(
      duration: const Duration(milliseconds: 180),
      curve: Curves.easeOut,
      width: 24,
      height: 24,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: done ? tint : Colors.transparent,
        border: Border.all(
          color: done ? tint : colors.outlineStrong,
          width: 1.6,
        ),
      ),
      child: AnimatedScale(
        scale: done ? 1 : 0,
        duration: const Duration(milliseconds: 180),
        curve: Curves.easeOutBack,
        child: Icon(PhosphorIconsRegular.check, size: 14, color: colors.onInk),
      ),
    );
  }
}

class _AddBar extends StatelessWidget {
  const _AddBar({
    required this.controller,
    required this.focusNode,
    required this.busy,
    required this.hint,
    required this.onSubmit,
  });

  final TextEditingController controller;
  final FocusNode focusNode;
  final bool busy;
  final String hint;
  final VoidCallback onSubmit;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return DecoratedBox(
      decoration: BoxDecoration(
        color: colors.canvas,
        border: Border(top: BorderSide(color: colors.outline)),
      ),
      child: SafeArea(
        top: false,
        child: Padding(
          padding: const EdgeInsets.fromLTRB(16, 10, 16, 10),
          child: ContentWidth(
            child: Row(
              children: [
                Expanded(
                  child: TextField(
                    key: const Key('list-add-field'),
                    controller: controller,
                    focusNode: focusNode,
                    maxLength: 200,
                    textCapitalization: TextCapitalization.sentences,
                    textInputAction: TextInputAction.done,
                    decoration: InputDecoration(
                      hintText: hint,
                      counterText: '',
                      prefixIcon: const Icon(
                        PhosphorIconsRegular.plus,
                        size: 18,
                      ),
                    ),
                    onSubmitted: (_) => onSubmit(),
                  ),
                ),
                const SizedBox(width: 8),
                ValueListenableBuilder(
                  valueListenable: controller,
                  builder: (context, value, _) => IconButton.filled(
                    key: const Key('list-add'),
                    tooltip: 'Add',
                    onPressed: busy || value.text.trim().isEmpty
                        ? null
                        : onSubmit,
                    icon: busy
                        ? const SizedBox.square(
                            dimension: 16,
                            child: CircularProgressIndicator(strokeWidth: 1.8),
                          )
                        : const Icon(PhosphorIconsBold.arrowUp, size: 18),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
