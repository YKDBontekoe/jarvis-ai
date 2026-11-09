import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../schedule_format.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'recent_searches_store.dart';
import 'search_api.dart';
import 'search_models.dart';
import 'quick_commands.dart';
import 'search_navigation.dart';

class SearchScreen extends StatefulWidget {
  const SearchScreen({
    required this.http,
    required this.onConversation,
    required this.onUtility,
    super.key,
  });

  final Dio http;
  final ConversationOpener onConversation;
  final UtilityOpener onUtility;

  @override
  State<SearchScreen> createState() => _SearchScreenState();
}

class _SearchScreenState extends State<SearchScreen> {
  final _query = TextEditingController();
  final _focus = FocusNode();
  RecentSearchesStore? _recentStore;
  Timer? _debounce;
  var _loading = false;
  String? _error;
  List<FederatedSearchHit> _results = const [];
  List<SearchProviderStatus> _providers = const [];
  Set<String> _kindFilter = {};

  /// Bumped whenever the query or filters change, so an older response can't land.
  var _revision = 0;

  /// Only hits from the last N days; null means any time. Hits with no date are left out when set.
  int? _windowDays;
  List<String> _recent = const [];

  @override
  void initState() {
    super.initState();
    unawaited(_loadRecent());
    WidgetsBinding.instance.addPostFrameCallback((_) => _focus.requestFocus());
  }

  Future<void> _loadRecent() async {
    _recentStore = await RecentSearchesStore.open();
    if (!mounted) return;
    setState(() => _recent = _recentStore!.read());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _query.dispose();
    _focus.dispose();
    super.dispose();
  }

  void _scheduleSearch() {
    _revision++;
    _debounce?.cancel();
    _debounce = Timer(
      const Duration(milliseconds: 220),
      () => unawaited(_runSearch()),
    );
  }

  Future<void> _runSearch() async {
    final revision = ++_revision;
    final text = _query.text.trim();
    if (text.isEmpty) {
      setState(() {
        _results = const [];
        _providers = const [];
        _error = null;
        _loading = false;
      });
      return;
    }
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await federatedSearch(
        widget.http,
        text,
        kinds: _kindFilter.isEmpty ? null : _kindFilter,
        from: _windowDays == null
            ? null
            : DateTime.now().subtract(Duration(days: _windowDays!)),
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _results = response.results;
        _providers = response.providers;
        _loading = false;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error = describeApiError(error);
      });
    } catch (error) {
      if (!mounted || revision != _revision) return;
      setState(() {
        _loading = false;
        _error = 'Search did not finish. Check your connection and try again.';
      });
    }
  }

  Future<void> _openHit(FederatedSearchHit hit) async {
    await _recentStore?.remember(_query.text.trim());
    if (!mounted) return;
    await navigateSearchRoute(
      context,
      http: widget.http,
      route: hit.route,
      onConversation: widget.onConversation,
      onUtility: widget.onUtility,
    );
  }

  /// Leaves search and does the command, e.g. opens the new-reminder editor.
  void _runCommand(QuickCommand command) {
    Navigator.of(context).pop();
    widget.onUtility(command.destination);
  }

  Future<void> _openRecent(String query) async {
    _query.text = query;
    await _runSearch();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Search'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(52),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            child: TextField(
              controller: _query,
              focusNode: _focus,
              autofocus: true,
              textInputAction: TextInputAction.search,
              onChanged: (_) {
                setState(() {});
                _scheduleSearch();
              },
              onSubmitted: (_) => unawaited(_runSearch()),
              decoration: InputDecoration(
                hintText: 'Search conversations, memories, files…',
                prefixIcon: const Icon(
                  PhosphorIconsRegular.magnifyingGlass,
                  size: 18,
                ),
                suffixIcon: SearchClearButton(
                  controller: _query,
                  onCleared: () {
                    setState(() {});
                    _scheduleSearch();
                    _focus.requestFocus();
                  },
                ),
              ),
            ),
          ),
        ),
      ),
      body: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 4),
            child: Row(
              children: [
                for (final kind in searchKindLabels.keys)
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: FilterChip(
                      label: Text(searchKindLabels[kind]!),
                      selected: _kindFilter.contains(kind),
                      onSelected: (selected) {
                        setState(() {
                          if (selected) {
                            _kindFilter = {..._kindFilter, kind};
                          } else {
                            _kindFilter = {..._kindFilter}..remove(kind);
                          }
                        });
                        _scheduleSearch();
                      },
                    ),
                  ),
              ],
            ),
          ),
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 4),
            child: Row(
              children: [
                for (final (label, days) in const [
                  ('Any time', null),
                  ('Past week', 7),
                  ('Past month', 30),
                  ('Past year', 365),
                ])
                  Padding(
                    padding: const EdgeInsets.only(right: 8),
                    child: ChoiceChip(
                      key: Key('search-window-${days ?? 'any'}'),
                      label: Text(label),
                      selected: _windowDays == days,
                      onSelected: (_) {
                        setState(() => _windowDays = days);
                        _scheduleSearch();
                      },
                    ),
                  ),
              ],
            ),
          ),
          if (_error != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              child: InlineNotice(message: _error!),
            ),
          Expanded(
            child: _query.text.trim().isEmpty
                ? _RecentList(
                    leading: [
                      const QuickCommandHeader('Create'),
                      for (final command in matchQuickCommands(''))
                        QuickCommandTile(
                          command: command,
                          onTap: () => _runCommand(command),
                        ),
                    ],
                    recent: _recent,
                    onOpen: (query) => unawaited(_openRecent(query)),
                    onRemove: (query) async {
                      await _recentStore?.remove(query);
                      await _loadRecent();
                    },
                  )
                : Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      for (final command in matchQuickCommands(_query.text))
                        QuickCommandTile(
                          command: command,
                          onTap: () => _runCommand(command),
                        ),
                      Expanded(
                        child: SearchResultsBody(
                          loading: _loading,
                          results: _results,
                          providers: _providers,
                          query: _query.text,
                          onClearFilters:
                              _kindFilter.isEmpty && _windowDays == null
                              ? null
                              : () {
                                  setState(() {
                                    _kindFilter = {};
                                    _windowDays = null;
                                  });
                                  unawaited(_runSearch());
                                },
                          onRetry: () => unawaited(_runSearch()),
                          onOpen: (hit) => unawaited(_openHit(hit)),
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

/// The icon that stands for a kind of search hit.
IconData searchKindIcon(String kind) => switch (kind) {
  'conversation' => PhosphorIconsRegular.chatsCircle,
  'message' || 'channel_thread' => PhosphorIconsRegular.chatText,
  'person' => PhosphorIconsRegular.user,
  'commitment' || 'task' || 'list' => PhosphorIconsRegular.listChecks,
  'memory' || 'journal' => PhosphorIconsRegular.notebook,
  'file' => PhosphorIconsRegular.fileText,
  'reminder' => PhosphorIconsRegular.bell,
  'automation' => PhosphorIconsRegular.lightning,
  'graph_entity' => PhosphorIconsRegular.graph,
  'coding_run' => PhosphorIconsRegular.code,
  'mission' || 'project' => PhosphorIconsRegular.flowArrow,
  'goal' => PhosphorIconsRegular.target,
  'trip' => PhosphorIconsRegular.airplaneTilt,
  _ => PhosphorIconsRegular.sparkle,
};

/// [text] with every word of [query] (two letters or more) in [emphasis], so
/// the eye lands on why a hit matched. Case is ignored; the text is unchanged.
TextSpan highlightMatches(String text, String query, TextStyle emphasis) {
  final words = {
    for (final word in query.toLowerCase().split(RegExp(r'\s+')))
      if (word.length >= 2) RegExp.escape(word),
  };
  if (words.isEmpty) return TextSpan(text: text);
  final pattern = RegExp(words.join('|'), caseSensitive: false);
  final spans = <TextSpan>[];
  var cursor = 0;
  for (final match in pattern.allMatches(text)) {
    if (match.start > cursor) {
      spans.add(TextSpan(text: text.substring(cursor, match.start)));
    }
    spans.add(TextSpan(text: match.group(0), style: emphasis));
    cursor = match.end;
  }
  if (cursor < text.length) spans.add(TextSpan(text: text.substring(cursor)));
  return TextSpan(children: spans);
}

class SearchResultsBody extends StatelessWidget {
  const SearchResultsBody({
    required this.loading,
    required this.results,
    required this.onOpen,
    this.providers = const [],
    this.onRetry,
    this.query = '',
    this.onClearFilters,
    super.key,
  });

  /// What was searched for; its words are emphasised in each hit.
  final String query;

  /// Set while a scope or time filter is narrowing the search, so an empty
  /// result can offer to search everything instead.
  final VoidCallback? onClearFilters;

  final bool loading;
  final List<FederatedSearchHit> results;
  final ValueChanged<FederatedSearchHit> onOpen;

  /// Per-source outcome of the last search; failed ones make the results incomplete.
  final List<SearchProviderStatus> providers;
  final VoidCallback? onRetry;

  @override
  Widget build(BuildContext context) {
    if (loading && results.isEmpty) {
      return const Center(child: CircularProgressIndicator(strokeWidth: 2));
    }
    final failed = loading
        ? const <SearchProviderStatus>[]
        : providers.where((p) => !p.succeeded).toList();
    final notice = failed.isEmpty
        ? null
        : Padding(
            padding: const EdgeInsets.fromLTRB(12, 8, 12, 0),
            child: InlineNotice(
              key: const Key('search-incomplete'),
              message: incompleteSearchMessage(failed),
              actions: [
                if (onRetry != null)
                  TextButton(onPressed: onRetry, child: const Text('Retry')),
              ],
            ),
          );
    if (results.isEmpty) {
      if (notice == null) {
        final colors = JarvisColors.of(context);
        return Center(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(24),
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                const Text(
                  'No matches yet.',
                  style: TextStyle(fontWeight: FontWeight.w600),
                ),
                const SizedBox(height: 6),
                Text(
                  onClearFilters == null
                      ? 'Try fewer or different words.'
                      : 'Your filters may be hiding it.',
                  textAlign: TextAlign.center,
                  style: TextStyle(color: colors.inkSoft),
                ),
                if (onClearFilters != null) ...[
                  const SizedBox(height: 14),
                  PillAction(
                    key: const Key('search-clear-filters'),
                    label: 'Search everything',
                    compact: true,
                    onPressed: onClearFilters,
                  ),
                ],
              ],
            ),
          ),
        );
      }
      return Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [notice, const SizedBox(height: 24)],
      );
    }
    final list = ListView.separated(
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 24),
      itemCount: results.length,
      separatorBuilder: (_, _) => const SizedBox(height: 6),
      itemBuilder: (context, index) {
        final hit = results[index];
        final colors = JarvisColors.of(context);
        final kind = searchKindLabels[hit.kind] ?? hit.kind;
        final when = hit.timestamp == null
            ? null
            : friendlyDate(context, hit.timestamp!).split(' · ').first;
        return Material(
          color: colors.surfaceMuted,
          borderRadius: BorderRadius.circular(14),
          child: InkWell(
            borderRadius: BorderRadius.circular(14),
            onTap: () => onOpen(hit),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 12, 14, 12),
              child: Row(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  // The kind's icon lets a mixed list be scanned by shape.
                  Container(
                    width: 34,
                    height: 34,
                    decoration: BoxDecoration(
                      color: colors.surface,
                      borderRadius: BorderRadius.circular(10),
                    ),
                    child: ExcludeSemantics(
                      child: Icon(
                        searchKindIcon(hit.kind),
                        size: 18,
                        color: colors.accent,
                      ),
                    ),
                  ),
                  const SizedBox(width: 12),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Row(
                          children: [
                            Expanded(
                              child: Text.rich(
                                highlightMatches(
                                  hit.title,
                                  query,
                                  TextStyle(color: colors.accentDeep),
                                ),
                                style: const TextStyle(
                                  fontWeight: FontWeight.w600,
                                ),
                              ),
                            ),
                            if (hit.isPinned)
                              const Padding(
                                padding: EdgeInsets.only(left: 8),
                                child: Icon(
                                  PhosphorIconsRegular.pushPin,
                                  size: 16,
                                ),
                              ),
                          ],
                        ),
                        const SizedBox(height: 4),
                        Text(
                          when == null ? kind : '$kind · $when',
                          style: Theme.of(context).textTheme.labelSmall
                              ?.copyWith(color: colors.inkSoft),
                        ),
                        if (hit.summary != null && hit.summary!.isNotEmpty) ...[
                          const SizedBox(height: 6),
                          Text.rich(
                            highlightMatches(
                              hit.summary!,
                              query,
                              TextStyle(
                                color: colors.ink,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                            style: TextStyle(
                              color: colors.inkSoft,
                              height: 1.35,
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ],
              ),
            ),
          ),
        );
      },
    );
    if (notice == null) return list;
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        notice,
        Expanded(child: list),
      ],
    );
  }
}

class _RecentList extends StatelessWidget {
  const _RecentList({
    required this.recent,
    required this.onOpen,
    required this.onRemove,
    this.leading = const [],
  });

  /// Rows shown above the recent searches (the quick commands).
  final List<Widget> leading;
  final List<String> recent;
  final ValueChanged<String> onOpen;
  final ValueChanged<String> onRemove;

  @override
  Widget build(BuildContext context) {
    return ListView(
      keyboardDismissBehavior: ScrollViewKeyboardDismissBehavior.onDrag,
      padding: const EdgeInsets.fromLTRB(8, 4, 8, 24),
      children: [
        ...leading,
        const QuickCommandHeader('Recent'),
        if (recent.isEmpty)
          Padding(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 8),
            child: Text(
              'Recent searches appear here on this device only.',
              style: TextStyle(color: JarvisColors.of(context).muted),
            ),
          ),
        for (final query in recent)
          ListTile(
            leading: const Icon(
              PhosphorIconsRegular.clockCounterClockwise,
              size: 20,
            ),
            title: Text(query),
            trailing: IconButton(
              tooltip: 'Remove',
              icon: const Icon(PhosphorIconsRegular.x, size: 18),
              onPressed: () => onRemove(query),
            ),
            onTap: () => onOpen(query),
          ),
      ],
    );
  }
}
