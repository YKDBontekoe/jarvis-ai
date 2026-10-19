import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../../http_urls.dart';
import 'chat_entries.dart';

part 'generative_ui_blocks.dart';
part 'generative_ui_browser.dart';
part 'generative_ui_card.dart';
part 'generative_ui_fields.dart';

/// The newest card the user can still act on. Older open cards are ignored.
UiSurfaceEntry? liveSurface(Iterable<ChatEntry> entries) {
  UiSurfaceEntry? live;
  for (final entry in entries) {
    if (entry is UiSurfaceEntry && entry.status == 'open') live = entry;
  }
  return live;
}

/// True when the card is waiting for a tap or a typed answer.
bool surfaceAwaitsReply(UiSurfaceEntry surface) =>
    surface.status == 'open' && surfaceAsksSomething(surface.schema);

/// True when the card has something to answer, as opposed to a card that
/// only shows information (a custom summary, a list).
bool surfaceAsksSomething(Map<String, dynamic> schema) {
  if (jsonMaps(schema['fields']).isNotEmpty) return true;
  if (jsonMaps(schema['actions']).isNotEmpty) return true;
  final kind = asString(schema['kind']) ?? '';
  return kind == 'choice' && jsonMaps(schema['items']).isNotEmpty;
}

/// [values] as they may be kept on screen: secret fields never are.
Map<String, String> redactSurfaceValues(
  Map<String, dynamic> schema,
  Map<String, String> values,
) {
  final secrets = {
    for (final field in jsonMaps(schema['fields']))
      if (asString(field['type']) == 'secret') ?asString(field['id']),
  };
  return {
    for (final pair in values.entries)
      pair.key: secrets.contains(pair.key) ? storedSecretMarker : pair.value,
  };
}

/// Stands in for a secret on screen and in the transcript, as the server does.
const storedSecretMarker = '(stored securely)';

/// A one-line summary of what was answered on a used card, such as
/// "Hard push" or "Energy 4 · Day Tue 20 Oct", or null when unknown.
String? surfaceAnswerSummary(UiSurfaceEntry surface) {
  final answer = surface.answer;
  if (answer == null) return null;
  final label = answer.values['label']?.trim();
  if (label != null && label.isNotEmpty) return label;
  final parts = <String>[];
  for (final field in jsonMaps(surface.schema['fields'])) {
    final id = asString(field['id']);
    final value = answer.values[id]?.trim();
    if (id == null || value == null || value.isEmpty) continue;
    final type = asString(field['type']) ?? 'text';
    if (type == 'secret' || value == storedSecretMarker) {
      parts.add('Saved securely');
      continue;
    }
    final shown = switch (type) {
      'toggle' => value == 'true' ? 'Yes' : 'No',
      'slider' || 'number' => [value, ?asString(field['unit'])].join(' '),
      _ => value,
    };
    parts.add(shown);
  }
  if (parts.isNotEmpty) return parts.take(3).join(' · ');
  for (final action in jsonMaps(surface.schema['actions'])) {
    if (asString(action['id']) == answer.actionId) {
      return asString(action['label']);
    }
  }
  for (final item in jsonMaps(surface.schema['items'])) {
    if (asString(item['id']) == answer.actionId) return asString(item['title']);
  }
  return null;
}

/// Native renderer for A2UI-style surfaces Jarvis emits with RenderUi.
class UiSurfaceCard extends StatefulWidget {
  const UiSurfaceCard({
    required this.surface,
    this.onAction,
    this.pinned = false,
    this.errorText,
    this.onTypeInstead,
    super.key,
  });

  final UiSurfaceEntry surface;
  final Future<void> Function(String actionId, Map<String, String> values)?
  onAction;
  final bool pinned;
  final String? errorText;

  /// Shown on the docked card: puts the options away and opens the composer.
  final VoidCallback? onTypeInstead;

  @override
  State<UiSurfaceCard> createState() => _UiSurfaceCardState();
}

/// Holds surface fields so the card body mixin can share state.
abstract class _UiSurfaceCardController extends State<UiSurfaceCard> {
  final _values = <String, String>{};
  final _controllers = <String, TextEditingController>{};
  final _selected = <String>{};
  String? _busyAction;
  String? _hint;
  bool _expanded = false;

  Map<String, dynamic> get _schema => widget.surface.schema;
  String get _kind => asString(_schema['kind']) ?? 'card';
  List<Map<String, dynamic>> get _items => jsonMaps(_schema['items']);
  List<Map<String, dynamic>> get _fields => jsonMaps(_schema['fields']);
  List<Map<String, dynamic>> get _actions => jsonMaps(_schema['actions']);

  Map<String, Map<String, dynamic>> get _itemById => {
    for (final item in _items) ?asString(item['id']): item,
  };

  bool get _interactive =>
      widget.surface.status == 'open' && widget.onAction != null;

  // Shared between the body and field mixins.
  Future<void> _run(String actionId);
  String? _submitActionId();
  String _text(String id);
  String _sliderValue(Map<String, dynamic> field);

  /// Several items may be picked before one button sends them.
  bool get _multiple => asString(_schema['selection']) == 'multiple';
  List<Map<String, dynamic>> get _blocks => jsonMaps(_schema['blocks']);

  @override
  void didUpdateWidget(UiSurfaceCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.surface.id == widget.surface.id) {
      if (_busyAction != null &&
          (widget.surface.status != 'open' || widget.onAction == null)) {
        _busyAction = null;
      }
      return;
    }
    _values.clear();
    _selected.clear();
    _hint = null;
    _busyAction = null;
    _expanded = false;
    for (final controller in _controllers.values) {
      controller.dispose();
    }
    _controllers.clear();
  }

  @override
  void dispose() {
    for (final controller in _controllers.values) {
      controller.dispose();
    }
    super.dispose();
  }

  Widget _header() {
    final colors = JarvisColors.of(context);
    final (icon, eyebrow) = switch (_kind) {
      'choice' when _multiple => (PhosphorIconsRegular.listChecks, 'Pick any'),
      'choice' => (PhosphorIconsRegular.handTap, 'Choose one'),
      'form' => (
        PhosphorIconsRegular.notePencil,
        _fields.any((field) => asString(field['type']) == 'secret')
            ? 'A secret'
            : _fields.length > 1
            ? 'A few questions'
            : 'A question',
      ),
      'list' => (PhosphorIconsRegular.listChecks, 'At a glance'),
      'status' => (PhosphorIconsRegular.lightning, 'Status'),
      _ => (PhosphorIconsRegular.sparkle, 'For you'),
    };
    final title = asString(_schema['title']) ?? widget.surface.title;
    final body = asString(_schema['body']);
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          children: [
            Icon(icon, size: 15, color: colors.accent),
            const SizedBox(width: 6),
            Expanded(
              child: Text(
                eyebrow.toUpperCase(),
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: JarvisType.capsOf(context),
              ),
            ),
            if (widget.onTypeInstead != null)
              _TypeInsteadButton(onPressed: widget.onTypeInstead!),
          ],
        ),
        const SizedBox(height: 8),
        Text(
          title,
          style: TextStyle(
            fontFamily: 'Geist',
            color: colors.ink,
            fontSize: 20,
            height: 1.2,
            fontWeight: FontWeight.w600,
            letterSpacing: -.5,
          ),
        ),
        if (body != null) ...[
          const SizedBox(height: 4),
          Text(
            body,
            style: TextStyle(
              color: colors.inkSoft,
              fontSize: 14.5,
              height: 1.4,
            ),
          ),
        ],
      ],
    );
  }
}

/// "Type instead": hides a docked card's options and opens the composer.
class _TypeInsteadButton extends StatelessWidget {
  const _TypeInsteadButton({required this.onPressed});

  final VoidCallback onPressed;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      button: true,
      label: 'Type an answer instead',
      onTap: onPressed,
      excludeSemantics: true,
      child: Tooltip(
        message: 'Type an answer instead',
        child: InkResponse(
          onTap: onPressed,
          radius: 22,
          child: SizedBox.square(
            dimension: 44,
            child: Center(
              child: Container(
                width: 32,
                height: 32,
                decoration: BoxDecoration(
                  color: colors.surfaceMuted,
                  shape: BoxShape.circle,
                ),
                child: Icon(
                  PhosphorIconsRegular.pencilSimple,
                  size: 17,
                  color: colors.inkSoft,
                ),
              ),
            ),
          ),
        ),
      ),
    );
  }
}

class _UiSurfaceCardState extends _UiSurfaceCardController
    with _UiSurfaceCardBody, _UiSurfaceCardFields {
  @override
  Widget build(BuildContext context) {
    final asks = surfaceAsksSomething(_schema);
    // A used question folds to its answer; a card that only shows something
    // stays readable for good.
    if (asks && !_interactive) return _receipt();
    final choosing = _kind == 'choice' && _items.isNotEmpty;
    final content = Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        _header(),
        if (_blocks.isNotEmpty)
          Padding(
            padding: const EdgeInsets.only(top: 14),
            child: SurfaceBlocks(blocks: _blocks),
          ),
        if (choosing) _choiceSection(),
        if (!choosing && _items.isNotEmpty) _itemList(),
        for (var index = 0; index < _fields.length; index++)
          _field(_fields[index], last: index == _fields.length - 1),
        if (asks) ..._confirmButtons(),
        if (_hint != null) _note(_hint!, JarvisColors.of(context).warning),
        if (widget.errorText != null)
          _note(widget.errorText!, JarvisColors.of(context).danger),
      ],
    );
    // Docked above the keyboard, the card takes the composer's place and
    // reads as part of it rather than as one more bubble.
    if (widget.pinned) {
      return SurfaceCard(
        elevated: true,
        radius: JarvisRadii.xl,
        padding: const EdgeInsets.fromLTRB(16, 6, 16, 16),
        child: content,
      );
    }
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(elevated: true, child: content),
    );
  }
}

/// Sits above the composer while the owner types instead of tapping an option,
/// so the question stays in view and the options are one tap away.
class DockedSurfaceBar extends StatelessWidget {
  const DockedSurfaceBar({
    required this.surface,
    required this.onShowOptions,
    super.key,
  });

  final UiSurfaceEntry surface;
  final VoidCallback onShowOptions;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final title = asString(surface.schema['title']) ?? surface.title;
    return Container(
      padding: const EdgeInsets.fromLTRB(14, 6, 6, 6),
      decoration: BoxDecoration(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(999),
      ),
      child: Row(
        children: [
          Icon(PhosphorIconsRegular.handTap, size: 16, color: colors.accent),
          const SizedBox(width: 8),
          Expanded(
            child: Text(
              title,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: TextStyle(
                color: colors.inkSoft,
                fontSize: 13.5,
                fontWeight: FontWeight.w600,
              ),
            ),
          ),
          const SizedBox(width: 8),
          PillAction(
            label: 'Options',
            icon: PhosphorIconsRegular.caretUp,
            compact: true,
            onPressed: onShowOptions,
          ),
        ],
      ),
    );
  }
}

String? asString(dynamic value) =>
    value is String && value.isNotEmpty ? value : null;
