import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import 'search_models.dart';

// These are also the client allowlist for server-provided utility routes.
const navigationTools = <String, String>{
  'today': 'Today',
  'whatsapp': 'WhatsApp',
  'tasks': 'Tasks',
  'reminders': 'Reminders',
  'memory': 'Memory',
  'people': 'People',
  'journal': 'Journal',
  'expenses': 'Expenses',
  'habits': 'Habits',
  'projects': 'Projects',
  'files': 'Files',
  'approvals': 'Approvals',
  'integrations': 'Connected apps',
  'channels': 'Messaging accounts',
  'weekly-review': 'Weekly review',
  'automations': 'Automations',
  'skills': 'Skills',
  'coding': 'Coding runs',
};

class NavigationAction {
  const NavigationAction({
    required this.label,
    required this.description,
    required this.route,
  });
  final String label;
  final String description;
  final SearchRouteTarget route;
  static NavigationAction? fromJson(Map<String, dynamic> json) {
    final route = SearchRouteTarget.fromJson(json['route']);
    final label = asJsonString(json['label']);
    if (route == null || label == null) return null;
    return NavigationAction(
      label: label,
      description: asJsonString(json['description']) ?? '',
      route: route,
    );
  }
}

/// Explicit submission runs inference; typing still uses the ordinary search.
/// Cancelling or editing a request invalidates its result, including late model responses.
class IntentNavigationController extends ChangeNotifier {
  IntentNavigationController(this.http);
  final Dio http;
  var loading = false;
  String message = 'A good place to start';
  String query = '';
  List<NavigationAction> actions = const [];
  String? error;
  int _revision = 0;
  bool _disposed = false;
  CancelToken? _request;
  List<NavigationAction> _suggestions = const [];

  Future<void> loadSuggestions() async {
    try {
      final response = await http.get<dynamic>('/api/v1/navigation');
      if (_disposed) return;
      _suggestions = _readActions(response.data);
      if (query.isEmpty && !loading) {
        actions = _suggestions;
        notifyListeners();
      }
    } catch (_) {
      // Manual destinations and ordinary search remain available on older/offline servers.
    }
  }

  void changeQuery(String text) {
    _request?.cancel();
    ++_revision;
    query = text.trim();
    loading = false;
    error = null;
    actions = query.isEmpty ? _suggestions : const [];
    message = query.isEmpty ? 'A good place to start' : '';
    notifyListeners();
  }

  Future<void> resolve(String text) async {
    changeQuery(text);
    if (query.isEmpty) return;
    if (query.length > 1000) {
      error = 'Keep your request under 1,000 characters.';
      notifyListeners();
      return;
    }
    final revision = _revision;
    _request = CancelToken();
    loading = true;
    notifyListeners();
    try {
      final response = await http.post<dynamic>(
        '/api/v1/navigation/resolve',
        data: {'request': query},
        cancelToken: _request,
        options: Options(receiveTimeout: const Duration(seconds: 30)),
      );
      if (_disposed || revision != _revision) return;
      actions = _readActions(response.data);
      message =
          asJsonString(jsonObject(response.data)?['message']) ??
          'Your next step';
    } catch (_) {
      if (_disposed || revision != _revision) return;
      error =
          'Could not work out the next step. Try again, or browse the tools below.';
    } finally {
      if (!_disposed && revision == _revision) {
        loading = false;
        notifyListeners();
      }
    }
  }

  static List<NavigationAction> _readActions(Object? data) => [
    for (final item in jsonMaps(jsonObject(data)?['actions']))
      ?NavigationAction.fromJson(item),
  ];

  @override
  void dispose() {
    _disposed = true;
    _request?.cancel();
    super.dispose();
  }
}

class IntentActions extends StatelessWidget {
  const IntentActions({
    required this.controller,
    required this.onOpen,
    required this.onExample,
    super.key,
  });
  final IntentNavigationController controller;
  final ValueChanged<SearchRouteTarget> onOpen;
  final ValueChanged<String> onExample;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final theme = Theme.of(context);
    return Padding(
      padding: const EdgeInsets.fromLTRB(8, 8, 8, 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          if (controller.query.isEmpty) ...[
            Text(
              'What would you like to do?',
              style: JarvisType.serifOf(context).copyWith(fontSize: 28),
            ),
            const SizedBox(height: 8),
            Text(
              'Tell Jarvis what you need, or find something you saved.',
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 8,
              runSpacing: 8,
              children: [
                for (final request in [
                  'What needs my attention?',
                  'Help me reply to someone',
                ])
                  ActionChip(
                    label: Text(request),
                    onPressed: () => onExample(request),
                  ),
              ],
            ),
            const SizedBox(height: 16),
          ],
          if (controller.loading)
            const Padding(
              padding: EdgeInsets.symmetric(vertical: 16),
              child: Row(
                children: [
                  SizedBox.square(
                    dimension: 16,
                    child: CircularProgressIndicator(strokeWidth: 2),
                  ),
                  SizedBox(width: 12),
                  Expanded(child: Text('Finding the right next step…')),
                ],
              ),
            ),
          if (controller.error != null)
            InlineNotice(message: controller.error!),
          if (controller.query.isNotEmpty &&
              !controller.loading &&
              controller.actions.isEmpty &&
              controller.error == null)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: FilledButton.icon(
                onPressed: () => controller.resolve(controller.query),
                icon: const Icon(PhosphorIconsRegular.sparkle, size: 17),
                label: const Text('Find the next step'),
              ),
            ),
          if (controller.actions.isNotEmpty) ...[
            Text(
              controller.message,
              style: theme.textTheme.labelMedium?.copyWith(color: colors.muted),
            ),
            const SizedBox(height: 8),
            for (final action in controller.actions)
              SurfaceCard(
                margin: const EdgeInsets.only(bottom: 8),
                padding: const EdgeInsets.all(14),
                onTap: () => onOpen(action.route),
                child: Row(
                  children: [
                    Icon(
                      action.route.kind == 'whatsapp_chat'
                          ? PhosphorIconsRegular.whatsappLogo
                          : PhosphorIconsRegular.arrowUpRight,
                      size: 20,
                      color: colors.inkSoft,
                    ),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(action.label, style: theme.textTheme.titleSmall),
                          const SizedBox(height: 3),
                          Text(
                            action.description,
                            style: theme.textTheme.bodySmall,
                            maxLines: 2,
                            overflow: TextOverflow.ellipsis,
                          ),
                        ],
                      ),
                    ),
                  ],
                ),
              ),
          ],
          ExpansionTile(
            key: const Key('navigation-browse-tools'),
            trailing: const Icon(PhosphorIconsRegular.caretDown, size: 16),
            tilePadding: EdgeInsets.zero,
            title: Text('Browse tools', style: theme.textTheme.bodySmall),
            childrenPadding: const EdgeInsets.only(bottom: 12),
            children: [
              Align(
                alignment: Alignment.centerLeft,
                child: Wrap(
                  spacing: 8,
                  runSpacing: 8,
                  children: [
                    for (final tool in navigationTools.entries)
                      ActionChip(
                        label: Text(tool.value),
                        onPressed: () => onOpen(
                          SearchRouteTarget(
                            kind: 'utility',
                            parameters: {'destination': tool.key},
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }
}
