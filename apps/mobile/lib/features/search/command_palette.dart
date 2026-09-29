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

class OpenSearchIntent extends Intent {
  const OpenSearchIntent();
}

Future<void> showJarvisCommandPalette(
  BuildContext context, {
  required Dio http,
  required ConversationOpener onConversation,
  required UtilityOpener onUtility,
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
    ),
  );
}

class _CommandPaletteDialog extends StatefulWidget {
  const _CommandPaletteDialog({
    required this.http,
    required this.recentStore,
    required this.onConversation,
    required this.onUtility,
  });

  final Dio http;
  final RecentSearchesStore recentStore;
  final ConversationOpener onConversation;
  final UtilityOpener onUtility;

  @override
  State<_CommandPaletteDialog> createState() => _CommandPaletteDialogState();
}

class _CommandPaletteDialogState extends State<_CommandPaletteDialog> {
  final _query = TextEditingController();
  final _focus = FocusNode();
  Timer? _debounce;
  var _loading = false;
  String? _error;
  List<FederatedSearchHit> _results = const [];
  Set<String> _kindFilter = {};

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _focus.requestFocus());
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
    _debounce = Timer(const Duration(milliseconds: 180), () => unawaited(_runSearch()));
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
    }
  }

  Future<void> _openHit(FederatedSearchHit hit) async {
    await widget.recentStore.remember(_query.text.trim());
    if (!mounted) return;
    Navigator.of(context).pop();
    await navigateSearchRoute(
      context,
      http: widget.http,
      route: hit.route,
      onConversation: widget.onConversation,
      onUtility: widget.onUtility,
    );
  }

  @override
  Widget build(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    final dialogWidth = width >= 720 ? 640.0 : width * 0.92;
    return Shortcuts(
      shortcuts: {
        LogicalKeySet(LogicalKeyboardKey.escape): DismissIntent(),
      },
      child: Actions(
        actions: {
          DismissIntent: CallbackAction<DismissIntent>(onInvoke: (_) {
            Navigator.of(context).pop();
            return null;
          }),
        },
        child: Center(
          child: Material(
            color: JarvisColors.of(context).canvas,
            elevation: 12,
            borderRadius: BorderRadius.circular(18),
            clipBehavior: Clip.antiAlias,
            child: SizedBox(
              width: dialogWidth,
              height: mathMin(520, MediaQuery.sizeOf(context).height * 0.78),
              child: Column(
                children: [
                  Padding(
                    padding: const EdgeInsets.fromLTRB(16, 16, 16, 8),
                    child: TextField(
                      controller: _query,
                      focusNode: _focus,
                      autofocus: true,
                      decoration: const InputDecoration(
                        hintText: 'Search or jump to…',
                        prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass, size: 18),
                        suffixText: 'Esc',
                      ),
                      onChanged: (_) {
                        setState(() {});
                        _scheduleSearch();
                      },
                      onSubmitted: (_) => unawaited(_runSearch()),
                    ),
                  ),
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
                      padding: const EdgeInsets.symmetric(horizontal: 16),
                      child: Text(
                        _error!,
                        style: TextStyle(color: JarvisColors.of(context).danger),
                      ),
                    ),
                  Expanded(
                    child: _query.text.trim().isEmpty
                        ? ListView(
                            padding: const EdgeInsets.fromLTRB(8, 0, 8, 12),
                            children: [
                              for (final query in widget.recentStore.read())
                                ListTile(
                                  leading: const Icon(PhosphorIconsRegular.clockCounterClockwise, size: 18),
                                  title: Text(query),
                                  onTap: () {
                                    _query.text = query;
                                    unawaited(_runSearch());
                                  },
                                ),
                              ListTile(
                                leading: const Icon(PhosphorIconsRegular.arrowSquareOut, size: 18),
                                title: const Text('Open full search'),
                                onTap: () {
                                  Navigator.of(context).pop();
                                  Navigator.of(context).push<void>(
                                    MaterialPageRoute<void>(
                                      builder: (_) => SearchScreen(
                                        http: widget.http,
                                        onConversation: widget.onConversation,
                                        onUtility: widget.onUtility,
                                      ),
                                    ),
                                  );
                                },
                              ),
                            ],
                          )
                        : SearchResultsBody(
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
