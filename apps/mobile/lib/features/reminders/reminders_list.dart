part of 'reminders_screen.dart';

/// The reminders and notifications tabs: grouped rows, cards, swipe and menu actions.
class _RemindersScreenState extends _RemindersController {
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const PageTitle('Reminders'),
      actions: [
        if (_unreadCount > 0)
          IconButton(
            onPressed: _markAllRead,
            tooltip: 'Mark all as read',
            icon: const Icon(PhosphorIconsRegular.checkCircle),
          ),
        IconButton(
          onPressed: _load,
          tooltip: 'Refresh',
          icon: const Icon(PhosphorIconsRegular.arrowsClockwise),
        ),
        HeaderAction(
          label: 'New',
          icon: PhosphorIconsRegular.plus,
          onPressed: _createReminder,
        ),
      ],
      bottom: PreferredSize(
        // Tab height (46) + track padding (8) + bottom margin (12); asking for
        // less squeezes the toolbar above it.
        preferredSize: const Size.fromHeight(66),
        child: ContentWidth(
          child: Container(
            constraints: const BoxConstraints(minHeight: 44),
            margin: const EdgeInsets.fromLTRB(16, 0, 16, 12),
            padding: const EdgeInsets.all(4),
            decoration: BoxDecoration(
              color: JarvisColors.of(context).surfaceRaised,
              borderRadius: BorderRadius.circular(JarvisRadii.md),
            ),
            child: TabBar(
              controller: _tabs,
              tabs: [
                const Tab(
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Text('Reminders'),
                  ),
                ),
                Tab(
                  child: FittedBox(
                    fit: BoxFit.scaleDown,
                    child: Row(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const Text('Notifications'),
                        if (_unreadCount > 0) ...[
                          const SizedBox(width: 6),
                          Container(
                            padding: const EdgeInsets.symmetric(
                              horizontal: 6,
                              vertical: 1,
                            ),
                            decoration: BoxDecoration(
                              color: JarvisColors.of(context).ink,
                              borderRadius: BorderRadius.circular(20),
                            ),
                            child: Text(
                              '$_unreadCount',
                              style: TextStyle(
                                color: JarvisColors.of(context).onInk,
                                fontSize: 11,
                                fontWeight: FontWeight.w700,
                              ),
                            ),
                          ),
                        ],
                      ],
                    ),
                  ),
                ),
              ],
            ),
          ),
        ),
      ),
    ),
    body: ListScreenBody(
      loading: _loading,
      error: (_reminders.isNotEmpty || _notifications.isNotEmpty)
          ? _error
          : null,
      isEmpty: false,
      onRetry: _load,
      empty: const SizedBox.shrink(),
      child: TabBarView(
        controller: _tabs,
        children: [_buildReminders(), _buildNotifications()],
      ),
    ),
  );

  /// Pending reminders first (overdue, today, upcoming) then finished ones,
  /// each under a small heading so the list reads as a timeline.
  List<Object> get _reminderRows {
    final now = DateTime.now();
    final startOfToday = DateTime(now.year, now.month, now.day);
    final endOfToday = startOfToday.add(const Duration(days: 1));
    final buckets = <String, List<Map<String, dynamic>>>{
      'Overdue': [],
      'Today': [],
      'At a place': [],
      'Upcoming': [],
      'Finished': [],
    };
    final sorted = [..._reminders]
      ..sort((a, b) {
        final left = jsonDate(a['dueAt']);
        final right = jsonDate(b['dueAt']);
        if (left == null || right == null) return 0;
        return left.compareTo(right);
      });
    for (final reminder in sorted) {
      final due = jsonDate(reminder['dueAt'], local: true);
      final pending =
          (asJsonString(reminder['status']) ?? 'pending') == 'pending';
      final key = !pending
          ? 'Finished'
          : reminder['place'] != null
          ? 'At a place'
          : due == null || !due.isBefore(endOfToday)
          ? 'Upcoming'
          : due.isBefore(now)
          ? 'Overdue'
          : 'Today';
      buckets[key]!.add(reminder);
    }
    // Most recent first, so the collapsed list shows what just happened.
    buckets['Finished'] = buckets['Finished']!.reversed.toList();
    final access = _locationAccess;
    final needsAccess = access != null && access != LocationPermission.always;
    return [
      for (final entry in buckets.entries)
        if (entry.value.isNotEmpty) ...[
          entry.key,
          if (entry.key == 'At a place' && needsAccess)
            _PlaceAccessHint(denied: access != LocationPermission.whileInUse),
          ...entry.value,
        ],
    ];
  }

  /// Notifications grouped by the day they arrived, newest first.
  List<Object> get _notificationRows {
    final now = DateTime.now();
    final today = DateTime(now.year, now.month, now.day);
    String label(DateTime? date) {
      if (date == null) return 'Earlier';
      final day = DateTime(date.year, date.month, date.day);
      final days = today.difference(day).inDays;
      if (days <= 0) return 'Today';
      if (days == 1) return 'Yesterday';
      if (days < 7) return 'This week';
      return 'Earlier';
    }

    final sorted = [..._notifications]
      ..sort((a, b) {
        final left = jsonDate(a['createdAt']);
        final right = jsonDate(b['createdAt']);
        if (left == null || right == null) return 0;
        return right.compareTo(left);
      });
    final rows = <Object>[];
    String? current;
    for (final notification in sorted) {
      final heading = label(jsonDate(notification['createdAt'], local: true));
      if (heading != current) {
        rows.add(heading);
        current = heading;
      }
      rows.add(notification);
    }
    return rows;
  }

  int get _unreadCount =>
      _notifications.where((item) => item['readAt'] == null).length;

  Widget _buildReminders() => _remindersFailed && _reminders.isEmpty
      ? ErrorState(message: 'Jarvis could not load reminders.', onRetry: _load)
      : _reminders.isEmpty
      ? const EmptyState(
          icon: PhosphorIconsRegular.alarm,
          title: 'No reminders yet.',
          message: 'Ask Jarvis to remind you, or create one here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          itemCount: _visibleReminderRows.length,
          itemBuilder: (context, index) {
            final row = _visibleReminderRows[index];
            if (row is String) return _GroupHeader(row);
            if (row is _PlaceAccessHint) {
              return ContentWidth(child: _placeAccessCard(row.denied));
            }
            if (row is _ShowMoreFinished) {
              return ContentWidth(
                child: Align(
                  alignment: Alignment.centerLeft,
                  child: TextButton(
                    onPressed: () => setState(() => _showAllFinished = true),
                    child: Text('Show ${row.hidden} older'),
                  ),
                ),
              );
            }
            final reminder = row as Map<String, dynamic>;
            return FadeSlideIn(
              index: index,
              child: ContentWidth(child: _swipeable(reminder)),
            );
          },
        );

  /// [_reminderRows] with the finished group trimmed to a few recent items.
  List<Object> get _visibleReminderRows {
    final rows = _reminderRows;
    if (_showAllFinished) return rows;
    final start = rows.indexOf('Finished');
    if (start < 0) return rows;
    final finished = rows.sublist(start + 1);
    if (finished.length <= _finishedPreview) return rows;
    return [
      ...rows.sublist(0, start + 1),
      ...finished.take(_finishedPreview),
      _ShowMoreFinished(finished.length - _finishedPreview),
    ];
  }

  /// Explains why a place reminder may stay quiet, with a way to fix it.
  Widget _placeAccessCard(bool denied) {
    final colors = JarvisColors.of(context);
    return SurfaceCard(
      key: const Key('place-access-hint'),
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
      color: denied ? colors.warningSoft : colors.surfaceMuted,
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Icon(
            PhosphorIconsRegular.mapPin,
            size: 20,
            color: denied ? colors.warning : colors.inkSoft,
          ),
          const SizedBox(width: 12),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  denied
                      ? 'Jarvis can’t see your location'
                      : 'Only while Jarvis is open',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 4),
                Text(
                  denied
                      ? 'Place reminders need location access. Turn it on in Settings.'
                      : 'Set location to Always in Settings so place reminders also work when the app is closed.',
                  style: Theme.of(context).textTheme.bodySmall?.copyWith(
                    color: colors.inkSoft,
                    height: 1.35,
                  ),
                ),
                const SizedBox(height: 6),
                TextButton(
                  onPressed: () => unawaited(widget.openLocationSettings()),
                  style: TextButton.styleFrom(
                    padding: EdgeInsets.zero,
                    minimumSize: const Size(0, 32),
                    tapTargetSize: MaterialTapTargetSize.shrinkWrap,
                    foregroundColor: colors.ink,
                    textStyle: Theme.of(context).textTheme.labelLarge?.copyWith(
                      fontWeight: FontWeight.w600,
                    ),
                  ),
                  child: const Text('Open Settings'),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }

  /// Swipe right to finish a one-off reminder, left to snooze it an hour.
  Widget _swipeable(Map<String, dynamic> reminder) {
    final colors = JarvisColors.of(context);
    final id = jsonId(reminder);
    final pending =
        (asJsonString(reminder['status']) ?? 'pending') == 'pending';
    final recurrence = asJsonString(reminder['recurrence']);
    final repeating =
        recurrence != null && recurrence != 'once' && recurrence != 'none';
    final atPlace = reminder['place'] != null;
    if (id == null || !pending) return _reminderCard(reminder);
    return SwipeActions(
      id: id,
      start: repeating || atPlace
          ? null
          : SwipeAction(
              label: 'Done',
              icon: PhosphorIconsRegular.check,
              color: colors.success,
              onTrigger: () => _markDone(reminder),
            ),
      end: atPlace
          ? null
          : SwipeAction(
              label: '1 hour',
              icon: PhosphorIconsRegular.clock,
              color: colors.accent,
              onTrigger: () => _snooze(id, const Duration(hours: 1)),
            ),
      child: _reminderCard(reminder),
    );
  }

  Widget _reminderCard(Map<String, dynamic> reminder) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(reminder['status']) ?? 'pending';
    final pending = status == 'pending';
    final repeat = repeatLabel(
      asJsonString(reminder['recurrence']),
      reminder['weekdays'] is int ? reminder['weekdays'] as int : 0,
    );
    final due = jsonDate(reminder['dueAt'], local: true);
    final outcome = _outcome(reminder);
    final place = ReminderPlace.fromJson(reminder['place']);
    final zone = asJsonString(reminder['timeZoneId']);
    final otherZone =
        place == null &&
        pending &&
        zone != null &&
        _deviceZone != null &&
        zone != _deviceZone;

    final String primary;
    if (pending && place != null) {
      primary = place.repeats ? '${place.label} · every visit' : place.label;
    } else if (pending && due != null) {
      final when = friendlyWhen(context, due);
      primary = repeat == null ? when : '$repeat · next $when';
    } else {
      primary = outcome.label;
    }
    final relative = pending && due != null && place == null
        ? relativeFromNow(due)
        : null;

    return SurfaceCard(
      margin: const EdgeInsets.only(bottom: 10),
      padding: const EdgeInsets.fromLTRB(16, 14, 4, 14),
      onTap: widget.onOpenConversation == null
          ? null
          : () => _openChat(reminder),
      child: Row(
        children: [
          IconBadge(
            icon: pending
                ? (place != null
                      ? (place.name.toLowerCase() == 'home'
                            ? PhosphorIconsRegular.house
                            : PhosphorIconsRegular.mapPin)
                      : repeat == null
                      ? PhosphorIconsRegular.alarm
                      : PhosphorIconsRegular.repeat)
                : outcome.icon,
            color: pending ? null : outcome.color,
          ),
          const SizedBox(width: 14),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  asJsonString(reminder['title']) ?? '',
                  style: Theme.of(context).textTheme.titleSmall?.copyWith(
                    fontSize: 15,
                    color: pending ? null : colors.inkSoft,
                  ),
                ),
                const SizedBox(height: 4),
                Text.rich(
                  TextSpan(
                    children: [
                      TextSpan(text: primary),
                      if (relative != null)
                        TextSpan(
                          text: ' · $relative',
                          style: TextStyle(color: colors.muted),
                        ),
                    ],
                  ),
                  style: Theme.of(
                    context,
                  ).textTheme.bodySmall?.copyWith(color: colors.inkSoft),
                ),
                if (otherZone)
                  Padding(
                    padding: const EdgeInsets.only(top: 2),
                    child: Text(
                      'Runs on ${zone.replaceAll('_', ' ')} time',
                      style: Theme.of(
                        context,
                      ).textTheme.bodySmall?.copyWith(color: colors.inkSoft),
                    ),
                  ),
              ],
            ),
          ),
          _reminderMenu(
            reminder,
            pending: pending,
            repeating: repeat != null || (place?.repeats ?? false),
            atPlace: place != null,
          ),
        ],
      ),
    );
  }

  /// What happened to a reminder that is no longer upcoming.
  ({String label, IconData icon, Color color}) _outcome(
    Map<String, dynamic> reminder,
  ) {
    final colors = JarvisColors.of(context);
    final status = asJsonString(reminder['status']) ?? 'pending';
    final delivered = jsonDate(reminder['lastDeliveredAt'], local: true);
    final completed = jsonDate(reminder['completedAt'], local: true);
    return switch (status) {
      'completed' || 'delivered' when delivered != null => (
        label: 'Reminded you ${friendlyWhen(context, delivered)}',
        icon: PhosphorIconsRegular.checkCircle,
        color: colors.success,
      ),
      'completed' || 'delivered' => (
        label: completed == null
            ? 'Done'
            : 'Marked done ${friendlyWhen(context, completed)}',
        icon: PhosphorIconsRegular.checkCircle,
        color: colors.success,
      ),
      'cancelled' => (
        label: 'Cancelled',
        icon: PhosphorIconsRegular.bellSlash,
        color: colors.muted,
      ),
      'failed' => (
        label: 'Could not be delivered',
        icon: PhosphorIconsRegular.warningCircle,
        color: colors.danger,
      ),
      _ => (
        label: statusStyle(status).label,
        icon: PhosphorIconsRegular.alarm,
        color: colors.inkSoft,
      ),
    };
  }

  Widget _reminderMenu(
    Map<String, dynamic> reminder, {
    required bool pending,
    required bool repeating,
    bool atPlace = false,
  }) {
    final id = jsonId(reminder);
    final status = asJsonString(reminder['status']) ?? 'pending';
    // A waiting place reminder fires on arrival; snoozing it would turn it into
    // a timed one, so that is only offered once it has fired.
    final canSnooze =
        (pending && !atPlace) || status == 'completed' || status == 'delivered';
    return PopupMenuButton<String>(
      tooltip: 'Reminder actions',
      icon: const Icon(PhosphorIconsRegular.dotsThree, size: 20),
      onSelected: (value) => switch (value) {
        'snooze10' => _snooze(id, const Duration(minutes: 10)),
        'snooze60' => _snooze(id, const Duration(hours: 1)),
        'tomorrow' => _snooze(id, Duration.zero, until: _tomorrowMorning()),
        'done' => _markDone(reminder),
        'chat' => _openChat(reminder),
        'cancel' => _cancelReminder(reminder),
        _ => Future<void>.value(),
      },
      itemBuilder: (_) => [
        if (canSnooze) ...[
          PopupMenuItem(
            value: 'snooze10',
            child: Text(
              pending ? 'Remind me in 10 min' : 'Remind me again in 10 min',
            ),
          ),
          const PopupMenuItem(value: 'snooze60', child: Text('In 1 hour')),
          const PopupMenuItem(
            value: 'tomorrow',
            child: Text('Tomorrow at 9:00'),
          ),
        ],
        if (pending && !repeating)
          const PopupMenuItem(value: 'done', child: Text('Mark as done')),
        if (widget.onOpenConversation != null)
          const PopupMenuItem(value: 'chat', child: Text('Open chat')),
        if (pending)
          PopupMenuItem(
            value: 'cancel',
            child: Text(
              repeating ? 'Stop repeating' : 'Cancel reminder',
              style: TextStyle(color: JarvisColors.of(context).danger),
            ),
          ),
      ],
    );
  }

  IconData _notificationIcon(Object? type) => switch (type) {
    'reminder.due' || 'reminder.failed' => PhosphorIconsRegular.alarm,
    'whatsapp.reminder' => PhosphorIconsRegular.whatsappLogo,
    'task.completed' => PhosphorIconsRegular.checkCircle,
    'task.failed' => PhosphorIconsRegular.warningCircle,
    'approval.required' => PhosphorIconsRegular.shieldCheck,
    'coding.pr.ready' || 'coding.run.ready' => PhosphorIconsRegular.code,
    'watch.triggered' || 'watch.failed' => PhosphorIconsRegular.pulse,
    _ => PhosphorIconsRegular.bell,
  };

  Widget _buildNotifications() => _notificationsFailed && _notifications.isEmpty
      ? ErrorState(
          message: 'Jarvis could not load notifications.',
          onRetry: _load,
        )
      : _notifications.isEmpty
      ? const EmptyState(
          icon: PhosphorIconsRegular.bell,
          title: 'No notifications yet.',
          message: 'Alerts from reminders, tasks, and watches will show here.',
        )
      : ListView.builder(
          padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
          itemCount: _notificationRows.length,
          itemBuilder: (context, index) {
            final row = _notificationRows[index];
            if (row is String) return _GroupHeader(row);
            final notification = row as Map<String, dynamic>;
            final unread = notification['readAt'] == null;
            final body = asJsonString(notification['body']) ?? '';
            return FadeSlideIn(
              index: index,
              child: ContentWidth(
                child: SurfaceCard(
                  margin: const EdgeInsets.only(bottom: 10),
                  padding: const EdgeInsets.fromLTRB(16, 14, 16, 14),
                  color: unread
                      ? JarvisColors.of(context).surface
                      : JarvisColors.of(context).canvas,
                  borderColor: JarvisColors.of(context).outline,
                  onTap: () => _openNotification(notification),
                  child: Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      IconBadge(icon: _notificationIcon(notification['type'])),
                      const SizedBox(width: 14),
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(
                              asJsonString(notification['title']) ?? '',
                              style: Theme.of(context).textTheme.titleSmall
                                  ?.copyWith(
                                    fontSize: 15,
                                    fontWeight: unread
                                        ? FontWeight.w700
                                        : FontWeight.w500,
                                  ),
                            ),
                            if (body.isNotEmpty) ...[
                              const SizedBox(height: 4),
                              Text(
                                body,
                                style: TextStyle(
                                  fontSize: 13.5,
                                  height: 1.4,
                                  color: JarvisColors.of(context).inkSoft,
                                ),
                              ),
                            ],
                            if (asJsonString(notification['type']) ==
                                    'reminder.due' &&
                                notification['sourceId'] != null)
                              _snoozeActions(
                                asJsonString(notification['sourceId']),
                              ),
                            if (asJsonString(notification['type']) ==
                                    'approval.required' &&
                                _pendingApprovals[asJsonString(
                                      notification['sourceId'],
                                    )] !=
                                    null)
                              _approvalActions(
                                _pendingApprovals[asJsonString(
                                  notification['sourceId'],
                                )]!,
                              ),
                            const SizedBox(height: 6),
                            Text(
                              _formatDate(notification['createdAt']),
                              style: Theme.of(context).textTheme.bodySmall
                                  ?.copyWith(
                                    color: JarvisColors.of(context).muted,
                                  ),
                            ),
                          ],
                        ),
                      ),
                      if (unread)
                        Container(
                          margin: const EdgeInsets.only(top: 6, left: 8),
                          width: 9,
                          height: 9,
                          decoration: BoxDecoration(
                            color: JarvisColors.of(context).accent,
                            shape: BoxShape.circle,
                          ),
                        ),
                    ],
                  ),
                ),
              ),
            );
          },
        );
}

/// Small caption above a run of rows in a grouped list.
class _GroupHeader extends StatelessWidget {
  const _GroupHeader(this.label);

  final String label;

  @override
  Widget build(BuildContext context) => ContentWidth(
    child: Padding(
      padding: const EdgeInsets.fromLTRB(4, 10, 0, 8),
      child: SizedBox(
        width: double.infinity,
        child: Text(
          label,
          style: Theme.of(context).textTheme.labelMedium?.copyWith(
            color: label == 'Overdue'
                ? JarvisColors.of(context).danger
                : JarvisColors.of(context).muted,
            letterSpacing: .3,
          ),
        ),
      ),
    ),
  );
}

class _PlaceAccessHint {
  const _PlaceAccessHint({required this.denied});

  final bool denied;
}

class _ShowMoreFinished {
  const _ShowMoreFinished(this.hidden);

  final int hidden;
}
