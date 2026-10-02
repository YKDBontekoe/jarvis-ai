import 'package:flutter/material.dart';

import '../../theme.dart';
import '../../ui/phosphor_icons.dart';
import 'people_models.dart';

/// Round initials avatar tinted per name. A small cake badge marks a birthday
/// today.
class PersonAvatar extends StatelessWidget {
  const PersonAvatar({
    required this.name,
    this.size = 40,
    this.birthdayToday = false,
    super.key,
  });

  final String name;
  final double size;
  final bool birthdayToday;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final tint = personTint(colors, name);
    final avatar = Container(
      width: size,
      height: size,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: tint.withValues(alpha: colors.isDark ? .24 : .14),
        border: Border.all(color: tint.withValues(alpha: .28)),
      ),
      child: Text(
        personInitials(name),
        style: TextStyle(
          fontSize: size * .36,
          fontWeight: FontWeight.w600,
          color: tint,
          letterSpacing: .2,
        ),
      ),
    );
    if (!birthdayToday) return ExcludeSemantics(child: avatar);
    final badge = size * .42;
    return ExcludeSemantics(
      child: SizedBox(
        width: size,
        height: size,
        child: Stack(
          clipBehavior: Clip.none,
          children: [
            avatar,
            Positioned(
              right: -2,
              bottom: -2,
              child: Container(
                width: badge,
                height: badge,
                decoration: BoxDecoration(
                  color: colors.rose,
                  shape: BoxShape.circle,
                  border: Border.all(color: colors.surface, width: 2),
                ),
                child: Icon(
                  PhosphorIconsRegular.cake,
                  size: badge * .58,
                  color: colors.onInk,
                ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}

typedef PersonDraft = ({
  String name,
  String? relationship,
  int? birthdayMonth,
  int? birthdayDay,
  int? birthYear,
  int? contactEveryDays,
  String? notes,
});

/// Bottom sheet for adding or editing a person.
Future<PersonDraft?> showPersonEditor(
  BuildContext context, {
  PersonData? person,
  String title = 'Add someone',
  String confirmLabel = 'Add',
}) => showModalBottomSheet<PersonDraft>(
  context: context,
  isScrollControlled: true,
  showDragHandle: true,
  builder: (_) => _PersonEditorSheet(
    person: person,
    title: title,
    confirmLabel: confirmLabel,
  ),
);

class _PersonEditorSheet extends StatefulWidget {
  const _PersonEditorSheet({
    required this.person,
    required this.title,
    required this.confirmLabel,
  });

  final PersonData? person;
  final String title;
  final String confirmLabel;

  @override
  State<_PersonEditorSheet> createState() => _PersonEditorSheetState();
}

class _PersonEditorSheetState extends State<_PersonEditorSheet> {
  late final _name = TextEditingController(text: widget.person?.name);
  late final _relationship = TextEditingController(
    text: widget.person?.relationship,
  );
  late final _year = TextEditingController(
    text: widget.person?.birthYear?.toString(),
  );
  late final _notes = TextEditingController(text: widget.person?.notes);
  late int? _month = widget.person?.birthdayMonth;
  late int? _day = widget.person?.birthdayDay;
  late int? _every = widget.person?.contactEveryDays;

  @override
  void dispose() {
    _name.dispose();
    _relationship.dispose();
    _year.dispose();
    _notes.dispose();
    super.dispose();
  }

  int get _daysInMonth => switch (_month) {
    2 => 29,
    4 || 6 || 9 || 11 => 30,
    _ => 31,
  };

  String? get _yearError {
    final text = _year.text.trim();
    if (text.isEmpty) return null;
    final year = int.tryParse(text);
    if (year == null || year < 1900 || year > DateTime.now().year) {
      return 'Enter a year like 1990';
    }
    if (_month == 2 && _day == 29 && year % 4 != 0) {
      return '$year has no 29 February';
    }
    return null;
  }

  bool get _valid =>
      _name.text.trim().isNotEmpty &&
      (_month == null) == (_day == null) &&
      _yearError == null;

  void _submit() {
    if (!_valid) return;
    String? clean(String text) => text.trim().isEmpty ? null : text.trim();
    Navigator.of(context).pop((
      name: _name.text.trim(),
      relationship: clean(_relationship.text),
      birthdayMonth: _month,
      birthdayDay: _day,
      birthYear: _month == null ? null : int.tryParse(_year.text.trim()),
      contactEveryDays: _every,
      notes: clean(_notes.text),
    ));
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final everyChoices = [
      ...cadenceChoices,
      if (!cadenceChoices.contains(_every)) _every,
    ];
    return Padding(
      padding: EdgeInsets.only(bottom: MediaQuery.viewInsetsOf(context).bottom),
      child: SingleChildScrollView(
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 20),
        child: Column(
          mainAxisSize: MainAxisSize.min,
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Text(widget.title, style: Theme.of(context).textTheme.titleLarge),
            const SizedBox(height: 16),
            TextField(
              key: const Key('person-name'),
              controller: _name,
              autofocus: widget.person == null,
              maxLength: 80,
              textCapitalization: TextCapitalization.words,
              textInputAction: TextInputAction.next,
              decoration: const InputDecoration(
                labelText: 'Name',
                hintText: 'Anna, Mum, Tom de Vries…',
                counterText: '',
              ),
              onChanged: (_) => setState(() {}),
            ),
            const SizedBox(height: 12),
            TextField(
              key: const Key('person-relationship'),
              controller: _relationship,
              maxLength: 40,
              textCapitalization: TextCapitalization.sentences,
              textInputAction: TextInputAction.next,
              decoration: const InputDecoration(
                labelText: 'Relationship (optional)',
                hintText: 'Mother, Friend, Colleague…',
                counterText: '',
              ),
            ),
            const SizedBox(height: 20),
            _Label(icon: PhosphorIconsRegular.cake, text: 'Birthday'),
            const SizedBox(height: 8),
            Row(
              children: [
                Expanded(
                  flex: 3,
                  // Rebuilt when the month changes so a clamped day shows.
                  child: DropdownButtonFormField<int?>(
                    key: ValueKey('person-birthday-day-$_daysInMonth'),
                    initialValue: _day,
                    isExpanded: true,
                    icon: const Icon(PhosphorIconsRegular.caretDown, size: 16),
                    decoration: const InputDecoration(labelText: 'Day'),
                    items: [
                      const DropdownMenuItem(value: null, child: Text('—')),
                      for (var day = 1; day <= _daysInMonth; day++)
                        DropdownMenuItem(value: day, child: Text('$day')),
                    ],
                    onChanged: (value) => setState(() => _day = value),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  flex: 5,
                  child: DropdownButtonFormField<int?>(
                    key: const Key('person-birthday-month'),
                    initialValue: _month,
                    isExpanded: true,
                    icon: const Icon(PhosphorIconsRegular.caretDown, size: 16),
                    decoration: const InputDecoration(labelText: 'Month'),
                    items: [
                      const DropdownMenuItem(value: null, child: Text('—')),
                      for (var month = 1; month <= 12; month++)
                        DropdownMenuItem(
                          value: month,
                          child: Text(monthName(month)),
                        ),
                    ],
                    onChanged: (value) => setState(() {
                      _month = value;
                      if (_day != null && _day! > _daysInMonth) {
                        _day = _daysInMonth;
                      }
                    }),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  flex: 3,
                  child: TextField(
                    key: const Key('person-birth-year'),
                    controller: _year,
                    enabled: _month != null,
                    keyboardType: TextInputType.number,
                    maxLength: 4,
                    decoration: InputDecoration(
                      labelText: 'Year',
                      hintText: 'Optional',
                      counterText: '',
                      errorText: _yearError,
                      errorMaxLines: 2,
                    ),
                    onChanged: (_) => setState(() {}),
                  ),
                ),
              ],
            ),
            if ((_month == null) != (_day == null))
              Padding(
                padding: const EdgeInsets.only(top: 6, left: 4),
                child: Text(
                  'Pick both a day and a month.',
                  style: TextStyle(fontSize: 12.5, color: colors.danger),
                ),
              ),
            const SizedBox(height: 20),
            _Label(
              icon: PhosphorIconsRegular.phoneCall,
              text: 'Remind me to stay in touch',
            ),
            const SizedBox(height: 8),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final every in everyChoices)
                  ChoiceChip(
                    key: Key('person-every-${every ?? 'off'}'),
                    label: Text(cadenceChoiceLabel(every)),
                    selected: _every == every,
                    showCheckmark: false,
                    onSelected: (_) => setState(() => _every = every),
                  ),
              ],
            ),
            const SizedBox(height: 20),
            TextField(
              key: const Key('person-notes'),
              controller: _notes,
              maxLength: 2000,
              minLines: 2,
              maxLines: 6,
              textCapitalization: TextCapitalization.sentences,
              decoration: const InputDecoration(
                labelText: 'Notes (optional)',
                hintText: 'Gift ideas, kids’ names, what you talked about…',
                counterText: '',
                alignLabelWithHint: true,
              ),
            ),
            const SizedBox(height: 12),
            FilledButton(
              key: const Key('person-save'),
              onPressed: _valid ? _submit : null,
              style: FilledButton.styleFrom(minimumSize: const Size(0, 46)),
              child: Text(widget.confirmLabel),
            ),
          ],
        ),
      ),
    );
  }
}

class _Label extends StatelessWidget {
  const _Label({required this.icon, required this.text});

  final IconData icon;
  final String text;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Row(
      children: [
        Icon(icon, size: 16, color: colors.inkSoft),
        const SizedBox(width: 8),
        Text(
          text,
          style: TextStyle(
            fontSize: 13.5,
            fontWeight: FontWeight.w600,
            color: colors.inkSoft,
          ),
        ),
      ],
    );
  }
}
