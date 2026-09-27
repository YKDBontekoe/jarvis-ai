import 'dart:async';

import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'chat_entries.dart';

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
  final fields = asMaps(surface.schema['fields']);
  final actions = asMaps(surface.schema['actions']);
  if (fields.isNotEmpty || actions.isNotEmpty) return true;
  final kind = asString(surface.schema['kind']) ?? '';
  return kind == 'choice' && asMaps(surface.schema['items']).isNotEmpty;
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

class _UiSurfaceCardState extends State<UiSurfaceCard> {
  final _values = <String, String>{};
  final _controllers = <String, TextEditingController>{};
  String? _selectedItem;
  String? _busyAction;
  String? _hint;
  bool _expanded = false;

  Map<String, dynamic> get _schema => widget.surface.schema;
  String get _kind => asString(_schema['kind']) ?? 'card';
  List<Map<String, dynamic>> get _items => asMaps(_schema['items']);
  List<Map<String, dynamic>> get _fields => asMaps(_schema['fields']);
  List<Map<String, dynamic>> get _actions => asMaps(_schema['actions']);

  Map<String, Map<String, dynamic>> get _itemById => {
    for (final item in _items) ?asString(item['id']): item,
  };

  bool get _interactive =>
      widget.surface.status == 'open' && widget.onAction != null;

  @override
  void didUpdateWidget(UiSurfaceCard oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.surface.id == widget.surface.id) return;
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

  List<Widget> _choiceSection() {
    final direct = _directActions();
    final emphasizeFirst = direct.every(
      (action) => (asString(action['style']) ?? 'secondary') != 'primary',
    );
    return [
      for (final item in _looseItems()) _selectTile(item),
      for (var index = 0; index < direct.length; index++)
        _actionButton(
          id: asString(direct[index]['id']) ?? '',
          label:
              asString(direct[index]['label']) ??
              asString(
                _itemById[asString(direct[index]['id']) ?? '']?['title'],
              ) ??
              '',
          subtitle: asString(
            _itemById[asString(direct[index]['id']) ?? '']?['subtitle'],
          ),
          style: emphasizeFirst && index == 0
              ? 'primary'
              : (asString(direct[index]['style']) ?? 'secondary'),
          option: true,
          solo: false,
        ),
    ];
  }

  List<Widget> _confirmButtons() {
    final actions = _confirmActions();
    return [
      for (final action in actions)
        _actionButton(
          id: asString(action['id']) ?? '',
          label: asString(action['label']) ?? asString(action['id']) ?? '',
          style: asString(action['style']) ?? 'secondary',
          option: false,
          solo: actions.length == 1,
        ),
    ];
  }

  List<Map<String, dynamic>> _directActions() {
    if (_actions.isEmpty) {
      return [
        for (final item in _items)
          {
            'id': asString(item['id']) ?? '',
            'label': asString(item['title']) ?? '',
            'style': 'secondary',
          },
      ];
    }
    return [
      for (final action in _actions)
        if (_itemById.containsKey(asString(action['id']) ?? '')) action,
    ];
  }

  List<Map<String, dynamic>> _looseItems() {
    if (_actions.isEmpty) return const [];
    final taken = {
      for (final action in _actions)
        if (_itemById.containsKey(asString(action['id']) ?? ''))
          asString(action['id'])!,
    };
    return [
      for (final item in _items)
        if (!taken.contains(asString(item['id']) ?? '')) item,
    ];
  }

  List<Map<String, dynamic>> _confirmActions() {
    if (_kind == 'choice' && _items.isNotEmpty) {
      return [
        for (final action in _actions)
          if (!_itemById.containsKey(asString(action['id']) ?? '')) action,
      ];
    }
    return _actions;
  }

  Widget _selectTile(Map<String, dynamic> item) {
    final id = asString(item['id']) ?? '';
    final title = asString(item['title']) ?? id;
    final subtitle = asString(item['subtitle']);
    final selected = _selectedItem == id;
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Material(
        color: selected ? JarvisColors.accentSoft : JarvisColors.surface,
        borderRadius: BorderRadius.circular(14),
        child: InkWell(
          borderRadius: BorderRadius.circular(14),
          onTap: !_interactive || _busyAction != null
              ? null
              : () => setState(() {
                  _selectedItem = id;
                  _hint = null;
                }),
          child: Container(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
            decoration: BoxDecoration(
              borderRadius: BorderRadius.circular(14),
              border: Border.all(
                color: selected
                    ? JarvisColors.accent
                    : JarvisColors.outlineStrong,
                width: selected ? 1.6 : 1,
              ),
            ),
            child: Row(
              children: [
                Icon(
                  selected
                      ? PhosphorIconsFill.checkCircle
                      : PhosphorIconsRegular.circle,
                  size: 20,
                  color: selected ? JarvisColors.accent : JarvisColors.muted,
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        title,
                        style: const TextStyle(
                          fontWeight: FontWeight.w600,
                          fontSize: 15,
                        ),
                      ),
                      if (subtitle != null)
                        Text(
                          subtitle,
                          style: const TextStyle(
                            color: JarvisColors.inkSoft,
                            fontSize: 13,
                          ),
                        ),
                    ],
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Widget _listRow(Map<String, dynamic> item, int index) {
    final title = asString(item['title']) ?? '';
    final subtitle = asString(item['subtitle']);
    final detail = asString(item['detail']);
    return Padding(
      padding: const EdgeInsets.only(top: 10),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Container(
            width: 26,
            height: 26,
            alignment: Alignment.center,
            decoration: BoxDecoration(
              color: JarvisColors.accentSoft,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Text(
              '${index + 1}',
              style: const TextStyle(
                color: JarvisColors.accentDeep,
                fontWeight: FontWeight.w700,
                fontSize: 12,
              ),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  title,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                if (subtitle != null)
                  Text(
                    subtitle,
                    style: const TextStyle(
                      color: JarvisColors.inkSoft,
                      fontSize: 13,
                    ),
                  ),
                if (detail != null)
                  Text(
                    detail,
                    style: const TextStyle(
                      color: JarvisColors.muted,
                      fontSize: 12.5,
                    ),
                  ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  Widget _field(Map<String, dynamic> field, {required bool last}) {
    final id = asString(field['id']) ?? '';
    final label = asString(field['label']) ?? id;
    final type = asString(field['type']) ?? 'text';
    if (type == 'toggle') {
      return SwitchListTile(
        contentPadding: EdgeInsets.zero,
        title: Text(label),
        value: _values[id] == 'true',
        onChanged: _interactive && _busyAction == null
            ? (value) => setState(() => _values[id] = value.toString())
            : null,
      );
    }
    if (type == 'choice') {
      final options = [
        for (final option
            in field['options'] is List ? field['options'] as List : const [])
          if (option is String && option.isNotEmpty) option,
      ];
      final selected = options.contains(_values[id]) ? _values[id] : null;
      return Padding(
        padding: const EdgeInsets.only(top: 12),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
            const SizedBox(height: 6),
            DropdownButtonFormField<String>(
              initialValue: selected,
              decoration: const InputDecoration(hintText: 'Choose'),
              items: [
                for (final option in options)
                  DropdownMenuItem(value: option, child: Text(option)),
              ],
              onChanged: _interactive && _busyAction == null
                  ? (value) {
                      if (value != null) {
                        setState(() {
                          _values[id] = value;
                          _hint = null;
                        });
                      }
                    }
                  : null,
            ),
          ],
        ),
      );
    }
    final controller = _controllers.putIfAbsent(
      id,
      () => TextEditingController(text: _values[id] ?? ''),
    );
    return Padding(
      padding: const EdgeInsets.only(top: 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
          const SizedBox(height: 6),
          Builder(
            builder: (fieldContext) => TextField(
              controller: controller,
              enabled: _interactive && _busyAction == null,
              textInputAction: last
                  ? TextInputAction.done
                  : TextInputAction.next,
              keyboardType: type == 'number'
                  ? const TextInputType.numberWithOptions(decimal: true)
                  : TextInputType.text,
              decoration: InputDecoration(
                hintText: asString(field['placeholder']) ?? 'Type your answer',
              ),
              onTap: () {
                WidgetsBinding.instance.addPostFrameCallback((_) {
                  if (!fieldContext.mounted) return;
                  Scrollable.ensureVisible(
                    fieldContext,
                    alignment: 0.2,
                    duration: const Duration(milliseconds: 180),
                  );
                });
              },
              onChanged: (_) {
                if (_hint != null) setState(() => _hint = null);
              },
              onSubmitted: last
                  ? (_) {
                      final actionId = _submitActionId();
                      if (actionId != null) unawaited(_run(actionId));
                    }
                  : null,
            ),
          ),
        ],
      ),
    );
  }

  String? _submitActionId() {
    final confirms = _confirmActions();
    if (confirms.isNotEmpty) return asString(confirms.first['id']);
    final direct = _directActions();
    if (direct.isNotEmpty) return asString(direct.first['id']);
    return null;
  }

  Widget _actionButton({
    required String id,
    required String label,
    required String style,
    String? subtitle,
    required bool option,
    required bool solo,
  }) {
    final busy = _busyAction != null;
    final thisBusy = _busyAction == id;
    final danger = style == 'danger';
    final filled = danger || style == 'primary' || solo;
    final onPressed = _interactive && !busy ? () => _run(id) : null;
    final child = Row(
      children: [
        Expanded(
          child: Column(
            crossAxisAlignment: option
                ? CrossAxisAlignment.start
                : CrossAxisAlignment.center,
            children: [
              Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
              if (subtitle != null)
                Text(
                  subtitle,
                  style: TextStyle(
                    fontSize: 12.5,
                    fontWeight: FontWeight.w400,
                    color: filled
                        ? Colors.white.withValues(alpha: .75)
                        : JarvisColors.inkSoft,
                  ),
                ),
            ],
          ),
        ),
        if (thisBusy)
          const SizedBox.square(
            dimension: 16,
            child: CircularProgressIndicator(strokeWidth: 2),
          )
        else if (option)
          Icon(
            PhosphorIconsRegular.caretRight,
            size: 16,
            color: filled ? Colors.white : JarvisColors.muted,
          ),
      ],
    );
    final shape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(14),
    );
    final button = filled
        ? FilledButton(
            onPressed: onPressed,
            style: FilledButton.styleFrom(
              backgroundColor: danger ? JarvisColors.danger : JarvisColors.ink,
              foregroundColor: Colors.white,
              disabledBackgroundColor: JarvisColors.surfaceRaised,
              minimumSize: const Size.fromHeight(52),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              shape: shape,
              alignment: option ? Alignment.centerLeft : Alignment.center,
            ),
            child: child,
          )
        : OutlinedButton(
            onPressed: onPressed,
            style: OutlinedButton.styleFrom(
              foregroundColor: JarvisColors.ink,
              minimumSize: const Size.fromHeight(52),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              shape: shape,
              alignment: option ? Alignment.centerLeft : Alignment.center,
            ),
            child: child,
          );
    return Padding(padding: const EdgeInsets.only(top: 8), child: button);
  }

  Widget _note(String text, Color color) => Padding(
    padding: const EdgeInsets.only(top: 8),
    child: Text(
      text,
      style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 13),
    ),
  );

  Widget _receipt() {
    final title = asString(_schema['title']) ?? widget.surface.title;
    final body = asString(_schema['body']);
    final answered = widget.surface.status == 'completed';
    final label = switch (widget.surface.status) {
      'completed' => 'Answered',
      'open' => 'Open',
      _ => 'Closed',
    };
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Material(
        color: JarvisColors.surfaceMuted,
        borderRadius: BorderRadius.circular(14),
        child: InkWell(
          borderRadius: BorderRadius.circular(14),
          onTap: () => setState(() => _expanded = !_expanded),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  children: [
                    Icon(
                      answered
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.x,
                      size: 16,
                      color: answered
                          ? JarvisColors.success
                          : JarvisColors.muted,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        title,
                        maxLines: 1,
                        overflow: TextOverflow.ellipsis,
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                    ),
                    const SizedBox(width: 8),
                    Text(
                      label,
                      style: const TextStyle(
                        color: JarvisColors.muted,
                        fontSize: 12,
                        fontWeight: FontWeight.w600,
                      ),
                    ),
                  ],
                ),
                if (_expanded && body != null) ...[
                  const SizedBox(height: 8),
                  Text(
                    body,
                    style: const TextStyle(
                      color: JarvisColors.inkSoft,
                      fontSize: 13,
                    ),
                  ),
                ],
                if (_expanded)
                  for (final item in _items)
                    if (asString(item['title']) case final itemTitle?)
                      Padding(
                        padding: const EdgeInsets.only(top: 4),
                        child: Text(
                          itemTitle,
                          style: const TextStyle(fontSize: 13),
                        ),
                      ),
              ],
            ),
          ),
        ),
      ),
    );
  }

  Future<void> _run(String actionId) async {
    if (widget.onAction == null || _busyAction != null) return;
    final hint = _validationHint(actionId);
    if (hint != null) {
      setState(() => _hint = hint);
      return;
    }
    setState(() {
      _hint = null;
      _busyAction = actionId;
    });
    try {
      await widget.onAction!(actionId, _payload(actionId));
    } finally {
      if (mounted) setState(() => _busyAction = null);
    }
  }

  String? _validationHint(String actionId) {
    final directChoice = _kind == 'choice' && _itemById.containsKey(actionId);
    if (_kind == 'choice' &&
        _looseItems().isNotEmpty &&
        !directChoice &&
        _selectedItem == null) {
      return 'Pick one option first.';
    }
    final textFields = [
      for (final field in _fields)
        if ((asString(field['type']) ?? 'text') == 'text' ||
            (asString(field['type']) ?? 'text') == 'number')
          field,
    ];
    if (textFields.isNotEmpty &&
        textFields.every(
          (field) => _text(asString(field['id']) ?? '').trim().isEmpty,
        )) {
      return 'Add an answer first.';
    }
    final menus = [
      for (final field in _fields)
        if ((asString(field['type']) ?? '') == 'choice') field,
    ];
    if (menus.any(
      (field) => (_values[asString(field['id']) ?? ''] ?? '').isEmpty,
    )) {
      return 'Choose an option in each menu.';
    }
    return null;
  }

  Map<String, String> _payload(String actionId) {
    final payload = <String, String>{};
    for (final field in _fields) {
      final id = asString(field['id']) ?? '';
      if (id.isEmpty) continue;
      final type = asString(field['type']) ?? 'text';
      if (type == 'toggle') {
        payload[id] = _values[id] == 'true' ? 'true' : 'false';
      } else {
        final value = (type == 'choice' ? (_values[id] ?? '') : _text(id))
            .trim();
        if (value.isNotEmpty) payload[id] = _clip(value);
      }
    }
    final selected = _itemById.containsKey(actionId) ? actionId : _selectedItem;
    if (selected != null && _itemById.containsKey(selected)) {
      payload['choice'] = _clip(selected);
      final label = asString(_itemById[selected]?['title']);
      if (label != null) payload['label'] = _clip(label);
    }
    return payload;
  }

  String _text(String id) => _controllers[id]?.text ?? _values[id] ?? '';

  String _clip(String value) =>
      value.length <= 500 ? value : value.substring(0, 500);
}

String? asString(dynamic value) =>
    value is String && value.isNotEmpty ? value : null;

List<Map<String, dynamic>> asMaps(dynamic value) {
  if (value is! List) return const [];
  return [
    for (final item in value)
      if (item is Map) Map<String, dynamic>.from(item),
  ];
}

class BrowserTimelineView extends StatelessWidget {
  const BrowserTimelineView({required this.session, super.key});

  final BrowserSessionEntry session;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Row(
              children: [
                const IconBadge(icon: PhosphorIconsRegular.browser, size: 34),
                const SizedBox(width: 10),
                Expanded(
                  child: Text(
                    session.goal,
                    style: Theme.of(context).textTheme.titleSmall,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 8),
            for (final step in session.steps)
              Padding(
                padding: const EdgeInsets.only(bottom: 4),
                child: Row(
                  children: [
                    Icon(
                      step.success
                          ? PhosphorIconsRegular.checkCircle
                          : PhosphorIconsRegular.warningCircle,
                      size: 14,
                      color: step.success
                          ? JarvisColors.success
                          : JarvisColors.danger,
                    ),
                    const SizedBox(width: 8),
                    Expanded(
                      child: Text(
                        step.summary,
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ),
                  ],
                ),
              ),
          ],
        ),
      ),
    );
  }
}
