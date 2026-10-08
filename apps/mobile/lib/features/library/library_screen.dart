import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'library_models.dart';

/// Pages, notes and research reports the owner keeps, with spaced-repetition
/// flashcards and a deep-research button that files a cited report here.
class LibraryScreen extends StatefulWidget {
  const LibraryScreen({required this.http, this.openLink, super.key});

  final Dio http;

  /// Opens a saved link; replaced in tests.
  final Future<void> Function(String url)? openLink;

  @override
  State<LibraryScreen> createState() => _LibraryScreenState();
}

class _LibraryScreenState extends State<LibraryScreen> {
  final _search = TextEditingController();
  List<LibraryItemData> _items = const [];
  CardStatsData _stats = const CardStatsData();
  List<FlashcardData> _due = const [];
  bool _loading = true;
  bool _busy = false;
  bool _revealed = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() => _loading = true);
    try {
      final library = await widget.http.get<dynamic>(
        '/api/v1/library',
        queryParameters: {if (_search.text.trim().isNotEmpty) 'q': _search.text.trim()},
      );
      final cards = await widget.http.get<dynamic>('/api/v1/library/cards/due');
      if (!mounted) return;
      final map = jsonObject(library.data);
      final cardMap = jsonObject(cards.data);
      setState(() {
        _items = [
          for (final item in jsonMaps(map?['items'])) ?LibraryItemData.fromJson(item),
        ];
        _stats = CardStatsData.fromJson(map?['cards']);
        _due = [
          for (final card in jsonMaps(cardMap?['cards'])) ?FlashcardData.fromJson(card),
        ];
        _revealed = false;
        _loading = false;
        _error = map == null ? 'Could not load your library.' : null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your library.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.of(
    context,
  ).showSnackBar(SnackBar(content: Text(message)));

  Future<void> _guard(String working, Future<void> Function() action) async {
    setState(() => _busy = true);
    try {
      await action();
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  Future<void> _addLink() async {
    final values = await _ask(
      title: 'Save a link',
      fields: const [('url', 'https://…'), ('note', 'Why keep it? (optional)')],
    );
    if (values == null) return;
    await _guard('Saving', () async {
      await widget.http.post<dynamic>('/api/v1/library/clip', data: {
        'url': values['url'],
        if (values['note']!.isNotEmpty) 'note': values['note'],
      });
      _toast('Saved to your library.');
      await _load();
    });
  }

  Future<void> _addNote() async {
    final values = await _ask(
      title: 'Write a note',
      fields: const [('title', 'Title'), ('content', 'What do you want to keep?')],
      multiline: 'content',
    );
    if (values == null) return;
    await _guard('Saving', () async {
      await widget.http.post<dynamic>('/api/v1/library/notes', data: {
        'title': values['title'],
        'content': values['content'],
      });
      await _load();
    });
  }

  Future<void> _research() async {
    final values = await _ask(
      title: 'Deep research',
      fields: const [('question', 'What should Jarvis find out?')],
      multiline: 'question',
      confirm: 'Start',
    );
    if (values == null) return;
    await _guard('Starting', () async {
      await widget.http.post<dynamic>('/api/v1/library/research', data: {
        'question': values['question'],
      });
      _toast('Research started. A cited report lands here when it is done.');
    });
  }

  Future<Map<String, String>?> _ask({
    required String title,
    required List<(String, String)> fields,
    String? multiline,
    String confirm = 'Save',
  }) => showJarvisDialog<Map<String, String>>(
    context: context,
    builder: (_) => _FieldsDialog(
      title: title,
      fields: fields,
      multiline: multiline,
      confirm: confirm,
    ),
  );

  Future<void> _open(LibraryItemData item) async {
    LibraryItemData? full;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/library/${item.id}',
      );
      full = LibraryItemData.fromJson(response.data);
    } on DioException {
      full = null;
    }
    if (!mounted) return;
    final shown = full ?? item;
    final deleted = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (context) => _ItemSheet(
        item: shown,
        onOpenLink: shown.url == null
            ? null
            : () => unawaited(
                (widget.openLink ??
                    (url) async {
                      await launchUrl(Uri.parse(url));
                    })(shown.url!),
              ),
      ),
    );
    if (deleted == true) {
      await _guard('Deleting', () async {
        await widget.http.delete<dynamic>('/api/v1/library/${item.id}');
        await _load();
      });
    }
  }

  Future<void> _grade(FlashcardData card, String button) async {
    await _guard('Grading', () async {
      await widget.http.post<dynamic>(
        '/api/v1/library/cards/${card.id}/review',
        data: {'button': button},
      );
      if (!mounted) return;
      setState(() {
        _due = _due.where((x) => x.id != card.id).toList();
        _stats = CardStatsData(
          total: _stats.total,
          due: (_stats.due - 1).clamp(0, 1 << 30),
        );
        _revealed = false;
      });
    });
  }

  @override
  Widget build(BuildContext context) => DefaultTabController(
    length: 2,
    child: Scaffold(
      appBar: AppBar(
        title: const Text('Library'),
        bottom: TabBar(
          tabs: [
            const Tab(key: Key('library-tab-items'), text: 'Saved'),
            Tab(
              key: const Key('library-tab-review'),
              text: _stats.due > 0 ? 'Review (${_stats.due})' : 'Review',
            ),
          ],
        ),
        actions: [
          HeaderAction(
            key: const Key('library-research'),
            label: 'Research',
            icon: PhosphorIconsRegular.atom,
            collapsesWhenNarrow: true,
            onPressed: _busy ? null : () => unawaited(_research()),
          ),
          const SizedBox(width: 8),
          HeaderAction(
            key: const Key('library-note'),
            label: 'Note',
            icon: PhosphorIconsRegular.notePencil,
            collapsesWhenNarrow: true,
            onPressed: _busy ? null : () => unawaited(_addNote()),
          ),
          const SizedBox(width: 8),
          HeaderAction(
            key: const Key('library-link'),
            label: 'Link',
            icon: PhosphorIconsRegular.link,
            onPressed: _busy ? null : () => unawaited(_addLink()),
          ),
        ],
      ),
      body: ContentWidth(
        child: TabBarView(children: [_itemsTab(), _reviewTab()]),
      ),
    ),
  );

  Widget _itemsTab() {
    final colors = JarvisColors.of(context);
    return ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _items.isEmpty && _search.text.isEmpty,
      onRetry: () => unawaited(_load()),
      empty: const EmptyState(
        icon: PhosphorIconsRegular.bookOpen,
        title: 'Your library is empty',
        message:
            'Save a link or a note, or ask Jarvis to research something. '
            'Everything is searchable from chat.',
      ),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 8, 16, 96),
        children: [
          TextField(
            key: const Key('library-search'),
            controller: _search,
            textInputAction: TextInputAction.search,
            onSubmitted: (_) => unawaited(_load()),
            decoration: const InputDecoration(
              hintText: 'Search what you saved',
              prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass),
            ),
          ),
          const SizedBox(height: 12),
          if (_items.isEmpty)
            Padding(
              padding: const EdgeInsets.all(24),
              child: Text(
                'Nothing matches.',
                textAlign: TextAlign.center,
                style: TextStyle(color: colors.muted),
              ),
            ),
          for (final item in _items)
            SurfaceCard(
              key: Key('library-item-${item.id}'),
              margin: const EdgeInsets.only(bottom: 10),
              onTap: () => unawaited(_open(item)),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      IconBadge(
                        icon: switch (item.kind) {
                          'web' => PhosphorIconsRegular.globeSimple,
                          'report' => PhosphorIconsRegular.atom,
                          _ => PhosphorIconsRegular.notePencil,
                        },
                        size: 34,
                      ),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Text(
                          item.title,
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 6),
                  Text(
                    item.summary,
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(color: colors.inkSoft),
                  ),
                  const SizedBox(height: 6),
                  Text(
                    [
                      libraryKindLabel(item.kind),
                      ?item.host,
                      ...item.tags.map((tag) => '#$tag'),
                    ].join(' · '),
                    style: TextStyle(color: colors.muted, fontSize: 12.5),
                  ),
                ],
              ),
            ),
        ],
      ),
    );
  }

  Widget _reviewTab() {
    final colors = JarvisColors.of(context);
    if (_due.isEmpty) {
      return EmptyState(
        icon: PhosphorIconsRegular.checkCircle,
        title: _stats.total == 0 ? 'No flashcards yet' : 'All caught up',
        message: _stats.total == 0
            ? 'Saved pages and notes can become flashcards. Ask Jarvis to '
                  'make some, or save a link.'
            : 'Nothing is due. Cards come back when it is time.',
      );
    }
    final card = _due.first;
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
      children: [
        Text(
          '${_due.length} to go',
          style: TextStyle(color: colors.muted),
          textAlign: TextAlign.center,
        ),
        const SizedBox(height: 12),
        SurfaceCard(
          key: const Key('flashcard'),
          padding: const EdgeInsets.all(24),
          child: Column(
            children: [
              Text(
                card.front,
                textAlign: TextAlign.center,
                style: Theme.of(context).textTheme.titleMedium,
              ),
              if (_revealed) ...[
                const Divider(height: 32),
                Text(
                  card.back,
                  key: const Key('flashcard-back'),
                  textAlign: TextAlign.center,
                ),
              ],
            ],
          ),
        ),
        const SizedBox(height: 16),
        if (!_revealed)
          FilledButton(
            key: const Key('flashcard-reveal'),
            onPressed: () => setState(() => _revealed = true),
            child: const Text('Show answer'),
          )
        else
          Wrap(
            alignment: WrapAlignment.center,
            spacing: 8,
            children: [
              for (final (key, label) in const [
                ('again', 'Again'),
                ('hard', 'Hard'),
                ('good', 'Good'),
                ('easy', 'Easy'),
              ])
                OutlinedButton(
                  key: Key('flashcard-$key'),
                  onPressed: _busy ? null : () => unawaited(_grade(card, key)),
                  child: Text(label),
                ),
            ],
          ),
      ],
    );
  }
}

class _ItemSheet extends StatelessWidget {
  const _ItemSheet({required this.item, this.onOpenLink});

  final LibraryItemData item;
  final VoidCallback? onOpenLink;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final content = item.content ?? '';
    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * .85,
        ),
        child: ListView(
          padding: const EdgeInsets.all(20),
          shrinkWrap: true,
          children: [
            Text(item.title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 4),
            Text(
              [libraryKindLabel(item.kind), ?item.host].join(' · '),
              style: TextStyle(color: colors.muted),
            ),
            const SizedBox(height: 12),
            Text(item.summary),
            for (final point in item.keyPoints)
              Padding(
                padding: const EdgeInsets.only(top: 6),
                child: Text('• $point', style: TextStyle(color: colors.inkSoft)),
              ),
            if (content.isNotEmpty) ...[
              const SizedBox(height: 16),
              Text(
                content.length > 2500 ? '${content.substring(0, 2500)}…' : content,
                style: TextStyle(color: colors.inkSoft, fontSize: 13.5),
              ),
            ],
            const SizedBox(height: 16),
            Row(
              children: [
                if (onOpenLink != null)
                  TextButton.icon(
                    key: const Key('library-open-link'),
                    onPressed: onOpenLink,
                    icon: const Icon(PhosphorIconsRegular.arrowSquareOut, size: 18),
                    label: const Text('Open original'),
                  ),
                const Spacer(),
                TextButton(
                  key: const Key('library-delete'),
                  onPressed: () => Navigator.pop(context, true),
                  style: TextButton.styleFrom(foregroundColor: colors.danger),
                  child: const Text('Delete'),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _FieldsDialog extends StatefulWidget {
  const _FieldsDialog({
    required this.title,
    required this.fields,
    required this.confirm,
    this.multiline,
  });

  final String title;
  final List<(String, String)> fields;
  final String confirm;
  final String? multiline;

  @override
  State<_FieldsDialog> createState() => _FieldsDialogState();
}

class _FieldsDialogState extends State<_FieldsDialog> {
  late final Map<String, TextEditingController> _controllers = {
    for (final (key, _) in widget.fields) key: TextEditingController(),
  };

  @override
  void dispose() {
    for (final controller in _controllers.values) {
      controller.dispose();
    }
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.title),
    content: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          for (final (key, label) in widget.fields)
            TextField(
              key: Key('library-field-$key'),
              controller: _controllers[key],
              maxLines: widget.multiline == key ? 5 : 1,
              decoration: InputDecoration(labelText: label),
            ),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('library-dialog-save'),
        onPressed: () {
          final first = _controllers[widget.fields.first.$1]!.text.trim();
          if (first.isEmpty) return;
          Navigator.pop(context, {
            for (final entry in _controllers.entries)
              entry.key: entry.value.text.trim(),
          });
        },
        child: Text(widget.confirm),
      ),
    ],
  );
}
