import '../../json_maps.dart';

/// Labels for conversation groups, in display order.
const conversationGroupLabels = [
  'Pinned',
  'Today',
  'Yesterday',
  'Previous 7 days',
  'Older',
];

/// Groups conversations into pinned first, then by the day they were last
/// active. Rows without an id are skipped; input order is kept within a group.
List<(String, List<Map<String, dynamic>>)> groupConversations(
  Iterable<Map<String, dynamic>> conversations, {
  DateTime? now,
}) {
  final clock = now ?? DateTime.now();
  final today = DateTime(clock.year, clock.month, clock.day);
  final buckets = <String, List<Map<String, dynamic>>>{};
  for (final conversation in conversations) {
    if (jsonString(conversation, 'id') == null) continue;
    final String label;
    if (conversation['pinned'] == true) {
      label = 'Pinned';
    } else {
      final updated = jsonDate(conversation['updatedAt'], local: true);
      final day = updated == null
          ? null
          : DateTime(updated.year, updated.month, updated.day);
      final age = day == null ? 999 : today.difference(day).inDays;
      label = switch (age) {
        <= 0 => 'Today',
        1 => 'Yesterday',
        < 7 => 'Previous 7 days',
        _ => 'Older',
      };
    }
    buckets.putIfAbsent(label, () => []).add(conversation);
  }
  return [
    for (final label in conversationGroupLabels)
      if (buckets[label] case final items?) (label, items),
  ];
}
