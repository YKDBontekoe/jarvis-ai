import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_markdown_plus/flutter_markdown_plus.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

part 'skill_detail_screen.dart';
part 'skill_editor_sheet.dart';

/// Visual label for where a skill came from.
({String label, IconData icon, Color color}) skillSource(String source) =>
    switch (source) {
      'learned' => (
        label: 'Learned',
        icon: PhosphorIconsRegular.sparkle,
        color: JarvisColors.violet,
      ),
      'imported' => (
        label: 'Imported',
        icon: PhosphorIconsRegular.downloadSimple,
        color: JarvisColors.info,
      ),
      _ => (
        label: 'Yours',
        icon: PhosphorIconsRegular.user,
        color: JarvisColors.accent,
      ),
    };

class SkillsScreen extends StatefulWidget {
  const SkillsScreen({required this.http, super.key});

  final Dio http;

  @override
  State<SkillsScreen> createState() => _SkillsScreenState();
}

class _SkillsScreenState extends State<SkillsScreen> {
  List<Map<String, dynamic>> _skills = const [];
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/skills');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _skills = jsonMaps(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load skills.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load skills.';
      });
    }
  }

  Future<void> _open(Map<String, dynamic> skill) async {
    final id = asJsonString(skill['id']);
    if (id == null) return;
    await Navigator.of(context).push<void>(
      MaterialPageRoute(
        builder: (_) => SkillDetailScreen(http: widget.http, skillId: id),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _create({bool import = false}) async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => SkillEditorSheet(http: widget.http, import: import),
    );
    if (created == true && mounted) unawaited(_load());
  }

  @override
  Widget build(BuildContext context) {
    List<Map<String, dynamic>> byStatus(String status) =>
        _skills.where((skill) => skill['status'] == status).toList();
    final proposed = byStatus('proposed');
    final active = byStatus('active');
    final disabled = byStatus('disabled');
    return Scaffold(
      appBar: AppBar(
        title: const Text('Skills'),
        actions: [
          IconButton(
            tooltip: 'Import SKILL.md',
            onPressed: () => unawaited(_create(import: true)),
            icon: const Icon(PhosphorIconsRegular.downloadSimple),
          ),
          HeaderAction(
            label: 'New',
            icon: PhosphorIconsRegular.plus,
            onPressed: () => unawaited(_create()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: _skills.isEmpty,
        onRetry: () => unawaited(_load()),
        empty: EmptyState(
          icon: PhosphorIconsRegular.magicWand,
          title: 'No skills yet',
          message:
              'Jarvis writes a skill when it finishes a workflow you are likely to repeat, or when you teach it how you like something done. You can also add or import SKILL.md files.',
          action: FilledButton.icon(
            onPressed: () => unawaited(_create()),
            icon: const Icon(PhosphorIconsRegular.plus, size: 18),
            label: const Text('Write a skill'),
          ),
        ),
        child: RefreshIndicator(
          onRefresh: _load,
          child: ListView(
            padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
            children: [
              ContentWidth(
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const InlineNotice(
                      tone: NoticeTone.info,
                      message:
                          'Skills are reusable procedures Jarvis loads only when relevant. Learned skills improve over time; skills you edit are locked so Jarvis never overwrites them.',
                    ),
                    if (proposed.isNotEmpty)
                      _section('Needs your review', proposed),
                    if (active.isNotEmpty) _section('Active', active),
                    if (disabled.isNotEmpty) _section('Disabled', disabled),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }

  Widget _section(String title, List<Map<String, dynamic>> skills) => Padding(
    padding: const EdgeInsets.only(top: 20),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(title),
        GroupedSection(
          dividerIndent: 64,
          children: [for (final skill in skills) _tile(skill)],
        ),
      ],
    ),
  );

  Widget _tile(Map<String, dynamic> skill) {
    final source = skillSource(asJsonString(skill['source']) ?? 'user');
    final uses = asJsonInt(skill['useCount']);
    return ListTile(
      key: Key('skill-${skill['name']}'),
      leading: IconBadge(icon: source.icon, color: source.color, size: 36),
      title: Row(
        children: [
          Flexible(
            child: Text(
              asJsonString(skill['name']) ?? '',
              overflow: TextOverflow.ellipsis,
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
          ),
          if (asJsonBool(skill['isLocked'])) ...[
            const SizedBox(width: 6),
            const Icon(
              PhosphorIconsRegular.lockSimple,
              size: 14,
              color: JarvisColors.muted,
            ),
          ],
        ],
      ),
      subtitle: Text(
        '${asJsonString(skill['description']) ?? ''}\n'
        '${source.label} · v${asJsonInt(skill['version'], 1)} · used $uses×',
        maxLines: 3,
        overflow: TextOverflow.ellipsis,
      ),
      isThreeLine: true,
      trailing: const Icon(
        PhosphorIconsRegular.caretRight,
        size: 16,
        color: JarvisColors.muted,
      ),
      onTap: () => unawaited(_open(skill)),
    );
  }
}
