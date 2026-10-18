import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Cards a voice answer put on screen: the rows the voice did not read out.
/// Saving keeps one private note; putting a card away removes it for good.
class VoiceCompanionCards extends StatelessWidget {
  const VoiceCompanionCards({
    required this.cards,
    required this.onOpen,
    required this.onSave,
    required this.onDismiss,
    super.key,
  });

  final List<Map<String, dynamic>> cards;
  final ValueChanged<String> onOpen;
  final ValueChanged<String> onSave;
  final ValueChanged<String> onDismiss;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.stretch,
    children: [for (final card in cards) _card(context, card)],
  );

  Widget _card(BuildContext context, Map<String, dynamic> card) {
    final colors = JarvisColors.of(context);
    final id = asJsonString(card['id']) ?? '';
    final saved = asJsonString(card['state']) == 'saved';
    return Padding(
      padding: const EdgeInsets.only(top: 16),
      child: SurfaceCard(
        key: Key('voice-card-$id'),
        elevated: true,
        padding: const EdgeInsets.fromLTRB(16, 14, 16, 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              (asJsonString(card['kind']) == 'comparison'
                      ? 'Comparison'
                      : 'On your screen')
                  .toUpperCase(),
              style: JarvisType.capsOf(context),
            ),
            const SizedBox(height: 4),
            Text(
              asJsonString(card['title']) ?? '',
              style: const TextStyle(
                fontSize: 18,
                fontWeight: FontWeight.w700,
                letterSpacing: -.3,
              ),
            ),
            const SizedBox(height: 8),
            for (final (i, item) in jsonMaps(card['items']).indexed)
              InkWell(
                key: Key('voice-card-$id-item-$i'),
                borderRadius: BorderRadius.circular(10),
                onTap: asJsonString(item['destination']) == null
                    ? null
                    : () => onOpen(asJsonString(item['destination'])!),
                child: Padding(
                  padding: const EdgeInsets.symmetric(vertical: 6),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              asJsonString(item['title']) ?? '',
                              style: const TextStyle(
                                fontWeight: FontWeight.w600,
                              ),
                            ),
                            if (asJsonString(item['detail']) case final detail?)
                              Text(
                                detail,
                                style: TextStyle(
                                  color: colors.inkSoft,
                                  fontSize: 13.5,
                                ),
                              ),
                          ],
                        ),
                      ),
                      if (asJsonString(item['destination']) != null)
                        Icon(
                          Icons.chevron_right,
                          size: 18,
                          color: colors.muted,
                        ),
                    ],
                  ),
                ),
              ),
            const SizedBox(height: 6),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                PillAction(
                  key: Key('voice-card-save-$id'),
                  compact: true,
                  primary: !saved,
                  icon: saved
                      ? PhosphorIconsRegular.checkCircle
                      : PhosphorIconsRegular.bookmarkSimple,
                  label: saved ? 'Saved' : 'Save',
                  onPressed: saved ? null : () => onSave(id),
                ),
                PillAction(
                  key: Key('voice-card-dismiss-$id'),
                  compact: true,
                  label: 'Put away',
                  onPressed: () => onDismiss(id),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

/// What the voice session did, with saved, put away and still-open kept apart.
Future<void> showVoiceRecap(
  BuildContext context,
  Map<String, dynamic> recap,
) => showModalBottomSheet<void>(
  context: context,
  showDragHandle: true,
  isScrollControlled: true,
  builder: (sheet) {
    final colors = JarvisColors.of(sheet);
    final lines = jsonMaps(recap['lines']);
    return SafeArea(
      child: SingleChildScrollView(
        key: const Key('voice-recap'),
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(
              'This session',
              style: JarvisType.displayOf(sheet).copyWith(fontSize: 26),
            ),
            const SizedBox(height: 6),
            Text(
              '${asJsonInt(recap['shown'])} shown · ${asJsonInt(recap['saved'])} saved · '
              '${asJsonInt(recap['dismissed'])} put away · ${asJsonInt(recap['open'])} still open',
              key: const Key('voice-recap-counts'),
              style: TextStyle(color: colors.inkSoft),
            ),
            const SizedBox(height: 10),
            for (final line in lines)
              FeatureRow(
                icon: switch (asJsonString(line['state'])) {
                  'saved' => PhosphorIconsRegular.checkCircle,
                  'dismissed' => PhosphorIconsRegular.xCircle,
                  _ => PhosphorIconsRegular.circle,
                },
                quiet: asJsonString(line['state']) == 'dismissed',
                title: asJsonString(line['title']) ?? '',
                subtitle: asJsonString(line['text']),
              ),
            Text(
              asJsonString(recap['statement']) ?? '',
              key: const Key('voice-recap-statement'),
              style: TextStyle(color: colors.muted, fontSize: 13),
            ),
          ],
        ),
      ),
    );
  },
);
