part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

/// "Summarize this chat": a recap sheet with a reminder per action item.
mixin _ChatScreenSummary on _ChatScreenController {
  bool get _canSummarize =>
      _conversationId != null &&
      _entries
              .whereType<MessageEntry>()
              .where((entry) => entry.id != null)
              .length >=
          2;

  void _showConversationSummary() {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    _dismissKeyboard();
    unawaited(
      showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        showDragHandle: true,
        builder: (_) => ConversationSummarySheet(
          load: () => _loadConversationSummary(conversationId),
          onRemind: _remindFromSummary,
        ),
      ),
    );
  }

  Future<ConversationSummaryData> _loadConversationSummary(
    String conversationId,
  ) async {
    try {
      final response = await _http.post<dynamic>(
        '/api/v1/conversations/$conversationId/summary',
        options: Options(receiveTimeout: const Duration(seconds: 120)),
      );
      final summary = ConversationSummaryData.fromJson(response.data);
      if (summary.summary.isEmpty) {
        throw const ConversationSummaryException(
          'Jarvis could not summarize this conversation.',
        );
      }
      return summary;
    } on DioException catch (error) {
      throw ConversationSummaryException(switch (error.response?.statusCode) {
        409 => 'This conversation is too short to summarize.',
        503 => 'Jarvis could not summarize this right now. Try again shortly.',
        _ when error.response == null =>
          'Jarvis is unreachable. Check your connection and try again.',
        _ =>
          firstProblemMessage(error.response?.data) ??
              'Jarvis could not summarize this conversation.',
      });
    }
  }

  /// Picks a date and time, then creates a one-off reminder for [item].
  Future<bool> _remindFromSummary(String item) async {
    final now = DateTime.now();
    final tomorrow = now.add(const Duration(days: 1));
    final date = await showDatePicker(
      context: context,
      initialDate: tomorrow,
      firstDate: DateTime(now.year, now.month, now.day),
      lastDate: now.add(const Duration(days: 365 * 5)),
      helpText: 'Remind me on',
    );
    if (date == null || !mounted) return false;
    final time = await showTimePicker(
      context: context,
      initialTime: const TimeOfDay(hour: 9, minute: 0),
      helpText: 'Remind me at',
    );
    if (time == null || !mounted) return false;
    final local = DateTime(
      date.year,
      date.month,
      date.day,
      time.hour,
      time.minute,
    );
    final messenger = ScaffoldMessenger.maybeOf(context);
    if (!local.isAfter(DateTime.now())) {
      messenger?.showSnackBar(
        const SnackBar(content: Text('Choose a time in the future.')),
      );
      return false;
    }
    final timeZoneId = await deviceTimeZoneLookup() ?? 'UTC';
    final title = item.length > 300 ? item.substring(0, 300) : item;
    try {
      await _http.post<dynamic>(
        '/api/v1/reminders',
        data: {
          'title': title,
          'dueAt': local.toUtc().toIso8601String(),
          'timeZoneId': timeZoneId,
          'localTime':
              '${time.hour.toString().padLeft(2, '0')}:${time.minute.toString().padLeft(2, '0')}:00',
        },
      );
      messenger?.showSnackBar(const SnackBar(content: Text('Reminder set.')));
      return true;
    } on DioException catch (error) {
      messenger?.showSnackBar(
        SnackBar(
          content: Text(
            firstProblemMessage(error.response?.data) ??
                'Jarvis could not create that reminder.',
          ),
        ),
      );
      return false;
    }
  }
}
