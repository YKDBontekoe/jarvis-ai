import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

class ApprovalsScreen extends StatefulWidget {
  const ApprovalsScreen({required this.http, this.conversationId, super.key});

  final Dio http;
  final String? conversationId;

  @override
  State<ApprovalsScreen> createState() => _ApprovalsScreenState();
}

class _ApprovalsScreenState extends State<ApprovalsScreen> {
  List<Map<String, dynamic>> _approvals = [];
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
      final response = await widget.http.get<dynamic>(
        '/api/v1/approvals',
      );
      if (mounted && revision == _requestRevision) {
        setState(
          () => _approvals = jsonMaps(response.data)
              .where(
                (approval) =>
                    widget.conversationId == null ||
                    approval['conversationId'] == widget.conversationId,
              )
              .toList(),
        );
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load pending approvals.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _decide(Map<String, dynamic> approval, bool approved) async {
    final id = jsonString(approval, 'id');
    if (id == null) return;
    final retrying = approval['status'] != 'pending';
    if (approved) {
      final confirmed = await showDialog<bool>(
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
                foregroundColor: JarvisColors.inkSoft,
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
        data: {'approved': approved},
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              retrying
                  ? 'Jarvis resumed the previous decision.'
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
    } finally {
      if (mounted) setState(() => _processingId = null);
    }
  }

  String _formatArguments(String? raw) {
    if (raw == null || raw.isEmpty) return '{}';
    try {
      return const JsonEncoder.withIndent('  ').convert(jsonDecode(raw));
    } on FormatException {
      return raw;
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: Text(
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
      isEmpty: _approvals.isEmpty,
      onRetry: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.shieldCheck,
        title: 'All clear',
        message: 'No tool calls are waiting for approval.',
      ),
      child: ListView.builder(
            padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
            itemCount: _approvals.length,
            itemBuilder: (context, index) =>
                ContentWidth(child: _approvalCard(_approvals[index])),
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
                color: pending ? JarvisColors.warning : JarvisColors.danger,
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
            style: Theme.of(
              context,
            ).textTheme.labelSmall?.copyWith(color: JarvisColors.muted),
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
        ],
      ),
    );
  }
}

class _ArgumentsBlock extends StatelessWidget {
  const _ArgumentsBlock(this.text);

  final String text;

  @override
  Widget build(BuildContext context) => Container(
    width: double.infinity,
    padding: const EdgeInsets.all(14),
    decoration: BoxDecoration(
      color: JarvisColors.canvas,
      borderRadius: BorderRadius.circular(JarvisRadii.sm),
      border: Border.all(color: JarvisColors.outline),
    ),
    child: SelectableText(
      text,
      style: const TextStyle(
        fontFamily: 'monospace',
        fontSize: 12.5,
        height: 1.5,
        color: JarvisColors.ink,
      ),
    ),
  );
}
