import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'message_context_screen.dart';
import 'search_api.dart';
import 'search_models.dart';

/// Finds words anywhere in one conversation, including messages far above what
/// is loaded on screen. Matches are listed newest first; opening one shows it
/// with the messages around it, with Previous and Next to move between matches.
class FindInChatScreen extends StatefulWidget {
  const FindInChatScreen({
    required this.http,
    required this.conversationId,
    required this.onOpenConversation,
    this.title,
    super.key,
  });

  final Dio http;
  final String conversationId;
  final String? title;
  final Future<void> Function(String conversationId) onOpenConversation;

  @override
  State<FindInChatScreen> createState() => _FindInChatScreenState();
}

class _FindInChatScreenState extends State<FindInChatScreen> {
  final _query = TextEditingController();
  final _focus = FocusNode();
  Timer? _debounce;
  CancelToken? _inFlight;
  int _revision = 0;
  var _loading = false;
  var _searched = false;
  String? _error;
  List<FederatedSearchHit> _hits = const [];
  bool _incomplete = false;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addPostFrameCallback((_) => _focus.requestFocus());
  }

  @override
  void dispose() {
    _debounce?.cancel();
    _inFlight?.cancel();
    _query.dispose();
    _focus.dispose();
    super.dispose();
  }

  void _schedule() {
    _revision++;
    _debounce?.cancel();
    _debounce = Timer(
      const Duration(milliseconds: 250),
      () => unawaited(_run()),
    );
  }

  Future<void> _run() async {
    final revision = ++_revision;
    final text = _query.text.trim();
    _inFlight?.cancel();
    if (text.isEmpty) {
      setState(() {
        _hits = const [];
        _error = null;
        _loading = false;
        _searched = false;
        _incomplete = false;
      });
      return;
    }
    final token = _inFlight = CancelToken();
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await federatedSearch(
        widget.http,
        text,
        conversationId: widget.conversationId,
        cancelToken: token,
      );
      if (!mounted || revision != _revision) return;
      setState(() {
        _hits = response.results;
        _incomplete = response.providers.any((p) => !p.succeeded);
        _loading = false;
        _searched = true;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _revision || CancelToken.isCancel(error)) {
        return;
      }
      setState(() {
        _loading = false;
        _error = error.response?.statusCode == 404
            ? 'This chat is no longer available.'
            : describeApiError(error);
      });
    }
  }

  /// The message id behind a hit, from its route.
  String? _messageId(FederatedSearchHit hit) =>
      hit.route.parameters['messageId'];

  Route<void> _contextRoute(int index) =>
      MaterialPageRoute<void>(builder: (_) => _contextAt(index));

  Widget _contextAt(int index) {
    final messageId = _messageId(_hits[index]);
    return MessageContextScreen(
      key: ValueKey('hit-$index'),
      http: widget.http,
      conversationId: widget.conversationId,
      messageId: messageId ?? '',
      title: widget.title,
      onOpenConversation: widget.onOpenConversation,
      position: '${index + 1} of ${_hits.length}',
      onPrevious: index + 1 < _hits.length ? () => _step(index + 1) : null,
      onNext: index > 0 ? () => _step(index - 1) : null,
    );
  }

  /// Moves to another match without stacking pages on top of each other.
  void _step(int index) {
    unawaited(Navigator.of(context).pushReplacement(_contextRoute(index)));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('Find in chat'),
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(52),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            child: TextField(
              key: const Key('find-in-chat-field'),
              controller: _query,
              focusNode: _focus,
              textInputAction: TextInputAction.search,
              onChanged: (_) {
                setState(() {});
                _schedule();
              },
              onSubmitted: (_) => unawaited(_run()),
              decoration: InputDecoration(
                hintText: 'Search this conversation',
                prefixIcon: const Icon(
                  PhosphorIconsRegular.magnifyingGlass,
                  size: 18,
                ),
                suffixIcon: _query.text.isEmpty
                    ? null
                    : IconButton(
                        key: const Key('find-in-chat-clear'),
                        tooltip: 'Clear',
                        onPressed: () {
                          _query.clear();
                          unawaited(_run());
                        },
                        icon: const Icon(PhosphorIconsRegular.x, size: 16),
                      ),
              ),
            ),
          ),
        ),
      ),
      body: _body(colors),
    );
  }

  Widget _body(JarvisColors colors) {
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(16),
        child: InlineNotice(
          key: const Key('find-in-chat-error'),
          message: _error!,
          tone: NoticeTone.danger,
          actions: [
            TextButton(
              onPressed: () => unawaited(_run()),
              child: const Text('Retry'),
            ),
          ],
        ),
      );
    }
    if (_loading && _hits.isEmpty) {
      return const Center(child: CircularProgressIndicator(strokeWidth: 2));
    }
    if (!_searched && _query.text.trim().isEmpty) {
      return Center(
        child: Text(
          'Type a word to find it anywhere in this chat.',
          style: TextStyle(color: colors.muted),
        ),
      );
    }
    if (_searched && _hits.isEmpty) {
      return Center(
        child: Text(
          _incomplete
              ? 'Search was incomplete. Try again.'
              : 'No matches in this chat.',
          key: const Key('find-in-chat-empty'),
          style: TextStyle(color: colors.muted),
        ),
      );
    }
    return ListView.separated(
      padding: const EdgeInsets.fromLTRB(12, 8, 12, 24),
      itemCount: _hits.length + 1,
      separatorBuilder: (_, _) => const SizedBox(height: 6),
      itemBuilder: (context, index) {
        if (index == 0) {
          return Padding(
            padding: const EdgeInsets.fromLTRB(4, 0, 4, 4),
            child: Text(
              _hits.length == 1 ? '1 match' : '${_hits.length} matches',
              key: const Key('find-in-chat-count'),
              style: TextStyle(color: colors.inkSoft, fontSize: 13),
            ),
          );
        }
        final hit = _hits[index - 1];
        return Material(
          color: colors.surfaceMuted,
          borderRadius: BorderRadius.circular(14),
          child: InkWell(
            key: Key('find-in-chat-hit-${index - 1}'),
            borderRadius: BorderRadius.circular(14),
            onTap: () => unawaited(
              Navigator.of(context).push<void>(_contextRoute(index - 1)),
            ),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    hit.summary ?? '',
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(height: 1.35),
                  ),
                  if (hit.timestamp != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 6),
                      child: Text(
                        MaterialLocalizations.of(context)
                            .formatMediumDate(hit.timestamp!),
                        style: TextStyle(color: colors.muted, fontSize: 12.5),
                      ),
                    ),
                ],
              ),
            ),
          ),
        );
      },
    );
  }
}
