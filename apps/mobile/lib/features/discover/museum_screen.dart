import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';
import '../game/game_format.dart';

/// The life museum: a year as rooms you walk through, each a wall of framed
/// exhibits taken from your own records.
class MuseumScreen extends StatefulWidget {
  const MuseumScreen({required this.http, super.key});

  final Dio http;

  @override
  State<MuseumScreen> createState() => _MuseumScreenState();
}

class _MuseumScreenState extends State<MuseumScreen> {
  Map<String, dynamic>? _museum;
  int? _year;
  bool _loading = true;
  String? _error;
  final _pages = PageController();
  int _room = 0;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  @override
  void dispose() {
    _pages.dispose();
    super.dispose();
  }

  Future<void> _load([int? year]) async {
    setState(() => _loading = _museum == null);
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/discover/museum',
        queryParameters: {'year': ?year},
      );
      if (!mounted) return;
      final museum = jsonObject(response.data);
      setState(() {
        _museum = museum;
        _year = asJsonInt(museum?['year']);
        _room = 0;
        _loading = false;
        _error = null;
      });
      if (_pages.hasClients) _pages.jumpToPage(0);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not open the museum.';
      });
    } catch (_) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = 'Could not open the museum.';
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final rooms = jsonMaps(_museum?['rooms']);
    final years = [
      for (final y in (_museum?['years'] as List? ?? const []))
        if (y is num) y.round(),
    ];
    return Scaffold(
      appBar: AppBar(
        title: PageTitle(_year == null ? 'Life museum' : 'Museum of $_year'),
      ),
      body: ListScreenBody(
        loading: _loading,
        error: _museum == null ? _error : null,
        isEmpty: rooms.isEmpty,
        onRetry: _load,
        empty: const EmptyState(
          icon: PhosphorIconsRegular.image,
          title: 'The museum is still empty',
          message:
              'Rate your days in the journal, keep habits and promises, and '
              'save things to the library. Rooms open as there is something '
              'to hang in them.',
        ),
        child: Column(
          children: [
            if (years.length > 1)
              SizedBox(
                height: 48,
                child: ListView(
                  scrollDirection: Axis.horizontal,
                  padding: const EdgeInsets.symmetric(horizontal: 16),
                  children: [
                    for (final year in years.take(5))
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          key: Key('museum-year-$year'),
                          label: Text('$year'),
                          selected: year == _year,
                          onSelected: (_) => unawaited(_load(year)),
                        ),
                      ),
                  ],
                ),
              ),
            Expanded(
              child: PageView.builder(
                key: const Key('museum-rooms'),
                controller: _pages,
                itemCount: rooms.length,
                onPageChanged: (room) => setState(() => _room = room),
                itemBuilder: (context, index) => _Room(room: rooms[index]),
              ),
            ),
            Padding(
              padding: EdgeInsets.fromLTRB(
                16,
                8,
                16,
                16 + MediaQuery.paddingOf(context).bottom,
              ),
              child: _RoomDots(count: rooms.length, current: _room),
            ),
          ],
        ),
      ),
    );
  }
}

class _Room extends StatelessWidget {
  const _Room({required this.room});

  final Map<String, dynamic> room;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final exhibits = jsonMaps(room['exhibits']);
    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 16),
      children: [
        ContentWidth(
          child: Text(
            (asJsonString(room['title']) ?? '').toUpperCase(),
            style: JarvisType.capsOf(context),
          ),
        ),
        const SizedBox(height: 4),
        ContentWidth(
          child: Text(
            asJsonString(room['caption']) ?? '',
            style: TextStyle(
              fontFamily: 'Geist',
              fontSize: 15,
              height: 1.4,
              color: colors.inkSoft,
            ),
          ),
        ),
        const SizedBox(height: 14),
        ContentWidth(
          child: LayoutBuilder(
            builder: (context, constraints) {
              final columns =
                  constraints.maxWidth >= 520 &&
                      MediaQuery.textScalerOf(context).scale(10) <= 14
                  ? 2
                  : 1;
              final width =
                  (constraints.maxWidth - 12 * (columns - 1)) / columns;
              return Wrap(
                spacing: 12,
                runSpacing: 12,
                children: [
                  for (final (i, exhibit) in exhibits.indexed)
                    SizedBox(
                      width: width,
                      child: FadeSlideIn(
                        index: i,
                        child: _Exhibit(exhibit: exhibit),
                      ),
                    ),
                ],
              );
            },
          ),
        ),
      ],
    );
  }
}

/// One framed exhibit: a thin inner frame on a surface card, the value large
/// when there is one, and a museum-style label underneath.
class _Exhibit extends StatelessWidget {
  const _Exhibit({required this.exhibit});

  final Map<String, dynamic> exhibit;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    final value = asJsonString(exhibit['value']);
    final detail = asJsonString(exhibit['detail']);
    final on = parseGameDate(exhibit['on']);
    return SurfaceCard(
      padding: const EdgeInsets.all(10),
      child: DecoratedBox(
        decoration: BoxDecoration(
          border: Border.all(color: colors.outlineStrong),
          borderRadius: BorderRadius.circular(10),
        ),
        child: Padding(
          padding: const EdgeInsets.fromLTRB(14, 14, 14, 12),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              if (value != null) ...[
                Text(
                  value,
                  style: TextStyle(
                    fontFamily: 'Geist',
                    fontSize: 26,
                    fontWeight: FontWeight.w700,
                    letterSpacing: -.8,
                    color: colors.accentDeep,
                    fontFeatures: const [FontFeature.tabularFigures()],
                  ),
                ),
                const SizedBox(height: 6),
              ],
              Text(
                asJsonString(exhibit['title']) ?? '',
                style: TextStyle(
                  fontFamily: 'Geist',
                  fontSize: 15.5,
                  height: 1.3,
                  fontWeight: FontWeight.w600,
                  color: colors.ink,
                ),
              ),
              if (detail != null) ...[
                const SizedBox(height: 4),
                Text(
                  detail,
                  style: TextStyle(
                    fontFamily: 'Geist',
                    fontSize: 13.5,
                    height: 1.4,
                    fontStyle: FontStyle.italic,
                    color: colors.inkSoft,
                  ),
                ),
              ],
              if (on != null) ...[
                const SizedBox(height: 8),
                Text(
                  shortDay(on).toUpperCase(),
                  style: JarvisType.capsOf(context).copyWith(fontSize: 10.5),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}

class _RoomDots extends StatelessWidget {
  const _RoomDots({required this.count, required this.current});

  final int count;
  final int current;

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    return Semantics(
      label: 'Room ${current + 1} of $count',
      excludeSemantics: true,
      child: Row(
        mainAxisAlignment: MainAxisAlignment.center,
        children: [
          for (var i = 0; i < count; i++)
            AnimatedContainer(
              duration: JarvisMotion.of(context, JarvisMotion.base),
              curve: JarvisMotion.standard,
              width: i == current ? 18 : 6,
              height: 6,
              margin: const EdgeInsets.symmetric(horizontal: 3),
              decoration: BoxDecoration(
                color: i == current ? colors.accent : colors.outlineStrong,
                borderRadius: BorderRadius.circular(3),
              ),
            ),
        ],
      ),
    );
  }
}
