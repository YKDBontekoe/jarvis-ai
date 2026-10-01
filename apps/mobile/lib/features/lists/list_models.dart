import 'package:flutter/widgets.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/phosphor_icons.dart';

/// One line on a personal list.
class ListItemData {
  const ListItemData({
    required this.id,
    required this.text,
    required this.done,
  });

  final String id;
  final String text;
  final bool done;

  ListItemData copyWith({String? text, bool? done}) =>
      ListItemData(id: id, text: text ?? this.text, done: done ?? this.done);

  static ListItemData? fromJson(Map<String, dynamic> json) {
    final id = jsonString(json, 'id');
    final text = jsonString(json, 'text');
    if (id == null || text == null) return null;
    return ListItemData(id: id, text: text, done: asJsonBool(json['done']));
  }
}

/// A personal list such as groceries or to-dos, as returned by /api/v1/lists.
class PersonalListData {
  const PersonalListData({
    required this.id,
    required this.name,
    required this.kind,
    required this.items,
  });

  final String id;
  final String name;

  /// "shopping", "todo", or "general".
  final String kind;
  final List<ListItemData> items;

  List<ListItemData> get open => [
    for (final item in items)
      if (!item.done) item,
  ];

  List<ListItemData> get done => [
    for (final item in items)
      if (item.done) item,
  ];

  PersonalListData copyWith({
    String? name,
    String? kind,
    List<ListItemData>? items,
  }) => PersonalListData(
    id: id,
    name: name ?? this.name,
    kind: kind ?? this.kind,
    items: items ?? this.items,
  );

  static PersonalListData? fromJson(dynamic data) {
    final json = jsonObject(data);
    if (json == null) return null;
    final id = jsonString(json, 'id');
    final name = jsonString(json, 'name');
    if (id == null || name == null) return null;
    return PersonalListData(
      id: id,
      name: name,
      kind: jsonString(json, 'kind') ?? 'general',
      items: jsonMaps(
        json['items'],
      ).map(ListItemData.fromJson).whereType<ListItemData>().toList(),
    );
  }

  static List<PersonalListData> listFromJson(dynamic data) => jsonMaps(
    data,
  ).map(PersonalListData.fromJson).whereType<PersonalListData>().toList();
}

const listKinds = ['shopping', 'todo', 'general'];

String listKindLabel(String kind) => switch (kind) {
  'shopping' => 'Shopping',
  'todo' => 'To-do',
  _ => 'Other',
};

IconData listKindIcon(String kind) => switch (kind) {
  'shopping' => PhosphorIconsRegular.shoppingCart,
  'todo' => PhosphorIconsRegular.checkSquare,
  _ => PhosphorIconsRegular.listBullets,
};

Color listKindColor(JarvisColors colors, String kind) => switch (kind) {
  'shopping' => colors.success,
  'todo' => colors.accent,
  _ => colors.violet,
};

/// "3 to go", "All done", or "Empty".
String listProgressLabel(PersonalListData list) {
  if (list.items.isEmpty) return 'Empty';
  final open = list.open.length;
  return open == 0 ? 'All done' : '$open to go';
}
