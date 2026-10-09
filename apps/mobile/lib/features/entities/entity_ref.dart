import 'package:flutter/widgets.dart';

import '../../ui/phosphor_icons.dart';

/// Destination prefix that opens one thing's page: `entity:task:<id>`.
const entityDestinationPrefix = 'entity:';

/// A pointer to one of the owner's things, written `type:id` (for example
/// `task:0190…`). The same vocabulary the server uses for events, links and
/// tool results, so chat cards, the activity feed and related lists all open
/// things the same way.
@immutable
class EntityRef {
  const EntityRef(this.type, this.id);

  final String type;
  final String id;

  static final _guid = RegExp(
    r'^[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}$',
  );

  /// Null unless [value] is `type:id` with a known type and a GUID id.
  static EntityRef? tryParse(String? value) {
    if (value == null) return null;
    final separator = value.indexOf(':');
    if (separator <= 0) return null;
    final type = value.substring(0, separator).trim().toLowerCase();
    final id = value.substring(separator + 1).trim();
    if (!entityTypes.containsKey(type) || !_guid.hasMatch(id)) return null;
    return EntityRef(type, id.toLowerCase());
  }

  /// [destination] is `entity:type:id`.
  static EntityRef? fromDestination(String destination) =>
      destination.startsWith(entityDestinationPrefix)
      ? tryParse(destination.substring(entityDestinationPrefix.length))
      : null;

  String get destination => '$entityDestinationPrefix$this';

  EntityType get kind => entityTypes[type]!;

  @override
  String toString() => '$type:$id';

  @override
  bool operator ==(Object other) =>
      other is EntityRef && other.type == type && other.id == id;

  @override
  int get hashCode => Object.hash(type, id);
}

/// How a kind of thing looks and where its own feature lives.
@immutable
class EntityType {
  const EntityType(this.label, this.icon, this.feature, this.featureLabel);

  final String label;
  final IconData icon;

  /// The destination of the feature page that lists this kind of thing.
  final String? feature;
  final String featureLabel;
}

const entityTypes = <String, EntityType>{
  'task': EntityType('Task', PhosphorIconsRegular.listChecks, 'tasks', 'Tasks'),
  'reminder': EntityType(
    'Reminder',
    PhosphorIconsRegular.alarm,
    'reminders',
    'Reminders',
  ),
  'watch': EntityType('Watch', PhosphorIconsRegular.eye, 'watches', 'Watches'),
  'memory': EntityType(
    'Memory',
    PhosphorIconsRegular.brain,
    'memory',
    'Memory',
  ),
  'journal': EntityType(
    'Journal entry',
    PhosphorIconsRegular.notebook,
    'journal',
    'Journal',
  ),
  'expense': EntityType(
    'Expense',
    PhosphorIconsRegular.receipt,
    'expenses',
    'Expenses',
  ),
  'project': EntityType(
    'Project',
    PhosphorIconsRegular.folderSimple,
    null,
    'Project',
  ),
  'conversation': EntityType(
    'Chat',
    PhosphorIconsRegular.chatCircle,
    null,
    'Chat',
  ),
  'approval': EntityType(
    'Approval',
    PhosphorIconsRegular.shieldCheck,
    'approvals',
    'Approvals',
  ),
  'notification': EntityType(
    'Notification',
    PhosphorIconsRegular.bell,
    'notifications',
    'Notifications',
  ),
  'person': EntityType('Person', PhosphorIconsRegular.user, 'people', 'People'),
  'decision': EntityType(
    'Decision',
    PhosphorIconsRegular.gavel,
    'decisions',
    'Decisions',
  ),
  'mission': EntityType(
    'Mission',
    PhosphorIconsRegular.rocketLaunch,
    'missions',
    'Missions',
  ),
  'file': EntityType('File', PhosphorIconsRegular.fileText, 'files', 'Files'),
  'commitment': EntityType(
    'Commitment',
    PhosphorIconsRegular.hand,
    'inbox',
    'Inbox',
  ),
  'inbox_thread': EntityType(
    'Conversation to answer',
    PhosphorIconsRegular.chatText,
    'inbox',
    'Inbox',
  ),
  'automation': EntityType(
    'Automation',
    PhosphorIconsRegular.flowArrow,
    'automations',
    'Automations',
  ),
  'habit': EntityType('Habit', PhosphorIconsRegular.repeat, 'habits', 'Habits'),
  'library': EntityType(
    'Library item',
    PhosphorIconsRegular.bookOpen,
    'library',
    'Library',
  ),
};

/// Where a thing opens in its own feature: a project's page, else the
/// feature's list. Chats open through the conversation callback instead.
String? featureDestinationFor(EntityRef ref) => switch (ref.type) {
  'project' => 'project:${ref.id}',
  _ => ref.kind.feature,
};

/// Every `type:id` ref in [values] that parses, in order and without repeats.
List<EntityRef> parseEntityRefs(Iterable<Object?> values) {
  final refs = <EntityRef>[];
  for (final value in values) {
    final ref = EntityRef.tryParse(value is String ? value : null);
    if (ref != null && !refs.contains(ref)) refs.add(ref);
  }
  return refs;
}
