import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../api/api_errors.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import 'recent_searches_store.dart';
import 'search_api.dart';
import 'search_models.dart';
import 'search_navigation.dart';
import 'search_screen.dart';
import 'intent_navigation.dart';

class OpenSearchIntent extends Intent {
  const OpenSearchIntent();
}

Future<void> showJarvisCommandPalette(
  BuildContext context, {
  required Dio http,
  required ConversationOpener onConversation,
  required UtilityOpener onUtility,
  Future<void> Function(String prompt)? onAsk,
}) async {
  final store = await RecentSearchesStore.open();
  if (!context.mounted) return;
  await showDialog<void>(
    context: context,
    barrierColor: Colors.black54,
    builder: (dialogContext) => _CommandPaletteDialog(
      http: http,
      recentStore: store,
      onConversation: onConversation,
      onUtility: onUtility,
      onAsk: onAsk,
    ),
  );
}

class _CommandPaletteDialog extends StatefulWidget {
  const _CommandPaletteDialog({
    required this.http,
    required this.recentStore,
    required this.onConversation,
    required this.onUtility,
    this.onAsk,
  });

  final Dio http;
  final RecentSearchesStore recentStore;
  final ConversationOpener onConversation;
  final UtilityOpener onUtility;
  final Future<void> Function(String prompt)? onAsk;

  @override
  State<_CommandPaletteDialog> createState() => _CommandPaletteDialogState();
}

class _CommandPaletteDialogState extends State<_CommandPaletteDialog> {
  final _query = TextEditingController();
  late final _intent = IntentNavigationController(widget.http);
  int _searchRevision = 0;
  final _focus = FocusNode();
  Timer? _debounce;
  var _loading = false;
  String? _error;
  List<FederatedSearchHit> _results = const [];
  Set<String> _kindFilter = {};

  @override
  void initState() {
    super.initState();
    _intent.addListener(_intentChanged);
    unawaited(_intent.loadSuggestions());
    WidgetsBinding.instance.addPostFrameCallback((_) => _focus.requestFocus());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _intent.dispose();
    _query.dispose();
    _focus.dispose();
    super.dispose();
  }

  void _intentChanged() {
    if (mounted) setState(() {});
  }

  Widget _intentActions() => IntentActions(
    controller: _intent,
    onOpen: (route) => unawaited(_openIntent(route)),
    onExample: (request) {
      _query.text = request;
      _intent.changeQuery(request);
      _scheduleSearch();
      unawaited(_intent.resolve(request));
    },
  );

  Future<void> _openIntent(SearchRouteTarget route) async {
    await widget.recentStore.remember(_query.text.trim());
    if (!mounted) return;
    final navigationContext = Navigator.of(context).context;
    Navigator.of(context).pop();
    if (!navigationContext.mounted) return;
    await navigateSearchRoute(
      navigationContext,
      http: widget.http,
      route: route,
      onConversation: widget.onConversation,
      onUtility: widget.onUtility,
      onAsk: widget.onAsk,
    );
  }

  void _scheduleSearch() {
    setState(() {
      _results = const [];
      _error = null;
      _loading = _query.text.trim().isNotEmpty;
    });
    ++_searchRevision;
    _debounce?.cancel();
    _debounce = Timer(
      const Duration(milliseconds: 180),
      () => unawaited(_runSearch()),
    );
  }

  Future<void> _runSearch() async {
    final revision = ++_searchRevision;
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
      if (!mounted ||
          revision != _searchRevision ||
          _query.text.trim() != text) {
        return;
      }
      setState(() {
        _results = response.results;
        _loading = false;
      });
    } on DioException catch (error) {
      if (!mounted ||
          revision != _searchRevision ||
          _query.text.trim() != text) {
        return;
      }
      setState(() {
        _loading = false;
        _error = describeApiError(error);
      });
    }
  }

  Future<void> _openHit(FederatedSearchHit hit) async {
    await widget.recentStore.remember(_query.text.trim());
    if (!mounted) return;
    final navigationContext = Navigator.of(context).context;
    Navigator.of(context).pop();
    if (!navigationContext.mounted) return;
    await navigateSearchRoute(
      navigationContext,
      http: widget.http,
      route: hit.route,
      onConversation: widget.onConversation,
      onUtility: widget.onUtility,
      onAsk: widget.onAsk,
    );
  }

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    final dialogWidth = width >= 720 ? 640.0 : width * 0.92;
    return Shortcuts(
      shortcuts: {LogicalKeySet(LogicalKeyboardKey.escape): DismissIntent()},
      child: Actions(
        actions: {
          DismissIntent: CallbackAction<DismissIntent>(
            onInvoke: (_) {
              Navigator.of(context).pop();
              return null;
            },
          ),
        },
        child: Center(
          child: Material(
            color: JarvisColors.of(context).canvas,
            shape: RoundedRectangleBorder(
              borderRadius: BorderRadius.circular(22),
              side: BorderSide(color: JarvisColors.of(context).outline),
            ),
            clipBehavior: Clip.antiAlias,
            child: SizedBox(
              width: dialogWidth,
              height: mathMin(520, MediaQuery.sizeOf(context).height * 0.78),
              child: Column(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                    child: TextField(
                      key: const Key('navigation-request'),
                      controller: _query,
                      focusNode: _focus,
                      autofocus: true,
                      decoration: InputDecoration(
                        hintText: 'What would you like to do?',
                        suffixIcon: IconButton(
                          key: const Key('navigation-submit'),
                          tooltip: 'Find the next step',
                          onPressed: _intent.loading
                              ? null
                              : () => unawaited(_intent.resolve(_query.text)),
                          icon: const Icon(
                            PhosphorIconsRegular.arrowUpRight,
                            size: 18,
                          ),
                        ),
                        prefixIcon: Icon(
                          PhosphorIconsRegular.magnifyingGlass,
                          size: 18,
                        ),
                        suffixText: width >= 720 ? 'Esc' : null,
                      ),
                      onChanged: (_) {
                        _intent.changeQuery(_query.text);
                        _scheduleSearch();
                      },
                      onSubmitted: (_) =>
                          unawaited(_intent.resolve(_query.text)),
                    ),
                  ),
                  if (_query.text.trim().isNotEmpty &&
                      _intent.actions.isEmpty &&
                      !_intent.loading)
                    SingleChildScrollView(
                      scrollDirection: Axis.horizontal,
                      padding: const EdgeInsets.fromLTRB(16, 0, 16, 8),
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
                                      _kindFilter = {..._kindFilter}
                                        ..remove(kind);
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
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: Text(
                        _error!,
                        style: TextStyle(
                          color: JarvisColors.of(context).danger,
                        ),
                      ),
                    ),
                  Expanded(
                    child: _query.text.trim().isEmpty
                        ? ListView(
                            padding: const EdgeInsets.fromLTRB(8, 0, 8, 12),
                            children: [
                              _intentActions(),
                              for (final query in widget.recentStore.read())
                                ListTile(
                                  leading: const Icon(
                                    PhosphorIconsRegular.clockCounterClockwise,
                                    size: 18,
                                  ),
                                  title: Text(query),
                                  onTap: () {
                                    _query.text = query;
                                    _intent.changeQuery(query);
                                    unawaited(_runSearch());
                                  },
                                ),
                              ListTile(
                                leading: const Icon(
                                  PhosphorIconsRegular.arrowSquareOut,
                                  size: 18,
                                ),
                                title: const Text('Open full search'),
                                onTap: () {
                                  final navigator = Navigator.of(context);
                                  navigator.pop();
                                  navigator.push<void>(
                                    MaterialPageRoute<void>(
                                      builder: (_) => SearchScreen(
                                        http: widget.http,
                                        onConversation: widget.onConversation,
                                        onUtility: widget.onUtility,
                                        onAsk: widget.onAsk,
                                      ),
                                    ),
                                  );
                                },
                              ),
                            ],
                          )
                        : SearchResultsBody(
                            header: _intentActions(),
                            loading: _loading,
                            results: _results,
                            onOpen: (hit) => unawaited(_openHit(hit)),
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

double mathMin(double a, double b) => a < b ? a : b;
