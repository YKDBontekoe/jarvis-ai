import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../api/api_errors.dart';
import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../search/message_context_screen.dart';

/// What Jarvis learned lately, what it was unsure of and what is about to lapse, each with where it came from and a
/// plain way to correct it. A correction replaces the memory; the old wording stays as history and is no longer used.
class MemoryReviewScreen extends StatefulWidget {
  const MemoryReviewScreen({
    required this.http,
    this.onOpenConversation,
    super.key,
  });

  final Dio http;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<MemoryReviewScreen> createState() => _MemoryReviewScreenState();
}

class _MemoryReviewScreenState extends State<MemoryReviewScreen> {
  List<Map<String, dynamic>> _items = const [];
  bool _loading = true;
  String? _error;
  final Set<String> _busy = {};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/memory/review');
      if (!mounted) return;
      setState(() {
        _items = [
          for (final item in jsonMaps(response.data))
            if (jsonString(item, 'id') != null) item,
        ];
        _loading = false;
      });
    } on DioException catch (error) {
      if (mounted) {
        setState(() {
          _loading = false;
          _error = describeApiError(error);
        });
      }
    }
  }

  void _snack(String message) => ScaffoldMessenger.maybeOf(context)
    ?..hideCurrentSnackBar()
    ..showSnackBar(SnackBar(content: Text(message)));

  Future<void> _correct(Map<String, dynamic> item) async {
    final id = jsonString(item, 'id')!;
    final text = await showModalBottomSheet<String>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _CorrectionSheet(initial: asJsonString(item['content'])),
    );
    if (text == null || !mounted || _busy.contains(id)) return;
    setState(() => _busy.add(id));
    try {
      await widget.http.post<dynamic>(
        '/api/v1/memory/$id/correct',
        data: {'content': text},
      );
      if (!mounted) return;
      _snack('Corrected. Jarvis will use the new version.');
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        _snack(
          error.response?.statusCode == 409
              ? (firstProblemMessage(error.response?.data) ??
                    'This memory can no longer be replaced.')
              : describeApiError(error),
        );
      }
    } finally {
      if (mounted) setState(() => _busy.remove(id));
    }
  }

  Future<void> _expire(Map<String, dynamic> item) async {
    final id = jsonString(item, 'id')!;
    final confirmed = await showJarvisConfirm(
      context,
      title: 'No longer true?',
      message:
          'Jarvis stops using this. It stays in the history of what Jarvis '
          'learned, so you can see it was once there.',
      cancelLabel: 'Keep it',
      confirmLabel: 'Stop using it',
      icon: PhosphorIconsRegular.prohibit,
    );
    if (!confirmed || !mounted || _busy.contains(id)) return;
    setState(() => _busy.add(id));
    try {
      await widget.http.post<dynamic>('/api/v1/memory/$id/expire');
      if (!mounted) return;
      _snack('Jarvis will not use that any more.');
      await _load();
    } on DioException catch (error) {
      if (mounted) _snack(describeApiError(error));
    } finally {
      if (mounted) setState(() => _busy.remove(id));
    }
  }

  Future<void> _history(Map<String, dynamic> item) async {
    final id = jsonString(item, 'id')!;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/memory/$id/history',
      );
      if (!mounted) return;
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (_) => _HistorySheet(versions: jsonMaps(response.data)),
      );
    } on DioException catch (error) {
      if (mounted) _snack(describeApiError(error));
    }
  }

  Future<void> _openSource(Map<String, dynamic> item) async {
    final source = jsonObject(item['source']);
    final chat = asJsonString(source?['conversationId']);
    final message = asJsonString(source?['messageId']);
    final open = widget.onOpenConversation;
    if (chat == null || message == null || open == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => MessageContextScreen(
          http: widget.http,
          conversationId: chat,
          messageId: message,
          onOpenConversation: open,
        ),
      ),
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const PageTitle('Review memories')),
    body: _body(),
  );

  Widget _body() {
    if (_loading) return const SkeletonList();
    if (_error != null) return ErrorState(message: _error!, onRetry: _load);
    if (_items.isEmpty) {
      return const EmptyState(
        icon: PhosphorIconsRegular.checkCircle,
        title: 'Nothing to review',
        message:
            'Jarvis has not learned anything new lately, and nothing it is '
            'unsure of is waiting. New things show up here.',
      );
    }
    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          ContentWidth(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: [
                Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(
                    'Jarvis uses these to answer you. Correct anything that is '
                    'wrong or out of date.',
                    style: TextStyle(
                      fontSize: 14,
                      height: 1.4,
                      color: JarvisColors.of(context).inkSoft,
                    ),
                  ),
                ),
                for (final item in _items)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: _ReviewCard(
                      key: Key('memory-review-${item['id']}'),
                      item: item,
                      busy: _busy.contains(item['id']),
                      canOpenSource:
                          widget.onOpenConversation != null &&
                          asJsonString(
                                jsonObject(item['source'])?['conversationId'],
                              ) !=
                              null,
                      onCorrect: () => unawaited(_correct(item)),
                      onExpire: () => unawaited(_expire(item)),
                      onHistory: () => unawaited(_history(item)),
                      onOpenSource: () => unawaited(_openSource(item)),
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

String _reasonLabel(String reason) => switch (reason) {
  'recent' => 'Learned lately',
  'low_confidence' => 'Jarvis was unsure',
  'expiring' => 'Expires soon',
  _ => reason,
};

String _day(Object? value) {
  final date = jsonDate(value, local: true);
  if (date == null) return '';
  const months = [
    'Jan',
    'Feb',
    'Mar',
    'Apr',
    'May',
    'Jun',
    'Jul',
    'Aug',
    'Sep',
    'Oct',
    'Nov',
    'Dec',
  ];
  return '${date.day} ${months[date.month - 1]} ${date.year}';
}

class _ReviewCard extends StatelessWidget {
  const _ReviewCard({
    required this.item,
    required this.busy,
    required this.canOpenSource,
    required this.onCorrect,
    required this.onExpire,
    required this.onHistory,
    required this.onOpenSource,
    super.key,
  });

  final Map<String, dynamic> item;
  final bool busy;
  final bool canOpenSource;
  final VoidCallback onCorrect;
  final VoidCallback onExpire;
  final VoidCallback onHistory;
  final VoidCallback onOpenSource;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final pinned = asJsonBool(item['isPinned']);
    final reasons = [
      for (final reason in item['reasons'] as List? ?? const [])
        if (reason is String) reason,
    ];
    final corrections = asJsonInt(item['corrections']);
    final validUntil = asJsonString(item['validUntil']);
    final profile = asJsonString(item['profileName']);
    final source = jsonObject(item['source']);
    return SurfaceCard(
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              for (final reason in reasons)
                StatusPill(
                  label: _reasonLabel(reason),
                  color: reason == 'low_confidence'
                      ? colors.warning
                      : colors.accent,
                ),
              if (pinned) StatusPill(label: 'Pinned', color: colors.muted),
              if (profile != null)
                StatusPill(label: 'Only for $profile', color: colors.info)
              else if (item['profileId'] == null)
                StatusPill(label: 'All profiles', color: colors.muted),
            ],
          ),
          const SizedBox(height: 10),
          Text(
            asJsonString(item['content']) ?? '',
            style: TextStyle(
              fontSize: 15.5,
              height: 1.4,
              fontWeight: FontWeight.w500,
              color: colors.ink,
            ),
          ),
          const SizedBox(height: 6),
          Text(
            [
              asJsonString(source?['label']) ?? 'Source unknown',
              'Learned ${_day(item['createdAt'])}',
              if (validUntil != null) 'Valid until ${_day(validUntil)}',
            ].join(' · '),
            style: TextStyle(fontSize: 12.5, color: colors.muted),
          ),
          if (pinned)
            Padding(
              padding: const EdgeInsets.only(top: 6),
              child: Text(
                'Pinned memories are kept on purpose. Edit or unpin this one '
                'in Memory to change it.',
                style: TextStyle(fontSize: 13, color: colors.inkSoft),
              ),
            ),
          const SizedBox(height: 4),
          Wrap(
            spacing: 4,
            children: [
              if (!pinned) ...[
                TextButton(
                  key: Key('memory-correct-${item['id']}'),
                  onPressed: busy ? null : onCorrect,
                  child: const Text('Correct'),
                ),
                TextButton(
                  key: Key('memory-expire-${item['id']}'),
                  onPressed: busy ? null : onExpire,
                  child: const Text('No longer true'),
                ),
              ],
              if (canOpenSource)
                TextButton(
                  key: Key('memory-source-${item['id']}'),
                  onPressed: onOpenSource,
                  child: const Text('See where'),
                ),
              if (corrections > 0)
                TextButton(
                  key: Key('memory-history-${item['id']}'),
                  onPressed: onHistory,
                  child: Text(
                    corrections == 1
                        ? 'Corrected once'
                        : 'Corrected $corrections times',
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _CorrectionSheet extends StatefulWidget {
  const _CorrectionSheet({this.initial});

  final String? initial;

  @override
  State<_CorrectionSheet> createState() => _CorrectionSheetState();
}

class _CorrectionSheetState extends State<_CorrectionSheet> {
  late final _text = TextEditingController(text: widget.initial ?? '');

  @override
  void dispose() {
    _text.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final trimmed = _text.text.trim();
    final valid =
        trimmed.length >= 3 && trimmed != (widget.initial ?? '').trim();
    return SafeArea(
      child: Padding(
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
            Text(
              'Correct this memory',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('memory-correction-field'),
              controller: _text,
              autofocus: true,
              maxLines: 4,
              minLines: 2,
              maxLength: 2000,
              onChanged: (_) => setState(() {}),
              decoration: const InputDecoration(hintText: 'What is true now?'),
            ),
            const SizedBox(height: 8),
            FilledButton(
              key: const Key('memory-correction-save'),
              onPressed: valid ? () => Navigator.pop(context, trimmed) : null,
              child: const Text('Save correction'),
            ),
          ],
        ),
      ),
    );
  }
}

class _HistorySheet extends StatelessWidget {
  const _HistorySheet({required this.versions});

  final List<Map<String, dynamic>> versions;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * .7,
        ),
        child: ListView(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          shrinkWrap: true,
          children: [
            Text(
              'Earlier versions',
              style: Theme.of(context).textTheme.titleMedium,
            ),
            const SizedBox(height: 4),
            Text(
              'Jarvis no longer uses these.',
              style: TextStyle(fontSize: 13, color: colors.inkSoft),
            ),
            const SizedBox(height: 12),
            if (versions.isEmpty)
              Text(
                'Nothing was replaced.',
                style: TextStyle(color: colors.inkSoft),
              ),
            for (final version in versions)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Text(
                      asJsonString(version['content']) ?? '',
                      style: TextStyle(
                        fontSize: 15,
                        color: colors.ink,
                        decoration: TextDecoration.lineThrough,
                      ),
                    ),
                    Text(
                      'Learned ${_day(version['createdAt'])}'
                      '${version['replacedAt'] == null ? '' : ' · replaced ${_day(version['replacedAt'])}'}',
                      style: TextStyle(fontSize: 12.5, color: colors.muted),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}
