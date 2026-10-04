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
    Future<void> openConversation(String id) => _presentConversation(id);
    if (_useCommandPalette(context)) {
      await showJarvisCommandPalette(
        context,
        http: _http,
        onConversation: openConversation,
        onUtility: _openUtility,
      );
      return;
    }
    await Navigator.of(context).push<void>(
      MaterialPageRoute<void>(
        builder: (_) => SearchScreen(
          http: _http,
          onConversation: openConversation,
          onUtility: _openUtility,
        ),
      ),
    );
  }

  Future<void> _openSearchRouteFromNotification(Map<String, dynamic> data) async {
    final route = searchRouteFromNotification(data);
    if (route == null || !mounted || _signedOut) return;
    await navigateSearchRoute(
      context,
      http: _http,
      route: route,
      onConversation: (id) => _presentConversation(id),
      onUtility: _openUtility,
    );
  }
}
