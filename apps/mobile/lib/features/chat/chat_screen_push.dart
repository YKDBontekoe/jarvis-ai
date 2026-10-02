part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenPush on _ChatScreenController {
  void _attachPushListeners() {
    if (Firebase.apps.isEmpty) return;
    _pushOpenedSubscription ??= FirebaseMessaging.onMessageOpenedApp.listen(
      _onPushOpened,
    );
    _pushForegroundSubscription ??= FirebaseMessaging.onMessage.listen(
      _onForegroundPush,
    );
  }

  Future<void> _enablePush() async {
    if (_signedOut || _signingOut || Firebase.apps.isEmpty || kIsWeb) return;
    try {
      final messaging = FirebaseMessaging.instance;
      final settings = await messaging.requestPermission(
        alert: true,
        badge: true,
        sound: true,
      );
      if (settings.authorizationStatus == AuthorizationStatus.denied) return;
      if (_signedOut || _signingOut) return;
      _attachPushListeners();
      _pushTokenSubscription ??= messaging.onTokenRefresh.listen((token) {
        unawaited(_registerPushToken(token).catchError((_) {}));
      });
      final token = await messaging.getToken();
      if (token != null && !_signedOut && !_signingOut) {
        await _registerPushToken(token);
      }
    } on FirebaseException catch (error) {
      if (mounted) {
        setState(
          () => _error = 'Push notifications unavailable: ${error.code}.',
        );
      }
    } on DioException {
      // The API may be offline while Firebase initialization and chat recovery continue.
    } catch (_) {
      // Push stays optional; chat continues if token registration fails unexpectedly.
    }
  }

  Future<void> _registerPushToken(String token) async {
    if (_signedOut || _signingOut) return;
    try {
      final platform = defaultTargetPlatform == TargetPlatform.iOS
          ? 'ios'
          : 'android';
      final previous = _pushToken;
      await _http.put<void>(
        '/api/v1/push-devices',
        data: {'token': token, 'platform': platform},
      );
      if (_signedOut || _signingOut) {
        try {
          await _http.delete<void>(
            '/api/v1/push-devices',
            data: {'token': token},
          );
        } catch (_) {
          // Sign-out already dropped local state; stale server rows expire at Firebase.
        }
        return;
      }
      if (previous != null && previous != token) {
        try {
          await _http.delete<void>(
            '/api/v1/push-devices',
            data: {'token': previous},
          );
        } catch (_) {
          // The new token is registered; Firebase expires the previous one.
        }
      }
      _pushToken = token;
    } on DioException {
      // Token registration is best-effort; chat continues without push.
    } catch (_) {
      // Same if Firebase or decoding throws unexpectedly.
    }
  }

  void _onForegroundPush(RemoteMessage message) {
    if (!mounted || _signedOut) return;
    if (message.data['type'] == 'task.completed' ||
        message.data['type'] == 'task.failed' ||
        message.data['type'] == 'approval.required') {
      setState(() => _homeRevision++);
    }
    final notificationId = asJsonString(message.data['notificationId']);
    if (notificationId != null &&
        !_shownPushNotifications.add(notificationId)) {
      return;
    }
    if (!mounted) return;
    final notification = message.notification;
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(notification?.title ?? 'Jarvis needs your attention.'),
        action: SnackBarAction(
          label: 'Open',
          onPressed: () => _handlePushPayload(message.data),
        ),
      ),
    );
  }

  /// A tap on a notification or on one of its buttons. Done and Snooze run in
  /// place; everything else, including Open on an approval, opens Jarvis.
  void _onPushOpened(RemoteMessage message) {
    final action = notificationQuickAction(message.actionIdentifier);
    if (action == null) {
      _handlePushPayload(message.data);
      return;
    }
    final notificationId = asJsonString(message.data['notificationId']);
    if (notificationId == null || notificationId.isEmpty) return;
    // Firebase can report the same button press as both the launch message
    // and an opened-app event.
    if (!_handledPushActions.add('$notificationId:$action')) return;
    unawaited(_runPushQuickAction(message.data, notificationId, action));
  }

  Future<void> _runPushQuickAction(
    Map<String, dynamic> data,
    String notificationId,
    String action,
  ) async {
    if (_signedOut) return;
    String message;
    var failed = false;
    try {
      message = await runNotificationQuickAction(
        _http,
        notificationId: notificationId,
        action: action,
      );
    } catch (_) {
      failed = true;
      message = notificationQuickActionFailure(action);
    }
    if (!mounted || _signedOut) return;
    setState(() => _homeRevision++);
    unawaited(_refreshUnreadNotifications());
    ScaffoldMessenger.of(context).showSnackBar(
      SnackBar(
        content: Text(message),
        action: failed
            ? SnackBarAction(
                label: 'Open',
                onPressed: () => _handlePushPayload(data),
              )
            : null,
      ),
    );
  }

  void _handlePushPayload(Map<String, dynamic> data) {
    if (!mounted || _signedOut) return;
    unawaited(_markPushNotificationRead(asJsonString(data['notificationId'])));
    if (searchRouteFromNotification(data) != null) {
      unawaited(_openSearchRouteFromNotification(data));
      return;
    }
    final type = asJsonString(data['type']);
    final sourceId = asJsonString(data['sourceId']);
    if (opensApprovalScreen(type)) {
      _openUtility('approvals');
      return;
    }
    if (opensDailyBriefing(type)) {
      _openUtility('briefing');
      return;
    }
    if (opensWeeklyReview(type)) {
      _openUtility('weekly-review');
      return;
    }
    if (opensTaskDetails(type) && sourceId != null) {
      unawaited(
        _openPushedDetail(TaskDetailsScreen(http: _http, taskId: sourceId)),
      );
      return;
    }
    if (opensCodingRun(type) && sourceId != null) {
      unawaited(
        _openPushedDetail(CodingRunDetailScreen(http: _http, runId: sourceId)),
      );
      return;
    }
    if (sourceId == null || !opensNotificationDetails(type)) {
      _openUtility('reminders');
      return;
    }
    unawaited(
      _openPushedDetail(
        NotificationDetailsScreen(
          http: _http,
          notificationType: type!,
          sourceId: sourceId,
        ),
      ),
    );
  }

  Future<void> _markPushNotificationRead(String? notificationId) async {
    if (notificationId == null || notificationId.isEmpty) return;
    try {
      await _http.post('/api/v1/notifications/$notificationId/read');
    } catch (_) {
      // The unread badge refreshes the next time Reminders is opened.
    }
  }

  Future<void> _openPushedDetail(Widget page) async {
    await Navigator.of(
      context,
    ).push<void>(MaterialPageRoute<void>(builder: (_) => page));
    if (mounted && !_signedOut && !_signingOut) {
      setState(() => _homeRevision++);
    }
  }
}
