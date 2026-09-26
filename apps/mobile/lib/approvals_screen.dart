import 'dart:convert';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    if (!mounted) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/approvals',
      );
      if (mounted) {
        setState(
          () => _approvals = (response.data ?? [])
              .cast<Map<String, dynamic>>()
              .where(
                (approval) =>
                    widget.conversationId == null ||
                    approval['conversationId'] == widget.conversationId,
              )
              .toList(),
        );
      }
    } on DioException {
      if (mounted) {
        setState(() => _error = 'Jarvis could not load pending approvals.');
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _decide(Map<String, dynamic> approval, bool approved) async {
    final id = approval['id'] as String;
    final retrying = approval['status'] != 'pending';
    if (approved) {
      final confirmed = await showDialog<bool>(
        context: context,
        builder: (context) => AlertDialog(
          title: Text(
            retrying ? 'Retry approved tool call?' : 'Approve tool call?',
          ),
          content: Text(
            '${retrying ? 'This tool call was approved, but Jarvis did not finish resuming it. Retrying may repeat the action if it completed before the interruption.\n\n' : 'Jarvis will run ${approval['toolName']} with these arguments:\n\n'}${_formatArguments(approval['argumentsJson'] as String?)}',
          ),
          actions: [
            TextButton(
              onPressed: () => Navigator.pop(context, false),
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
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? Center(
            child: Column(
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(_error!),
                TextButton(onPressed: _load, child: const Text('Retry')),
              ],
            ),
          )
        : _approvals.isEmpty
        ? const Center(child: Text('No tool calls are waiting for approval.'))
        : ListView.builder(
            padding: const EdgeInsets.all(16),
            itemCount: _approvals.length,
            itemBuilder: (context, index) {
              final approval = _approvals[index];
              final busy = _processingId == approval['id'];
              return Card(
                margin: const EdgeInsets.only(bottom: 12),
                child: Padding(
                  padding: const EdgeInsets.all(16),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Row(
                        children: [
                          const Icon(Icons.gpp_maybe_outlined),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Text(
                              approval['toolName'] as String? ?? 'Unknown tool',
                              style: Theme.of(context).textTheme.titleMedium,
                            ),
                          ),
                        ],
                      ),
                      if (approval['status'] != 'pending') ...[
                        const SizedBox(height: 8),
                        Text(
                          'Decision recorded: ${approval['approved'] == true ? 'approved' : 'rejected'}. Jarvis needs to resume this call.',
                          style: TextStyle(
                            color: Theme.of(context).colorScheme.error,
                          ),
                        ),
                      ],
                      const SizedBox(height: 12),
                      const Text(
                        'Arguments',
                        style: TextStyle(fontWeight: FontWeight.w600),
                      ),
                      const SizedBox(height: 6),
                      Container(
                        width: double.infinity,
                        padding: const EdgeInsets.all(12),
                        decoration: BoxDecoration(
                          color: Theme.of(
                            context,
                          ).colorScheme.surfaceContainerHighest,
                          borderRadius: BorderRadius.circular(12),
                        ),
                        child: SelectableText(
                          _formatArguments(
                            approval['argumentsJson'] as String?,
                          ),
                          style: const TextStyle(
                            fontFamily: 'monospace',
                            fontSize: 12,
                          ),
                        ),
                      ),
                      const SizedBox(height: 12),
                      Row(
                        mainAxisAlignment: MainAxisAlignment.end,
                        children: [
                          if (approval['status'] == 'pending') ...[
                            TextButton(
                              onPressed: busy
                                  ? null
                                  : () => _decide(approval, false),
                              child: const Text('Reject'),
                            ),
                            const SizedBox(width: 8),
                          ],
                          FilledButton.icon(
                            onPressed: busy
                                ? null
                                : () => _decide(
                                    approval,
                                    approval['status'] == 'pending'
                                        ? true
                                        : approval['approved'] == true,
                                  ),
                            icon: busy
                                ? const SizedBox(
                                    width: 16,
                                    height: 16,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : const Icon(Icons.check),
                            label: Text(
                              approval['status'] == 'pending'
                                  ? 'Approve'
                                  : 'Retry resume',
                            ),
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
              );
            },
          ),
  );
}
