import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import 'ui/phosphor_icons.dart';

import 'approvals_screen.dart';
import 'features/chat/tool_catalog.dart';
import 'api/api_config.dart';
import 'features/coding/coding_run_detail_screen.dart';
import 'features/habits/habits_screen.dart';
import 'daily_briefing_screen.dart';
import 'features/devices/device_sensors.dart';
import 'features/devices/place_reminder_tracker.dart';
import 'features/review/weekly_review_screen.dart';
import 'notification_details_screen.dart';
import 'notification_routing.dart';
import 'schedule_format.dart';
import 'json_maps.dart';
import 'task_details_screen.dart';
import 'theme.dart';
import 'ui/jarvis_ui.dart';

part 'reminder_editor.dart';

enum RemindersTab { reminders, notifications }

class RemindersScreen extends StatefulWidget {
  const RemindersScreen({
    required this.http,
    this.onOpenConversation,
    this.initialTab = RemindersTab.reminders,
    this.startCreating = false,
    this.locate = readDeviceLocationSnapshot,
    this.locationAccess = _checkLocationAccess,
    this.openLocationSettings = Geolocator.openAppSettings,
    super.key,
  });

  static Future<LocationPermission?> _checkLocationAccess() async {
    if (kIsWeb) return null;
    try {
      return await Geolocator.checkPermission();
    } catch (_) {
      return null;
    }
  }

  /// The phone's location permission, or null where it does not apply.
  final Future<LocationPermission?> Function() locationAccess;

  final Future<bool> Function() openLocationSettings;

  final Dio http;

  /// Reads the phone's position (and asks for permission) for place reminders.
  final Future<({double? latitude, double? longitude, double? accuracy})>
  Function({bool requestPermission})
  locate;
  final RemindersTab initialTab;

  /// Opens the "new reminder" editor as soon as the page has settled.
  final bool startCreating;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<RemindersScreen> createState() => _RemindersScreenState();
}

class _RemindersScreenState extends State<RemindersScreen>
    with SingleTickerProviderStateMixin {
  late final TabController _tabs = TabController(
    length: 2,
    vsync: this,
    initialIndex: widget.initialTab.index,
  );
  List<Map<String, dynamic>> _reminders = [];
  List<Map<String, dynamic>> _notifications = [];
  // Pending approvals keyed by id, so an approval notification can be decided in place.
  Map<String, Map<String, dynamic>> _pendingApprovals = {};
  final Set<String> _decidingApprovals = {};
  bool _loading = true;
  bool _remindersFailed = false;
  bool _notificationsFailed = false;
  String? _error;
  int _requestRevision = 0;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.startCreating) afterRouteSettles(this, _createReminder);
    deviceTimeZoneLookup().then((zone) {
      if (mounted && zone != null) setState(() => _deviceZone = zone);
    });
  }

  Future<void> _load() async {
    if (!mounted) return;
    final revision = ++_requestRevision;
    setState(() {
      _loading = true;
      _error = null;
      _remindersFailed = false;
      _notificationsFailed = false;
    });
    try {
      var remindersFailed = false;
      var notificationsFailed = false;
      try {
        final reminders = await widget.http.get<dynamic>('/api/v1/reminders');
        if (mounted && revision == _requestRevision) {
          setState(() => _reminders = jsonMaps(reminders.data));
          unawaited(
            PlaceReminderTracker.instance.update(_reminders, http: widget.http),
          );
          if (hasPendingPlaceReminder(_reminders)) {
            final access = await widget.locationAccess();
            if (mounted) setState(() => _locationAccess = access);
          }
        }
      } on DioException {
        remindersFailed = true;
      } catch (_) {
        remindersFailed = true;
      }
      try {
        final notifications = await widget.http.get<dynamic>(
          '/api/v1/notifications',
        );
        if (mounted && revision == _requestRevision) {
          setState(() => _notifications = jsonMaps(notifications.data));
        }
      } on DioException {
        notificationsFailed = true;
      } catch (_) {
        notificationsFailed = true;
      }
      try {
        final approvals = await widget.http.get<dynamic>('/api/v1/approvals');
        if (mounted && revision == _requestRevision) {
          setState(
            () => _pendingApprovals = {
              for (final approval in jsonMaps(approvals.data))
                if (approval['status'] == 'pending' && jsonId(approval) != null)
                  jsonId(approval)!: approval,
            },
          );
        }
      } on DioException {
        // Approvals still open from the notification; this only hides the shortcut.
      } catch (_) {
        // Same: a malformed list never blocks the inbox.
      }
      if (mounted && revision == _requestRevision) {
        setState(() {
          _remindersFailed = remindersFailed;
          _notificationsFailed = notificationsFailed;
          _error = remindersFailed
              ? 'Jarvis could not load reminders.'
              : notificationsFailed
              ? 'Jarvis could not load notifications.'
              : null;
        });
      }
    } finally {
      if (mounted && revision == _requestRevision) {
        setState(() => _loading = false);
      }
    }
  }

  /// The zone a new reminder uses: the device's own, else the one from the
  /// briefing settings. [seedOwnerZone] is set when the owner has no zone on
  /// file yet, so the first reminder can save it for chat and briefings too.
  Future<String> _reminderTimeZone() async {
    final device = await deviceTimeZoneLookup();
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/briefings/daily',
      );
      _briefing = jsonObject(response.data);
    } on DioException {
      _briefing = null;
    } catch (_) {
      _briefing = null;
    }
    final saved = asJsonString(_briefing?['timeZoneId']);
    return device ?? saved ?? 'UTC';
  }

  Map<String, dynamic>? _briefing;

  /// Saves the device zone as the owner's zone (briefing stays off) when none
  /// was ever set, so reminders made in chat run on the same clock.
  Future<void> _seedOwnerZone(String zone) async {
    final briefing = _briefing;
    if (briefing == null || zone == 'UTC') return;
    if ((asJsonString(briefing['workflowId']) ?? '').isNotEmpty) return;
    try {
      await widget.http.put<dynamic>(
        '/api/v1/briefings/daily',
        data: {
          'enabled': false,
          'localTime': asJsonString(briefing['localTime']) ?? '08:00:00',
          'timeZoneId': zone,
        },
      );
    } on DioException {
      // The reminder already carries its own zone.
    } catch (_) {
      // Same.
    }
  }

  Future<void> _createReminder() async {
    final zoneLookup = _reminderTimeZone();
    final created = await showDialog<_NewReminder>(
      context: context,
      builder: (_) => _NewReminderDialog(
        timeZone: zoneLookup,
        places: savedPlaces(_reminders),
        locate: () => widget.locate(requestPermission: true),
      ),
    );
    if (created == null || !mounted) return;
    final timeZoneId = await zoneLookup;
    if (!mounted) return;
    if (created.place case final place?) {
      await _createPlaceReminder(created.title, place, timeZoneId);
      return;
    }

    final localDueAt = DateTime(
      created.date.year,
      created.date.month,
      created.date.day,
      created.time.hour,
      created.time.minute,
    );
    if (!localDueAt.isAfter(DateTime.now()) && created.recurrence == 'once') {
      _showError('Choose a time in the future.');
      return;
    }
    final localTime =
        '${created.time.hour.toString().padLeft(2, '0')}:${created.time.minute.toString().padLeft(2, '0')}:00';
    try {
      await widget.http.post(
        '/api/v1/reminders',
        data: {
          'title': created.title,
          'dueAt': localDueAt.toUtc().toIso8601String(),
          if (created.recurrence != 'once') 'recurrence': created.recurrence,
          if (created.recurrence == 'weekly') 'weekdays': created.weekdays,
          'timeZoneId': timeZoneId,
          'localTime': localTime,
        },
      );
      await _seedOwnerZone(timeZoneId);
      await _load();
      if (mounted) _showMessage('Reminder set.');
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The reminder service is unavailable. Try again shortly.'
            : firstProblemMessage(error.response?.data) ??
                  'Jarvis could not create that reminder.';
        _showError(message);
      }
    } catch (_) {
      if (mounted) _showError('Jarvis could not create that reminder.');
    }
  }

  Future<void> _createPlaceReminder(
    String title,
    ReminderPlace place,
    String timeZoneId,
  ) async {
    try {
      // Report where the phone is first, so the server knows whether you are
      // already there and waits for the next arrival instead of firing now.
      final here = await widget.locate(requestPermission: true);
      if (here.latitude != null && here.longitude != null) {
        try {
          await widget.http.post<void>(
            '/api/v1/devices/telemetry',
            data: {
              'latitude': here.latitude,
              'longitude': here.longitude,
              if (here.accuracy != null) 'accuracyMeters': here.accuracy,
            },
          );
        } catch (_) {
          // The tracker reports the next position anyway.
        }
      }
      await widget.http.post(
        '/api/v1/reminders',
        data: {
          'title': title,
          'timeZoneId': timeZoneId,
          'place': place.toJson(),
        },
      );
      await _load();
      if (!mounted) return;
      _showMessage(
        here.latitude == null
            ? 'Reminder set. Allow location for Jarvis so it can fire.'
            : '${place.label}, Jarvis will remind you.',
      );
    } on DioException catch (error) {
      if (mounted) {
        _showError(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not create that reminder.',
        );
      }
    } catch (_) {
      if (mounted) _showError('Jarvis could not create that reminder.');
    }
  }

  Future<void> _snooze(String? id, Duration by, {DateTime? until}) async {
    if (id == null) return;
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/reminders/$id/snooze',
        data: until != null
            ? {'until': until.toUtc().toIso8601String()}
            : {'minutes': by.inMinutes},
      );
      final dueAt = jsonDate(jsonObject(response.data)?['dueAt'], local: true);
      await _load();
      if (mounted && dueAt != null) {
        _showMessage('Snoozed until ${friendlyWhen(context, dueAt)}.');
      }
    } on DioException catch (error) {
      if (mounted) {
        _showError(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not snooze that reminder.',
        );
      }
    } catch (_) {
      if (mounted) _showError('Jarvis could not snooze that reminder.');
    }
  }

  Future<void> _markDone(Map<String, dynamic> reminder) async {
    final id = jsonId(reminder);
    if (id == null) return;
    try {
      await widget.http.post<dynamic>('/api/v1/reminders/$id/complete');
      await _load();
      if (mounted) _showMessage('Marked as done.');
    } on DioException {
      if (mounted) _showError('Jarvis could not update that reminder.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not update that reminder.');
    }
  }

  /// Tomorrow at 09:00 local time, the "later" choice in snooze menus.
  static DateTime _tomorrowMorning() {
    final now = DateTime.now();
    return DateTime(now.year, now.month, now.day + 1, 9);
  }

  Future<void> _cancelReminder(Map<String, dynamic> reminder) async {
    final confirmed = await showJarvisConfirm(
      context,
      title: 'Cancel reminder?',
      message: '“${reminder['title']}” will no longer notify you.',
      cancelLabel: 'Keep',
      confirmLabel: 'Cancel reminder',
      destructive: true,
      icon: PhosphorIconsRegular.bellSlash,
    );
    if (!confirmed) return;
    if (!mounted) return;
    final id = jsonId(reminder);
    if (id == null) return;
    try {
      await widget.http.delete('/api/v1/reminders/$id');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not cancel that reminder.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not cancel that reminder.');
    }
  }

  Future<void> _openChat(Map<String, dynamic> reminder) async {
    final opener = widget.onOpenConversation;
    if (opener == null) return;
    var conversationId = asJsonString(reminder['conversationId']);
    if (conversationId == null) {
      final id = jsonId(reminder);
      if (id == null) return;
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/reminders/$id',
        );
        conversationId = asJsonString(
          jsonObject(response.data)?['conversationId'],
        );
      } on DioException {
        if (mounted) _showError('Jarvis could not open that reminder chat.');
        return;
      } catch (_) {
        if (mounted) _showError('Jarvis could not open that reminder chat.');
        return;
      }
    }
    if (conversationId == null) {
      if (mounted) _showError('This reminder does not have a chat yet.');
      return;
    }
    await opener(conversationId);
  }

  Future<void> _markRead(Map<String, dynamic> notification) async {
    if (notification['readAt'] != null) return;
    final id = jsonId(notification);
    if (id == null) return;
    try {
      await widget.http.post('/api/v1/notifications/$id/read');
      if (mounted) await _load();
    } on DioException {
      if (mounted) _showError('Jarvis could not update that notification.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not update that notification.');
    }
  }

  Future<void> _decideApproval(String approvalId, bool approved) async {
    setState(() => _decidingApprovals.add(approvalId));
    try {
      // Resuming the paused agent run can take a while, so wait for it.
      await widget.http.post<dynamic>(
        '/api/v1/approvals/$approvalId/decision',
        data: {'approved': approved},
        options: longRunningOptions(),
      );
    } on DioException catch (error) {
      if (mounted) {
        _showError(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not record that decision.',
        );
      }
    } catch (_) {
      if (mounted) _showError('Jarvis could not record that decision.');
    }
    if (!mounted) return;
    setState(() => _decidingApprovals.remove(approvalId));
    await _load();
  }

  Widget _approvalActions(Map<String, dynamic> approval) {
    final id = jsonId(approval)!;
    final busy = _decidingApprovals.contains(id);
    final tool = describeTool(asJsonString(approval['toolName']) ?? '');
    return Padding(
      padding: const EdgeInsets.only(top: 10),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Wants to: ${tool.active.toLowerCase()}',
            style: Theme.of(context).textTheme.bodySmall?.copyWith(
              color: JarvisColors.of(context).ink,
              fontWeight: FontWeight.w500,
            ),
          ),
          const SizedBox(height: 10),
          Row(
            children: [
              OutlinedButton(
                onPressed: busy ? null : () => _decideApproval(id, false),
                style: OutlinedButton.styleFrom(minimumSize: const Size(0, 38)),
                child: const Text('Decline'),
              ),
              const SizedBox(width: 8),
              FilledButton.icon(
                onPressed: busy ? null : () => _decideApproval(id, true),
                style: FilledButton.styleFrom(minimumSize: const Size(0, 38)),
                icon: busy
                    ? const SizedBox.square(
                        dimension: 14,
                        child: CircularProgressIndicator(strokeWidth: 2),
                      )
                    : const Icon(PhosphorIconsRegular.check, size: 18),
                label: Text(busy ? 'Working…' : 'Approve'),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _snoozeActions(String? reminderId) => Padding(
    padding: const EdgeInsets.only(top: 10),
    child: Wrap(
      spacing: 8,
      runSpacing: 8,
      children: [
        OutlinedButton.icon(
          onPressed: () => _snooze(reminderId, const Duration(minutes: 10)),
          style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36)),
          icon: const Icon(
            PhosphorIconsRegular.clockCounterClockwise,
            size: 16,
          ),
          label: const Text('Snooze 10 min'),
        ),
        OutlinedButton(
          onPressed: () => _snooze(reminderId, const Duration(hours: 1)),
          style: OutlinedButton.styleFrom(minimumSize: const Size(0, 36)),
          child: const Text('1 hour'),
        ),
      ],
    ),
  );

  Future<void> _markAllRead() async {
    final unread = _notifications.where((item) => item['readAt'] == null);
    final ids = [for (final item in unread) ?jsonId(item)];
    if (ids.isEmpty) return;
    try {
      await Future.wait([
        for (final id in ids)
          widget.http.post('/api/v1/notifications/$id/read'),
      ]);
    } on DioException {
      if (mounted) _showError('Jarvis could not mark everything as read.');
    } catch (_) {
      if (mounted) _showError('Jarvis could not mark everything as read.');
    }
    if (mounted) await _load();
  }

  Future<void> _openNotification(Map<String, dynamic> notification) async {
    await _markRead(notification);
    if (!mounted) return;
    final type = asJsonString(notification['type']);
    final sourceId = asJsonString(notification['sourceId']);
    if (opensApprovalScreen(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => ApprovalsScreen(http: widget.http),
        ),
      );
    } else if (opensCodingRun(type) && sourceId != null) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) =>
              CodingRunDetailScreen(http: widget.http, runId: sourceId),
        ),
      );
    } else if (opensDailyBriefing(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => DailyBriefingScreen(http: widget.http),
        ),
      );
    } else if (opensWeeklyReview(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => WeeklyReviewScreen(http: widget.http),
        ),
      );
    } else if (opensHabits(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => HabitsScreen(http: widget.http),
        ),
      );
    } else if (opensTaskDetails(type) && sourceId != null) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) =>
              TaskDetailsScreen(http: widget.http, taskId: sourceId),
        ),
      );
    } else if ((type == 'reminder.due' || type == 'reminder.failed') &&
        widget.onOpenConversation != null &&
        sourceId != null) {
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/reminders/$sourceId',
        );
        final conversationId = asJsonString(
          jsonObject(response.data)?['conversationId'],
        );
        if (conversationId != null) {
          await widget.onOpenConversation!(conversationId);
        } else if (mounted && opensNotificationDetails(type)) {
          await Navigator.of(context).push<void>(
            MaterialPageRoute<void>(
              builder: (_) => NotificationDetailsScreen(
                http: widget.http,
                notificationType: type!,
                sourceId: sourceId,
              ),
            ),
          );
        }
      } on DioException {
        if (mounted && opensNotificationDetails(type)) {
          await Navigator.of(context).push<void>(
            MaterialPageRoute<void>(
              builder: (_) => NotificationDetailsScreen(
                http: widget.http,
                notificationType: type!,
                sourceId: sourceId,
              ),
            ),
          );
        }
      }
    } else if (type == 'automation.notification' &&
        widget.onOpenConversation != null &&
        sourceId != null) {
      try {
        final response = await widget.http.get<dynamic>(
          '/api/v1/automations/$sourceId',
        );
        final conversationId = asJsonString(
          jsonObject(response.data)?['conversationId'],
        );
        if (conversationId != null) {
          await widget.onOpenConversation!(conversationId);
        }
      } on DioException {
        if (mounted) _showError('Jarvis could not open that automation.');
      }
    } else if (sourceId != null && opensNotificationDetails(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => NotificationDetailsScreen(
            http: widget.http,
            notificationType: type!,
            sourceId: sourceId,
          ),
        ),
      );
    } else {
      return;
    }
    if (mounted) await _load();
  }

  void _showError(String message) {
    ScaffoldMessenger.of(
      context,
    ).showSnackBar(SnackBar(content: Text(message)));
  }

  void _showMessage(String message) => _showError(message);

  String _formatDate(dynamic raw) {
    final date = jsonDate(raw, local: true);
    return date == null ? '' : friendlyWhen(context, date);
  }

  @override
  void dispose() {
    _tabs.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Reminders'),
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
        preferredSize: const Size.fromHeight(56),
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
              child: ContentWidth(child: _reminderCard(reminder)),
            );
          },
        );

  bool _showAllFinished = false;
  LocationPermission? _locationAccess;
  String? _deviceZone;
  static const _finishedPreview = 5;

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
