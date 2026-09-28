import 'dart:async';

import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'chat_entries.dart';

part 'generative_ui_card.dart';
part 'generative_ui_browser.dart';

/// The newest card the user can still act on. Older open cards are ignored.
UiSurfaceEntry? liveSurface(Iterable<ChatEntry> entries) {
  UiSurfaceEntry? live;
  for (final entry in entries) {
    if (entry is UiSurfaceEntry && entry.status == 'open') live = entry;
  }
  return live;
}

/// True when the card is waiting for a tap or a typed answer.
bool surfaceAwaitsReply(UiSurfaceEntry surface) {
  if (surface.status != 'open') return false;
  final fields = jsonMaps(surface.schema['fields']);
  final actions = jsonMaps(surface.schema['actions']);
  if (fields.isNotEmpty || actions.isNotEmpty) return true;
  final kind = asString(surface.schema['kind']) ?? '';
  return kind == 'choice' && jsonMaps(surface.schema['items']).isNotEmpty;
}

/// Native renderer for A2UI-style surfaces Jarvis emits with RenderUi.
class UiSurfaceCard extends StatefulWidget {
  const UiSurfaceCard({
    required this.surface,
    this.onAction,
    this.pinned = false,
    this.errorText,
    super.key,
  });

  final UiSurfaceEntry surface;
  final Future<void> Function(String actionId, Map<String, String> values)?
  onAction;
  final bool pinned;
  final String? errorText;

  @override
  State<UiSurfaceCard> createState() => _UiSurfaceCardState();
}

/// Holds surface fields so the card body mixin can share state.
abstract class _UiSurfaceCardController extends State<UiSurfaceCard> {
  final _values = <String, String>{};
  final _controllers = <String, TextEditingController>{};
  String? _selectedItem;
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
    _selectedItem = null;
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
    final (icon, eyebrow) = switch (_kind) {
      'choice' => (PhosphorIconsRegular.handTap, 'Choose one'),
      'form' => (
        PhosphorIconsRegular.notePencil,
        _fields.length > 1 ? 'A few questions' : 'A question',
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
            Container(
              width: 34,
              height: 34,
              decoration: BoxDecoration(
                color: JarvisColors.accentSoft,
                borderRadius: BorderRadius.circular(11),
              ),
              child: Icon(icon, size: 18, color: JarvisColors.accentDeep),
            ),
            const SizedBox(width: 10),
            Text(
              eyebrow.toUpperCase(),
              style: const TextStyle(
                color: JarvisColors.accentDeep,
                fontSize: 11,
                fontWeight: FontWeight.w700,
                letterSpacing: .8,
              ),
            ),
          ],
        ),
        const SizedBox(height: 12),
        Text(
          title,
          style: JarvisType.serif.copyWith(fontSize: 28, height: 1.12),
        ),
        if (body != null) ...[
          const SizedBox(height: 6),
          Text(
            body,
            style: const TextStyle(color: JarvisColors.inkSoft, height: 1.4),
          ),
        ],
      ],
    );
  }
}

class _UiSurfaceCardState extends _UiSurfaceCardController
    with _UiSurfaceCardBody {
  @override
  Widget build(BuildContext context) {
    if (!_interactive) return _receipt();
    final choosing = _kind == 'choice' && _items.isNotEmpty;
    return Padding(
      padding: EdgeInsets.only(bottom: widget.pinned ? 6 : 16),
      child: SurfaceCard(
        elevated: true,
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            _header(),
            if (choosing) ..._choiceSection(),
            if (!choosing)
              for (var index = 0; index < _items.length; index++)
                _listRow(_items[index], index),
            for (var index = 0; index < _fields.length; index++)
              _field(_fields[index], last: index == _fields.length - 1),
            ..._confirmButtons(),
            if (_hint != null) _note(_hint!, JarvisColors.warning),
            if (widget.errorText != null)
              _note(widget.errorText!, JarvisColors.danger),
          ],
        ),
      ),
    );
  }
}

String? asString(dynamic value) =>
    value is String && value.isNotEmpty ? value : null;
