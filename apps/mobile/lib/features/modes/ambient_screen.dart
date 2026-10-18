import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../json_maps.dart';
import '../../ui/phosphor_icons.dart';
import 'modes_models.dart';

/// A calm, always-on display for a desk or a tablet: the time, the current
/// mode, the next reminder and calendar event. Tap anywhere to leave.
class AmbientScreen extends StatefulWidget {
  const AmbientScreen({required this.http, this.now, super.key});

  final Dio http;

  /// Fixed clock for tests; when set the display does not tick.
  final DateTime? now;

  @override
  State<AmbientScreen> createState() => _AmbientScreenState();
}

class _AmbientScreenState extends State<AmbientScreen> {
  late DateTime _now = widget.now ?? DateTime.now();
  Timer? _tick;
  Timer? _refresh;
  ModeStateData? _mode;
  ({String time, String title})? _nextReminder;
  ({String time, String title})? _nextEvent;
  int? _battery;
  int _approvals = 0;

  /// A shared screen hides what things are called until the owner turns titles on for this device.
  bool _showTitles = false;
  static const _showTitlesKey = 'jarvis.ambient.showTitles';

  @override
  void initState() {
    super.initState();
    unawaited(
      SystemChrome.setEnabledSystemUIMode(SystemUiMode.immersiveSticky),
    );
    if (widget.now == null) {
      _tick = Timer.periodic(
        const Duration(seconds: 20),
        (_) => setState(() => _now = DateTime.now()),
      );
      _refresh = Timer.periodic(
        const Duration(minutes: 2),
        (_) => unawaited(_load()),
      );
    }
    unawaited(_load());
    unawaited(_loadPrivacy());
  }

  Future<void> _loadPrivacy() async {
    try {
      final prefs = await SharedPreferences.getInstance();
      if (mounted) {
        setState(() => _showTitles = prefs.getBool(_showTitlesKey) ?? false);
      }
    } catch (_) {
      // Storage can be unavailable; titles then stay hidden.
    }
  }

  Future<void> _toggleTitles() async {
    final next = !_showTitles;
    setState(() => _showTitles = next);
    try {
      await (await SharedPreferences.getInstance()).setBool(
        _showTitlesKey,
        next,
      );
    } catch (_) {
      // Kept for this session only.
    }
  }

  @override
  void dispose() {
    _tick?.cancel();
    _refresh?.cancel();
    unawaited(SystemChrome.setEnabledSystemUIMode(SystemUiMode.edgeToEdge));
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final mode = await widget.http.get<dynamic>('/api/v1/modes');
      final home = await widget.http.get<dynamic>('/api/v1/home');
      if (!mounted) return;
      final map = jsonObject(home.data);
      final reminders = jsonMaps(map?['reminders']);
      final events = jsonMaps(jsonObject(map?['calendar'])?['events']);
      setState(() {
        _mode = ModeStateData.fromJson(mode.data);
        _nextReminder = reminders.isEmpty
            ? null
            : _line(reminders.first, 'dueAt');
        _nextEvent = events.isEmpty ? null : _line(events.first, 'startAt');
        _battery = asJsonInt(jsonObject(map?['device'])?['batteryPercent'], -1);
        if (_battery! < 0) _battery = null;
        _approvals = jsonMaps(map?['approvals']).length;
      });
    } on DioException {
      // Keep showing what we had; the clock still works offline.
    }
  }

  ({String time, String title})? _line(
    Map<String, dynamic> item,
    String timeKey,
  ) {
    final title = jsonString(item, 'title');
    final at = jsonDate(item[timeKey], local: true);
    if (title == null) return null;
    return (time: at == null ? '' : _time(at), title: title);
  }

  /// A shared screen leaves out what things are called, keeping the time, until the owner turns names on.
  String _shown(({String time, String title}) line, String hidden) {
    final name = _showTitles ? line.title : hidden;
    return line.time.isEmpty ? name : '${line.time}  $name';
  }

  static String _time(DateTime value) =>
      '${value.hour.toString().padLeft(2, '0')}:${value.minute.toString().padLeft(2, '0')}';

  @override
  Widget build(BuildContext context) {
    const soft = Color(0xFFB8BCC8);
    return Scaffold(
      backgroundColor: Colors.black,
      body: GestureDetector(
        key: const Key('ambient-exit'),
        behavior: HitTestBehavior.opaque,
        onTap: () => Navigator.of(context).maybePop(),
        child: SafeArea(
          child: Stack(
            children: [
              Center(
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: [
                    Text(
                      _time(_now),
                      key: const Key('ambient-time'),
                      style: const TextStyle(
                        color: Colors.white,
                        fontSize: 96,
                        fontWeight: FontWeight.w200,
                        letterSpacing: 2,
                      ),
                    ),
                    if (_mode != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Chip(
                          key: const Key('ambient-mode'),
                          avatar: Icon(modeIcon(_mode!.mode), size: 18),
                          label: Text(_mode!.label),
                        ),
                      ),
                    const SizedBox(height: 32),
                    if (_nextEvent != null)
                      Text(
                        'Next: ${_shown(_nextEvent!, 'an event')}',
                        style: const TextStyle(color: soft, fontSize: 20),
                      ),
                    if (_nextReminder != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          'Reminder: ${_shown(_nextReminder!, 'a reminder')}',
                          style: const TextStyle(color: soft, fontSize: 20),
                        ),
                      ),
                    if (_approvals > 0)
                      Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Text(
                          '$_approvals waiting for your approval',
                          style: const TextStyle(
                            color: Colors.amber,
                            fontSize: 18,
                          ),
                        ),
                      ),
                    if (_battery != null)
                      Padding(
                        padding: const EdgeInsets.only(top: 24),
                        child: Text(
                          'Phone battery $_battery%',
                          style: const TextStyle(
                            color: Color(0xFF7C8092),
                            fontSize: 14,
                          ),
                        ),
                      ),
                  ],
                ),
              ),
              Align(
                alignment: Alignment.topRight,
                child: IconButton(
                  key: const Key('ambient-privacy'),
                  tooltip: _showTitles
                      ? 'Hide event and reminder names on this screen'
                      : 'Show event and reminder names on this screen',
                  icon: Icon(
                    _showTitles
                        ? PhosphorIconsRegular.eye
                        : PhosphorIconsRegular.eyeSlash,
                    color: const Color(0xFF7C8092),
                  ),
                  onPressed: () => unawaited(_toggleTitles()),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}
