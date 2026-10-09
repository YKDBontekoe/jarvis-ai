part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

/// Computer use: step screenshots from the sandbox desktop, the live view,
/// and taking over from Jarvis or handing control back.
mixin _ChatScreenComputer on _ChatScreenController {
  /// A step's screenshot, fetched once per session (cached with the photos).
  Future<Uint8List?> _loadComputerScreenshot(String sessionId, int ordinal) =>
      _photoBytes.putIfAbsent('computer:$sessionId:$ordinal', () async {
        try {
          final response = await _http.get<List<int>>(
            '/api/v1/browser-sessions/$sessionId/steps/$ordinal/screenshot',
            options: Options(responseType: ResponseType.bytes),
          );
          final data = response.data;
          return data == null ? null : Uint8List.fromList(data);
        } catch (_) {
          _photoBytes.remove('computer:$sessionId:$ordinal');
          return null;
        }
      });

  /// Opens the live desktop. With [takeOver], Jarvis pauses its computer
  /// tools and the view accepts mouse and keyboard input.
  Future<void> _watchComputer(
    BrowserSessionEntry session, {
    bool takeOver = false,
  }) async {
    try {
      if (takeOver) {
        await _http.post<void>(
          '/api/v1/browser-sessions/${session.id}/control',
          data: {'mode': 'user'},
        );
        if (mounted) setState(() => _setComputerControl(session.id, 'user'));
      }
      final response = await _http.post<dynamic>(
        '/api/v1/browser-sessions/${session.id}/view',
      );
      final viewerUrl = asJsonString(
        (response.data as Map<String, dynamic>?)?['viewerUrl'],
      );
      if (viewerUrl == null) throw StateError('No viewer URL.');
      final url = Uri.parse(apiBaseUrl).resolve(
        takeOver || session.userHasControl
            ? '$viewerUrl&viewOnly=0'
            : viewerUrl,
      );
      // The viewer authenticates with a one-time ticket in the URL, so it
      // opens in an in-app browser tab (a new tab on the web).
      final opened = await launchUrl(
        url,
        mode: kIsWeb ? LaunchMode.platformDefault : LaunchMode.inAppBrowserView,
      );
      if (!opened) _showComputerNotice('Could not open the live view.');
    } on DioException {
      _showComputerNotice('The computer session is no longer available.');
    } catch (_) {
      _showComputerNotice('Could not open the live view.');
    }
  }

  Future<void> _handBackComputer(BrowserSessionEntry session) async {
    try {
      await _http.post<void>(
        '/api/v1/browser-sessions/${session.id}/control',
        data: {'mode': 'agent'},
      );
      if (mounted) setState(() => _setComputerControl(session.id, 'agent'));
    } catch (_) {
      _showComputerNotice('Could not hand the computer back to Jarvis.');
    }
  }

  void _setComputerControl(String sessionId, String mode) {
    final index = _entries.indexWhere(
      (entry) => entry is BrowserSessionEntry && entry.id == sessionId,
    );
    if (index < 0) return;
    _entries[index] = (_entries[index] as BrowserSessionEntry).copyWith(
      controlMode: mode,
    );
  }

  void _showComputerNotice(String message) {
    if (!mounted) return;
    ScaffoldMessenger.maybeOf(context)
      ?..hideCurrentSnackBar()
      ..showSnackBar(SnackBar(content: Text(message)));
  }
}
