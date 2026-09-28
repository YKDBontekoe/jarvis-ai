part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenTranscript on _ChatScreenController {
  Future<void> _cancelActiveRun() async {
    final conversationId = _conversationId;
    _stopRequested = true;
    _finishRemoteQuery();
    _runCancel?.cancel();
    if (mounted) {
      setState(() {
        _removePlaceholder();
        _settleToolRuns();
        final index = _entries.lastIndexWhere(
          (entry) => entry is MessageEntry && entry.isUser,
        );
        if (index >= 0) {
          _entries[index] = (_entries[index] as MessageEntry).copyWith(
            failed: true,
          );
        }
      });
    }
    if (conversationId != null) await _cancelServerRun(conversationId);
  }

  Future<void> _handleDeviceInvoke(
    String invokeId,
    String capability,
    Map<Object?, Object?>? event,
  ) async {
    final outcome = await performDeviceCapability(
      capability: capability,
      event: event,
      mounted: mounted,
      context: context,
    );
    final result = outcome.result;
    final error = outcome.error;
    try {
      await _http.post<void>(
        '/api/v1/devices/invoke/$invokeId/result',
        data: {'result': result, 'error': error},
      );
    } on DioException {
      try {
        await _hub?.invoke(
          'CompleteDeviceInvoke',
          args: [invokeId, result ?? '', error ?? ''],
        );
      } catch (_) {}
    } catch (_) {}
  }

  int get _placeholderIndex {
    if (_entries.isEmpty) return -1;
    final last = _entries.last;
    return last is MessageEntry &&
            !last.isUser &&
            last.pending &&
            last.content.isEmpty
        ? _entries.length - 1
        : -1;
  }

  void _removePlaceholder() {
    final index = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    if (index < 0) return;
    final message = _entries[index] as MessageEntry;
    if (message.content.isEmpty) {
      _entries.removeAt(index);
    } else {
      _entries[index] = message.copyWith(pending: false);
    }
  }

  void _appendDelta(String delta) => appendAssistantDelta(_entries, delta);

  void _completeAssistant(String content, {String? id}) {
    final index = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && !entry.isUser && entry.pending,
    );
    if (content.trim().isEmpty) {
      if (index >= 0) {
        final pending = _entries[index] as MessageEntry;
        if (pending.content.isEmpty) {
          _entries.removeAt(index);
        } else {
          _entries[index] = pending.copyWith(pending: false);
        }
      }
      _settleToolRuns();
      return;
    }
    final message = MessageEntry(role: 'assistant', content: content, id: id);
    if (index >= 0) {
      _entries[index] = message;
    } else {
      final last = _entries.isEmpty ? null : _entries.last;
      if (last is MessageEntry && !last.isUser && last.content == content) {
        if (last.id == null && id != null) {
          _entries[_entries.length - 1] = last.copyWith(id: id);
        }
        _settleToolRuns();
        return;
      }
      _entries.add(message);
    }
    _settleToolRuns();
  }

  void _toolEvent(String tool, {bool? success}) {
    final runIndex = _entries.lastIndexWhere((entry) => entry is ToolRunEntry);
    final lastUser = _entries.lastIndexWhere(
      (entry) => entry is MessageEntry && entry.isUser,
    );
    final lastApproval = _entries.lastIndexWhere(
      (entry) => entry is ApprovalEntry,
    );
    final current =
        runIndex > lastUser && runIndex > lastApproval && runIndex >= 0
        ? _entries[runIndex] as ToolRunEntry
        : null;
    final updated = success == null
        ? (current ?? const ToolRunEntry([])).started(tool)
        : (current ?? const ToolRunEntry([])).finished(tool, success: success);
    if (current != null) {
      _entries[runIndex] = updated;
    } else {
      final placeholder = _placeholderIndex;
      if (placeholder >= 0) {
        _entries.insert(placeholder, updated);
      } else {
        _entries.add(updated);
      }
    }
  }

  void _settleToolRuns() {
    for (var i = 0; i < _entries.length; i++) {
      final entry = _entries[i];
      if (entry is ToolRunEntry && entry.running) _entries[i] = entry.settle();
    }
  }

  void _settleSubmittingApprovals({ApprovalStatus? fallback}) {
    for (var i = 0; i < _entries.length; i++) {
      final entry = _entries[i];
      if (entry is! ApprovalEntry) continue;
      final resolved = resolveSubmittingApproval(entry, fallback: fallback);
      if (!identical(resolved, entry)) _entries[i] = resolved;
    }
  }

  void _addApprovals(Iterable<ApprovalEntry> approvals) {
    final placeholder = _placeholderIndex;
    if (placeholder >= 0) _entries.removeAt(placeholder);
    _settleToolRuns();
    for (final approval in approvals) {
      final existing = _entries.indexWhere(
        (entry) => entry is ApprovalEntry && entry.id == approval.id,
      );
      if (existing >= 0) {
        final current = _entries[existing] as ApprovalEntry;
        if (current.status == ApprovalStatus.submitting) {
          if (approval.retry) {
            _entries[existing] = current.copyWith(
              status: ApprovalStatus.failed,
              decision: approval.decision ?? current.decision,
            );
          }
          continue;
        }
        if (current.status != ApprovalStatus.pending &&
            approval.status == ApprovalStatus.pending &&
            !approval.retry) {
          continue;
        }
        _entries[existing] = approval;
      } else {
        _entries.add(approval);
      }
    }
  }

  Future<void> _syncConversationApprovals() async {
    final conversationId = _conversationId;
    if (conversationId == null) return;
    final approvals = await _loadConversationApprovals(conversationId);
    if (approvals == null ||
        !mounted ||
        _signedOut ||
        _signingOut ||
        _conversationId != conversationId) {
      return;
    }
    setState(() {
      _entries.removeWhere((entry) {
        if (entry is! ApprovalEntry) return false;
        if (entry.status != ApprovalStatus.pending &&
            entry.status != ApprovalStatus.failed) {
          return false;
        }
        return !approvals.any((approval) => approval.id == entry.id);
      });
      for (var i = 0; i < _entries.length; i++) {
        final entry = _entries[i];
        if (entry is! ApprovalEntry ||
            entry.status != ApprovalStatus.submitting) {
          continue;
        }
        if (approvals.any((approval) => approval.id == entry.id)) continue;
        _entries[i] = resolveSubmittingApproval(entry);
      }
      _addApprovals(approvals);
    });
  }
}
