import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'person_widgets.dart';
import 'radar_models.dart';

Color radarSeverityColor(JarvisColors colors, int severity) =>
    severity >= 2 ? colors.warning : colors.muted;

/// The relationship radar on the People list: who may be drifting, from message times alone, and the switch
/// for the optional tone check.
class RadarSection extends StatelessWidget {
  const RadarSection({
    required this.overview,
    required this.onOpen,
    required this.onToneChanged,
    super.key,
  });

  final RadarOverviewData overview;
  final void Function(String personId) onOpen;
  final ValueChanged<bool> onToneChanged;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final drifting = overview.people.where((x) => x.severity > 0).toList();
    return Padding(
      key: const Key('radar-section'),
      padding: const EdgeInsets.only(bottom: 22),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          SectionHeader(drifting.isEmpty ? 'Relationship radar' : 'Drifting'),
          if (drifting.isEmpty)
            SurfaceCard(
              key: const Key('radar-steady'),
              child: Text(
                'Nothing stands out. Everyone you linked is keeping in touch '
                'as usual.',
                style: TextStyle(fontSize: 14, color: colors.inkSoft),
              ),
            )
          else
            GroupedSection(
              dividerIndent: 68,
              children: [
                for (final report in drifting)
                  InkWell(
                    key: Key('radar-person-${report.personId}'),
                    onTap: () => onOpen(report.personId),
                    child: Padding(
                      padding: const EdgeInsets.fromLTRB(16, 12, 12, 12),
                      child: Row(
                        children: [
                          PersonAvatar(name: report.name),
                          const SizedBox(width: 12),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(
                                  report.name,
                                  maxLines: 1,
                                  overflow: TextOverflow.ellipsis,
                                  style: const TextStyle(
                                    fontSize: 15.5,
                                    fontWeight: FontWeight.w600,
                                  ),
                                ),
                                const SizedBox(height: 2),
                                Text(
                                  report.topSignal?.headline ?? '',
                                  maxLines: 2,
                                  overflow: TextOverflow.ellipsis,
                                  style: TextStyle(
                                    fontSize: 13,
                                    color: radarSeverityColor(
                                      colors,
                                      report.severity,
                                    ),
                                  ),
                                ),
                              ],
                            ),
                          ),
                          Icon(
                            PhosphorIconsRegular.caretRight,
                            size: 16,
                            color: colors.muted,
                          ),
                        ],
                      ),
                    ),
                  ),
              ],
            ),
          const SizedBox(height: 6),
          SwitchListTile(
            key: const Key('radar-tone-switch'),
            contentPadding: const EdgeInsets.symmetric(horizontal: 4),
            value: overview.toneEnabled,
            onChanged: onToneChanged,
            title: const Text('Check tone'),
            subtitle: const Text(
              'Jarvis reads a few recent messages of each linked chat with '
              'the model to guess how they feel. Off by default.',
            ),
          ),
        ],
      ),
    );
  }
}

/// People and chats that look like the same human. Nothing is linked until the owner confirms.
class LinkSuggestionsSection extends StatelessWidget {
  const LinkSuggestionsSection({
    required this.suggestions,
    required this.busy,
    required this.onLink,
    super.key,
  });

  final List<LinkSuggestionData> suggestions;
  final Set<String> busy;
  final void Function(LinkSuggestionData suggestion) onLink;

  @override
  Widget build(BuildContext context) {
    if (suggestions.isEmpty) return const SizedBox.shrink();
    final colors = JarvisColors.of(context);
    return Padding(
      key: const Key('link-suggestions'),
      padding: const EdgeInsets.only(bottom: 22),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.stretch,
        children: [
          const SectionHeader('Link a WhatsApp chat?'),
          GroupedSection(
            dividerIndent: 16,
            children: [
              for (final suggestion in suggestions)
                Padding(
                  padding: const EdgeInsets.fromLTRB(16, 10, 12, 10),
                  child: Row(
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              suggestion.personName,
                              style: const TextStyle(
                                fontSize: 15,
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            Text(
                              'Chat: ${suggestion.chatName}',
                              style: TextStyle(
                                fontSize: 13,
                                color: colors.inkSoft,
                              ),
                            ),
                          ],
                        ),
                      ),
                      FilledButton.tonal(
                        key: Key('link-suggestion-${suggestion.key}'),
                        onPressed: busy.contains(suggestion.key)
                            ? null
                            : () => onLink(suggestion),
                        style: FilledButton.styleFrom(
                          visualDensity: VisualDensity.compact,
                        ),
                        child: const Text('Link'),
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
}

/// A person's radar: linked chats and, when there are messages to look at, how you two keep in touch.
class RadarCard extends StatelessWidget {
  const RadarCard({
    required this.radar,
    required this.onLink,
    required this.onUnlink,
    super.key,
  });

  /// Null while it loads or when the radar is not available.
  final PersonRadarData? radar;
  final VoidCallback onLink;
  final void Function(PersonLinkData link) onUnlink;

  @override
  Widget build(BuildContext context) {
    final data = radar;
    if (data == null) return const SizedBox.shrink();
    final colors = JarvisColors.of(context);
    final text = Theme.of(context).textTheme;
    final report = data.report;
    final silent = data.links.where((x) => !x.readAlong).toList();
    return Column(
      key: const Key('person-radar'),
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        SectionHeader(
          'Staying in touch',
          trailing: data.links.isEmpty
              ? null
              : TextButton(
                  key: const Key('radar-link-more'),
                  onPressed: onLink,
                  child: const Text('Link chat'),
                ),
        ),
        if (data.links.isEmpty)
          SurfaceCard(
            key: const Key('radar-link-empty'),
            onTap: onLink,
            child: Row(
              children: [
                const IconBadge(icon: PhosphorIconsRegular.chatCircle),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'Link their WhatsApp chat and Jarvis can tell when you '
                    'drift apart, from message times alone.',
                    style: TextStyle(fontSize: 14, color: colors.inkSoft),
                  ),
                ),
              ],
            ),
          )
        else
          SurfaceCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                for (final link in data.links)
                  Row(
                    key: Key('radar-link-${link.id}'),
                    children: [
                      Icon(
                        PhosphorIconsRegular.chatCircle,
                        size: 16,
                        color: colors.muted,
                      ),
                      const SizedBox(width: 8),
                      Expanded(
                        child: Text(
                          link.displayName,
                          maxLines: 1,
                          overflow: TextOverflow.ellipsis,
                        ),
                      ),
                      IconButton(
                        key: Key('radar-unlink-${link.id}'),
                        tooltip: 'Unlink ${link.displayName}',
                        visualDensity: VisualDensity.compact,
                        onPressed: () => onUnlink(link),
                        icon: const Icon(PhosphorIconsRegular.x, size: 16),
                      ),
                    ],
                  ),
                if (silent.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 4),
                    child: Text(
                      'Jarvis only sees messages from chats it reads along '
                      'with. Turn read-along on for '
                      '${silent.map((x) => x.displayName).join(', ')} in '
                      'WhatsApp.',
                      key: const Key('radar-read-along-hint'),
                      style: TextStyle(fontSize: 12.5, color: colors.warning),
                    ),
                  ),
                if (report != null && report.hasMessages) ...[
                  const Divider(height: 24),
                  RadarSparkline(values: report.weeklyMessages),
                  const SizedBox(height: 10),
                  Text(
                    '${perWeekLabel(report.recentPerWeek)} lately · '
                    '${perWeekLabel(report.baselinePerWeek)} before',
                    key: const Key('radar-rate'),
                    style: text.bodyMedium,
                  ),
                  if (report.myReplyMinutes != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 4),
                      child: Text(
                        report.baselineReplyMinutes == null
                            ? 'You reply in about '
                                  '${replyTimeLabel(report.myReplyMinutes!)}'
                            : 'You reply in about '
                                  '${replyTimeLabel(report.myReplyMinutes!)} '
                                  '(before: '
                                  '${replyTimeLabel(report.baselineReplyMinutes!)})',
                        key: const Key('radar-reply'),
                        style: TextStyle(fontSize: 13, color: colors.inkSoft),
                      ),
                    ),
                  if (report.toneScore != null)
                    Padding(
                      padding: const EdgeInsets.only(top: 4),
                      child: Text(
                        'Tone: ${toneLabel(report.toneScore!)} (a guess)',
                        key: const Key('radar-tone'),
                        style: TextStyle(fontSize: 13, color: colors.inkSoft),
                      ),
                    ),
                ],
                if (report != null && report.signals.isNotEmpty) ...[
                  const SizedBox(height: 10),
                  for (final signal in report.signals)
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Row(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Padding(
                            padding: const EdgeInsets.only(top: 6),
                            child: Container(
                              width: 8,
                              height: 8,
                              decoration: BoxDecoration(
                                color: radarSeverityColor(
                                  colors,
                                  signal.severity,
                                ),
                                shape: BoxShape.circle,
                              ),
                            ),
                          ),
                          const SizedBox(width: 10),
                          Expanded(
                            child: Column(
                              crossAxisAlignment: CrossAxisAlignment.start,
                              children: [
                                Text(signal.headline, style: text.titleSmall),
                                if (signal.detail.isNotEmpty)
                                  Text(
                                    signal.detail,
                                    style: TextStyle(
                                      fontSize: 13,
                                      color: colors.inkSoft,
                                    ),
                                  ),
                              ],
                            ),
                          ),
                        ],
                      ),
                    ),
                ],
              ],
            ),
          ),
      ],
    );
  }
}

/// Messages per week for the last weeks, oldest first, as small bars.
class RadarSparkline extends StatelessWidget {
  const RadarSparkline({required this.values, super.key});

  final List<int> values;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final highest = values.fold<int>(0, (a, b) => a > b ? a : b);
    return Semantics(
      label: 'Messages per week, oldest first: ${values.join(', ')}',
      excludeSemantics: true,
      child: SizedBox(
        key: const Key('radar-sparkline'),
        height: 36,
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.end,
          children: [
            for (final value in values)
              Expanded(
                child: Padding(
                  padding: const EdgeInsets.symmetric(horizontal: 2),
                  child: FractionallySizedBox(
                    heightFactor: highest == 0
                        ? 0.08
                        : (value / highest).clamp(0.08, 1.0),
                    child: Container(
                      decoration: BoxDecoration(
                        color: value == 0 ? colors.surfaceMuted : colors.accent,
                        borderRadius: BorderRadius.circular(3),
                      ),
                    ),
                  ),
                ),
              ),
          ],
        ),
      ),
    );
  }
}

/// Lets the owner pick which of their one-to-one chats to link. Chats Jarvis reads along with come first.
Future<LinkCandidateData?> showChatPicker(BuildContext context, Dio http) =>
    showModalBottomSheet<LinkCandidateData>(
      context: context,
      isScrollControlled: true,
      showDragHandle: true,
      builder: (_) => _ChatPickerSheet(http: http),
    );

class _ChatPickerSheet extends StatefulWidget {
  const _ChatPickerSheet({required this.http});

  final Dio http;

  @override
  State<_ChatPickerSheet> createState() => _ChatPickerSheetState();
}

class _ChatPickerSheetState extends State<_ChatPickerSheet> {
  List<LinkCandidateData> _chats = const [];
  bool _loading = true;
  String? _error;
  String _filter = '';

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/people/link-candidates',
      );
      if (!mounted) return;
      setState(() {
        _chats = LinkCandidateData.listFromJson(response.data);
        _loading = false;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load your chats.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final shown = [
      for (final chat in _chats)
        if (_filter.isEmpty ||
            chat.displayName.toLowerCase().contains(_filter.toLowerCase()))
          chat,
    ];
    return SafeArea(
      child: Padding(
        padding: EdgeInsets.fromLTRB(
          20,
          0,
          20,
          16 + MediaQuery.viewInsetsOf(context).bottom,
        ),
        child: ConstrainedBox(
          constraints: BoxConstraints(
            maxHeight: MediaQuery.sizeOf(context).height * 0.7,
          ),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.stretch,
            children: [
              Text(
                'Link a WhatsApp chat',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              const SizedBox(height: 10),
              TextField(
                key: const Key('chat-picker-filter'),
                onChanged: (value) => setState(() => _filter = value),
                decoration: const InputDecoration(
                  labelText: 'Search chats',
                  prefixIcon: Icon(PhosphorIconsRegular.magnifyingGlass),
                ),
              ),
              const SizedBox(height: 8),
              if (_loading)
                const Padding(
                  padding: EdgeInsets.all(24),
                  child: Center(child: CircularProgressIndicator()),
                )
              else if (_error != null)
                Padding(
                  padding: const EdgeInsets.all(16),
                  child: Text(_error!, style: TextStyle(color: colors.danger)),
                )
              else if (shown.isEmpty)
                Padding(
                  padding: const EdgeInsets.all(16),
                  child: Text(
                    _chats.isEmpty
                        ? 'No chats to link yet. Link your WhatsApp and open a '
                              'chat in read-along first.'
                        : 'No chat matches that.',
                    key: const Key('chat-picker-empty'),
                    style: TextStyle(color: colors.inkSoft),
                  ),
                )
              else
                Flexible(
                  child: ListView(
                    shrinkWrap: true,
                    children: [
                      for (final chat in shown)
                        ListTile(
                          key: Key(
                            'chat-pick-${chat.connectionId}-${chat.chatId}',
                          ),
                          contentPadding: EdgeInsets.zero,
                          leading: const Icon(PhosphorIconsRegular.chatCircle),
                          title: Text(chat.displayName),
                          subtitle: Text(
                            chat.readAlong
                                ? 'Read-along on'
                                : 'Read-along off: no messages to look at',
                            style: TextStyle(
                              color: chat.readAlong
                                  ? colors.inkSoft
                                  : colors.warning,
                            ),
                          ),
                          onTap: () => Navigator.of(context).pop(chat),
                        ),
                    ],
                  ),
                ),
            ],
          ),
        ),
      ),
    );
  }
}
