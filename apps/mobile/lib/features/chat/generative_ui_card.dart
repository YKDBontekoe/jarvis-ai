part of 'generative_ui.dart';

mixin _UiSurfaceCardBody on _UiSurfaceCardController {
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
      final raw = field['options'];
      final options = [
        if (raw is List)
          for (final option in raw)
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
