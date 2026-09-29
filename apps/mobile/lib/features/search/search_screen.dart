import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'recent_searches_store.dart';
import 'search_api.dart';
import 'search_models.dart';
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
  Set<String> _kindFilter = {};
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
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 220), () => unawaited(_runSearch()));
  }

  Future<void> _runSearch() async {
    final text = _query.text.trim();
    if (text.isEmpty) {
      setState(() {
        _results = const [];
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
      );
      if (!mounted) return;
      setState(() {
        _results = response.results;
        _loading = false;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = describeApiError(error);
      });
    } catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Search failed: $error';
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

  Future<void> _openRecent(String query) async {
    _query.text = query;
    await _runSearch();
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Search'),
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
              decoration: const InputDecoration(
                hintText: 'Search conversations, memories, files…',
                prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass, size: 18),
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
          if (_error != null)
            Padding(
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
              child: InlineNotice(message: _error!),
            ),
          Expanded(
            child: _query.text.trim().isEmpty
                ? _RecentList(
                    recent: _recent,
                    onOpen: (query) => unawaited(_openRecent(query)),
                    onRemove: (query) async {
                      await _recentStore?.remove(query);
                      await _loadRecent();
                    },
                  )
                : SearchResultsBody(
                    loading: _loading,
                    results: _results,
                    onOpen: (hit) => unawaited(_openHit(hit)),
                  ),
          ),
        ],
      ),
    );
  }
}

class SearchResultsBody extends StatelessWidget {
  const SearchResultsBody({
    required this.loading,
    required this.results,
    required this.onOpen,
    super.key,
  });

  final bool loading;
  final List<FederatedSearchHit> results;
  final ValueChanged<FederatedSearchHit> onOpen;

  @override
  Widget build(BuildContext context) {
    if (loading && results.isEmpty) {
      return const Center(child: CircularProgressIndicator(strokeWidth: 2));
    }
    if (results.isEmpty) {
      return const Center(child: Text('No matches yet.'));
    }
    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 24),
      itemCount: results.length,
      separatorBuilder: (_, __) => const SizedBox(height: 6),
      itemBuilder: (context, index) {
        final hit = results[index];
        return Material(
          color: JarvisColors.surfaceMuted,
          borderRadius: BorderRadius.circular(14),
          child: InkWell(
            borderRadius: BorderRadius.circular(14),
            onTap: () => onOpen(hit),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Expanded(
                        child: Text(
                          hit.title,
                          style: const TextStyle(fontWeight: FontWeight.w600),
                        ),
                      ),
                      if (hit.isPinned)
                        const Padding(
                          padding: EdgeInsets.only(left: 8),
                          child: Icon(PhosphorIconsRegular.pin, size: 16),
                        ),
                    ],
                  ),
                  const SizedBox(height: 4),
                  Text(
                    searchKindLabels[hit.kind] ?? hit.kind,
                    style: Theme.of(context).textTheme.labelSmall?.copyWith(
                      color: JarvisColors.inkSoft,
                    ),
                  ),
                  if (hit.summary != null && hit.summary!.isNotEmpty) ...[
                    const SizedBox(height: 6),
                    Text(
                      hit.summary!,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: TextStyle(color: JarvisColors.inkSoft, height: 1.35),
                    ),
                  ],
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}

class _RecentList extends StatelessWidget {
  const _RecentList({
    required this.recent,
    required this.onOpen,
    required this.onRemove,
  });

  final List<String> recent;
  final ValueChanged<String> onOpen;
  final ValueChanged<String> onRemove;

  @override
  Widget build(BuildContext context) {
    if (recent.isEmpty) {
      return const Center(
        child: Text('Recent searches appear here on this device only.'),
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(16, 12, 16, 24),
      itemCount: recent.length,
      separatorBuilder: (_, __) => const Divider(height: 1),
      itemBuilder: (context, index) {
        final query = recent[index];
        return ListTile(
          leading: const Icon(PhosphorIconsRegular.clockCounterClockwise, size: 20),
          title: Text(query),
          trailing: IconButton(
            icon: const Icon(PhosphorIconsRegular.x, size: 18),
            onPressed: () => onRemove(query),
          ),
          onTap: () => onOpen(query),
        );
      },
    );
  }
}
