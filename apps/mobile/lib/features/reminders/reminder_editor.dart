part of 'reminders_screen.dart';

class _NewReminder {
  const _NewReminder({
    required this.title,
    required this.date,
    required this.time,
    required this.recurrence,
    required this.weekdays,
    this.place,
  });

  final String title;
  final DateTime date;
  final TimeOfDay time;
  final String recurrence;
  final int weekdays;

  /// Set for a reminder that fires at a place instead of at a time.
  final ReminderPlace? place;
}

/// Where a place reminder fires, as the API expects it.
class ReminderPlace {
  const ReminderPlace({
    required this.name,
    required this.latitude,
    required this.longitude,
    this.radiusMeters = 150,
    this.trigger = 'arrive',
    this.repeats = false,
  });

  final String name;
  final double latitude;
  final double longitude;
  final double radiusMeters;
  final String trigger;
  final bool repeats;

  static ReminderPlace? fromJson(Object? raw) {
    final json = jsonObject(raw);
    if (json == null) return null;
    final latitude = json['latitude'];
    final longitude = json['longitude'];
    if (latitude is! num || longitude is! num) return null;
    final radius = json['radiusMeters'];
    return ReminderPlace(
      name: asJsonString(json['name']) ?? 'Place',
      latitude: latitude.toDouble(),
      longitude: longitude.toDouble(),
      radiusMeters: radius is num ? radius.toDouble() : 150,
      trigger: asJsonString(json['trigger']) == 'leave' ? 'leave' : 'arrive',
      repeats: json['repeats'] == true,
    );
  }

  ReminderPlace copyWith({
    String? name,
    double? radiusMeters,
    String? trigger,
    bool? repeats,
  }) => ReminderPlace(
    name: name ?? this.name,
    latitude: latitude,
    longitude: longitude,
    radiusMeters: radiusMeters ?? this.radiusMeters,
    trigger: trigger ?? this.trigger,
    repeats: repeats ?? this.repeats,
  );

  Map<String, dynamic> toJson() => {
    'name': name,
    'latitude': latitude,
    'longitude': longitude,
    'radiusMeters': radiusMeters,
    'trigger': trigger,
    'repeats': repeats,
  };

  /// "When you arrive at Home" or "When you leave Work".
  String get label =>
      trigger == 'leave' ? 'When you leave $name' : 'When you arrive at $name';
}

/// Places from earlier place reminders, newest first, one per name.
List<ReminderPlace> savedPlaces(Iterable<Map<String, dynamic>> reminders) {
  final sorted = [...reminders]
    ..sort((a, b) {
      final left = jsonDate(a['createdAt']);
      final right = jsonDate(b['createdAt']);
      if (left == null || right == null) return 0;
      return right.compareTo(left);
    });
  final seen = <String>{};
  return [
    for (final reminder in sorted)
      if (ReminderPlace.fromJson(reminder['place']) case final place?)
        if (seen.add(place.name.toLowerCase())) place,
  ];
}

class _NewReminderDialog extends StatefulWidget {
  const _NewReminderDialog({
    required this.timeZone,
    this.initialTitle,
    this.fromChat = false,
    this.places = const [],
    this.locate = readDeviceLocationSnapshot,
    required this.onSubmit,
  });

  /// Saves the reminder; null on success, otherwise the message to show here.
  final Future<String?> Function(_NewReminder reminder) onSubmit;

  /// Text the title starts with, for a reminder made from a chat message.
  final String? initialTitle;

  /// The reminder is being made from a chat message, which the editor says.
  final bool fromChat;

  /// Resolves to the zone the reminder will be saved in, shown under the time.
  final Future<String> timeZone;

  /// Places the owner used before, offered as one-tap choices.
  final List<ReminderPlace> places;

  /// Reads the phone's position for "Use where I am now".
  final Future<({double? latitude, double? longitude, double? accuracy})>
  Function()
  locate;

  @override
  State<_NewReminderDialog> createState() => _NewReminderDialogState();
}

class _NewReminderDialogState extends State<_NewReminderDialog>
    with SubmitState {
  final _formKey = GlobalKey<FormState>();
  late final _title = TextEditingController(text: widget.initialTitle);
  DateTime _date = DateTime.now().add(const Duration(days: 1));
  TimeOfDay _time = const TimeOfDay(hour: 9, minute: 0);
  String _recurrence = 'once';
  int _weekdays = 0;
  bool _atPlace = false;
  ReminderShortcut? _shortcut;
  final _placeName = TextEditingController();
  ({double latitude, double longitude, double? accuracy})? _spot;
  String? _savedPlace;
  String _trigger = 'arrive';
  double _radius = 150;
  bool _everyVisit = false;
  bool _locating = false;
  String? _placeError;

  @override
  void initState() {
    super.initState();
    // Starts on "Tomorrow morning" on the reminder's own clock.
    widget.timeZone
        .then((_) {
          if (mounted && _shortcut == null) {
            unawaited(_useShortcut(ReminderShortcut.tomorrowMorning));
          }
        })
        .catchError((_) {});
  }

  static const _radii = [(150.0, '150 m'), (300.0, '300 m'), (750.0, '750 m')];

  @override
  void dispose() {
    _title.dispose();
    _placeName.dispose();
    super.dispose();
  }

  Future<void> _useCurrentLocation() async {
    setState(() {
      _locating = true;
      _placeError = null;
    });
    final fix = await widget.locate();
    if (!mounted) return;
    setState(() {
      _locating = false;
      if (fix.latitude == null || fix.longitude == null) {
        _placeError = 'Jarvis could not read your location. Allow location for Jarvis in Settings, then try again.';
        return;
      }
      _spot = (
        latitude: fix.latitude!,
        longitude: fix.longitude!,
        accuracy: fix.accuracy,
      );
      _savedPlace = null;
      // A fuzzy fix needs a wider circle, or the reminder may never see you arrive.
      final accuracy = fix.accuracy ?? 0;
      if (accuracy * 2 > _radius) {
        _radius = _radii
            .map((option) => option.$1)
            .firstWhere((value) => value >= accuracy * 2, orElse: () => 750);
      }
    });
  }

  /// Fills the date and time from a shortcut, on the clock of the reminder's own zone.
  Future<void> _useShortcut(ReminderShortcut shortcut) async {
    final zone = await widget.timeZone;
    if (!mounted) return;
    final when = resolveReminderShortcut(shortcut, nowOnClock(zone));
    if (when == null) return;
    setState(() {
      _shortcut = shortcut;
      _date = DateTime(when.year, when.month, when.day);
      _time = TimeOfDay(hour: when.hour, minute: when.minute);
    });
  }

  /// Leaves the shortcuts and opens the time picker.
  Future<void> _pickATime() async {
    setState(() => _shortcut = null);
    final value = await showTimePicker(context: context, initialTime: _time);
    if (value != null && mounted) setState(() => _time = value);
  }

  void _pickSaved(ReminderPlace place) => setState(() {
    _savedPlace = place.name;
    _spot = (
      latitude: place.latitude,
      longitude: place.longitude,
      accuracy: null,
    );
    _placeName.text = place.name;
    _radius = _radii.any((option) => option.$1 == place.radiusMeters)
        ? place.radiusMeters
        : 150;
    _placeError = null;
  });

  Future<void> _save() async {
    if (submitting || !(_formKey.currentState?.validate() ?? false)) return;
    if (_atPlace) {
      final spot = _spot;
      if (spot == null) {
        setState(() => _placeError = 'Choose where this reminder should fire.');
        return;
      }
      await _send(
        _NewReminder(
          title: _title.text.trim(),
          date: _date,
          time: _time,
          recurrence: 'once',
          weekdays: 0,
          place: ReminderPlace(
            name: _placeName.text.trim(),
            latitude: spot.latitude,
            longitude: spot.longitude,
            radiusMeters: _radius,
            trigger: _trigger,
            repeats: _everyVisit,
          ),
        ),
      );
      return;
    }
    if ((_recurrence == 'weekly' || _recurrence == 'biweekly') &&
        _weekdays == 0) {
      ScaffoldMessenger.of(context)
        ..hideCurrentSnackBar()
        ..showSnackBar(
          const SnackBar(content: Text('Choose at least one weekday.')),
        );
      return;
    }
    await _send(
      _NewReminder(
        title: _title.text.trim(),
        date: _date,
        time: _time,
        recurrence: _recurrence,
        weekdays: _weekdays,
      ),
    );
  }

  Future<void> _send(_NewReminder reminder) async {
    if (await submit(() => widget.onSubmit(reminder)) && mounted) {
      Navigator.pop(context, true);
    }
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Create a reminder'),
    // Use the dialog's full width so dates and options stay on one line.
    content: SizedBox(
      width: 420,
      child: Form(
        key: _formKey,
        child: SingleChildScrollView(
          padding: const EdgeInsets.only(top: 8),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (widget.fromChat)
                Padding(
                  padding: const EdgeInsets.only(bottom: 6),
                  child: Row(
                    children: [
                      const Icon(PhosphorIconsRegular.chatCircle, size: 14),
                      const SizedBox(width: 6),
                      Text(
                        'From a message in this chat',
                        key: const Key('reminder-from-chat'),
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                    ],
                  ),
                ),
              TextFormField(
                textCapitalization: TextCapitalization.sentences,
                controller: _title,
                autofocus: true,
                maxLength: 300,
                buildCounter: quietCounter,
                decoration: const InputDecoration(labelText: 'Remind me about'),
                validator: (value) => value == null || value.trim().isEmpty
                    ? 'Enter a reminder.'
                    : null,
              ),
              const SizedBox(height: 4),
              SizedBox(
                width: double.infinity,
                child: SegmentedButton<bool>(
                  key: const Key('reminder-kind'),
                  showSelectedIcon: false,
                  segments: const [
                    ButtonSegment(
                      value: false,
                      label: Text('At a time'),
                      icon: Icon(PhosphorIconsRegular.clock, size: 16),
                    ),
                    ButtonSegment(
                      value: true,
                      label: Text('At a place'),
                      icon: Icon(PhosphorIconsRegular.mapPin, size: 16),
                    ),
                  ],
                  selected: {_atPlace},
                  onSelectionChanged: (value) =>
                      setState(() => _atPlace = value.first),
                ),
              ),
              const SizedBox(height: 14),
              AnimatedSize(
                duration: const Duration(milliseconds: 220),
                curve: Curves.easeOutCubic,
                alignment: Alignment.topCenter,
                child: _atPlace ? _placeFields(context) : _timeFields(context),
              ),
              submitErrorNotice(),
            ],
          ),
        ),
      ),
    ),
    actions: [
      TextButton(
        onPressed: submitting ? null : () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      SubmitButton(label: 'Save', busy: submitting, onPressed: _save),
    ],
  );

  Widget _placeFields(BuildContext context) {
    final colors = JarvisColors.of(context);
    final textTheme = Theme.of(context).textTheme;
    final spot = _spot;
    return Column(
      key: const Key('place-fields'),
      mainAxisSize: MainAxisSize.min,
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: [
            for (final option in const [
              ('arrive', 'When I arrive', PhosphorIconsRegular.signIn),
              ('leave', 'When I leave', PhosphorIconsRegular.signOut),
            ])
              ChoiceChip(
                key: Key('trigger-${option.$1}'),
                avatar: Icon(option.$3, size: 16),
                label: Text(option.$2),
                selected: _trigger == option.$1,
                onSelected: (_) => setState(() => _trigger = option.$1),
              ),
          ],
        ),
        const SizedBox(height: 12),
        if (widget.places.isNotEmpty) ...[
          const SizedBox(height: 4),
          Text(
            'Your places',
            style: textTheme.labelMedium?.copyWith(color: colors.muted),
          ),
          const SizedBox(height: 6),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              for (final place in widget.places.take(6))
                ChoiceChip(
                  key: Key('saved-place-${place.name}'),
                  avatar: Icon(
                    place.name.toLowerCase() == 'home'
                        ? PhosphorIconsRegular.house
                        : PhosphorIconsRegular.mapPin,
                    size: 16,
                  ),
                  label: Text(place.name),
                  selected: _savedPlace == place.name,
                  onSelected: (_) => _pickSaved(place),
                ),
            ],
          ),
          const SizedBox(height: 10),
        ],
        TextFormField(
          key: const Key('place-name'),
          controller: _placeName,
          maxLength: 120,
          buildCounter: quietCounter,
          textCapitalization: TextCapitalization.words,
          decoration: const InputDecoration(
            labelText: 'Place name',
            hintText: 'Home, Work, Supermarket…',
          ),
          onChanged: (_) {
            // Renaming a saved place means a new place, so its spot is dropped.
            if (_savedPlace != null) {
              setState(() {
                _savedPlace = null;
                _spot = null;
              });
            }
          },
          validator: (value) =>
              _atPlace && (value == null || value.trim().isEmpty)
              ? 'Name the place.'
              : null,
        ),
        Material(
          color: colors.surfaceMuted,
          borderRadius: BorderRadius.circular(JarvisRadii.md),
          clipBehavior: Clip.antiAlias,
          child: ListTile(
            key: const Key('use-current-location'),
            leading: _locating
                ? const SizedBox.square(
                    dimension: 20,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  )
                : Icon(
                    spot != null && _savedPlace == null
                        ? PhosphorIconsRegular.checkCircle
                        : PhosphorIconsRegular.mapPin,
                    color: spot != null && _savedPlace == null
                        ? colors.success
                        : null,
                  ),
            title: Text(
              spot != null && _savedPlace == null
                  ? 'Using where you are now'
                  : 'Use where I am now',
            ),
            subtitle: spot != null && _savedPlace == null
                ? Text(
                    '${spot.latitude.toStringAsFixed(4)}, ${spot.longitude.toStringAsFixed(4)}'
                    '${spot.accuracy != null ? ' · ±${spot.accuracy!.round()} m' : ''}',
                    style: textTheme.bodySmall?.copyWith(color: colors.muted),
                  )
                : null,
            onTap: _locating ? null : _useCurrentLocation,
          ),
        ),
        if (_placeError != null)
          Padding(
            padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
            child: Text(
              _placeError!,
              style: textTheme.bodySmall?.copyWith(color: colors.danger),
            ),
          ),
        const SizedBox(height: 14),
        Text(
          'How close counts',
          style: textTheme.labelMedium?.copyWith(color: colors.muted),
        ),
        const SizedBox(height: 6),
        Wrap(
          spacing: 6,
          runSpacing: 6,
          children: [
            for (final option in _radii)
              ChoiceChip(
                key: Key('radius-${option.$1.round()}'),
                label: Text(option.$2),
                selected: _radius == option.$1,
                onSelected: (_) => setState(() => _radius = option.$1),
              ),
          ],
        ),
        const SizedBox(height: 6),
        SwitchListTile.adaptive(
          key: const Key('every-visit'),
          contentPadding: const EdgeInsets.symmetric(horizontal: 4),
          title: const Text('Every visit'),
          subtitle: Text(
            _everyVisit
                ? 'Keeps reminding you each time.'
                : 'Only the next time.',
            style: textTheme.bodySmall?.copyWith(color: colors.muted),
          ),
          value: _everyVisit,
          onChanged: (value) => setState(() => _everyVisit = value),
        ),
        Padding(
          padding: const EdgeInsets.fromLTRB(4, 2, 4, 0),
          child: Text(
            'Works while Jarvis is open, and in the background when location is set to Always. To use an address, ask Jarvis in chat.',
            style: textTheme.bodySmall?.copyWith(
              color: colors.muted,
              height: 1.35,
            ),
          ),
        ),
      ],
    );
  }

  Widget _timeFields(BuildContext context) => Column(
    key: const Key('time-fields'),
    mainAxisSize: MainAxisSize.min,
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      SegmentedPills<String>(
        keyPrefix: 'recurrence',
        options: const [
          ('once', 'Once'),
          ('daily', 'Daily'),
          ('weekdays', 'Weekdays'),
          ('weekly', 'Weekly'),
          ('biweekly', 'Every 2 weeks'),
        ],
        selected: _recurrence,
        onSelected: (value) => setState(() => _recurrence = value),
      ),
      if (_recurrence == 'weekly' || _recurrence == 'biweekly') ...[
        const SizedBox(height: 10),
        Wrap(
          spacing: 6,
          runSpacing: 6,
          children: [
            for (final day in weekdayNames)
              FilterChip(
                key: Key('weekday-${day.$1}'),
                label: Text(day.$2),
                selected: _weekdays & day.$1 != 0,
                onSelected: (selected) => setState(() {
                  _weekdays = selected
                      ? _weekdays | day.$1
                      : _weekdays & ~day.$1;
                }),
              ),
          ],
        ),
      ],
      if (_recurrence == 'once') ...[
        const SizedBox(height: 10),
        FutureBuilder<String>(
          future: widget.timeZone,
          builder: (context, snapshot) {
            final now = nowOnClock(snapshot.data);
            return Wrap(
              key: const Key('reminder-shortcuts'),
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final shortcut in ReminderShortcut.values)
                  if (resolveReminderShortcut(shortcut, now) != null)
                    ChoiceChip(
                      key: Key('shortcut-${shortcut.name}'),
                      label: Text(shortcut.label),
                      selected: _shortcut == shortcut,
                      onSelected: snapshot.hasData
                          ? (_) => _useShortcut(shortcut)
                          : null,
                    ),
                ChoiceChip(
                  key: const Key('shortcut-pick'),
                  label: const Text('Pick a time'),
                  selected: _shortcut == null,
                  onSelected: (_) => _pickATime(),
                ),
              ],
            );
          },
        ),
      ],
      const SizedBox(height: 10),
      ListTile(
        tileColor: JarvisColors.of(context).surfaceMuted,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(JarvisRadii.md),
        ),
        leading: const Icon(PhosphorIconsRegular.calendarBlank),
        trailing: const Icon(PhosphorIconsRegular.caretDown),
        title: Text(friendlyDate(context, _date)),
        onTap: () async {
          final value = await showDatePicker(
            context: context,
            initialDate: _date,
            firstDate: DateTime.now(),
            lastDate: DateTime.now().add(const Duration(days: 730)),
          );
          if (value != null && mounted) {
            setState(() {
              _date = value;
              _shortcut = null;
            });
          }
        },
      ),
      const SizedBox(height: 8),
      ListTile(
        tileColor: JarvisColors.of(context).surfaceMuted,
        shape: RoundedRectangleBorder(
          borderRadius: BorderRadius.circular(JarvisRadii.md),
        ),
        leading: const Icon(PhosphorIconsRegular.clock),
        trailing: const Icon(PhosphorIconsRegular.caretDown),
        title: Text(timeOfDayLabel(context, _time)),
        onTap: () async {
          final value = await showTimePicker(
            context: context,
            builder: use24HourClock,
            initialTime: _time,
          );
          if (value != null && mounted) {
            setState(() {
              _time = value;
              _shortcut = null;
            });
          }
        },
      ),
      FutureBuilder<String>(
        future: widget.timeZone,
        builder: (context, snapshot) {
          final zone = snapshot.data;
          final style = Theme.of(context).textTheme.bodySmall
              ?.copyWith(color: JarvisColors.of(context).muted);
          // The exact moment this will fire, on the reminder's clock and the phone's.
          String? when;
          String? phone;
          if (zone != null && _recurrence == 'once') {
            final picked = DateTime(
              _date.year,
              _date.month,
              _date.day,
              _time.hour,
              _time.minute,
            );
            final instant = instantInZone(picked, zone) ?? picked.toUtc();
            final described = describeScheduledInstant(
              context,
              instant,
              zone,
              inSentence: true,
            );
            when = 'Reminds you ${described.primary}';
            phone = described.other;
          }
          return Padding(
            padding: const EdgeInsets.fromLTRB(4, 8, 4, 0),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  zone != null
                      ? 'Time zone: ${zone.replaceAll('_', ' ')}'
                      : ' ',
                  style: style,
                ),
                if (when != null)
                  Text(when, key: const Key('reminder-when'), style: style),
                if (phone != null)
                  Text(
                    phone,
                    key: const Key('reminder-when-phone'),
                    style: style,
                  ),
              ],
            ),
          );
        },
      ),
    ],
  );
}
