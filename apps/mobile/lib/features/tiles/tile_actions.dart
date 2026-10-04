import 'package:dio/dio.dart';

import '../../ui/phosphor_icons.dart';
import 'tile_models.dart';

/// What happened when a tile action ran.
class TileActionOutcome {
  const TileActionOutcome(this.ok, this.message);

  final bool ok;

  /// Said to the person afterwards, such as "Marked done".
  final String message;
}

/// Runs [actionId] on [itemId] for tile [tileId]. Only quick, low-risk things
/// can be done from a tile: finishing or snoozing a reminder, checking in a
/// habit and declining a tool call. Approving one always goes through the
/// Approvals page, where its details are shown.
Future<TileActionOutcome> runTileAction(
  Dio http,
  String tileId,
  String actionId,
  String? itemId,
) async {
  if (itemId == null || itemId.isEmpty) {
    return const TileActionOutcome(false, 'Could not find that item.');
  }
  final id = Uri.encodeComponent(itemId);
  try {
    switch ('$tileId/$actionId') {
      case 'reminders/done':
        await http.post<dynamic>('/api/v1/reminders/$id/complete');
        return const TileActionOutcome(true, 'Marked done');
      case 'reminders/snooze':
        await http.post<dynamic>(
          '/api/v1/reminders/$id/snooze',
          data: const {'minutes': 10},
        );
        return const TileActionOutcome(true, 'Snoozed for 10 minutes');
      case 'habits/check':
        await http.post<dynamic>(
          '/api/v1/habits/$id/check-ins',
          data: const {'done': true},
        );
        return const TileActionOutcome(true, 'Checked in');
      case 'habits/uncheck':
        await http.post<dynamic>(
          '/api/v1/habits/$id/check-ins',
          data: const {'done': false},
        );
        return const TileActionOutcome(true, 'Check-in removed');
      case 'approvals/deny':
        await http.post<dynamic>(
          '/api/v1/approvals/$id/decision',
          data: const {'approved': false},
        );
        return const TileActionOutcome(true, 'Declined');
    }
  } on DioException catch (error) {
    if (error.response?.statusCode == 409) {
      return const TileActionOutcome(false, 'That was already decided.');
    }
  } catch (_) {
    // Falls through to the generic message.
  }
  return TileActionOutcome(false, 'Could not do that. Try again.');
}

/// What [data] should look like right after [actionId] succeeded, before the
/// server is asked again, so the tile answers the tap at once.
TileData applyTileAction(
  String tileId,
  TileData data,
  String actionId,
  String? itemId,
) {
  if (itemId == null) return data;
  switch ('$tileId/$actionId') {
    case 'reminders/done' || 'reminders/snooze' || 'approvals/deny':
      final rows = [
        for (final row in data.rows)
          if (row.id != itemId) row,
      ];
      final count = int.tryParse(data.stat ?? '');
      return _copy(
        data,
        rows: rows,
        stat: count == null ? data.stat : '${count > 0 ? count - 1 : 0}',
        focus: rows.isEmpty ? null : rows.first,
        attention: rows.any((row) => row.attention) && data.attention,
      );
    case 'habits/check' || 'habits/uncheck':
      final checking = actionId == 'check';
      final rows = [
        for (final row in data.rows)
          if (row.id == itemId)
            TileRow(
              row.text,
              meta: checking ? 'Done' : 'Open',
              attention: !checking,
              id: row.id,
              done: checking,
              actions: [
                TileAction(
                  checking ? 'uncheck' : 'check',
                  checking ? 'Undo' : 'Check in',
                  icon: PhosphorIconsRegular.check,
                ),
              ],
            )
          else
            row,
      ];
      final done = rows.where((row) => row.done).length;
      final open = rows.where((row) => !row.done).toList();
      return _copy(
        data,
        rows: rows,
        stat: '$done/${rows.length}',
        progress: rows.isEmpty ? 0 : done / rows.length,
        focus: open.isEmpty ? null : open.first,
        subtitle: open.isEmpty ? 'All done today' : '${open.first.text} left',
      );
  }
  return data;
}

TileData _copy(
  TileData data, {
  List<TileRow>? rows,
  String? stat,
  double? progress,
  TileRow? focus,
  String? subtitle,
  bool? attention,
}) => TileData(
  stat: stat ?? data.stat,
  unit: data.unit,
  subtitle: subtitle ?? (focus == null ? 'All clear' : focus.text),
  rows: rows ?? data.rows,
  attention: attention ?? data.attention,
  progress: progress ?? data.progress,
  visual: data.visual,
  bars: data.bars,
  timeline: data.timeline,
  actions: focus == null ? const [] : focus.actions,
  focusId: focus?.id,
  focusLabel: focus?.text,
  countdownTo: data.countdownTo,
);
