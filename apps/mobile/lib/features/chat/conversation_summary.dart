import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// What `POST /conversations/{id}/summary` returns.
class ConversationSummaryData {
  const ConversationSummaryData({
    required this.summary,
    this.keyPoints = const [],
    this.actionItems = const [],
    this.messageCount = 0,
  });

  factory ConversationSummaryData.fromJson(Object? json) {
    final map = jsonObject(json);
    List<String> strings(Object? value) => value is List
        ? [
            for (final item in value)
              if (item is String && item.trim().isNotEmpty) item.trim(),
          ]
        : const [];
    return ConversationSummaryData(
      summary: asJsonString(map?['summary'])?.trim() ?? '',
      keyPoints: strings(map?['keyPoints']),
      actionItems: strings(map?['actionItems']),
      messageCount: map?['messageCount'] is int
          ? map!['messageCount'] as int
          : 0,
    );
  }

  final String summary;
  final List<String> keyPoints;
  final List<String> actionItems;
  final int messageCount;

  /// Plain Markdown for the clipboard.
  String toMarkdown() {
    final buffer = StringBuffer(summary);
    if (keyPoints.isNotEmpty) {
      buffer.write('\n\n**Key points**\n');
      for (final point in keyPoints) {
        buffer.write('\n- $point');
      }
    }
    if (actionItems.isNotEmpty) {
      buffer.write('\n\n**To do**\n');
      for (final item in actionItems) {
        buffer.write('\n- [ ] $item');
      }
    }
    return buffer.toString();
  }
}

/// Thrown by a summary loader with a message that can be shown as is.
class ConversationSummaryException implements Exception {
  const ConversationSummaryException(this.message);

  final String message;

  @override
  String toString() => message;
}

/// A bottom sheet that loads a recap of the open chat, with a reminder button
/// for each action item.
class ConversationSummarySheet extends StatefulWidget {
  const ConversationSummarySheet({
    required this.load,
    required this.onRemind,
    super.key,
  });

  final Future<ConversationSummaryData> Function() load;

  /// Sets a reminder for one action item; true once it is set.
  final Future<bool> Function(String item) onRemind;

  @override
  State<ConversationSummarySheet> createState() =>
      _ConversationSummarySheetState();
}

class _ConversationSummarySheetState extends State<ConversationSummarySheet> {
  ConversationSummaryData? _summary;
  String? _error;
  final _reminded = <String>{};
  final _reminding = <String>{};

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    setState(() {
      _summary = null;
      _error = null;
    });
    try {
      final summary = await widget.load();
      if (mounted) setState(() => _summary = summary);
    } on ConversationSummaryException catch (error) {
      if (mounted) setState(() => _error = error.message);
    } catch (_) {
      if (mounted) {
        setState(
          () => _error = 'Jarvis could not summarize this conversation.',
        );
      }
    }
  }

  Future<void> _remind(String item) async {
    setState(() => _reminding.add(item));
    var done = false;
    try {
      done = await widget.onRemind(item);
    } finally {
      if (mounted) {
        setState(() {
          _reminding.remove(item);
          if (done) _reminded.add(item);
        });
      }
    }
  }

  Future<void> _copy(ConversationSummaryData summary) async {
    await Clipboard.setData(ClipboardData(text: summary.toMarkdown()));
    if (!mounted) return;
    ScaffoldMessenger.maybeOf(
      context,
    )?.showSnackBar(const SnackBar(content: Text('Summary copied.')));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final summary = _summary;
    final Widget body;
    if (_error != null) {
      body = Padding(
        padding: const EdgeInsets.symmetric(vertical: 24),
        child: ErrorState(message: _error!, onRetry: _load),
      );
    } else if (summary == null) {
      body = Padding(
        padding: const EdgeInsets.symmetric(vertical: 40),
        child: Column(
          children: [
            const SizedBox.square(
              dimension: 28,
              child: CircularProgressIndicator(strokeWidth: 2.6),
            ),
            const SizedBox(height: 16),
            Text(
              'Reading the conversation…',
              style: TextStyle(color: colors.inkSoft),
            ),
          ],
        ),
      );
    } else {
      body = Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          SelectableText(
            summary.summary,
            style: TextStyle(fontSize: 16, height: 1.45, color: colors.ink),
          ),
          if (summary.keyPoints.isNotEmpty) ...[
            const SizedBox(height: 22),
            const SectionHeader('Key points'),
            for (final point in summary.keyPoints)
              Padding(
                padding: const EdgeInsets.only(bottom: 8),
                child: Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: [
                    Padding(
                      padding: const EdgeInsets.only(top: 8, right: 10),
                      child: Container(
                        width: 5,
                        height: 5,
                        decoration: BoxDecoration(
                          color: colors.inkSoft,
                          shape: BoxShape.circle,
                        ),
                      ),
                    ),
                    Expanded(
                      child: Text(
                        point,
                        style: TextStyle(
                          fontSize: 15,
                          height: 1.4,
                          color: colors.ink,
                        ),
                      ),
                    ),
                  ],
                ),
              ),
          ],
          const SizedBox(height: 22),
          const SectionHeader('To do'),
          if (summary.actionItems.isEmpty)
            Text(
              'Nothing left to do from this chat.',
              style: TextStyle(color: colors.inkSoft),
            )
          else
            for (final item in summary.actionItems)
              _ActionItemRow(
                item: item,
                reminded: _reminded.contains(item),
                busy: _reminding.contains(item),
                onRemind: () => unawaited(_remind(item)),
              ),
        ],
      );
    }

    return SafeArea(
      child: ConstrainedBox(
        constraints: BoxConstraints(
          maxHeight: MediaQuery.sizeOf(context).height * .85,
        ),
        child: SingleChildScrollView(
          padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Row(
                children: [
                  Icon(
                    PhosphorIconsRegular.sparkle,
                    size: 20,
                    color: colors.ink,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      'Summary',
                      style: Theme.of(context).textTheme.titleLarge,
                    ),
                  ),
                  if (summary != null)
                    IconButton(
                      tooltip: 'Copy summary',
                      onPressed: () => unawaited(_copy(summary)),
                      icon: const Icon(PhosphorIconsRegular.copy, size: 19),
                    ),
                ],
              ),
              const SizedBox(height: 14),
              body,
            ],
          ),
        ),
      ),
    );
  }
}

class _ActionItemRow extends StatelessWidget {
  const _ActionItemRow({
    required this.item,
    required this.reminded,
    required this.busy,
    required this.onRemind,
  });

  final String item;
  final bool reminded;
  final bool busy;
  final VoidCallback onRemind;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Container(
      margin: const EdgeInsets.only(bottom: 8),
      padding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(16),
        border: Border.all(color: colors.outline),
      ),
      child: Row(
        children: [
          Expanded(
            child: Text(
              item,
              style: TextStyle(fontSize: 15, height: 1.35, color: colors.ink),
            ),
          ),
          const SizedBox(width: 8),
          if (busy)
            const Padding(
              padding: EdgeInsets.all(12),
              child: SizedBox.square(
                dimension: 20,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
            )
          else
            IconButton(
              tooltip: reminded ? 'Reminder set' : 'Remind me',
              onPressed: reminded ? null : onRemind,
              icon: Icon(
                reminded
                    ? PhosphorIconsRegular.check
                    : PhosphorIconsRegular.bell,
                size: 19,
              ),
            ),
        ],
      ),
    );
  }
}
