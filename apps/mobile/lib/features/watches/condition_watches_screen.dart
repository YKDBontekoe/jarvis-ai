import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../ui/phosphor_icons.dart';

import '../../http_urls.dart';
import '../../theme.dart';
import '../../json_maps.dart';
import '../../ui/jarvis_ui.dart';

part 'watch_editor.dart';

class ConditionWatchesScreen extends StatefulWidget {
  const ConditionWatchesScreen({required this.http, super.key});

  final Dio http;

  @override
  State<ConditionWatchesScreen> createState() => _ConditionWatchesScreenState();
}

class _ConditionWatchesScreenState extends State<ConditionWatchesScreen> {
  List<Map<String, dynamic>> _watches = [];
  bool _loading = true;
  bool _creating = false;
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
      final response = await widget.http.get<dynamic>('/api/v1/watches');
      if (mounted && revision == _requestRevision) {
        setState(() => _watches = jsonMaps(response.data));
      }
    } on DioException {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load watches.');
      }
    } catch (_) {
      if (mounted && revision == _requestRevision) {
        setState(() => _error = 'Jarvis could not load watches.');
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  Future<void> _createWatch() async {
    final created = await showJarvisDialog<_NewWatch>(
      context: context,
      builder: (_) => const _NewWatchDialog(),
    );
    if (created == null || !mounted) return;

    setState(() => _creating = true);
    try {
      await widget.http.post<void>(
        '/api/v1/watches',
        data: {
          'title': created.title,
          'kind': created.kind,
          'url': created.url,
          'jsonPath': created.jsonPath,
          'comparison': created.comparison,
          'threshold': created.threshold,
          'intervalMinutes': created.intervalMinutes,
          if (created.credentialProvider != null)
            'credentialProvider': created.credentialProvider,
          if (created.latitude != null) 'latitude': created.latitude,
          if (created.longitude != null) 'longitude': created.longitude,
          if (created.radiusMeters != null)
            'radiusMeters': created.radiusMeters,
          if (created.minutesBefore != null)
            'minutesBefore': created.minutesBefore,
        },
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The durable watch service is unavailable. Try again shortly.'
            : 'Jarvis could not start that watch. Check the watch type, URL, and threshold.';
        _showError(message);
      }
    } catch (_) {
      if (mounted) {
        _showError('Jarvis could not start that watch.');
      }
    } finally {
      if (mounted) setState(() => _creating = false);
    }
  }

  Future<void> _cancel(Map<String, dynamic> watch) async {
    final title = asJsonString(watch['title']) ?? 'Condition watch';
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Stop watching?',
      message: '“$title” will no longer be checked.',
      cancelLabel: 'Keep watch',
      confirmLabel: 'Stop watching',
      destructive: true,
      icon: PhosphorIconsRegular.stopCircle,
    );
    if (!confirmed) return;
    if (!mounted) return;
    final id = jsonId(watch);
    if (id == null) return;
    try {
      await widget.http.delete<void>('/api/v1/watches/$id');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not stop that watch.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not stop that watch.');
    }
  }

  void _showError(String message) {
    ScaffoldMessenger.of(context)
        .showSnackBar(SnackBar(content: Text(message)));
  }

  String _condition(Map<String, dynamic> watch) {
    final kind = asJsonString(watch['kind']) ?? 'public_json';
    final comparison = watch['comparison'] == 'below' ? '≤' : '≥';
    return switch (kind) {
      'device_battery' => 'battery $comparison ${watch['threshold']}%',
      'device_location' =>
        'distance $comparison ${watch['radiusMeters'] ?? watch['threshold']} m',
      'calendar' =>
        'next event in ≤ ${watch['minutesBefore'] ?? watch['threshold']} min',
      'authenticated_json' =>
        '${watch['jsonPath']} $comparison ${watch['threshold']} (auth)',
      _ => '${watch['jsonPath']} $comparison ${watch['threshold']}',
    };
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Condition watches'),
      actions: [
        IconButton(
          tooltip: 'Refresh watches',
          onPressed: _loading ? null : _load,
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'New',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createWatch,
          busy: _creating,
        ),
      ],
    ),
    body: ListScreenBody(
      loading: _loading,
      error: _error,
      isEmpty: _watches.isEmpty,
      onRetry: _load,
      onRefresh: _load,
      empty: const EmptyState(
        icon: PhosphorIconsRegular.pulse,
        title: 'No watches yet',
        message: 'Set a threshold, like a price or a temperature, and Jarvis keeps an eye on it for you.',
      ),
      child: OrbRefresh(
        onRefresh: _load,
        child: ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
          itemCount: _watches.length,
          itemBuilder: (context, index) =>
              ContentWidth(child: _watchCard(_watches[index])),
        ),
      ),
    ),
  );

  Widget _watchCard(Map<String, dynamic> watch) {
    final status = asJsonString(watch['status']) ?? '';
    final active = status == 'active';
    final lastValue = watch['lastValue'];
    final style = statusStyle(status);
    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 6, 14),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          IconBadge(
            icon: active
                ? PhosphorIconsRegular.pulse
                : PhosphorIconsRegular.checkCircle,
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        asJsonString(watch['title']) ?? 'Condition watch',
                        style: Theme.of(context).textTheme.titleSmall
                            ?.copyWith(fontSize: 15),
                      ),
                    ),
                    StatusPill(label: style.label, color: style.color),
                  ],
                ),
                const SizedBox(height: 10),
                Container(
                  padding: const EdgeInsets.symmetric(
                    horizontal: 10,
                    vertical: 6,
                  ),
                  decoration: BoxDecoration(
                    color: JarvisColors.of(context).canvas,
                    borderRadius: BorderRadius.circular(JarvisRadii.sm),
                    border: Border.all(color: JarvisColors.of(context).outline),
                  ),
                  child: Text(
                    _condition(watch),
                    style: TextStyle(
                      fontFamily: 'monospace',
                      fontSize: 13,
                      color: JarvisColors.of(context).ink,
                    ),
                  ),
                ),
                if (active) ...[
                  const SizedBox(height: 10),
                  Wrap(
                    spacing: 14,
                    runSpacing: 4,
                    children: [
                      _meta(
                        PhosphorIconsRegular.timer,
                        'Checks every ${watch['intervalMinutes']} min',
                      ),
                      if (lastValue != null)
                        _meta(
                          PhosphorIconsRegular.chartLine,
                          'latest $lastValue',
                        ),
                    ],
                  ),
                ],
              ],
            ),
          ),
          if (active)
            IconButton(
              tooltip: 'Stop watching',
              onPressed: () => _cancel(watch),
              icon: const Icon(PhosphorIconsRegular.stopCircle, size: 22),
            ),
        ],
      ),
    );
  }

  Widget _meta(IconData icon, String text) => Row(
    mainAxisSize: MainAxisSize.min,
    children: [
      Icon(icon, size: 15, color: JarvisColors.of(context).muted),
      const SizedBox(width: 5),
      Text(text, style: Theme.of(context).textTheme.bodySmall),
    ],
  );
}
