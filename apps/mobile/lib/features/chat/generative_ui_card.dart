part of 'generative_ui.dart';

/// How a row in the option group behaves when tapped.
enum _OptionMode {
  /// Sends this option right away.
  send,

  /// Marks this one option; a button sends it.
  pickOne,

  /// Toggles this option; a button sends all marked ones.
  pickMany,
}

mixin _UiSurfaceCardBody on _UiSurfaceCardController {
  Widget _choiceSection() {
    if (_multiple) {
      return _optionGroup([
        for (final item in _items)
          (id: asString(item['id']) ?? '', item: item, style: null),
      ], _OptionMode.pickMany);
    }
    final direct = _directActions();
    final loose = _looseItems();
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        if (loose.isNotEmpty)
          _optionGroup([
            for (final item in loose)
              (id: asString(item['id']) ?? '', item: item, style: null),
          ], _OptionMode.pickOne),
        if (direct.isNotEmpty)
          _optionGroup([
            for (final action in direct)
              (
                id: asString(action['id']) ?? '',
                item: {
                  ...?_itemById[asString(action['id']) ?? ''],
                  'title': ?asString(action['label']),
                },
                style: asString(action['style']),
              ),
          ], _OptionMode.send),
      ],
    );
  }

  List<Widget> _confirmButtons() {
    final actions = _confirmActions();
    return [
      for (final action in actions)
        _actionButton(
          id: asString(action['id']) ?? '',
          label: asString(action['label']) ?? asString(action['id']) ?? '',
          style: asString(action['style']) ?? 'secondary',
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

  /// The answers, one row each, in a single rounded group.
  Widget _optionGroup(
    List<({String id, Map<String, dynamic> item, String? style})> options,
    _OptionMode mode,
  ) {
    final colors = JarvisColors.of(context);
    final numbered = MediaQuery.textScalerOf(context).scale(1) < 1.5;
    return Padding(
      padding: const EdgeInsets.only(top: 12),
      child: ClipRRect(
        borderRadius: BorderRadius.circular(18),
        child: ColoredBox(
          color: colors.surfaceMuted,
          child: Column(
            children: [
              for (var index = 0; index < options.length; index++) ...[
                if (index > 0)
                  Divider(
                    height: 1,
                    thickness: 1,
                    indent: numbered ? 54 : 16,
                    color: colors.outline,
                  ),
                _optionRow(options[index], index, mode, numbered: numbered),
              ],
            ],
          ),
        ),
      ),
    );
  }

  Widget _optionRow(
    ({String id, Map<String, dynamic> item, String? style}) option,
    int index,
    _OptionMode mode, {
    required bool numbered,
  }) {
    final colors = JarvisColors.of(context);
    final id = option.id;
    final label = asString(option.item['title']) ?? id;
    final subtitle = asString(option.item['subtitle']);
    final icon = surfaceIcon(asString(option.item['icon']));
    final danger = option.style == 'danger';
    final busy = _busyAction != null;
    final thisBusy = mode == _OptionMode.send && _busyAction == id;
    final selected = _selected.contains(id);
    final onTap = !_interactive || busy
        ? null
        : switch (mode) {
            _OptionMode.send => () {
              HapticFeedback.selectionClick();
              unawaited(_run(id));
            },
            _OptionMode.pickOne => () {
              HapticFeedback.selectionClick();
              setState(() {
                _selected
                  ..clear()
                  ..add(id);
                _hint = null;
              });
            },
            _OptionMode.pickMany => () {
              HapticFeedback.selectionClick();
              setState(() {
                if (!_selected.remove(id)) _selected.add(id);
                _hint = null;
              });
            },
          };
    final Widget trailing = switch (mode) {
      _OptionMode.send => AnimatedSwitcher(
        duration: JarvisMotion.of(context, JarvisMotion.fast),
        child: thisBusy
            ? const SizedBox.square(
                key: ValueKey('busy'),
                dimension: 16,
                child: CircularProgressIndicator(strokeWidth: 2),
              )
            : Icon(
                PhosphorIconsRegular.arrowUpRight,
                key: const ValueKey('go'),
                size: 16,
                color: busy ? colors.outlineStrong : colors.muted,
              ),
      ),
      _OptionMode.pickOne || _OptionMode.pickMany => AnimatedSwitcher(
        duration: JarvisMotion.of(context, JarvisMotion.fast),
        transitionBuilder: (child, animation) =>
            ScaleTransition(scale: animation, child: child),
        child: Icon(
          selected
              ? PhosphorIconsFill.checkCircle
              : PhosphorIconsRegular.circle,
          key: ValueKey(selected),
          size: 22,
          color: selected ? colors.accent : colors.outlineStrong,
        ),
      ),
    };
    return Semantics(
      button: true,
      enabled: onTap != null,
      selected: mode == _OptionMode.send ? null : selected,
      label: subtitle == null ? label : '$label, $subtitle',
      onTap: onTap,
      excludeSemantics: true,
      child: AnimatedContainer(
        duration: JarvisMotion.of(context, JarvisMotion.fast),
        color: selected ? colors.accentSoft : colors.surfaceMuted,
        child: InkWell(
          onTap: onTap,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 56),
            child: Padding(
              padding: const EdgeInsets.fromLTRB(12, 10, 14, 10),
              child: Row(
                children: [
                  if (numbered) ...[
                    _leadingBadge(icon: icon, number: index + 1),
                    const SizedBox(width: 14),
                  ] else
                    const SizedBox(width: 4),
                  Expanded(
                    child: Column(
                      crossAxisAlignment: CrossAxisAlignment.start,
                      children: [
                        Text(
                          label,
                          style: TextStyle(
                            color: danger
                                ? colors.danger
                                : selected
                                ? colors.accentDeep
                                : colors.ink,
                            fontSize: 15.5,
                            fontWeight: FontWeight.w600,
                            letterSpacing: -.25,
                          ),
                        ),
                        if (subtitle != null)
                          Text(
                            subtitle,
                            style: TextStyle(
                              color: colors.inkSoft,
                              fontSize: 13,
                              height: 1.35,
                            ),
                          ),
                      ],
                    ),
                  ),
                  const SizedBox(width: 8),
                  trailing,
                ],
              ),
            ),
          ),
        ),
      ),
    );
  }

  /// The item's icon in a small tile, or its position when it has none.
  Widget _leadingBadge({IconData? icon, required int number}) {
    final colors = JarvisColors.of(context);
    return Container(
      width: 28,
      height: 28,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: colors.surface,
        borderRadius: BorderRadius.circular(9),
      ),
      child: icon != null
          ? Icon(icon, size: 16, color: colors.accent)
          : Text(
              '$number',
              style: TextStyle(
                color: colors.inkSoft,
                fontWeight: FontWeight.w600,
                fontSize: 13,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
    );
  }

  /// Items of a list or status card: icon or number, text, and a trailing
  /// value or badge.
  Widget _itemList() {
    final colors = JarvisColors.of(context);
    final large = MediaQuery.textScalerOf(context).scale(1) >= 1.5;
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Column(
        children: [
          for (var index = 0; index < _items.length; index++) ...[
            if (index > 0)
              Divider(
                height: 1,
                thickness: 1,
                indent: large ? 0 : 40,
                color: colors.outline,
              ),
            _itemRow(_items[index], index, large: large),
          ],
        ],
      ),
    );
  }

  Widget _itemRow(Map<String, dynamic> item, int index, {required bool large}) {
    final colors = JarvisColors.of(context);
    final title = asString(item['title']) ?? '';
    final subtitle = asString(item['subtitle']);
    final detail = asString(item['detail']);
    final value = asString(item['value']);
    final badge = asString(item['badge']);
    final text = Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Text(
          title,
          style: TextStyle(
            color: colors.ink,
            fontSize: 15,
            fontWeight: FontWeight.w600,
            letterSpacing: -.2,
          ),
        ),
        if (subtitle != null)
          Text(
            subtitle,
            style: TextStyle(color: colors.inkSoft, fontSize: 13, height: 1.35),
          ),
        if (detail != null)
          Padding(
            padding: const EdgeInsets.only(top: 2),
            child: Text(
              detail,
              style: TextStyle(color: colors.muted, fontSize: 12.5),
            ),
          ),
      ],
    );
    final trailing = [
      if (value != null)
        Text(
          value,
          style: TextStyle(
            color: colors.ink,
            fontSize: 15,
            fontWeight: FontWeight.w600,
            fontFeatures: const [FontFeature.tabularFigures()],
          ),
        ),
      if (badge != null) SurfaceTag(label: badge),
    ];
    return Padding(
      padding: const EdgeInsets.symmetric(vertical: 10),
      child: large
          ? Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                text,
                if (trailing.isNotEmpty)
                  Padding(
                    padding: const EdgeInsets.only(top: 6),
                    child: Wrap(
                      spacing: 8,
                      runSpacing: 4,
                      crossAxisAlignment: WrapCrossAlignment.center,
                      children: trailing,
                    ),
                  ),
              ],
            )
          : Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Padding(
                  padding: const EdgeInsets.only(top: 1),
                  child: _leadingBadge(
                    icon: surfaceIcon(asString(item['icon'])),
                    number: index + 1,
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(child: text),
                if (trailing.isNotEmpty) ...[
                  const SizedBox(width: 10),
                  Column(
                    crossAxisAlignment: CrossAxisAlignment.end,
                    children: [
                      for (var i = 0; i < trailing.length; i++) ...[
                        if (i > 0) const SizedBox(height: 4),
                        trailing[i],
                      ],
                    ],
                  ),
                ],
              ],
            ),
    );
  }

  @override
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
    required bool solo,
  }) {
    final busy = _busyAction != null;
    final thisBusy = _busyAction == id;
    final danger = style == 'danger';
    final filled = danger || style == 'primary' || solo;
    final onPressed = _interactive && !busy ? () => _run(id) : null;
    final child = Row(
      mainAxisAlignment: MainAxisAlignment.center,
      children: [
        Flexible(
          child: Text(
            label,
            textAlign: TextAlign.center,
            style: const TextStyle(
              fontFamily: 'Geist',
              fontWeight: FontWeight.w600,
            ),
          ),
        ),
        if (thisBusy) ...[
          const SizedBox(width: 10),
          const SizedBox.square(
            dimension: 16,
            child: CircularProgressIndicator(strokeWidth: 2),
          ),
        ],
      ],
    );
    final shape = RoundedRectangleBorder(
      borderRadius: BorderRadius.circular(999),
    );
    final button = filled
        ? FilledButton(
            onPressed: onPressed,
            style: FilledButton.styleFrom(
              backgroundColor: danger
                  ? JarvisColors.of(context).danger
                  : JarvisColors.of(context).ink,
              foregroundColor: danger
                  ? Colors.white
                  : JarvisColors.of(context).onInk,
              disabledBackgroundColor: JarvisColors.of(context).surfaceRaised,
              minimumSize: const Size.fromHeight(48),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              shape: shape,
            ),
            child: child,
          )
        : OutlinedButton(
            onPressed: onPressed,
            style: OutlinedButton.styleFrom(
              foregroundColor: JarvisColors.of(context).ink,
              minimumSize: const Size.fromHeight(48),
              padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 12),
              shape: shape,
            ),
            child: child,
          );
    return Padding(padding: const EdgeInsets.only(top: 10), child: button);
  }

  Widget _note(String text, Color color) => Padding(
    padding: const EdgeInsets.only(top: 8),
    child: Text(
      text,
      style: TextStyle(color: color, fontWeight: FontWeight.w600, fontSize: 13),
    ),
  );

  /// A used or closed card folds to one line with what was answered; a tap
  /// opens what it asked.
  Widget _receipt() {
    final colors = JarvisColors.of(context);
    final title = asString(_schema['title']) ?? widget.surface.title;
    final body = asString(_schema['body']);
    final answered = widget.surface.status == 'completed';
    final summary = answered ? surfaceAnswerSummary(widget.surface) : null;
    final label = switch (widget.surface.status) {
      'completed' => 'Answered',
      'open' => 'Open',
      'dismissed' => 'Dismissed',
      _ => 'Closed',
    };
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: Material(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(16),
        child: InkWell(
          borderRadius: BorderRadius.circular(16),
          onTap: () => setState(() => _expanded = !_expanded),
          child: Padding(
            padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 10),
            child: MotionSize(
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
                        color: answered ? colors.success : colors.muted,
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
                        style: TextStyle(
                          color: colors.muted,
                          fontSize: 12,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ],
                  ),
                  if (summary != null)
                    Padding(
                      padding: const EdgeInsets.only(left: 24, top: 2),
                      child: Text(
                        summary,
                        maxLines: _expanded ? null : 2,
                        overflow: _expanded ? null : TextOverflow.ellipsis,
                        style: TextStyle(
                          color: colors.accentDeep,
                          fontSize: 13.5,
                          fontWeight: FontWeight.w600,
                        ),
                      ),
                    ),
                  if (_expanded && body != null) ...[
                    const SizedBox(height: 8),
                    Text(
                      body,
                      style: TextStyle(color: colors.inkSoft, fontSize: 13),
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
      ),
    );
  }

  @override
  Future<void> _run(String actionId) async {
    if (widget.onAction == null || _busyAction != null) return;
    Map<String, dynamic>? action;
    for (final item in _actions) {
      if (asString(item['id']) == actionId) {
        action = item;
        break;
      }
    }
    final skipFields = asString(action?['url']) != null;
    final hint = skipFields ? null : _validationHint(actionId);
    if (hint != null) {
      setState(() => _hint = hint);
      return;
    }
    setState(() {
      _hint = null;
      _busyAction = actionId;
    });
    try {
      final url = parsePublicHttpsUrl(asString(action?['url']));
      if (url != null) await launchHttpUrl(url);
      await widget.onAction!(actionId, _payload(actionId));
    } finally {
      if (mounted) setState(() => _busyAction = null);
    }
  }

  String? _validationHint(String actionId) {
    final directChoice = _kind == 'choice' && _itemById.containsKey(actionId);
    if (_kind == 'choice' && !directChoice && _selected.isEmpty) {
      if (_multiple) return 'Pick at least one option.';
      if (_looseItems().isNotEmpty) return 'Pick one option first.';
    }
    final answerable = [
      for (final field in _fields)
        if (const {
          'text',
          'longtext',
          'number',
          'secret',
          'date',
          'time',
        }.contains(asString(field['type']) ?? 'text'))
          field,
    ];
    if (answerable.isNotEmpty &&
        answerable.every(
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
      final value = switch (type) {
        'toggle' => _values[id] == 'true' ? 'true' : 'false',
        'slider' => _sliderValue(field),
        'choice' || 'date' || 'time' => _values[id] ?? '',
        _ => _text(id),
      }.trim();
      if (value.isNotEmpty) {
        payload[id] = _clip(value, type == 'secret' ? 8192 : 500);
      }
    }
    final picked = _itemById.containsKey(actionId)
        ? [actionId]
        : [
            for (final item in _items)
              if (_selected.contains(asString(item['id'])))
                asString(item['id'])!,
          ];
    if (picked.isNotEmpty) {
      payload['choice'] = _clip(picked.join(','));
      final labels = [
        for (final id in picked) ?asString(_itemById[id]?['title']),
      ];
      if (labels.isNotEmpty) payload['label'] = _clip(labels.join(', '));
    }
    return payload;
  }

  String _clip(String value, [int max = 500]) =>
      value.length <= max ? value : value.substring(0, max);
}
