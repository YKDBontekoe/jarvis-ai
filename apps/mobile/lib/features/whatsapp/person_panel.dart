import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../home/home_context.dart';

/// Beside a WhatsApp chat on wide screens: who this is to you, how you keep in
/// touch, and what Jarvis remembers about them.
class PersonPanel extends StatefulWidget {
  const PersonPanel({required this.person, required this.http, super.key});

  final HomePerson person;
  final Dio http;

  /// The chat pane is wide enough for the panel.
  static bool fits(double width) => width >= 860;

  @override
  State<PersonPanel> createState() => _PersonPanelState();
}

class _PersonPanelState extends State<PersonPanel> {
  Map<String, dynamic>? _detail;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void didUpdateWidget(PersonPanel oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.person.personId != widget.person.personId) {
      _detail = null;
      unawaited(_load());
    }
  }

  Future<void> _load() async {
    final id = widget.person.personId;
    try {
      final response = await widget.http.get<dynamic>('/api/v1/people/$id');
      if (mounted && id == widget.person.personId) {
        setState(() => _detail = jsonObject(response.data));
      }
    } catch (_) {
      // The panel still shows what Home knows.
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final person = widget.person;
    final record = jsonObject(_detail?['person']);
    final facts = [
      for (final fact in jsonMaps(_detail?['facts']))
        if (asJsonString(fact['value']) case final value?)
          (asJsonString(fact['predicate']) ?? '', value),
    ];
    final days = record?['daysUntilBirthday'];
    final turning = record?['turningAge'];
    final notes = asJsonString(record?['notes']);
    final rows = <(IconData, String)>[
      if (days is num)
        (
          PhosphorIconsRegular.gift,
          days == 0
              ? 'Birthday today${turning is num ? ' · turns $turning' : ''}'
              : 'Birthday in ${days.round()} ${days == 1 ? 'day' : 'days'}'
                    '${turning is num ? ' · turns $turning' : ''}',
        ),
      if (person.usualReply case final reply?)
        (PhosphorIconsRegular.clock, reply),
      if (person.drift case final drift?)
        (PhosphorIconsRegular.heart, drift)
      else if (person.baselinePerWeek case final rate? when rate > 0)
        (
          PhosphorIconsRegular.chatCircle,
          'About ${rate.round()} messages a week',
        ),
    ];
    return Container(
      key: const Key('whatsapp-person-panel'),
      width: 320,
      decoration: BoxDecoration(
        color: colors.surface,
        border: Border(left: BorderSide(color: colors.outline)),
      ),
      child: ListView(
        padding: const EdgeInsets.fromLTRB(22, 26, 22, 26),
        children: [
          Text('ABOUT', style: JarvisType.capsOf(context)),
          const SizedBox(height: 10),
          Text(
            person.name,
            style: JarvisType.displayOf(
              context,
            ).copyWith(fontSize: 26, fontWeight: FontWeight.w700),
          ),
          if (person.relationship case final relationship?) ...[
            const SizedBox(height: 4),
            Text(
              relationship,
              style: TextStyle(
                fontSize: 14,
                fontWeight: FontWeight.w600,
                color: colors.rose,
              ),
            ),
          ],
          const SizedBox(height: 18),
          for (final (icon, text) in rows)
            Padding(
              padding: const EdgeInsets.symmetric(vertical: 7),
              child: Row(
                children: [
                  Icon(icon, size: 17, color: colors.inkSoft),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      text,
                      style: TextStyle(fontSize: 14, color: colors.ink),
                    ),
                  ),
                ],
              ),
            ),
          if (notes != null && notes.trim().isNotEmpty) ...[
            const SizedBox(height: 14),
            Text(
              notes,
              style: TextStyle(
                fontSize: 14,
                height: 1.45,
                color: colors.inkSoft,
              ),
            ),
          ],
          if (facts.isNotEmpty) ...[
            const SizedBox(height: 22),
            Text('JARVIS REMEMBERS', style: JarvisType.capsOf(context)),
            const SizedBox(height: 8),
            for (final (predicate, value) in facts.take(6))
              FadeSlideIn(
                offset: 6,
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 5),
                  child: Text.rich(
                    TextSpan(
                      children: [
                        if (predicate.isNotEmpty)
                          TextSpan(
                            text: '${predicate.replaceAll('_', ' ')}  ',
                            style: TextStyle(color: colors.muted),
                          ),
                        TextSpan(text: value),
                      ],
                    ),
                    style: TextStyle(
                      fontSize: 14,
                      height: 1.4,
                      color: colors.ink,
                    ),
                  ),
                ),
              ),
          ],
        ],
      ),
    );
  }
}
