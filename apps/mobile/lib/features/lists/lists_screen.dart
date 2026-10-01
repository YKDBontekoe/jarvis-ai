import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'list_detail_screen.dart';
import 'list_models.dart';

/// Personal lists (groceries, to-dos, packing lists). Jarvis can add to and
/// check off these lists from chat, voice, and WhatsApp.
class ListsScreen extends StatefulWidget {
  const ListsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ListsScreen> createState() => _ListsScreenState();
}

class _ListsScreenState extends State<ListsScreen> with WidgetsBindingObserver {
  List<PersonalListData> _lists = const [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_load());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  // Jarvis may have changed a list from WhatsApp while the app was away.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/lists');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _lists = PersonalListData.listFromJson(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your lists.';
      });
    }
  }

  Future<void> _open(PersonalListData list) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => ListDetailScreen(http: widget.http, list: list),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create([String? name, String? kind]) async {
    final draft = name == null
        ? await showListEditor(context)
        : (name: name, kind: kind ?? 'general');
    if (draft == null || !mounted) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/lists',
        data: {'name': draft.name, 'kind': draft.kind},
      );
      final list = PersonalListData.fromJson(response.data);
      if (!mounted) return;
      if (list == null) {
        unawaited(_load());
        return;
      }
      await _open(list);
    } on DioException catch (error) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ??
                'Could not create the list.',
          ),
        ),
      );
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Lists'),
      actions: [
        HeaderAction(
          label: 'New list',
          icon: PhosphorIconsRegular.plus,
          onPressed: () => unawaited(_create()),
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _lists.isEmpty,
      onRetry: () => unawaited(_load()),
      onRefresh: _load,
      empty: EmptyState(
        icon: PhosphorIconsRegular.checkSquare,
        title: 'No lists yet',
        message:
            'Keep groceries, to-dos, and anything else you want to tick off. '
            'You can also tell Jarvis “put milk on my shopping list”.',
        action: Wrap(
          alignment: WrapAlignment.center,
          spacing: 8,
          runSpacing: 8,
          children: [
            FilledButton.icon(
              key: const Key('lists-start-shopping'),
              onPressed: () => unawaited(_create('Groceries', 'shopping')),
              icon: const Icon(PhosphorIconsRegular.shoppingCart, size: 18),
              label: const Text('Groceries'),
            ),
            OutlinedButton.icon(
              key: const Key('lists-start-todo'),
              onPressed: () => unawaited(_create('To-do', 'todo')),
              icon: const Icon(PhosphorIconsRegular.checkSquare, size: 18),
              label: const Text('To-do'),
            ),
          ],
        ),
      ),
      child: ListView(
        padding: EdgeInsets.fromLTRB(
          16,
          8,
          16,
          32 + MediaQuery.paddingOf(context).bottom,
        ),
        children: [
          for (final (index, list) in _lists.indexed)
            ContentWidth(
              child: FadeSlideIn(
                index: index,
                child: _ListCard(
                  list: list,
                  onTap: () => unawaited(_open(list)),
                ),
              ),
            ),
          ContentWidth(
            child: Padding(
              padding: const EdgeInsets.fromLTRB(4, 10, 4, 0),
              child: Text(
                'Tip: tell Jarvis “put milk on my shopping list” in chat, '
                'voice, or WhatsApp.',
                textAlign: TextAlign.center,
                style: TextStyle(
                  fontSize: 12.5,
                  color: JarvisColors.of(context).muted,
                ),
              ),
            ),
          ),
        ],
      ),
    ),
  );
}

class _ListCard extends StatelessWidget {
  const _ListCard({required this.list, required this.onTap});

  final PersonalListData list;
  final VoidCallback onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final open = list.open;
    final total = list.items.length;
    final tint = listKindColor(colors, list.kind);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Row(
            children: [
              IconBadge(icon: listKindIcon(list.kind), color: tint, size: 40),
              const SizedBox(width: 14),
              Expanded(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      list.name,
                      maxLines: 1,
                      overflow: TextOverflow.ellipsis,
                      style: Theme.of(context).textTheme.titleMedium,
                    ),
                    const SizedBox(height: 2),
                    Text(
                      total == 0
                          ? 'Empty'
                          : '${listProgressLabel(list)} · ${total - open.length} of $total done',
                      style: TextStyle(fontSize: 13, color: colors.inkSoft),
                    ),
                  ],
                ),
              ),
              Icon(
                PhosphorIconsRegular.caretRight,
                size: 16,
                color: colors.muted,
              ),
            ],
          ),
          if (total > 0) ...[
            const SizedBox(height: 12),
            ClipRRect(
              borderRadius: BorderRadius.circular(3),
              child: TweenAnimationBuilder<double>(
                tween: Tween(end: (total - open.length) / total),
                duration: const Duration(milliseconds: 450),
                curve: Curves.easeOutCubic,
                builder: (context, value, _) => LinearProgressIndicator(
                  value: value,
                  minHeight: 4,
                  color: tint,
                  backgroundColor: colors.surfaceMuted,
                  semanticsLabel: '${list.name} progress',
                ),
              ),
            ),
          ],
          if (open.isNotEmpty) ...[
            const SizedBox(height: 10),
            Text(
              open.take(4).map((item) => item.text).join(' · ') +
                  (open.length > 4 ? ' · +${open.length - 4}' : ''),
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(fontSize: 13.5, color: colors.ink, height: 1.4),
            ),
          ],
        ],
      ),
    );
  }
}

typedef ListDraft = ({String name, String kind});

/// Bottom sheet for naming a list and choosing its kind.
Future<ListDraft?> showListEditor(
  BuildContext context, {
  String? name,
  String kind = 'shopping',
  String title = 'New list',
  String confirmLabel = 'Create',
}) => showModalBottomSheet<ListDraft>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _ListEditorSheet(
    name: name,
    kind: kind,
    title: title,
    confirmLabel: confirmLabel,
  ),
);

class _ListEditorSheet extends StatefulWidget {
  const _ListEditorSheet({
    required this.name,
    required this.kind,
    required this.title,
    required this.confirmLabel,
  });

  final String? name;
  final String kind;
  final String title;
  final String confirmLabel;

  @override
  State<_ListEditorSheet> createState() => _ListEditorSheetState();
}

class _ListEditorSheetState extends State<_ListEditorSheet> {
  late final _name = TextEditingController(text: widget.name);
  late String _kind = widget.kind;

  @override
  void dispose() {
    _name.dispose();
    super.dispose();
  }

  void _submit() {
    final name = _name.text.trim();
    if (name.isEmpty) return;
    Navigator.of(context).pop((name: name, kind: _kind));
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(
      20,
      0,
      20,
      20 + MediaQuery.viewInsetsOf(context).bottom,
    ),
    child: Column(
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 16),
        TextField(
          key: const Key('list-name'),
          controller: _name,
          autofocus: true,
          maxLength: 60,
          textCapitalization: TextCapitalization.sentences,
          textInputAction: TextInputAction.done,
          decoration: const InputDecoration(
            labelText: 'Name',
            hintText: 'Groceries, To-do, Packing…',
            counterText: '',
          ),
          onChanged: (_) => setState(() {}),
          onSubmitted: (_) => _submit(),
        ),
        const SizedBox(height: 16),
        SegmentedButton<String>(
          segments: [
            for (final kind in listKinds)
              ButtonSegment(
                value: kind,
                icon: Icon(listKindIcon(kind), size: 16),
                label: Text(listKindLabel(kind)),
              ),
          ],
          selected: {_kind},
          showSelectedIcon: false,
          onSelectionChanged: (selection) =>
              setState(() => _kind = selection.first),
        ),
        const SizedBox(height: 20),
        FilledButton(
          key: const Key('list-save'),
          onPressed: _name.text.trim().isEmpty ? null : _submit,
          style: FilledButton.styleFrom(minimumSize: const Size(0, 46)),
          child: Text(widget.confirmLabel),
        ),
      ],
    ),
  );
}
