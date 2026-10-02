part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenSearch on _ChatScreenController {
  bool _useCommandPalette(BuildContext context) {
    final width = MediaQuery.sizeOf(context).width;
    return width >= _wideLayoutWidth || kIsWeb;
  }

  Future<void> _openSearch(BuildContext context) async {
    if (_signedOut || _signingOut) return;
    _dismissKeyboard();
    Future<void> openConversation(String id) => _openConversation(id);
    Future<void> ask(String prompt) async {
      if (_busy || _hasPendingApproval) {
        ScaffoldMessenger.of(this.context).showSnackBar(
          const SnackBar(
            content: Text('Finish or stop the current request first.'),
          ),
        );
        return;
      }
      _closeUtilityPane();
      if (_selectedDestination != 0) _selectDestination(0);
      _showTranscript();
      final sent = await _send(prompt);
      if (!sent && mounted) {
        ScaffoldMessenger.of(this.context).showSnackBar(
          const SnackBar(
            content: Text(
              'Could not start that request. Check your connection and try again.',
            ),
          ),
        );
      }
    }

    if (_useCommandPalette(context)) {
      await showJarvisCommandPalette(
        context,
        http: _http,
        onConversation: openConversation,
        onUtility: _openUtility,
        onAsk: ask,
      );
      return;
    }
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => SearchScreen(
          http: _http,
          onConversation: openConversation,
          onUtility: _openUtility,
          onAsk: ask,
        ),
      ),
    );
  }

  Future<void> _openSearchRouteFromNotification(
    Map<String, dynamic> data,
  ) async {
    final route = searchRouteFromNotification(data);
    if (route == null || !mounted || _signedOut) return;
    await navigateSearchRoute(
      context,
      http: _http,
      route: route,
      onConversation: (id) => _openConversation(id),
      onUtility: _openUtility,
    );
  }
}
