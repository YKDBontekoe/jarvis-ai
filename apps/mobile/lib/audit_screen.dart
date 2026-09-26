import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

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

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final response = await widget.http.get<List<dynamic>>(
        '/api/v1/audit',
        queryParameters: const {'limit': 150},
      );
      if (mounted) {
        setState(
          () => _events = (response.data ?? []).cast<Map<String, dynamic>>(),
        );
      }
    } on DioException {
      if (mounted) setState(() => _error = 'Jarvis could not load the audit log.');
    } finally {
      if (mounted) setState(() => _loading = false);
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
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: _loading && _events.isEmpty
        ? const Center(child: CircularProgressIndicator())
        : _error != null && _events.isEmpty
        ? Center(child: Text(_error!))
        : _events.isEmpty
        ? const Center(child: Text('No audited actions yet.'))
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView.separated(
              padding: const EdgeInsets.fromLTRB(16, 12, 16, 28),
              itemCount: _events.length,
              separatorBuilder: (_, _) => const SizedBox(height: 8),
              itemBuilder: (context, index) {
                final event = _events[index];
                final success = event['success'] as bool? ?? false;
                final timestamp = DateTime.tryParse(
                  event['timestamp'] as String? ?? '',
                )?.toLocal();
                final metadata = event['metadataJson'] as String?;
                return Card(
                  color: const Color(0xff1a1c25),
                  child: ListTile(
                    leading: Icon(
                      success ? Icons.verified_outlined : Icons.error_outline,
                      color: success
                          ? const Color(0xff68d6a8)
                          : const Color(0xfff08b8b),
                    ),
                    title: Text(
                      (event['action'] as String? ?? 'action').replaceAll('.', ' '),
                      style: const TextStyle(fontWeight: FontWeight.w600),
                    ),
                    subtitle: Padding(
                      padding: const EdgeInsets.only(top: 5),
                      child: Text(
                        [
                          event['tool'] as String? ?? 'Jarvis',
                          event['riskClass'] as String? ?? 'unknown risk',
                          if (timestamp != null) _formatTime(timestamp),
                          if (metadata != null && metadata != '{}') metadata,
                        ].join(' · '),
                      ),
                    ),
                    isThreeLine: metadata != null && metadata != '{}',
                  ),
                );
              },
            ),
          ),
  );

  String _formatTime(DateTime value) =>
      '${value.year}-${value.month.toString().padLeft(2, '0')}-${value.day.toString().padLeft(2, '0')} '
      '${value.hour.toString().padLeft(2, '0')}:${value.minute.toString().padLeft(2, '0')}';
}
