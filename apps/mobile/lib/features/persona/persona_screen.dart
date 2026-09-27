import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

const personaCategories = {
  'tone': ('Tone', PhosphorIconsRegular.chatCenteredText),
  'format': ('Format', PhosphorIconsRegular.listChecks),
  'language': ('Language', PhosphorIconsRegular.globe),
  'workstyle': ('Working style', PhosphorIconsRegular.lightning),
  'boundaries': ('Boundaries', PhosphorIconsRegular.shieldCheck),
  'schedule': ('Schedule', PhosphorIconsRegular.clock),
  'other': ('Other', PhosphorIconsRegular.sparkle),
};

/// Shows and edits how Jarvis has learned to work with the owner.
class PersonaScreen extends StatefulWidget {
  const PersonaScreen({required this.http, super.key});

  final Dio http;

  @override
  State<PersonaScreen> createState() => _PersonaScreenState();
}

class _PersonaScreenState extends State<PersonaScreen> {
  final _name = TextEditingController();
  final _language = TextEditingController();
  final _instructions = TextEditingController();
  List<Map<String, dynamic>> _traits = const [];
  String? _lastReflected;
  bool _loading = true;
  bool _saving = false;
  String? _error;
  String? _notice;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _name.dispose();
    _language.dispose();
    _instructions.dispose();
    super.dispose();
  }

  void _apply(Map<String, dynamic> data, {bool traitsOnly = false}) {
    _traits = jsonMaps(data['traits']);
    _lastReflected = asJsonString(data['lastReflectedAt']);
    if (traitsOnly) return;
    _name.text = asJsonString(data['preferredName']) ?? '';
    _language.text = asJsonString(data['replyLanguage']) ?? '';
    _instructions.text = asJsonString(data['customInstructions']) ?? '';
  }

  Future<void> _load({bool traitsOnly = false}) async {
    try {
      final response = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/persona',
      );
      if (!mounted) return;
      setState(() {
        _apply(response.data ?? const {}, traitsOnly: traitsOnly);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your persona.';
      });
    }
  }

  Future<void> _mutate(Future<void> Function() action, [String? notice]) async {
    setState(() {
      _saving = true;
      _error = null;
      _notice = null;
    });
    try {
      await action();
      await _load(traitsOnly: true);
      if (mounted && notice != null) setState(() => _notice = notice);
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Jarvis could not save that change.',
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _saveProfile() => _mutate(
    () => widget.http.put<void>(
      '/api/v1/persona',
      data: {
        'preferredName': _name.text,
        'replyLanguage': _language.text,
        'customInstructions': _instructions.text,
      },
    ),
    'Saved. Jarvis uses this from the next reply.',
  );

  Future<void> _teach() async {
    final result = await showDialog<(String, String)>(
      context: context,
      builder: (_) => const _TraitDialog(),
    );
    if (result == null) return;
    await _mutate(
      () => widget.http.post<void>(
        '/api/v1/persona/traits',
        data: {'category': result.$1, 'statement': result.$2},
      ),
      'Jarvis will follow that rule.',
    );
  }

  Future<void> _edit(Map<String, dynamic> trait) async {
    final result = await showDialog<(String, String)>(
      context: context,
      builder: (_) => _TraitDialog(
        category: asJsonString(trait['category']),
        statement: asJsonString(trait['statement']),
      ),
    );
    if (result == null) return;
    await _mutate(
      () => widget.http.patch<void>(
        '/api/v1/persona/traits/${trait['id']}',
        data: {'statement': result.$2},
      ),
    );
  }

  Future<void> _togglePin(Map<String, dynamic> trait) => _mutate(
    () => widget.http.patch<void>(
      '/api/v1/persona/traits/${trait['id']}',
      data: {'pinned': !asJsonBool(trait['pinned'])},
    ),
  );

  Future<void> _remove(Map<String, dynamic> trait) => _mutate(
    () => widget.http.delete<void>('/api/v1/persona/traits/${trait['id']}'),
    'Jarvis forgot that preference.',
  );

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Persona'),
      actions: [
        HeaderAction(
          label: 'Teach',
          icon: PhosphorIconsRegular.graduationCap,
          onPressed: _saving ? null : () => unawaited(_teach()),
        ),
      ],
    ),
    body: _loading
        ? const LoadingState()
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _intro(),
                      const SizedBox(height: 16),
                      _profileCard(),
                      if (_error != null)
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      if (_notice != null)
                        InlineNotice(
                          message: _notice!,
                          tone: NoticeTone.success,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      const SizedBox(height: 20),
                      SectionHeader(
                        'What Jarvis has learned',
                        trailing: Text(
                          '${_traits.length} rules',
                          style: const TextStyle(color: JarvisColors.muted),
                        ),
                      ),
                      if (_traits.isEmpty)
                        const SurfaceCard(
                          child: Text(
                            'Nothing yet. Tell Jarvis how you like things done, rate replies with 👍 or 👎, or turn on the learning heartbeat — it picks up your preferences over time.',
                            style: TextStyle(color: JarvisColors.inkSoft),
                          ),
                        )
                      else
                        GroupedSection(
                          dividerIndent: 64,
                          children: [
                            for (final trait in _traits) _trait(trait),
                          ],
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
  );

  Widget _intro() => SurfaceCard(
    gradient: const LinearGradient(
      colors: [JarvisColors.accentSoft, JarvisColors.surface],
      begin: Alignment.topLeft,
      end: Alignment.bottomRight,
    ),
    child: Row(
      children: [
        const IconBadge(
          icon: PhosphorIconsRegular.userCircle,
          color: JarvisColors.accent,
          size: 44,
        ),
        const SizedBox(width: 14),
        Expanded(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Text(
                'How Jarvis works with you',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 4),
              Text(
                _lastReflected == null
                    ? 'Learned from what you say, your feedback, and reflection.'
                    : 'Last reflection ${_relative(_lastReflected!)}.',
                style: const TextStyle(color: JarvisColors.inkSoft),
              ),
            ],
          ),
        ),
      ],
    ),
  );

  Widget _profileCard() => SurfaceCard(
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          children: [
            Expanded(
              child: TextField(
                key: const Key('persona-name'),
                controller: _name,
                decoration: const InputDecoration(labelText: 'Call me'),
              ),
            ),
            const SizedBox(width: 12),
            Expanded(
              child: TextField(
                key: const Key('persona-language'),
                controller: _language,
                decoration: const InputDecoration(
                  labelText: 'Reply language',
                  hintText: 'e.g. Dutch',
                ),
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        TextField(
          key: const Key('persona-instructions'),
          controller: _instructions,
          minLines: 3,
          maxLines: 8,
          maxLength: 4000,
          decoration: const InputDecoration(
            labelText: 'Your instructions for Jarvis',
            hintText:
                'Anything Jarvis should always keep in mind — your role, priorities, pet peeves…',
            alignLabelWithHint: true,
          ),
        ),
        Align(
          alignment: Alignment.centerRight,
          child: FilledButton(
            key: const Key('save-persona'),
            onPressed: _saving ? null : () => unawaited(_saveProfile()),
            child: const Text('Save'),
          ),
        ),
      ],
    ),
  );

  Widget _trait(Map<String, dynamic> trait) {
    final category =
        personaCategories[asJsonString(trait['category'])] ??
        personaCategories['other']!;
    final confidence = (trait['confidence'] is num)
        ? (trait['confidence'] as num).toDouble().clamp(0.0, 1.0)
        : 0.0;
    final pinned = asJsonBool(trait['pinned']);
    final learned = asJsonString(trait['source']) == 'learned';
    final evidence = asJsonInt(trait['evidence'], 1);
    return ListTile(
      key: Key('trait-${trait['id']}'),
      leading: IconBadge(
        icon: category.$2,
        color: learned ? JarvisColors.violet : JarvisColors.accent,
        size: 36,
      ),
      title: Text(asJsonString(trait['statement']) ?? ''),
      subtitle: Padding(
        padding: const EdgeInsets.only(top: 6),
        child: Row(
          children: [
            Text(
              '${category.$1} · ${learned ? 'learned' : 'yours'} · seen $evidence×',
              style: const TextStyle(fontSize: 12.5),
            ),
            const SizedBox(width: 10),
            Expanded(
              child: ClipRRect(
                borderRadius: BorderRadius.circular(4),
                child: LinearProgressIndicator(
                  value: pinned ? 1 : confidence,
                  minHeight: 4,
                  backgroundColor: JarvisColors.surfaceMuted,
                  color: pinned ? JarvisColors.accent : JarvisColors.violet,
                ),
              ),
            ),
          ],
        ),
      ),
      onTap: () => unawaited(_edit(trait)),
      trailing: Row(
        mainAxisSize: MainAxisSize.min,
        children: [
          IconButton(
            tooltip: pinned ? 'Unpin' : 'Pin — never forget',
            onPressed: _saving ? null : () => unawaited(_togglePin(trait)),
            icon: Icon(
              pinned ? PhosphorIconsFill.pushPin : PhosphorIconsRegular.pushPin,
              size: 18,
              color: pinned ? JarvisColors.accent : JarvisColors.muted,
            ),
          ),
          IconButton(
            tooltip: 'Forget',
            onPressed: _saving ? null : () => unawaited(_remove(trait)),
            icon: const Icon(
              PhosphorIconsRegular.trash,
              size: 18,
              color: JarvisColors.muted,
            ),
          ),
        ],
      ),
    );
  }

  String _relative(String iso) {
    final time = DateTime.tryParse(iso);
    if (time == null) return 'recently';
    final minutes = DateTime.now().difference(time).inMinutes;
    if (minutes < 1) return 'just now';
    if (minutes < 60) return '$minutes min ago';
    if (minutes < 1440) return '${minutes ~/ 60} h ago';
    return '${minutes ~/ 1440} days ago';
  }
}

class _TraitDialog extends StatefulWidget {
  const _TraitDialog({this.category, this.statement});

  final String? category;
  final String? statement;

  @override
  State<_TraitDialog> createState() => _TraitDialogState();
}

class _TraitDialogState extends State<_TraitDialog> {
  late String _category = widget.category ?? 'format';
  late final _statement = TextEditingController(text: widget.statement ?? '');

  @override
  void dispose() {
    _statement.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: Text(widget.statement == null ? 'Teach Jarvis' : 'Edit rule'),
    content: Column(
      mainAxisSize: MainAxisSize.min,
      children: [
        if (widget.statement == null)
          DropdownButtonFormField<String>(
            initialValue: _category,
            decoration: const InputDecoration(labelText: 'About'),
            items: [
              for (final entry in personaCategories.entries)
                DropdownMenuItem(value: entry.key, child: Text(entry.value.$1)),
            ],
            onChanged: (value) => setState(() => _category = value ?? 'other'),
          ),
        const SizedBox(height: 12),
        TextField(
          key: const Key('trait-statement'),
          controller: _statement,
          autofocus: true,
          maxLines: 3,
          maxLength: 280,
          decoration: const InputDecoration(
            hintText: 'e.g. Keep answers under five sentences.',
          ),
        ),
      ],
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      FilledButton(
        key: const Key('save-trait'),
        onPressed: () =>
            Navigator.pop(context, (_category, _statement.text.trim())),
        child: const Text('Save'),
      ),
    ],
  );
}
