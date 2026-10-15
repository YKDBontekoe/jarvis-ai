import '../../json_maps.dart';
import '../integrations/app_presentation.dart';
import 'tile_models.dart';

/// Home tiles for single connected apps. A tile id carries the app's key and
/// name (`app:<serverId>~<name>`, or `app:host:<name>~<name>` for apps the
/// Jarvis server runs), so a saved layout can draw it before anything loads.
const appTilePrefix = 'app:';
const appTileHostPrefix = 'host:';

/// Where an app tile opens: the app's own page.
const appDestinationPrefix = 'app:';

String appTileId(String key, String name) =>
    '$appTilePrefix$key~${Uri.encodeComponent(name).replaceAll('~', '%7E')}';

/// The app key (server id or `host:<name>`) and name in an app tile id.
({String key, String name})? parseAppTileId(String id) {
  if (!id.startsWith(appTilePrefix)) return null;
  final rest = id.substring(appTilePrefix.length);
  final split = rest.lastIndexOf('~');
  if (split <= 0) return null;
  final key = rest.substring(0, split);
  String name;
  try {
    name = Uri.decodeComponent(rest.substring(split + 1));
  } on ArgumentError {
    return null;
  }
  if (key == appTileHostPrefix || name.trim().isEmpty) return null;
  return (key: key, name: name);
}

TileSpec? appTileSpecFor(String id) {
  final parsed = parseAppTileId(id);
  if (parsed == null) return null;
  final info = appInfo(parsed.name);
  return TileSpec(
    id: id,
    name: info.name,
    description: 'How ${info.name} is doing and what Jarvis did with it.',
    icon: info.icon,
    category: TileCategory.system,
    sizes: const [
      TileSize.icon,
      TileSize.strip,
      TileSize.square,
      TileSize.wide,
    ],
    destination: '$appDestinationPrefix${parsed.key}',
    load: (env) => loadAppTile(env, parsed.key, parsed.name),
    fallback: 'Connected app',
  );
}

/// Finds the app's connection by id, or by name for an app the server runs.
Map<String, dynamic>? appConnectionFor(
  List<Map<String, dynamic>> connections,
  String key,
) => key.startsWith(appTileHostPrefix)
    ? connections
          .where(
            (item) =>
                item['hostManaged'] == true &&
                item['name'] == key.substring(appTileHostPrefix.length),
          )
          .firstOrNull
    : connections.where((item) => item['id'] == key).firstOrNull;

Future<TileData?> loadAppTile(TileEnv env, String key, String name) async {
  final response = await env.http.get<dynamic>(
    '/api/v1/integrations/connections',
  );
  final connection = appConnectionFor(jsonMaps(response.data), key);
  if (connection == null) {
    return const TileData(subtitle: 'Not connected anymore', attention: true);
  }
  final state = asJsonString(connection['state']) ?? 'unavailable';
  final working = state == 'connected';
  final calls = asJsonInt(connection['callsLast7Days']);
  final tools = jsonStrings(connection['tools']);
  return TileData(
    stat: working ? '${tools.length}' : null,
    unit: working ? (tools.length == 1 ? 'action' : 'actions') : null,
    subtitle: switch (state) {
      'connected' =>
        calls == 0
            ? 'Working'
            : 'Used ${calls == 1 ? 'once' : '$calls times'} this week',
      'paused' => 'Paused',
      'needs_credentials' => 'Needs a key or sign-in',
      'needs_sign_in' => 'Sign in again',
      _ => 'Can’t connect',
    },
    attention: !working && state != 'paused',
    rows: [
      for (final tool in tools.take(8))
        TileRow(humanizeAppTool(tool, appName: name)),
    ],
  );
}
