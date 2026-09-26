import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import 'theme.dart';
import 'ui/jarvis_ui.dart';

class DailyBriefingScreen extends StatefulWidget {
  const DailyBriefingScreen({required this.http, super.key});

  final Dio http;

  @override
  State<DailyBriefingScreen> createState() => _DailyBriefingScreenState();
}

class _DailyBriefingScreenState extends State<DailyBriefingScreen> {
  bool _enabled = false;
  bool _loading = true;
  bool _saving = false;
  TimeOfDay _time = const TimeOfDay(hour: 8, minute: 0);
  final _zone = TextEditingController(text: 'UTC');
  String? _error;
  String? _saved;

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _zone.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<Map<String, dynamic>>(
        '/api/v1/briefings/daily',
      );
      final data = response.data ?? const <String, dynamic>{};
      final time = (data['localTime'] as String? ?? '08:00:00').split(':');
      if (mounted) {
        setState(() {
          _enabled = data['enabled'] as bool? ?? false;
          _time = TimeOfDay(
            hour: int.tryParse(time.first) ?? 8,
            minute: time.length > 1 ? int.tryParse(time[1]) ?? 0 : 0,
          );
          _zone.text = data['timeZoneId'] as String? ?? 'UTC';
          _loading = false;
        });
      }
    } on DioException catch (error) {
      if (mounted) {
        setState(() {
          _error = error.response?.data is Map<String, dynamic>
              ? ((error.response!.data as Map<String, dynamic>)['detail']
                    as String?)
              : 'Could not load briefing settings.';
          _loading = false;
        });
      }
    }
  }

  Future<void> _chooseTime() async {
    final selected = await showTimePicker(context: context, initialTime: _time);
    if (selected != null && mounted) setState(() => _time = selected);
  }

  Future<void> _save() async {
    final zone = _zone.text.trim();
    if (zone.isEmpty) {
      setState(() => _error = 'Enter a time zone such as Europe/Amsterdam.');
      return;
    }
    setState(() {
      _saving = true;
      _error = null;
      _saved = null;
    });
    try {
      await widget.http.put<void>(
        '/api/v1/briefings/daily',
        data: {
          'enabled': _enabled,
          'localTime':
              '${_time.hour.toString().padLeft(2, '0')}:${_time.minute.toString().padLeft(2, '0')}:00',
          'timeZoneId': zone,
        },
      );
      if (mounted) setState(() => _saved = 'Briefing settings saved.');
    } on DioException catch (error) {
      if (mounted) {
        final body = error.response?.data;
        final detail = body is Map<String, dynamic>
            ? body['detail'] as String? ??
                  (body['errors'] as Map<String, dynamic>?)?.values.firstOrNull
                      ?.toString()
            : null;
        setState(() => _error = detail ?? 'Could not save briefing settings.');
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final theme = Theme.of(context);
    return Scaffold(
      appBar: AppBar(title: const Text('Morning briefing')),
      body: _loading
          ? const LoadingState()
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  maxWidth: 560,
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      Container(
                        padding: const EdgeInsets.all(22),
                        decoration: BoxDecoration(
                          borderRadius: BorderRadius.circular(JarvisRadii.xl),
                          gradient: const LinearGradient(
                            begin: Alignment.topLeft,
                            end: Alignment.bottomRight,
                            colors: [Color(0xfffff1dc), Color(0xffffe4e8)],
                          ),
                          border: Border.all(color: const Color(0x1fe8833a)),
                        ),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            const IconBadge(
                              icon: Icons.wb_sunny_rounded,
                              color: Color(0xffe8833a),
                              size: 44,
                            ),
                            const SizedBox(height: 18),
                            Text(
                              _time.format(context),
                              style: theme.textTheme.displaySmall,
                            ),
                            const SizedBox(height: 6),
                            Text(
                              _enabled
                                  ? 'Every day · ${_zone.text}'
                                  : 'Briefing is off',
                              style: theme.textTheme.bodyMedium?.copyWith(
                                color: JarvisColors.inkSoft,
                              ),
                            ),
                            const SizedBox(height: 14),
                            const Text(
                              'A daily reminder with what is coming up in Jarvis: reminders due today and active tasks.',
                              style: TextStyle(
                                height: 1.5,
                                color: JarvisColors.inkSoft,
                              ),
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 16),
                      SurfaceCard(
                        padding: const EdgeInsets.symmetric(vertical: 6),
                        child: Column(
                          children: [
                            SwitchListTile.adaptive(
                              title: const Text('Send a daily briefing'),
                              value: _enabled,
                              onChanged: (value) =>
                                  setState(() => _enabled = value),
                            ),
                            const Divider(indent: 16, endIndent: 16),
                            ListTile(
                              title: const Text('Delivery time'),
                              subtitle: Text(_time.format(context)),
                              trailing: const Icon(Icons.schedule_rounded),
                              onTap: _chooseTime,
                            ),
                          ],
                        ),
                      ),
                      const SizedBox(height: 16),
                      TextField(
                        controller: _zone,
                        textCapitalization: TextCapitalization.none,
                        autocorrect: false,
                        onChanged: (_) => setState(() {}),
                        decoration: const InputDecoration(
                          labelText: 'Time zone',
                          hintText: 'Europe/Amsterdam',
                          helperText: 'Use an IANA time zone identifier.',
                          prefixIcon: Icon(Icons.public_rounded),
                          fillColor: JarvisColors.surface,
                        ),
                      ),
                      const SizedBox(height: 24),
                      FilledButton.icon(
                        onPressed: _saving ? null : _save,
                        style: FilledButton.styleFrom(
                          minimumSize: const Size.fromHeight(52),
                        ),
                        icon: _saving
                            ? const SizedBox.square(
                                dimension: 18,
                                child: CircularProgressIndicator(
                                  strokeWidth: 2,
                                ),
                              )
                            : const Icon(Icons.check_rounded),
                        label: Text(_saving ? 'Saving' : 'Save briefing'),
                      ),
                      if (_error != null)
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(top: 14),
                        ),
                      if (_saved != null)
                        InlineNotice(
                          message: _saved!,
                          tone: NoticeTone.success,
                          margin: const EdgeInsets.only(top: 14),
                        ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}
