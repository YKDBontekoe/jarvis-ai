import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:geolocator/geolocator.dart';

import '../../ui/phosphor_icons.dart';

import '../approvals/approvals_screen.dart';
import '../chat/tool_catalog.dart';
import '../../api/api_config.dart';
import '../coding/coding_run_detail_screen.dart';
import '../automations/automations_screen.dart';
import '../habits/habits_screen.dart';
import '../briefing/daily_briefing_screen.dart';
import '../devices/device_sensors.dart';
import '../devices/place_reminder_tracker.dart';
import '../review/weekly_review_screen.dart';
import '../notifications/notification_details_screen.dart';
import '../notifications/notification_routing.dart';
import '../../schedule_format.dart';
import '../../json_maps.dart';
import '../tasks/task_details_screen.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';

part 'reminder_editor.dart';
part 'reminders_list.dart';

enum RemindersTab { reminders, notifications }

class RemindersScreen extends StatefulWidget {
  const RemindersScreen({
    required this.http,
    this.onOpenConversation,
    this.initialTab = RemindersTab.reminders,
    this.startCreating = false,
    this.onCreateDone,
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

  /// Called after a [startCreating] editor closes: with a confirmation when
  /// it saved, or null when it was cancelled.
  final ValueChanged<String?>? onCreateDone;
  final Future<void> Function(String conversationId)? onOpenConversation;

  @override
  State<RemindersScreen> createState() => _RemindersScreenState();
}

/// State, loading and actions for [RemindersScreen]. The list and card UI is
/// [_RemindersScreenState] in reminders_list.dart.
abstract class _RemindersController extends State<RemindersScreen>
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
  bool _showAllFinished = false;
  LocationPermission? _locationAccess;
  String? _deviceZone;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.startCreating) afterRouteSettles(this, _quickCreate);
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

  /// The "new reminder" editor opened straight from chat: hands control back
  /// once it is saved or cancelled, and stays here if saving failed.
  Future<void> _quickCreate() async {
    final created = await _createReminder();
    if (created == false || !mounted) return;
    widget.onCreateDone?.call(created == true ? 'Reminder set' : null);
  }

  /// Returns true when saved, null when cancelled, false when it failed
  /// (the editor's caller stays put so nothing typed is lost).
  Future<bool?> _createReminder() async {
    final zoneLookup = _reminderTimeZone();
    final created = await showDialog<_NewReminder>(
      context: context,
      builder: (_) => _NewReminderDialog(
        timeZone: zoneLookup,
        places: savedPlaces(_reminders),
        locate: () => widget.locate(requestPermission: true),
      ),
    );
    if (created == null || !mounted) return null;
    final timeZoneId = await zoneLookup;
    if (!mounted) return null;
    if (created.place case final place?) {
      return _createPlaceReminder(created.title, place, timeZoneId);
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
      return false;
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
      return true;
    } on DioException catch (error) {
      if (mounted) {
        final message = error.response?.statusCode == 503
            ? 'The reminder service is unavailable. Try again shortly.'
            : firstProblemMessage(error.response?.data) ??
                  'Jarvis could not create that reminder.';
        _showError(message);
      }
      return false;
    } catch (_) {
      if (mounted) _showError('Jarvis could not create that reminder.');
      return false;
    }
  }

  Future<bool> _createPlaceReminder(
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
      if (!mounted) return true;
      _showMessage(
        here.latitude == null
            ? 'Reminder set. Allow location for Jarvis so it can fire.'
            : '${place.label}, Jarvis will remind you.',
      );
      return true;
    } on DioException catch (error) {
      if (mounted) {
        _showError(
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not create that reminder.',
        );
      }
      return false;
    } catch (_) {
      if (mounted) _showError('Jarvis could not create that reminder.');
      return false;
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
    } else if (opensRoutineSuggestions(type)) {
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => AutomationsScreen(http: widget.http),
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
}

/// Tomorrow at 09:00 local time, the "later" choice in snooze menus.
DateTime _tomorrowMorning() {
  final now = DateTime.now();
  return DateTime(now.year, now.month, now.day + 1, 9);
}

/// How many finished reminders show before "Show more".
const _finishedPreview = 5;
