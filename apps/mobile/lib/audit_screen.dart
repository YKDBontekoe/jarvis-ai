import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'ui/phosphor_icons.dart';

import 'theme.dart';
import 'json_maps.dart';
import 'ui/jarvis_ui.dart';

class AuditScreen extends StatefulWidget {
  const AuditScreen({required this.http, super.key});

  final Dio http;

  @override
  State<AuditScreen> createState() => _AuditScreenState();
}

class _AuditScreenState extends State<AuditScreen> {
  List<Map<String, dynamic>> _events = [];
  bool _loading = true;
  String? _error;
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
        '/api/v1/audit',
        queryParameters: const {'limit': 150},
      );
      if (mounted && revision == _requestRevision) {
        setState(
          () => _events = jsonMaps(response.data),
        );
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load the audit log.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Audit log'),
      actions: [
        IconButton(
          tooltip: 'Refresh audit log',
          onPressed: _loading ? null : _load,
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        const SizedBox(width: 8),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _events.isEmpty,
      onRetry: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.listChecks,
        title: 'No audited actions yet.',
        message:
            'Approvals, tasks, reminders, files, and memory changes are recorded here.',
      ),
      child: RefreshIndicator(
            onRefresh: _load,
            child: ListView.builder(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              itemCount: _events.length,
              itemBuilder: (context, index) => ContentWidth(
                child: _AuditRow(
                  event: _events[index],
                  first: index == 0,
                  last: index == _events.length - 1,
                ),
              ),
            ),
          ),
    ),
  );
}

class _AuditRow extends StatelessWidget {
  const _AuditRow({
    required this.event,
    required this.first,
    required this.last,
  });

  final Map<String, dynamic> event;
  final bool first;
  final bool last;

  @override
  Widget build(BuildContext context) {
    final success = asJsonBool(event['success']);
    final timestamp = DateTime.tryParse(
      asJsonString(event['timestamp']) ?? '',
    )?.toLocal();
    final metadata = asJsonString(event['metadataJson']);
    final risk = asJsonString(event['riskClass']) ?? 'unknown risk';
    final color = success ? JarvisColors.success : JarvisColors.danger;
    return IntrinsicHeight(
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SizedBox(
            width: 28,
            child: Column(
              children: [
                Container(
                  width: 2,
                  height: 18,
                  color: first ? Colors.transparent : JarvisColors.outline,
                ),
                Container(
                  width: 12,
                  height: 12,
                  decoration: BoxDecoration(
                    color: JarvisColors.surface,
                    shape: BoxShape.circle,
                    border: Border.all(color: color, width: 3),
                  ),
                ),
                Expanded(
                  child: Container(
                    width: 2,
                    color: last ? Colors.transparent : JarvisColors.outline,
                  ),
                ),
              ],
            ),
          ),
          const SizedBox(width: 8),
          Expanded(
            child: SurfaceCard(
              margin: const EdgeInsets.only(bottom: 10),
              padding: const EdgeInsets.fromLTRB(14, 12, 14, 12),
              radius: JarvisRadii.md,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      Icon(
                        success
                            ? PhosphorIconsRegular.checkCircle
                            : PhosphorIconsRegular.warningCircle,
                        size: 17,
                        color: color,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          (asJsonString(event['action']) ?? 'action').replaceAll(
                            '.',
                            ' ',
                          ),
                          style: Theme.of(context).textTheme.titleSmall,
                        ),
                      ),
                      if (timestamp != null)
                        Text(
                          _formatTime(timestamp),
                          style: Theme.of(context).textTheme.bodySmall
                              ?.copyWith(color: JarvisColors.muted),
                        ),
                    ],
                  ),
                  const SizedBox(height: 8),
                  Wrap(
                    spacing: 6,
                    runSpacing: 6,
                    children: [
                      _Tag(asJsonString(event['tool']) ?? 'Jarvis'),
                      StatusPill(label: risk, color: _riskColor(risk)),
                    ],
                  ),
                  if (metadata != null && metadata != '{}') ...[
                    const SizedBox(height: 8),
                    Text(
                      metadata,
                      maxLines: 3,
                      overflow: TextOverflow.ellipsis,
                      style: const TextStyle(
                        fontFamily: 'monospace',
                        fontSize: 12,
                        color: JarvisColors.inkSoft,
                      ),
                    ),
                  ],
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }

  static Color _riskColor(String risk) {
    final value = risk.toLowerCase();
    if (value.contains('high') || value.contains('destructive')) {
      return JarvisColors.danger;
    }
    if (value.contains('medium') || value.contains('write')) {
      return JarvisColors.warning;
    }
    if (value.contains('low') || value.contains('read')) {
      return JarvisColors.success;
    }
    return JarvisColors.inkSoft;
  }

  static String _formatTime(DateTime value) =>
      '${value.year}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')} '
      '${value.hour.toString().padLeft(2, '0')}:${value.minute.toString().padLeft(2, '0')}';
}

class _Tag extends StatelessWidget {
  const _Tag(this.label);

  final String label;

  @override
  Widget build(BuildContext context) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
    decoration: BoxDecoration(
      color: JarvisColors.surfaceMuted,
      borderRadius: BorderRadius.circular(8),
    ),
    child: Text(
      label,
      style: const TextStyle(
        fontFamily: 'monospace',
        fontSize: 12,
        color: JarvisColors.inkSoft,
      ),
    ),
  );
}
