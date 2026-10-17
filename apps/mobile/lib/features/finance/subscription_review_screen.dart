import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../search/context_commands.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../comparisons/comparison_screen.dart';
import 'negotiate_sheet.dart';

/// The renewal review of one subscription: what the records show, the paths
/// you can take and what each leads to, a pack saved to the Library, the
/// provider's reply tracked as a promise, and only what you confirmed counted
/// as saved or cancelled.
class SubscriptionReviewScreen extends StatefulWidget {
  const SubscriptionReviewScreen({
    required this.http,
    required this.subscriptionId,
    this.onAskInChat,
    super.key,
  });

  final Dio http;
  final String subscriptionId;
  final void Function(String prompt)? onAskInChat;

  @override
  State<SubscriptionReviewScreen> createState() =>
      _SubscriptionReviewScreenState();
}

class _SubscriptionReviewScreenState extends State<SubscriptionReviewScreen>
    with PaletteContextMixin<SubscriptionReviewScreen> {
  @override
  String get paletteContextValue => 'subscription:${widget.subscriptionId}';

  Map<String, dynamic>? _view;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  String get _path =>
      '/api/v1/finance/subscriptions/${widget.subscriptionId}/review';
  Map<String, dynamic> get _subscription =>
      jsonObject(_view?['subscription']) ?? const {};
  Map<String, dynamic> get _review => jsonObject(_view?['review']) ?? const {};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      setState(() {
        _view = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load this review.';
      });
    }
  }

  void _toast(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  /// Runs a call; [apply] reads the answer. Returns whether it worked.
  Future<bool> _run(
    Future<Response<dynamic>> Function() call, {
    void Function(dynamic data)? apply,
    String? done,
  }) async {
    setState(() => _busy = true);
    try {
      final response = await call();
      if (!mounted) return true;
      if (apply != null) apply(response.data);
      if (done != null) _toast(done);
      return true;
    } on DioException catch (error) {
      if (mounted) {
        _toast(
          firstProblemMessage(error.response?.data) ?? 'That did not work.',
        );
      }
      return false;
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  void _replace(dynamic data) => setState(() => _view = jsonObject(data));

  Future<void> _choose(String choice) async {
    await _run(
      () =>
          widget.http.post<dynamic>('$_path/choice', data: {'choice': choice}),
      apply: _replace,
    );
    if (choice == 'archive' && mounted) Navigator.of(context).maybePop();
  }

  Future<void> _compare() async {
    String? id;
    final ok = await _run(
      () => widget.http.post<dynamic>('$_path/comparison'),
      apply: (data) =>
          id = jsonString(jsonObject(data) ?? const {}, 'comparisonId'),
    );
    if (!ok || id == null || !mounted) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => ComparisonScreen(http: widget.http, comparisonId: id!),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _pack(String goal) async {
    final ok = await _run(
      () => widget.http.post<dynamic>('$_path/pack', data: {'goal': goal}),
      done: 'Saved to your Library. Nothing was sent.',
    );
    if (ok && mounted) unawaited(_load());
  }

  Future<void> _negotiate() async {
    final ask = widget.onAskInChat;
    final choice = await showNegotiateSheet(
      context,
      merchant: asJsonString(_subscription['merchant']) ?? '',
      savedCancelUrl: asJsonString(_subscription['cancelUrl']),
      browserAvailable: ask != null,
    );
    if (choice == null || !mounted) return;
    String? prompt;
    final ok = await _run(
      () => widget.http.post<dynamic>(
        '/api/v1/finance/subscriptions/${widget.subscriptionId}/negotiate',
        data: {
          'goal': choice.goal,
          'mode': choice.mode,
          'cancelUrl': choice.cancelUrl,
        },
      ),
      apply: (data) =>
          prompt = jsonString(jsonObject(data) ?? const {}, 'prompt'),
      done: choice.mode == 'browser'
          ? null
          : 'Jarvis is drafting the message. You will find it in Tasks.',
    );
    if (!ok || !mounted) return;
    if (choice.mode == 'browser' && prompt != null && ask != null) {
      Navigator.of(context).pop();
      ask(prompt!);
      return;
    }
    unawaited(_load());
  }

  Future<void> _followUp() => _run(
    () => widget.http.post<dynamic>('$_path/follow-up', data: const {}),
    apply: (_) => unawaited(_load()),
    done: 'Tracking their reply in your promises.',
  );

  Future<String?> _ask(String title, String label, {bool numeric = false}) =>
      showJarvisDialog<String>(
        context: context,
        builder: (_) =>
            _FieldDialog(title: title, label: label, numeric: numeric),
      );

  Future<void> _confirmPrice() async {
    final text = await _ask(
      'The new price you have in writing',
      'New price per charge',
      numeric: true,
    );
    final amount = double.tryParse((text ?? '').replaceAll(',', '.'));
    if (amount == null) return;
    await _run(
      () => widget.http.post<dynamic>(
        '$_path/outcome',
        data: {'confirmedAmount': amount},
      ),
      apply: _replace,
      done: 'Saved. Only this confirmed price counts as a saving.',
    );
  }

  Future<void> _confirmCancel() async {
    final text = await _ask(
      'Their confirmation',
      'Reference number, or date and wording of their message',
    );
    if (text == null || text.isEmpty) return;
    await _run(
      () => widget.http.post<dynamic>(
        '$_path/cancellation',
        data: {'evidence': text},
      ),
      apply: _replace,
      done: 'Marked cancelled, with their confirmation noted.',
    );
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(_subscription['status']) ?? 'active';
    final saving = jsonObject(_view?['saving']);
    final paths = jsonMaps(_view?['paths']);
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(
          asJsonString(_subscription['merchant']) ?? 'Subscription',
        ),
      ),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      if (status != 'active')
                        InlineNotice(
                          key: const Key('review-status'),
                          tone: NoticeTone.info,
                          message: status == 'cancelled'
                              ? 'Cancelled${asJsonString(_review['cancelEvidence']) == null ? '' : ': ${_review['cancelEvidence']}'}'
                              : 'Not tracked as a subscription.',
                        ),
                      const GroupLabel('What the records show'),
                      for (final (i, fact) in jsonStrings(
                        _view?['facts'],
                      ).indexed)
                        FeatureRow(
                          key: Key('review-fact-$i'),
                          index: i,
                          icon: PhosphorIconsRegular.receipt,
                          title: fact,
                        ),
                      if (_view?['quiet'] == true)
                        FeatureRow(
                          key: const Key('review-quiet'),
                          icon: PhosphorIconsRegular.bellSlash,
                          quiet: true,
                          title: 'You chose to keep it; Jarvis is quiet about it for now.',
                        ),
                      if (saving != null)
                        FeatureRow(
                          key: const Key('review-saving'),
                          index: 3,
                          icon: PhosphorIconsRegular.trendDown,
                          title: asJsonString(saving['basis']) ?? '',
                        ),
                      if (status == 'active') ...[
                        const GroupLabel('What would you like to do?'),
                        for (final (i, path) in paths.indexed)
                          _pathCard(path, i),
                        const GroupLabel('Along the way'),
                        Wrap(
                          spacing: 8,
                          runSpacing: 8,
                          children: [
                            PillAction(
                              key: const Key('review-pack-price'),
                              icon: PhosphorIconsRegular.fileText,
                              label: 'Price pack',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_pack('lower_price')),
                            ),
                            PillAction(
                              key: const Key('review-pack-cancel'),
                              icon: PhosphorIconsRegular.fileText,
                              label: 'Cancellation pack',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_pack('cancel')),
                            ),
                            PillAction(
                              key: const Key('review-follow-up'),
                              icon: PhosphorIconsRegular.arrowBendUpLeft,
                              label: _review['followUpCommitmentId'] == null
                                  ? 'Track their reply'
                                  : 'Their reply is tracked',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_followUp()),
                            ),
                            PillAction(
                              key: const Key('review-confirm-price'),
                              label: 'I have a lower price',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_confirmPrice()),
                            ),
                            PillAction(
                              key: const Key('review-confirm-cancel'),
                              label: 'They confirmed the cancellation',
                              onPressed: _busy
                                  ? null
                                  : () => unawaited(_confirmCancel()),
                            ),
                          ],
                        ),
                        if (_review['libraryItemId'] != null)
                          Padding(
                            padding: const EdgeInsets.only(top: 10),
                            child: Text(
                              'A pack is saved in your Library.',
                              key: const Key('review-pack-saved'),
                              style: TextStyle(
                                color: colors.muted,
                                fontSize: 13,
                              ),
                            ),
                          ),
                      ],
                    ],
                  ),
                ),
              ],
            ),
    );
  }

  Widget _pathCard(Map<String, dynamic> path, int index) {
    final id = asJsonString(path['id']) ?? '';
    final chosen = _review['choice'] == id;
    return FeatureRow(
      key: Key('review-path-$id'),
      index: index,
      icon: switch (id) {
        'keep' => PhosphorIconsRegular.checkCircle,
        'investigate' => PhosphorIconsRegular.magnifyingGlass,
        'negotiate' => PhosphorIconsRegular.chatText,
        _ => PhosphorIconsRegular.xCircle,
      },
      caption: chosen ? 'Your choice' : null,
      title: asJsonString(path['label']) ?? '',
      subtitle: asJsonString(path['nextAction']),
      trailing: const Icon(Icons.chevron_right, size: 20),
      onTap: _busy
          ? null
          : () => unawaited(switch (id) {
              'investigate' => _compare(),
              'negotiate' || 'cancel' => _negotiate(),
              _ => _choose(id),
            }),
    );
  }
}

class _FieldDialog extends StatefulWidget {
  const _FieldDialog({
    required this.title,
    required this.label,
    required this.numeric,
  });

  final String title;
  final String label;
  final bool numeric;

  @override
  State<_FieldDialog> createState() => _FieldDialogState();
}

class _FieldDialogState extends State<_FieldDialog> {
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
      key: const Key('review-field'),
      controller: _controller,
      keyboardType: widget.numeric
          ? const TextInputType.numberWithOptions(decimal: true)
          : null,
      decoration: InputDecoration(labelText: widget.label),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('review-field-ok'),
        onPressed: () => Navigator.pop(context, _controller.text.trim()),
        child: const Text('Save'),
      ),
    ],
  );
}
