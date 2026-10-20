part of 'generative_ui.dart';

/// Form fields on a card: text, numbers, sliders, dates, times, toggles,
/// choices and secrets.
mixin _UiSurfaceCardFields on _UiSurfaceCardController {
  bool get _editable => _interactive && _busyAction == null;

  Widget _field(Map<String, dynamic> field, {required bool last}) {
    final id = asString(field['id']) ?? '';
    final label = asString(field['label']) ?? id;
    final type = asString(field['type']) ?? 'text';
    return Padding(
      padding: const EdgeInsets.only(top: 14),
      child: switch (type) {
        'toggle' => _toggle(id, label),
        'choice' => _labelled(label, _choiceField(id, field)),
        'slider' => _slider(id, label, field),
        'date' || 'time' => _labelled(label, _pickerField(id, type, field)),
        _ => _labelled(label, _textField(id, type, field, last: last)),
      },
    );
  }

  Widget _labelled(String label, Widget child) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(label, style: const TextStyle(fontWeight: FontWeight.w600)),
      const SizedBox(height: 6),
      child,
    ],
  );

  Widget _toggle(String id, String label) {
    final on = _values[id] == 'true';
    return Semantics(
      toggled: on,
      label: label,
      onTap: _editable
          ? () => setState(() => _values[id] = (!on).toString())
          : null,
      excludeSemantics: true,
      child: InkWell(
        borderRadius: BorderRadius.circular(12),
        onTap: _editable
            ? () => setState(() => _values[id] = (!on).toString())
            : null,
        child: ConstrainedBox(
          constraints: const BoxConstraints(minHeight: 44),
          child: Row(
            children: [
              Expanded(
                child: Text(
                  label,
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
              ),
              const SizedBox(width: 12),
              Switch.adaptive(
                value: on,
                onChanged: _editable
                    ? (value) => setState(() => _values[id] = value.toString())
                    : null,
              ),
            ],
          ),
        ),
      ),
    );
  }

  /// A few short options show as chips; longer lists use a menu.
  Widget _choiceField(String id, Map<String, dynamic> field) {
    final raw = field['options'];
    final options = [
      if (raw is List)
        for (final option in raw)
          if (option is String && option.isNotEmpty) option,
    ];
    final selected = options.contains(_values[id]) ? _values[id] : null;
    void choose(String? value) {
      if (value == null) return;
      HapticFeedback.selectionClick();
      setState(() {
        _values[id] = value;
        _hint = null;
      });
    }

    if (options.length <= 5 && options.every((option) => option.length <= 24)) {
      return Wrap(
        spacing: 8,
        runSpacing: 8,
        children: [
          for (final option in options)
            SurfaceChoiceChip(
              label: option,
              selected: option == selected,
              onTap: _editable ? () => choose(option) : null,
            ),
        ],
      );
    }
    return DropdownButtonFormField<String>(
      isExpanded: true,
      initialValue: selected,
      decoration: const InputDecoration(hintText: 'Choose'),
      items: [
        for (final option in options)
          DropdownMenuItem(value: option, child: Text(option)),
      ],
      onChanged: _editable ? choose : null,
    );
  }

  double _sliderMin(Map<String, dynamic> field) => _number(field['min']) ?? 0;
  double _sliderMax(Map<String, dynamic> field) {
    final max = _number(field['max']) ?? 10;
    return max > _sliderMin(field) ? max : _sliderMin(field) + 1;
  }

  double _sliderCurrent(Map<String, dynamic> field) {
    final id = asString(field['id']) ?? '';
    final min = _sliderMin(field);
    final max = _sliderMax(field);
    final stored = double.tryParse(_values[id] ?? '');
    if (stored != null) return stored.clamp(min, max).toDouble();
    // Start in the middle, on a step.
    final step = _number(field['step']);
    final middle = (min + max) / 2;
    return step == null
        ? middle
        : (min + ((middle - min) / step).round() * step)
              .clamp(min, max)
              .toDouble();
  }

  @override
  String _sliderValue(Map<String, dynamic> field) =>
      _formatNumber(_sliderCurrent(field));

  Widget _slider(String id, String label, Map<String, dynamic> field) {
    final colors = JarvisColors.of(context);
    final min = _sliderMin(field);
    final max = _sliderMax(field);
    final step = _number(field['step']);
    final value = _sliderCurrent(field);
    final unit = asString(field['unit']);
    final shown = [_formatNumber(value), ?unit].join(' ');
    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: [
        Row(
          crossAxisAlignment: CrossAxisAlignment.baseline,
          textBaseline: TextBaseline.alphabetic,
          children: [
            Expanded(
              child: Text(
                label,
                style: const TextStyle(fontWeight: FontWeight.w600),
              ),
            ),
            const SizedBox(width: 8),
            Text(
              shown,
              style: TextStyle(
                color: colors.accentDeep,
                fontWeight: FontWeight.w700,
                fontSize: 16,
                fontFeatures: const [FontFeature.tabularFigures()],
              ),
            ),
          ],
        ),
        Slider(
          value: value,
          min: min,
          max: max,
          divisions: step == null
              ? null
              : ((max - min) / step).round().clamp(1, 1000),
          label: shown,
          semanticFormatterCallback: (_) => shown,
          onChanged: _editable
              ? (next) => setState(() => _values[id] = _formatNumber(next))
              : null,
        ),
        Row(
          children: [
            Text(
              _formatNumber(min),
              style: TextStyle(color: colors.muted, fontSize: 12),
            ),
            const Spacer(),
            Text(
              _formatNumber(max),
              style: TextStyle(color: colors.muted, fontSize: 12),
            ),
          ],
        ),
      ],
    );
  }

  Widget _pickerField(String id, String type, Map<String, dynamic> field) {
    final colors = JarvisColors.of(context);
    final stored = _values[id];
    final shown = stored == null ? null : _formatPicked(type, stored);
    final placeholder =
        asString(field['placeholder']) ??
        (type == 'date' ? 'Pick a date' : 'Pick a time');
    return Semantics(
      button: true,
      label: shown ?? placeholder,
      excludeSemantics: true,
      onTap: _editable ? () => unawaited(_pick(id, type)) : null,
      child: Material(
        color: colors.surfaceMuted,
        borderRadius: BorderRadius.circular(14),
        child: InkWell(
          borderRadius: BorderRadius.circular(14),
          onTap: _editable ? () => unawaited(_pick(id, type)) : null,
          child: ConstrainedBox(
            constraints: const BoxConstraints(minHeight: 48),
            child: Padding(
              padding: const EdgeInsets.symmetric(horizontal: 14, vertical: 12),
              child: Row(
                children: [
                  Icon(
                    type == 'date'
                        ? PhosphorIconsRegular.calendarBlank
                        : PhosphorIconsRegular.clock,
                    size: 18,
                    color: shown == null ? colors.muted : colors.accent,
                  ),
                  const SizedBox(width: 10),
                  Expanded(
                    child: Text(
                      shown ?? placeholder,
                      style: TextStyle(
                        color: shown == null ? colors.muted : colors.ink,
                        fontWeight: shown == null
                            ? FontWeight.w400
                            : FontWeight.w600,
                      ),
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

  Future<void> _pick(String id, String type) async {
    FocusManager.instance.primaryFocus?.unfocus();
    final stored = _values[id];
    String? picked;
    if (type == 'date') {
      final now = DateTime.now();
      final initial = DateTime.tryParse(stored ?? '') ?? now;
      final date = await showDatePicker(
        context: context,
        initialDate: initial,
        firstDate: DateTime(now.year - 5),
        lastDate: DateTime(now.year + 5, 12, 31),
      );
      if (date != null) picked = _isoDate(date);
    } else {
      final time = await showTimePicker(
        context: context,
        initialTime: _parseTime(stored) ?? TimeOfDay.now(),
      );
      if (time != null) {
        picked =
            '${time.hour.toString().padLeft(2, '0')}:'
            '${time.minute.toString().padLeft(2, '0')}';
      }
    }
    if (picked == null || !mounted) return;
    setState(() {
      _values[id] = picked!;
      _hint = null;
    });
  }

  String _formatPicked(String type, String value) {
    final localizations = MaterialLocalizations.of(context);
    if (type == 'date') {
      final date = DateTime.tryParse(value);
      return date == null ? value : localizations.formatMediumDate(date);
    }
    final time = _parseTime(value);
    return time == null
        ? value
        : localizations.formatTimeOfDay(
            time,
            alwaysUse24HourFormat: MediaQuery.alwaysUse24HourFormatOf(context),
          );
  }

  Widget _textField(
    String id,
    String type,
    Map<String, dynamic> field, {
    required bool last,
  }) {
    final controller = _controllers.putIfAbsent(
      id,
      () => TextEditingController(text: _values[id] ?? ''),
    );
    final multiline = type == 'longtext';
    return Builder(
      builder: (fieldContext) => TextField(
        controller: controller,
        enabled: _editable,
        obscureText: type == 'secret',
        enableSuggestions: type != 'secret',
        autocorrect: type != 'secret',
        minLines: multiline ? 3 : 1,
        maxLines: multiline ? 6 : 1,
        textInputAction: multiline
            ? TextInputAction.newline
            : last
            ? TextInputAction.done
            : TextInputAction.next,
        keyboardType: switch (type) {
          'number' => const TextInputType.numberWithOptions(decimal: true),
          'longtext' => TextInputType.multiline,
          _ => TextInputType.text,
        },
        decoration: InputDecoration(
          hintText: asString(field['placeholder']) ?? 'Type your answer',
          suffixText: type == 'number' ? asString(field['unit']) : null,
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
        onSubmitted: last && !multiline
            ? (_) {
                final actionId = _submitActionId();
                if (actionId != null) unawaited(_run(actionId));
              }
            : null,
      ),
    );
  }

  @override
  String _text(String id) => _controllers[id]?.text ?? _values[id] ?? '';
}

double? _number(Object? value) => switch (value) {
  final num number when number.isFinite => number.toDouble(),
  final String text => double.tryParse(text),
  _ => null,
};

/// Whole numbers without ".0"; others with at most two decimals.
String _formatNumber(double value) {
  if (value == value.roundToDouble()) return value.round().toString();
  return value
      .toStringAsFixed(2)
      .replaceFirst(RegExp(r'0+$'), '')
      .replaceFirst(RegExp(r'\.$'), '');
}

String _isoDate(DateTime date) =>
    '${date.year.toString().padLeft(4, '0')}-'
    '${date.month.toString().padLeft(2, '0')}-'
    '${date.day.toString().padLeft(2, '0')}';

TimeOfDay? _parseTime(String? value) {
  final match = RegExp(r'^(\d{1,2}):(\d{2})$').firstMatch(value ?? '');
  if (match == null) return null;
  final hour = int.parse(match[1]!);
  final minute = int.parse(match[2]!);
  if (hour > 23 || minute > 59) return null;
  return TimeOfDay(hour: hour, minute: minute);
}
