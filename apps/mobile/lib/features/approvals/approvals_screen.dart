import 'dart:async';

import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../ui/phosphor_icons.dart';

import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';

class ApprovalsScreen extends StatefulWidget {
  const ApprovalsScreen({required this.http, this.conversationId, super.key});

  final Dio http;
  final String? conversationId;

  @override
  State<ApprovalsScreen> createState() => _ApprovalsScreenState();
}

class _ApprovalsScreenState extends State<ApprovalsScreen> {
  List<Map<String, dynamic>> _approvals = [];
  List<Map<String, dynamic>> _standing = [];
  bool _loading = true;
  String? _error;
  String? _processingId;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<dynamic>('/api/v1/approvals');
      var standing = _standing;
      if (widget.conversationId == null) {
        try {
          final granted = await widget.http.get<dynamic>(
            '/api/v1/approvals/standing',
          );
          standing = jsonMaps(granted.data);
        } catch (_) {
          standing = _standing;
        }
      }
      if (mounted && revision == _requestRevision) {
        setState(() {
          _approvals = jsonMaps(response.data)
              .where(
                (approval) =>
                    widget.conversationId == null ||
                    approval['conversationId'] == widget.conversationId,
              )
              .toList();
          _standing = standing;
        });
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load pending approvals.');
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load pending approvals.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _decide(
    Map<String, dynamic> approval,
    bool approved, {
    bool rememberCategory = false,
  }) async {
    final id = jsonString(approval, 'id');
    if (id == null) return;
    unawaited(
      approved
          ? HapticFeedback.mediumImpact()
          : HapticFeedback.selectionClick(),
    );
    final retrying = approval['status'] != 'pending';
    if (approved) {
      final confirmed = await showJarvisDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          icon: const Align(
            child: IconBadge(icon: PhosphorIconsRegular.shieldCheck, size: 48),
          ),
          title: Text(
            retrying ? 'Retry approved tool call?' : 'Approve tool call?',
          ),
          content: SingleChildScrollView(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  retrying
                      ? 'This tool call was approved, but Jarvis did not finish resuming it. Retrying may repeat the action if it completed before the interruption.'
                      : 'Jarvis will run ${approval['toolName']} with these arguments:',
                ),
                const SizedBox(height: 14),
                _ArgumentsBlock(
                  _formatArguments(asJsonString(approval['argumentsJson'])),
                ),
              ],
            ),
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
              style: TextButton.styleFrom(
                foregroundColor: JarvisColors.of(context).inkSoft,
              ),
              child: const Text('Cancel'),
            ),
            FilledButton(
              onPressed: () => Navigator.pop(context, true),
              child: const Text('Approve and run'),
            ),
          ],
        ),
      );
      if (confirmed != true) return;
    }

    if (!mounted) return;
    setState(() => _processingId = id);
    try {
      await widget.http.post(
        '/api/v1/approvals/$id/decision',
        data: {
          'approved': approved,
          if (rememberCategory) 'rememberCategory': true,
        },
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              retrying
                  ? 'Jarvis resumed the previous decision.'
                  : rememberCategory
                  ? 'Allowed from now on. Jarvis can do this without asking.'
                  : approved
                  ? 'Tool call approved.'
                  : 'Tool call rejected.',
            ),
          ),
        );
      }
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 409
            ? 'This approval was already decided.'
            : 'Jarvis could not process this decision.';
        ScaffoldMessenger.of(
          context,
        ).showSnackBar(SnackBar(content: Text(message)));
        await _load();
      }
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Jarvis could not process this decision.'),
          ),
        );
        await _load();
      }
    } finally {
      if (mounted) setState(() => _processingId = null);
    }
  }

  Future<void> _alwaysAllow(Map<String, dynamic> approval) async {
    final label = _lowerFirst(
      asJsonString(approval['categoryLabel']) ?? 'this kind of action',
    );
    final confirmed = await showJarvisDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Always allow this?'),
        content: Text(
          'Jarvis will approve $label from now on, without asking. You can turn this off here later.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Not now'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Always allow'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    await _decide(approval, true, rememberCategory: true);
  }

  Future<void> _revoke(Map<String, dynamic> grant) async {
    final category = asJsonString(grant['category']);
    if (category == null) return;
    final label = asJsonString(grant['label']) ?? 'this action';
    final confirmed = await showJarvisDialog<bool>(
      context: context,
      builder: (context) => AlertDialog(
        title: const Text('Ask again next time?'),
        content: Text(
          'Jarvis will stop approving ${_lowerFirst(label)} on its own.',
        ),
        actions: [
          TextButton(
            onPressed: () => Navigator.pop(context, false),
            child: const Text('Keep allowing'),
          ),
          FilledButton(
            onPressed: () => Navigator.pop(context, true),
            child: const Text('Turn off'),
          ),
        ],
      ),
    );
    if (confirmed != true || !mounted) return;
    setState(() => _processingId = 'standing:$category');
    try {
      await widget.http.delete<dynamic>(
        '/api/v1/approvals/standing',
        queryParameters: {'category': category},
      );
      await _load();
    } catch (_) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Jarvis could not turn this off.')),
        );
      }
    } finally {
      if (mounted) setState(() => _processingId = null);
    }
  }

  Widget _standingSection() {
    final colors = JarvisColors.of(context);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 8, 4, 8),
          child: Text(
            'ALWAYS ALLOWED',
            style: Theme.of(
              context,
            ).textTheme.labelSmall?.copyWith(color: colors.muted),
          ),
        ),
        for (final grant in _standing)
          SurfaceCard(
            margin: const EdgeInsets.only(bottom: 12),
            child: Row(
              children: [
                const IconBadge(icon: PhosphorIconsRegular.shieldCheck),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    asJsonString(grant['label']) ?? 'Allowed action',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
                TextButton(
                  onPressed: _processingId == null
                      ? () => _revoke(grant)
                      : null,
                  child: const Text('Turn off'),
                ),
              ],
            ),
          ),
        if (_approvals.isNotEmpty) const SizedBox(height: 8),
      ],
    );
  }

  String _formatArguments(String? raw) {
    if (raw == null || raw.isEmpty) return '{}';
    try {
      return const JsonEncoder.withIndent('  ').convert(jsonDecode(raw));
    } catch (_) {
      return raw;
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: PageTitle(
        widget.conversationId == null ? 'Tool approvals' : 'Task approvals',
      ),
      actions: [
        IconButton(
          onPressed: _load,
          tooltip: 'Refresh',
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        const SizedBox(width: 8),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty:
          _approvals.isEmpty &&
          (widget.conversationId != null || _standing.isEmpty),
      onRetry: _load,
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.shieldCheck,
        title: 'All clear',
        message: 'No tool calls are waiting for approval.',
      ),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
        children: [
          if (widget.conversationId == null && _standing.isNotEmpty)
            ContentWidth(child: _standingSection()),
          if (_approvals.isEmpty)
            const Padding(
              padding: EdgeInsets.fromLTRB(8, 28, 8, 12),
              child: Text('No tool calls are waiting for approval.'),
            ),
          for (var index = 0; index < _approvals.length; index++)
            FadeSlideIn(
              index: index,
              child: ContentWidth(child: _approvalCard(_approvals[index])),
            ),
        ],
      ),
    ),
  );

  Widget _approvalCard(Map<String, dynamic> approval) {
    final busy = _processingId == approval['id'];
    final pending = approval['status'] == 'pending';
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 12),
      elevated: pending,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              const IconBadge(icon: PhosphorIconsRegular.shieldCheck),
              const SizedBox(width: 12),
              Expanded(
                child: Text(
                  asJsonString(approval['toolName']) ?? 'Unknown tool',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              StatusPill(
                label: pending ? 'Pending' : 'Needs resume',
                color: pending
                    ? JarvisColors.of(context).warning
                    : JarvisColors.of(context).danger,
              ),
            ],
          ),
          if (!pending)
            InlineNotice(
              message:
                  'Decision recorded: ${approval['approved'] == true ? 'approved' : 'rejected'}. Jarvis needs to resume this call.',
              tone: NoticeTone.danger,
              margin: const EdgeInsets.only(top: 12),
            ),
          const SizedBox(height: 16),
          Text(
            'ARGUMENTS',
            style: Theme.of(context).textTheme.labelSmall?.copyWith(
              color: JarvisColors.of(context).muted,
            ),
          ),
          const SizedBox(height: 8),
          _ArgumentsBlock(
            _formatArguments(asJsonString(approval['argumentsJson'])),
          ),
          const SizedBox(height: 16),
          Row(
            mainAxisAlignment: MainAxisAlignment.end,
            children: [
              if (pending) ...[
                OutlinedButton(
                  onPressed: busy ? null : () => _decide(approval, false),
                  child: const Text('Reject'),
                ),
                const SizedBox(width: 8),
              ],
              FilledButton.icon(
                onPressed: busy
                    ? null
                    : () => _decide(
                        approval,
                        pending ? true : approval['approved'] == true,
                      ),
                icon: busy
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : Icon(
                        pending
                            ? PhosphorIconsRegular.check
                            : PhosphorIconsRegular.arrowsClockwise,
                      ),
                label: Text(pending ? 'Approve' : 'Retry resume'),
              ),
            ],
          ),
          if (pending && approval['canRememberCategory'] == true)
            Align(
              alignment: Alignment.centerRight,
              child: TextButton(
                onPressed: busy ? null : () => _alwaysAllow(approval),
                child: Text(
                  'Always allow ${_lowerFirst(asJsonString(approval['categoryLabel']) ?? 'this kind of action')}',
                ),
              ),
            ),
        ],
      ),
    );
  }
}

String _lowerFirst(String text) =>
    text.isEmpty ? text : text[0].toLowerCase() + text.substring(1);

class _ArgumentsBlock extends StatelessWidget {
  const _ArgumentsBlock(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(14),
    decoration: BoxDecoration(
      color: JarvisColors.of(context).canvas,
      borderRadius: BorderRadius.circular(JarvisRadii.sm),
      border: Border.all(color: JarvisColors.of(context).outline),
    ),
    child: SelectableText(
      text,
      style: TextStyle(
        fontFamily: 'monospace',
        fontSize: 12.5,
        height: 1.5,
        color: JarvisColors.of(context).ink,
      ),
    ),
  );
}
