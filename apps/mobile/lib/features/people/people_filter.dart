import '../everything/feature_search.dart' show foldForSearch;
import 'people_models.dart';

/// A relationship that several people share, for the filter chips.
typedef RelationshipChoice = ({String label, int count});

String _relationshipKey(PersonData person) =>
    foldForSearch(person.relationship ?? '');

/// The relationships in use, most common first, with how many people have each.
/// "Friend" and "friend " count as one; the first spelling seen is shown.
List<RelationshipChoice> relationshipChoices(List<PersonData> people) {
  final labels = <String, String>{};
  final counts = <String, int>{};
  for (final person in people) {
    final key = _relationshipKey(person);
    if (key.isEmpty) continue;
    labels.putIfAbsent(key, () => person.relationship!.trim());
    counts[key] = (counts[key] ?? 0) + 1;
  }
  final keys = counts.keys.toList()
    ..sort((a, b) {
      final byCount = counts[b]!.compareTo(counts[a]!);
      return byCount != 0 ? byCount : labels[a]!.compareTo(labels[b]!);
    });
  return [for (final key in keys) (label: labels[key]!, count: counts[key]!)];
}

/// People whose name or relationship contains [query] and, when given, whose
/// relationship is [relationship]. The incoming order (the follow-through
/// order) is kept.
List<PersonData> filterPeople(
  List<PersonData> people, {
  String query = '',
  String? relationship,
}) {
  final needle = foldForSearch(query);
  final wanted = relationship == null ? null : foldForSearch(relationship);
  return [
    for (final person in people)
      if ((wanted == null || _relationshipKey(person) == wanted) &&
          (needle.isEmpty ||
              foldForSearch(person.name).contains(needle) ||
              _relationshipKey(person).contains(needle)))
        person,
  ];
}
