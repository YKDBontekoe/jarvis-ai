import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'people_models.dart';
import 'person_detail_screen.dart';
import 'person_widgets.dart';
import 'radar_models.dart';
import 'radar_widgets.dart';

/// The people in the owner's life: upcoming birthdays, who is due for a
/// check-in, and everyone else. Jarvis sends a notification on birthdays and
/// when it is time to get in touch.
class PeopleScreen extends StatefulWidget {
  const PeopleScreen({required this.http, this.openPersonId, super.key});

  final Dio http;

  /// Opens this person's page once the list has loaded, as when a birthday or
  /// check-in notification is tapped.
  final String? openPersonId;

  @override
  State<PeopleScreen> createState() => _PeopleScreenState();
}

class _PeopleScreenState extends State<PeopleScreen>
    with WidgetsBindingObserver {
  static const _upcomingDays = 30;

  List<PersonData> _people = const [];
  List<PersonSuggestionData> _suggestions = const [];
  RadarOverviewData? _radar;
  List<LinkSuggestionData> _linkSuggestions = const [];
  final Set<String> _linking = {};
  bool _loading = true;
  String? _error;
  int _requestRevision = 0;
  final Set<String> _busy = {};
  late String? _pendingOpen = widget.openPersonId;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    unawaited(_load());
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  // Jarvis may have logged a call from WhatsApp while the app was away.
  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    setState(() => _loading = true);
    try {
      final response = await widget.http.get<dynamic>('/api/v1/people');
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _people = PersonData.listFromJson(response.data);
        _loading = false;
        _error = null;
      });
      unawaited(_loadSuggestions());
      unawaited(_loadRadar());
      final open = _pendingOpen;
      if (open != null) {
        _pendingOpen = null;
        final person = _people.where((x) => x.id == open).firstOrNull;
        if (person != null) unawaited(_open(person));
      }
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your people.';
      });
    }
  }

  // Suggestions are a bonus; the list works without them.
  Future<void> _loadSuggestions() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/people/suggestions',
      );
      if (!mounted) return;
      setState(
        () => _suggestions = PersonSuggestionData.listFromJson(response.data),
      );
    } on DioException {
      if (mounted) setState(() => _suggestions = const []);
    }
  }

  // The radar is a bonus too: without it the list just has no radar section.
  Future<void> _loadRadar() async {
    try {
      final response = await widget.http.get<dynamic>('/api/v1/people/radar');
      if (!mounted) return;
      setState(() => _radar = RadarOverviewData.fromJson(response.data));
    } on DioException {
      if (mounted) setState(() => _radar = null);
    }
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/people/link-suggestions',
      );
      if (!mounted) return;
      setState(
        () => _linkSuggestions = LinkSuggestionData.listFromJson(response.data),
      );
    } on DioException {
      if (mounted) setState(() => _linkSuggestions = const []);
    }
  }

  Future<void> _setTone(bool enabled) async {
    final before = _radar;
    if (before == null) return;
    setState(
      () => _radar = RadarOverviewData(
        toneEnabled: enabled,
        people: before.people,
      ),
    );
    try {
      await widget.http.put<dynamic>(
        '/api/v1/people/radar/settings',
        data: {'toneEnabled': enabled},
      );
    } on DioException catch (error) {
      if (mounted) setState(() => _radar = before);
      _toast(
        firstProblemMessage(error.response?.data) ?? 'Could not save that.',
      );
    }
  }

  Future<void> _link(LinkSuggestionData suggestion) async {
    setState(() => _linking.add(suggestion.key));
    try {
      await widget.http.post<dynamic>(
        '/api/v1/people/${suggestion.personId}/links',
        data: {
          'connectionId': suggestion.connectionId,
          'chatId': suggestion.chatId,
        },
      );
      _toast('Linked ${suggestion.personName} to ${suggestion.chatName}.');
      await _loadRadar();
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ??
            'Could not link that chat.',
      );
    } finally {
      if (mounted) setState(() => _linking.remove(suggestion.key));
    }
  }

  void _openById(String personId) {
    final person = _people.where((x) => x.id == personId).firstOrNull;
    if (person != null) unawaited(_open(person));
  }

  Future<void> _open(PersonData person) async {
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => PersonDetailScreen(http: widget.http, person: person),
      ),
    );
    if (mounted) unawaited(_load());
  }

  Future<void> _add() async {
    final draft = await showPersonEditor(context);
    if (draft == null || !mounted) return;
    await _create({
      'name': draft.name,
      'relationship': draft.relationship,
      'birthdayMonth': draft.birthdayMonth,
      'birthdayDay': draft.birthdayDay,
      'birthYear': draft.birthYear,
      'contactEveryDays': draft.contactEveryDays,
      'notes': draft.notes,
    });
  }

  Future<void> _addSuggestion(PersonSuggestionData suggestion) async {
    setState(() => _busy.add(suggestion.graphEntityId));
    await _create({
      'name': suggestion.name,
      'relationship': suggestion.relationship,
      'birthdayMonth': suggestion.birthdayMonth,
      'birthdayDay': suggestion.birthdayDay,
      'birthYear': suggestion.birthYear,
      'graphEntityId': suggestion.graphEntityId,
    }, openAfter: false);
    if (mounted) setState(() => _busy.remove(suggestion.graphEntityId));
  }

  Future<void> _create(
    Map<String, Object?> body, {
    bool openAfter = true,
  }) async {
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/people',
        data: body,
      );
      final person = PersonData.fromJson(response.data);
      if (!mounted) return;
      if (person == null || !openAfter) {
        unawaited(_load());
        return;
      }
      await _open(person);
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ??
            'Could not add that person.',
      );
    }
  }

  Future<void> _logContact(PersonData person) async {
    setState(() => _busy.add(person.id));
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/people/${person.id}/contact',
        data: const <String, Object?>{},
      );
      final updated = PersonData.fromJson(response.data);
      if (!mounted) return;
      setState(() {
        if (updated != null) {
          _people = [for (final x in _people) x.id == updated.id ? updated : x];
        }
      });
      _toast('Nice. Marked ${person.name} as talked to today.');
    } on DioException catch (error) {
      _toast(
        firstProblemMessage(error.response?.data) ?? 'Could not save that.',
      );
    } finally {
      if (mounted) setState(() => _busy.remove(person.id));
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
    final upcoming =
        _people
            .where(
              (x) =>
                  x.daysUntilBirthday != null &&
                  x.daysUntilBirthday! <= _upcomingDays,
            )
            .toList()
          ..sort(
            (a, b) => a.daysUntilBirthday!.compareTo(b.daysUntilBirthday!),
          );
    final due = _people.where((x) => x.contactDue).toList()
      ..sort(
        (a, b) => (b.daysSinceContact ?? 1 << 20).compareTo(
          a.daysSinceContact ?? 1 << 20,
        ),
      );
    var index = 0;
    return Scaffold(
      appBar: AppBar(
        title: const PageTitle('People'),
        actions: [
          HeaderAction(
            label: 'Add',
            icon: PhosphorIconsRegular.userPlus,
            onPressed: () => unawaited(_add()),
          ),
        ],
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _error,
        isEmpty: _people.isEmpty,
        onRetry: () => unawaited(_load()),
        onRefresh: _load,
        empty: _empty(),
        child: ListView(
          padding: EdgeInsets.fromLTRB(
            16,
            8,
            16,
            32 + MediaQuery.paddingOf(context).bottom,
          ),
          children: [
            if (upcoming.isNotEmpty)
              ContentWidth(
                child: FadeSlideIn(
                  index: index++,
                  child: _Section(
                    title: 'Coming up',
                    child: _BirthdayStrip(
                      people: upcoming,
                      onOpen: (person) => unawaited(_open(person)),
                    ),
                  ),
                ),
              ),
            if (due.isNotEmpty)
              ContentWidth(
                child: FadeSlideIn(
                  index: index++,
                  child: _Section(
                    title: 'Time to reach out',
                    child: GroupedSection(
                      dividerIndent: 68,
                      children: [
                        for (final person in due)
                          _PersonRow(
                            person: person,
                            subtitle:
                                '${lastTalkedLabel(person)} · you wanted ${cadenceLabel(person.contactEveryDays!)}',
                            subtitleColor: JarvisColors.of(context).warning,
                            onTap: () => unawaited(_open(person)),
                            trailing: FilledButton.tonalIcon(
                              key: Key('people-talked-${person.id}'),
                              onPressed: _busy.contains(person.id)
                                  ? null
                                  : () => unawaited(_logContact(person)),
                              icon: const Icon(
                                PhosphorIconsRegular.check,
                                size: 16,
                              ),
                              label: const Text('Talked'),
                              style: FilledButton.styleFrom(
                                visualDensity: VisualDensity.compact,
                              ),
                            ),
                          ),
                      ],
                    ),
                  ),
                ),
              ),
            if (_radar case final radar? when radar.people.isNotEmpty)
              ContentWidth(
                child: FadeSlideIn(
                  index: index++,
                  child: RadarSection(
                    overview: radar,
                    onOpen: _openById,
                    onToneChanged: (value) => unawaited(_setTone(value)),
                  ),
                ),
              ),
            if (_linkSuggestions.isNotEmpty)
              ContentWidth(
                child: FadeSlideIn(
                  index: index++,
                  child: LinkSuggestionsSection(
                    suggestions: _linkSuggestions,
                    busy: _linking,
                    onLink: (suggestion) => unawaited(_link(suggestion)),
                  ),
                ),
              ),
            ContentWidth(
              child: FadeSlideIn(
                index: index++,
                child: _Section(
                  title: 'Everyone',
                  trailing: Text(
                    '${_people.length}',
                    style: TextStyle(
                      fontSize: 13,
                      color: JarvisColors.of(context).muted,
                    ),
                  ),
                  child: GroupedSection(
                    dividerIndent: 68,
                    children: [
                      for (final person in _people)
                        _PersonRow(
                          person: person,
                          subtitle: _summary(person),
                          onTap: () => unawaited(_open(person)),
                          trailing: Icon(
                            PhosphorIconsRegular.caretRight,
                            size: 16,
                            color: JarvisColors.of(context).muted,
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            ),
            if (_suggestions.isNotEmpty)
              ContentWidth(
                child: FadeSlideIn(
                  index: index++,
                  child: _Suggestions(
                    suggestions: _suggestions,
                    busy: _busy,
                    onAdd: (suggestion) =>
                        unawaited(_addSuggestion(suggestion)),
                  ),
                ),
              ),
            ContentWidth(
              child: Padding(
                padding: const EdgeInsets.fromLTRB(4, 14, 4, 0),
                child: Text(
                  'Tip: tell Jarvis “remind me to call Mum every two weeks” or '
                  '“I just saw Anna”.',
                  textAlign: TextAlign.center,
                  style: TextStyle(
                    fontSize: 12.5,
                    color: JarvisColors.of(context).muted,
                  ),
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _empty() => EmptyState(
    icon: PhosphorIconsRegular.users,
    title: 'Keep the people who matter close',
    message:
        'Add birthdays and how often you want to catch up. Jarvis reminds '
        'you on the day and nudges you when it has been a while.',
    action: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Center(
          child: FilledButton.icon(
            key: const Key('people-add-first'),
            onPressed: () => unawaited(_add()),
            icon: const Icon(PhosphorIconsRegular.userPlus, size: 18),
            label: const Text('Add someone'),
          ),
        ),
        if (_suggestions.isNotEmpty) ...[
          const SizedBox(height: 28),
          _Suggestions(
            suggestions: _suggestions,
            busy: _busy,
            onAdd: (suggestion) => unawaited(_addSuggestion(suggestion)),
          ),
        ],
      ],
    ),
  );

  static String _summary(PersonData person) {
    final parts = <String>[
      ?person.relationship,
      if (person.hasBirthday)
        birthdayDateLabel(person.birthdayMonth!, person.birthdayDay!),
      if (person.contactEveryDays != null || person.daysSinceContact != null)
        lastTalkedLabel(person),
    ];
    return parts.isEmpty ? 'Tap to add details' : parts.join(' · ');
  }
}

class _Section extends StatelessWidget {
  const _Section({required this.title, required this.child, this.trailing});

  final String title;
  final Widget child;
  final Widget? trailing;

  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 22),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(title, trailing: trailing),
        child,
      ],
    ),
  );
}

class _PersonRow extends StatelessWidget {
  const _PersonRow({
    required this.person,
    required this.subtitle,
    required this.onTap,
    required this.trailing,
    this.subtitleColor,
  });

  final PersonData person;
  final String subtitle;
  final VoidCallback onTap;
  final Widget trailing;
  final Color? subtitleColor;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return InkWell(
      onTap: onTap,
      child: Padding(
        padding: const EdgeInsets.fromLTRB(16, 12, 12, 12),
        child: Row(
          children: [
            PersonAvatar(
              name: person.name,
              birthdayToday: person.birthdayToday,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    person.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontSize: 15.5,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    subtitle,
                    maxLines: 2,
                    overflow: TextOverflow.ellipsis,
                    style: TextStyle(
                      fontSize: 13,
                      color: subtitleColor ?? colors.inkSoft,
                    ),
                  ),
                ],
              ),
            ),
            const SizedBox(width: 8),
            trailing,
          ],
        ),
      ),
    );
  }
}

/// Horizontal cards for birthdays in the next month; today's is highlighted.
class _BirthdayStrip extends StatelessWidget {
  const _BirthdayStrip({required this.people, required this.onOpen});

  final List<PersonData> people;
  final ValueChanged<PersonData> onOpen;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final scale = MediaQuery.textScalerOf(context).scale(1).clamp(1.0, 1.6);
    return SizedBox(
      height: 158 * scale,
      child: ListView.separated(
        scrollDirection: Axis.horizontal,
        clipBehavior: Clip.none,
        itemCount: people.length,
        separatorBuilder: (_, _) => const SizedBox(width: 10),
        itemBuilder: (context, index) {
          final person = people[index];
          final today = person.birthdayToday;
          final age = person.turningAge;
          return SizedBox(
            width: 148 * scale,
            child: SurfaceCard(
              key: Key('people-birthday-${person.id}'),
              onTap: () => onOpen(person),
              padding: const EdgeInsets.all(14),
              gradient: today
                  ? LinearGradient(
                      begin: Alignment.topLeft,
                      end: Alignment.bottomRight,
                      colors: [
                        colors.rose.withValues(
                          alpha: colors.isDark ? .30 : .16,
                        ),
                        colors.warning.withValues(
                          alpha: colors.isDark ? .18 : .10,
                        ),
                      ],
                    )
                  : null,
              borderColor: today ? colors.rose.withValues(alpha: .4) : null,
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    children: [
                      PersonAvatar(name: person.name, size: 34),
                      const Spacer(),
                      Icon(
                        today
                            ? PhosphorIconsRegular.cake
                            : PhosphorIconsRegular.gift,
                        size: 18,
                        color: today ? colors.rose : colors.muted,
                      ),
                    ],
                  ),
                  const Spacer(),
                  Text(
                    person.name,
                    maxLines: 1,
                    overflow: TextOverflow.ellipsis,
                    style: const TextStyle(
                      fontSize: 15,
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  const SizedBox(height: 2),
                  Text(
                    birthdayCountdown(person),
                    style: TextStyle(
                      fontSize: 13,
                      fontWeight: today ? FontWeight.w600 : FontWeight.w500,
                      color: today ? colors.ink : colors.inkSoft,
                    ),
                  ),
                  Text(
                    age == null
                        ? birthdayDateLabel(
                            person.birthdayMonth!,
                            person.birthdayDay!,
                          )
                        : 'Turns $age',
                    style: TextStyle(fontSize: 12.5, color: colors.muted),
                  ),
                ],
              ),
            ),
          );
        },
      ),
    );
  }
}

class _Suggestions extends StatelessWidget {
  const _Suggestions({
    required this.suggestions,
    required this.busy,
    required this.onAdd,
  });

  final List<PersonSuggestionData> suggestions;
  final Set<String> busy;
  final ValueChanged<PersonSuggestionData> onAdd;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return _Section(
      title: 'From your memory',
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 0, 4, 10),
            child: Text(
              'Jarvis has heard about these people. Add them to keep track '
              'of birthdays and catch-ups.',
              style: TextStyle(fontSize: 13, color: colors.inkSoft),
            ),
          ),
          GroupedSection(
            dividerIndent: 68,
            children: [
              for (final suggestion in suggestions.take(8))
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 10, 12, 10),
                  child: Row(
                    children: [
                      PersonAvatar(name: suggestion.name),
                      const SizedBox(width: 12),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              suggestion.name,
                              maxLines: 1,
                              overflow: TextOverflow.ellipsis,
                              style: const TextStyle(
                                fontSize: 15,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            if (_detail(suggestion) case final detail?)
                              Text(
                                detail,
                                style: TextStyle(
                                  fontSize: 13,
                                  color: colors.inkSoft,
                                ),
                              ),
                          ],
                        ),
                      ),
                      OutlinedButton.icon(
                        key: Key('people-suggest-${suggestion.graphEntityId}'),
                        onPressed: busy.contains(suggestion.graphEntityId)
                            ? null
                            : () => onAdd(suggestion),
                        icon: const Icon(PhosphorIconsRegular.plus, size: 16),
                        label: const Text('Add'),
                        style: OutlinedButton.styleFrom(
                          visualDensity: VisualDensity.compact,
                        ),
                      ),
                    ],
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }

  static String? _detail(PersonSuggestionData suggestion) {
    final parts = [
      ?suggestion.relationship,
      if (suggestion.birthdayMonth != null && suggestion.birthdayDay != null)
        'Birthday ${birthdayDateLabel(suggestion.birthdayMonth!, suggestion.birthdayDay!)}',
    ];
    return parts.isEmpty ? null : parts.join(' · ');
  }
}
