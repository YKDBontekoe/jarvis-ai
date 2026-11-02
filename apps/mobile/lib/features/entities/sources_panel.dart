import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import '../../ui/source_row.dart';
import 'entity_ref.dart';
import 'entity_screen.dart';

/// One resolved source: what it is called, when, and whether it still exists.
class ResolvedSource {
  const ResolvedSource({
    required this.ref,
    this.name,
    this.date,
    this.available = true,
  });

  final EntityRef? ref;
  final String? name;
  final DateTime? date;
  final bool available;
}

/// Evidence for something, in the same rows everywhere. Either the sources
/// the server links to a thing ([SourcesPanel.of], for example the chat a
/// memory was learned in), or a list of `type:id` references a draft cites
/// ([SourcesPanel.refs]). Opening reuses the entity page and chat routing, and a
/// source that was deleted shows as unavailable.
class SourcesPanel extends StatefulWidget {
  const SourcesPanel.of({
    required this.http,
    required EntityRef this.entity,
    this.expectsSource = true,
    this.relations = const {'source'},
    this.collapsedLabel,
    this.onOpenConversation,
    super.key,
  }) : refs = null;

  const SourcesPanel.refs({
    required this.http,
    required List<String> this.refs,
    this.onOpenConversation,
    super.key,
  }) : entity = null,
       expectsSource = false,
       relations = const {'source'},
       collapsedLabel = null;

  final Dio http;
  final EntityRef? entity;
  final List<String>? refs;

  /// With [SourcesPanel.of]: show "no longer available" when the thing has a
  /// source but it cannot be found any more.
  final bool expectsSource;

  /// Which links count as evidence: `source` for what a memory was learned
  /// from, plus `created` for the chat a decision was logged in.
  final Set<String> relations;

  /// In long lists the source is looked up only when asked for: the panel
  /// starts as this one line and loads when tapped. Null loads straight away.
  final String? collapsedLabel;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<SourcesPanel> createState() => _SourcesPanelState();
}

class _SourcesPanelState extends State<SourcesPanel> {
  List<ResolvedSource>? _sources;
  late bool _expanded = widget.collapsedLabel == null;

  @override
  void initState() {
    super.initState();
    if (_expanded) unawaited(_load());
  }

  @override
  void didUpdateWidget(SourcesPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.entity != widget.entity ||
        oldWidget.refs?.join('|') != widget.refs?.join('|')) {
      unawaited(_load());
    }
  }

  Future<String?> _title(EntityRef ref) async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/entities/${ref.type}/${ref.id}/related',
      );
      return asJsonString(jsonObject(response.data)?['title']);
    } on DioException {
      return null;
    }
  }

  Future<DateTime?> _conversationDate(EntityRef ref) async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/conversations/${ref.id}',
        queryParameters: {'includeMessages': false},
      );
      return jsonDate(jsonObject(response.data)?['createdAt'], local: true);
    } on DioException {
      return null;
    }
  }

  Future<void> _load() async {
    final entity = widget.entity;
    List<ResolvedSource> sources;
    if (entity != null) {
      sources = await _linkedSources(entity);
    } else {
      sources = [
        for (final raw in widget.refs ?? const <String>[]) await _cited(raw),
      ];
    }
    if (mounted) setState(() => _sources = sources);
  }

  Future<List<ResolvedSource>> _linkedSources(EntityRef entity) async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/entities/${entity.type}/${entity.id}/related',
      );
      final related = jsonMaps(jsonObject(response.data)?['related']);
      final found = <ResolvedSource>[];
      for (final item in related) {
        if (!widget.relations.contains(asJsonString(item['relation'])) ||
            asJsonString(item['direction']) != 'in') {
          continue;
        }
        final ref = EntityRef.tryParse(asJsonString(item['ref']));
        if (ref == null) continue;
        var date = jsonDate(item['at'], local: true);
        if (date == null && ref.type == 'conversation') {
          date = await _conversationDate(ref);
        }
        found.add(
          ResolvedSource(
            ref: ref,
            name: asJsonString(item['title']),
            date: date,
          ),
        );
      }
      if (found.isEmpty && widget.expectsSource) {
        return const [ResolvedSource(ref: null, available: false)];
      }
      return found;
    } on DioException {
      return widget.expectsSource
          ? const [ResolvedSource(ref: null, available: false)]
          : const [];
    }
  }

  Future<ResolvedSource> _cited(String raw) async {
    final ref = EntityRef.tryParse(raw);
    // A reference this app cannot open still names where the fact came from.
    if (ref == null) return ResolvedSource(ref: null, name: raw);
    final title = await _title(ref);
    if (title == null) return ResolvedSource(ref: ref, available: false);
    final date = ref.type == 'conversation'
        ? await _conversationDate(ref)
        : null;
    return ResolvedSource(ref: ref, name: title, date: date);
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    if (!_expanded) {
      return InkWell(
        key: const Key('sources-expand'),
        onTap: () {
          setState(() => _expanded = true);
          unawaited(_load());
        },
        borderRadius: BorderRadius.circular(6),
        child: Padding(
          padding: const EdgeInsets.only(top: 10, bottom: 2),
          child: Row(
            children: [
              Icon(PhosphorIconsRegular.sparkle, size: 14, color: colors.muted),
              const SizedBox(width: 6),
              Text(
                widget.collapsedLabel!,
                style: TextStyle(
                  fontSize: 12.5,
                  color: colors.inkSoft,
                  fontWeight: FontWeight.w500,
                ),
              ),
              const SizedBox(width: 4),
              Icon(
                PhosphorIconsRegular.caretRight,
                size: 12,
                color: colors.muted,
              ),
            ],
          ),
        ),
      );
    }
    final sources = _sources;
    if (sources == null || sources.isEmpty) return const SizedBox.shrink();
    return Column(
      key: const Key('sources-panel'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Padding(
          padding: const EdgeInsets.only(top: 10, bottom: 2),
          child: Text(
            sources.length == 1 ? 'Source' : 'Sources',
            style: TextStyle(
              fontSize: 12,
              fontWeight: FontWeight.w600,
              color: colors.muted,
            ),
          ),
        ),
        for (final source in sources)
          SourceRow(
            name: source.name ?? source.ref?.kind.label ?? 'Source',
            kind: source.ref?.kind.label,
            date: source.date,
            unavailable: !source.available,
            onOpen: source.ref == null || !source.available
                ? null
                : () => unawaited(
                    openEntity(
                      context,
                      widget.http,
                      source.ref!,
                      onOpenConversation: widget.onOpenConversation,
                    ),
                  ),
          ),
      ],
    );
  }
}
