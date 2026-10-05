import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'people_models.dart';
import 'person_widgets.dart';
import 'radar_models.dart';
import 'radar_widgets.dart';

/// One person: birthday, keep-in-touch rhythm, notes, and what Jarvis
/// remembers about them from conversations.
class PersonDetailScreen extends StatefulWidget {
  const PersonDetailScreen({
    required this.http,
    required this.person,
    super.key,
  });

  final Dio http;
  final PersonData person;

  @override
  State<PersonDetailScreen> createState() => _PersonDetailScreenState();
}

class _PersonDetailScreenState extends State<PersonDetailScreen> {
  late PersonData _person = widget.person;
  List<({String predicate, String value})> _facts = const [];
  bool _saving = false;
  PersonRadarData? _radar;

  String get _path => '/api/v1/people/${_person.id}';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
    unawaited(_loadRadar());
  }

  // The radar is a bonus: without it the page simply has no radar card.
  Future<void> _loadRadar() async {
    try {
      final response = await widget.http.get<dynamic>('$_path/radar');
      if (!mounted) return;
      setState(() => _radar = PersonRadarData.fromJson(response.data));
    } on DioException {
      // Keep whatever is already shown.
    }
  }

  Future<void> _linkChat() async {
    final chat = await showChatPicker(context, widget.http);
    if (chat == null || !mounted) return;
    try {
      await widget.http.post<dynamic>(
        '$_path/links',
        data: {'connectionId': chat.connectionId, 'chatId': chat.chatId},
      );
      _toast('Linked ${_person.name} to ${chat.displayName}.');
      await _loadRadar();
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ??
            'Could not link that chat.',
      );
    }
  }

  Future<void> _unlink(PersonLinkData link) async {
    try {
      await widget.http.delete<dynamic>('$_path/links/${link.id}');
      _toast('Unlinked ${link.displayName}.');
      await _loadRadar();
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ?? 'Could not unlink that.',
      );
    }
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      final json = jsonObject(response.data);
      final person = PersonData.fromJson(json?['person']);
      if (!mounted || person == null) return;
      setState(() {
        _person = person;
        _facts = [
          for (final fact in jsonMaps(json?['facts']))
            if (jsonString(fact, 'predicate') case final predicate?)
              if (jsonString(fact, 'value') case final value?)
                (predicate: predicate, value: value),
        ];
      });
    } on DioException catch (error) {
      if (error.response?.statusCode == 404 && mounted) {
        Navigator.of(context).pop();
      }
    }
  }

  Future<void> _talkedToday() async {
    setState(() => _saving = true);
    try {
      final response = await widget.http.post<dynamic>(
        '$_path/contact',
        data: const <String, Object?>{},
      );
      final person = PersonData.fromJson(response.data);
      if (!mounted) return;
      if (person != null) setState(() => _person = person);
      _toast('Marked ${_person.name} as talked to today.');
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ?? 'Could not save that.',
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _edit() async {
    final draft = await showPersonEditor(
      context,
      person: _person,
      title: 'Edit ${_person.name}',
      confirmLabel: 'Save',
    );
    if (draft == null || !mounted) return;
    setState(() => _saving = true);
    try {
      final response = await widget.http.put<dynamic>(
        _path,
        data: {
          'name': draft.name,
          'relationship': draft.relationship,
          'birthdayMonth': draft.birthdayMonth,
          'birthdayDay': draft.birthdayDay,
          'birthYear': draft.birthYear,
          'contactEveryDays': draft.contactEveryDays,
          'notes': draft.notes,
        },
      );
      final person = PersonData.fromJson(response.data);
      if (mounted && person != null) setState(() => _person = person);
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ?? 'Could not save changes.',
      );
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _delete() async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Remove ${_person.name}?',
      message:
          'Their birthday, notes, and check-in reminders are removed. What '
          'Jarvis remembers from your chats stays in Memory.',
      confirmLabel: 'Remove',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!confirmed || !mounted) return;
    try {
      await widget.http.delete<dynamic>(_path);
      if (mounted) Navigator.of(context).pop();
    } on DioException catch (error) {
      _toast(firstProblemMessage(error.response?.data) ?? 'Could not remove.');
    }
  }

  void _toast(String message) {
    if (!mounted) return;
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final person = _person;
    return Scaffold(
      appBar: AppBar(
        actions: [
          HeaderAction(
            label: 'Edit',
            icon: PhosphorIconsRegular.pencilSimple,
            onPressed: _saving ? null : () => unawaited(_edit()),
          ),
          PopupMenuButton<String>(
            tooltip: 'More',
            icon: const Icon(PhosphorIconsRegular.dotsThree),
            onSelected: (_) => unawaited(_delete()),
            itemBuilder: (_) => const [
              PopupMenuItem(value: 'delete', child: Text('Remove person')),
            ],
          ),
          const SizedBox(width: 4),
        ],
      ),
      body: ListView(
        padding: EdgeInsets.fromLTRB(
          16,
          4,
          16,
          32 + MediaQuery.paddingOf(context).bottom,
        ),
        children: [
          ContentWidth(
            child: FadeSlideIn(
              child: Column(
                children: [
                  PersonAvatar(
                    name: person.name,
                    size: 84,
                    birthdayToday: person.birthdayToday,
                  ),
                  const SizedBox(height: 14),
                  Text(
                    person.name,
                    textAlign: TextAlign.center,
                    style: JarvisType.displayOf(context).copyWith(fontSize: 34),
                  ),
                  if (person.relationship case final relationship?)
                    Padding(
                      padding: const EdgeInsets.only(top: 4),
                      child: Text(
                        relationship,
                        style: TextStyle(fontSize: 15, color: colors.inkSoft),
                      ),
                    ),
                  if (person.birthdayToday)
                    Padding(
                      padding: const EdgeInsets.only(top: 14),
                      child: InlineNotice(
                        message: person.turningAge == null
                            ? 'It’s ${person.name}’s birthday today!'
                            : '${person.name} turns ${person.turningAge} today!',
                        tone: NoticeTone.info,
                      ),
                    ),
                  const SizedBox(height: 22),
                ],
              ),
            ),
          ),
          ContentWidth(
            child: FadeSlideIn(
              index: 1,
              child: LayoutBuilder(
                builder: (context, constraints) {
                  final tiles = [
                    _StatTile(
                      key: const Key('person-birthday-tile'),
                      icon: PhosphorIconsRegular.cake,
                      tint: colors.rose,
                      label: 'Birthday',
                      value: person.hasBirthday
                          ? birthdayDateLabel(
                              person.birthdayMonth!,
                              person.birthdayDay!,
                              person.birthYear,
                            )
                          : 'Not set',
                      detail: person.hasBirthday
                          ? [
                              birthdayCountdown(person),
                              if (person.turningAge case final age?)
                                'turns $age',
                            ].join(' · ')
                          : 'Add it to get a reminder',
                      onTap: person.hasBirthday
                          ? null
                          : () => unawaited(_edit()),
                    ),
                    _StatTile(
                      key: const Key('person-contact-tile'),
                      icon: PhosphorIconsRegular.phoneCall,
                      tint: person.contactDue ? colors.warning : colors.accent,
                      label: 'Keep in touch',
                      value: person.contactEveryDays == null
                          ? 'No reminder'
                          : _capitalize(cadenceLabel(person.contactEveryDays!)),
                      detail: person.contactDue
                          ? '${lastTalkedLabel(person)} · due now'
                          : lastTalkedLabel(person),
                      detailColor: person.contactDue ? colors.warning : null,
                      onTap: person.contactEveryDays == null
                          ? () => unawaited(_edit())
                          : null,
                    ),
                  ];
                  if (constraints.maxWidth < 320) {
                    return Column(
                      children: [
                        tiles[0],
                        const SizedBox(height: 10),
                        tiles[1],
                      ],
                    );
                  }
                  return IntrinsicHeight(
                    child: Row(
                      crossAxisAlignment: CrossAxisAlignment.stretch,
                      children: [
                        Expanded(child: tiles[0]),
                        const SizedBox(width: 10),
                        Expanded(child: tiles[1]),
                      ],
                    ),
                  );
                },
              ),
            ),
          ),
          const SizedBox(height: 14),
          ContentWidth(
            child: FadeSlideIn(
              index: 2,
              child: FilledButton.icon(
                key: const Key('person-talked-today'),
                onPressed: _saving ? null : () => unawaited(_talkedToday()),
                icon: const Icon(PhosphorIconsRegular.handWaving, size: 18),
                label: const Text('We talked today'),
                style: FilledButton.styleFrom(
                  minimumSize: const Size.fromHeight(48),
                ),
              ),
            ),
          ),
          if (_radar != null) ...[
            const SizedBox(height: 24),
            ContentWidth(
              child: FadeSlideIn(
                index: 3,
                child: RadarCard(
                  radar: _radar,
                  onLink: () => unawaited(_linkChat()),
                  onUnlink: (link) => unawaited(_unlink(link)),
                ),
              ),
            ),
          ],
          const SizedBox(height: 24),
          ContentWidth(
            child: FadeSlideIn(
              index: 3,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: [
                  const SectionHeader('Notes'),
                  SurfaceCard(
                    onTap: () => unawaited(_edit()),
                    child: Text(
                      person.notes ??
                          'Gift ideas, kids’ names, what you talked about '
                              'last time. Tap to add.',
                      style: TextStyle(
                        fontSize: 15,
                        height: 1.45,
                        color: person.notes == null ? colors.muted : colors.ink,
                      ),
                    ),
                  ),
                ],
              ),
            ),
          ),
          if (_facts.isNotEmpty) ...[
            const SizedBox(height: 24),
            ContentWidth(
              child: FadeSlideIn(
                index: 4,
                child: Column(
                  crossAxisAlignment: CrossAxisAlignment.stretch,
                  children: [
                    const SectionHeader('What Jarvis remembers'),
                    GroupedSection(
                      children: [
                        for (final fact in _facts)
                          Padding(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 16,
                              vertical: 12,
                            ),
                            child: Row(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                SizedBox(
                                  width: 120,
                                  child: Text(
                                    _capitalize(
                                      fact.predicate.replaceAll('_', ' '),
                                    ),
                                    style: TextStyle(
                                      fontSize: 13.5,
                                      color: colors.inkSoft,
                                    ),
                                  ),
                                ),
                                const SizedBox(width: 12),
                                Expanded(
                                  child: Text(
                                    fact.value,
                                    style: const TextStyle(fontSize: 14.5),
                                  ),
                                ),
                              ],
                            ),
                          ),
                      ],
                    ),
                  ],
                ),
              ),
            ),
          ],
        ],
      ),
    );
  }

  static String _capitalize(String text) =>
      text.isEmpty ? text : text[0].toUpperCase() + text.substring(1);
}

class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.icon,
    required this.tint,
    required this.label,
    required this.value,
    required this.detail,
    this.detailColor,
    this.onTap,
    super.key,
  });

  final IconData icon;
  final Color tint;
  final String label;
  final String value;
  final String detail;
  final Color? detailColor;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      onTap: onTap,
      padding: const EdgeInsets.all(16),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            children: [
              Icon(icon, size: 18, color: tint),
              const SizedBox(width: 8),
              Text(
                label,
                style: TextStyle(
                  fontSize: 13,
                  fontWeight: FontWeight.w600,
                  color: colors.inkSoft,
                ),
              ),
            ],
          ),
          const SizedBox(height: 12),
          Text(
            value,
            style: const TextStyle(fontSize: 17, fontWeight: FontWeight.w600),
          ),
          const SizedBox(height: 2),
          Text(
            detail,
            style: TextStyle(
              fontSize: 13,
              color: detailColor ?? colors.inkSoft,
            ),
          ),
        ],
      ),
    );
  }
}
