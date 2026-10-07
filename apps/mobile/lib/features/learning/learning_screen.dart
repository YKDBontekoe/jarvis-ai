import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

import 'improvement_suggestions.dart';

part 'learning_cards.dart';

/// Heartbeat, dreaming, and continuous-learning controls with a timeline of what Jarvis learned.
class LearningScreen extends StatefulWidget {
  const LearningScreen({required this.http, super.key});

  final Dio http;

  @override
  State<LearningScreen> createState() => _LearningScreenState();
}

/// Holds learning fields so the cards mixin can share state.
abstract class _LearningController extends State<LearningScreen> {
  Map<String, dynamic> _settings = const {};

  /// How much Jarvis may do on its own. Empty until loaded, which hides the card.
  Map<String, dynamic> _autonomy = const {};
  Map<String, dynamic> _state = const {};
  Map<String, dynamic> _dreaming = const {};
  List<Map<String, dynamic>> _activity = const [];

  /// Changes Jarvis would like to make, and recent ones it made itself. Empty on an older server.
  List<Map<String, dynamic>> _improvements = const [];
  final Set<String> _improvementBusy = {};
  bool _loading = true;
  bool _saving = false;
  bool _running = false;
  bool _dreamingNow = false;
  String? _error;
  String? _result;
  int _requestRevision = 0;

  Future<void> _update(String key, Object value);
  Future<void> _updateAutonomy(String key, Object value);
  Future<void> _runNow();
  Future<void> _dreamNow();
  bool _flag(String key, [bool fallback = false]);
}

class _LearningScreenState extends _LearningController with _LearningCards {
  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    final revision = ++_requestRevision;
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/learning/status',
      );
      final data = jsonObject(response.data) ?? const {};
      var autonomy = const <String, dynamic>{};
      try {
        final loaded = await widget.http.get<dynamic>(
          '/api/v1/settings/autonomy',
        );
        autonomy = jsonObject(loaded.data) ?? const {};
      } catch (_) {
        // An older server has no autonomy settings; the card stays hidden.
      }
      var improvements = const <Map<String, dynamic>>[];
      try {
        final loaded = await widget.http.get<dynamic>('/api/v1/improvements');
        improvements = jsonMaps(loaded.data);
      } catch (_) {
        // An older server has no improvements; the section stays hidden.
      }
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _improvements = improvements;
        _autonomy = autonomy;
        _settings = jsonObject(data['settings']) ?? const {};
        _state = jsonObject(data['state']) ?? const {};
        _dreaming = jsonObject(data['dreaming']) ?? const {};
        _activity = jsonMaps(data['activity']);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load learning settings.';
      });
    } catch (_) {
      if (!mounted || revision != _requestRevision) return;
      setState(() {
        _loading = false;
        _error = 'Could not load learning settings.';
      });
    }
  }

  @override
  Future<void> _update(String key, Object value) async {
    final next = {..._settings, key: value};
    setState(() {
      _settings = next;
      _saving = true;
      _error = null;
    });
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/settings/learning',
        data: next,
      );
      if (mounted) {
        setState(() => _settings = jsonObject(response.data) ?? next);
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not save that setting.',
      );
      await _load();
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Jarvis could not save that setting.');
      await _load();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Future<void> _updateAutonomy(String key, Object value) async {
    final next = {..._autonomy, key: value};
    setState(() {
      _autonomy = next;
      _saving = true;
      _error = null;
    });
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/settings/autonomy',
        data: next,
      );
      if (mounted) {
        setState(() => _autonomy = jsonObject(response.data) ?? next);
      }
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            firstProblemMessage(error.response?.data) ??
            'Jarvis could not save that setting.',
      );
      await _load();
    } catch (_) {
      if (!mounted) return;
      setState(() => _error = 'Jarvis could not save that setting.');
      await _load();
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  Future<void> _decide(String id, String action) async {
    setState(() {
      _improvementBusy.add(id);
      _error = null;
    });
    try {
      await widget.http.post<dynamic>('/api/v1/improvements/$id/$action');
      final loaded = await widget.http.get<dynamic>('/api/v1/improvements');
      if (mounted) setState(() => _improvements = jsonMaps(loaded.data));
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Jarvis could not do that.',
        );
        await _load();
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Jarvis could not do that.');
      }
    } finally {
      if (mounted) setState(() => _improvementBusy.remove(id));
    }
  }

  @override
  Future<void> _runNow() async {
    setState(() {
      _running = true;
      _result = null;
      _error = null;
    });
    try {
      final response = await widget.http.post<dynamic>('/api/v1/learning/run');
      if (!mounted) return;
      setState(
        () => _result = asJsonString(jsonObject(response.data)?['summary']),
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'The heartbeat could not run.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'The heartbeat could not run.');
      }
    } finally {
      if (mounted) setState(() => _running = false);
    }
  }

  @override
  Future<void> _dreamNow() async {
    setState(() {
      _dreamingNow = true;
      _result = null;
      _error = null;
    });
    try {
      final response = await widget.http.post<dynamic>(
        '/api/v1/learning/dream',
      );
      if (!mounted) return;
      setState(
        () => _result = asJsonString(jsonObject(response.data)?['summary']),
      );
      await _load();
    } on DioException catch (error) {
      if (mounted) {
        setState(
          () => _error =
              firstProblemMessage(error.response?.data) ??
              'Dreaming could not run.',
        );
      }
    } catch (_) {
      if (mounted) {
        setState(() => _error = 'Dreaming could not run.');
      }
    } finally {
      if (mounted) setState(() => _dreamingNow = false);
    }
  }

  @override
  bool _flag(String key, [bool fallback = false]) =>
      asJsonBool(_settings[key], fallback);

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(title: const Text('Learning')),
    body: _loading
        ? const LoadingState()
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView(
              padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      _heartbeatCard(),
                      const SizedBox(height: 16),
                      _dreamingCard(),
                      if (_autonomy.isNotEmpty) ...[
                        const SizedBox(height: 20),
                        const SectionHeader('Acting on its own'),
                        _autonomyCard(),
                      ],
                      if (_improvements.isNotEmpty) ...[
                        const SizedBox(height: 20),
                        ImprovementSuggestionsSection(
                          items: _improvements,
                          busy: _improvementBusy,
                          onAccept: (id) => unawaited(_decide(id, 'accept')),
                          onDismiss: (id) => unawaited(_decide(id, 'dismiss')),
                          onUndo: (id) => unawaited(_decide(id, 'undo')),
                        ),
                      ],
                      if (_error != null)
                        InlineNotice(
                          message: _error!,
                          tone: NoticeTone.danger,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      if (_result != null)
                        InlineNotice(
                          message: _result!,
                          tone: NoticeTone.success,
                          margin: const EdgeInsets.only(top: 16),
                        ),
                      const SizedBox(height: 20),
                      const SectionHeader('About you'),
                      _portrait(),
                      const SizedBox(height: 20),
                      const SectionHeader('What Jarvis may learn'),
                      GroupedSection(
                        children: [
                          _switch(
                            'learnPersona',
                            'Learn my preferences',
                            'Tone, format, language, and working style from chats and 👍/👎.',
                            PhosphorIconsRegular.userCircle,
                            fallback: true,
                          ),
                          _switch(
                            'autoCreateSkills',
                            'Write skills automatically',
                            'Save repeatable workflows as skills when Jarvis thinks it needs them.',
                            PhosphorIconsRegular.magicWand,
                            fallback: true,
                          ),
                          _switch(
                            'autoActivateSkills',
                            'Activate new skills right away',
                            'Off: learned skills wait for your review in Skills.',
                            PhosphorIconsRegular.sealCheck,
                            fallback: false,
                            enabled: _flag('autoCreateSkills', true),
                          ),
                          _switch(
                            'captureSignals',
                            'Learn from how replies land',
                            'Notes 👍/👎, regenerates, denied approvals and failing tools by name only, never what was said.',
                            PhosphorIconsRegular.chartLine,
                            fallback: true,
                          ),
                          _switch(
                            'proposeImprovements',
                            'Suggest improvements',
                            'Offers skills and fixes it spots overnight for you to review. Nothing changes until you accept.',
                            PhosphorIconsRegular.lightbulb,
                            fallback: true,
                            enabled: _flag('captureSignals', true),
                          ),
                          _switch(
                            'autoApplyLowRiskMemory',
                            'Save confident memories itself',
                            'Clear, high-confidence facts are saved right away and listed here so you can undo them. Off: they wait for review.',
                            PhosphorIconsRegular.brain,
                            fallback: true,
                          ),
                          _switch(
                            'proactiveCheckIns',
                            'Proactive check-ins',
                            'Heads-ups about upcoming reminders, waiting approvals, and failed tasks.',
                            PhosphorIconsRegular.bellRinging,
                            fallback: true,
                          ),
                        ],
                      ),
                      const SizedBox(height: 20),
                      const SectionHeader('Quiet hours'),
                      _quietHours(),
                      const SizedBox(height: 20),
                      const SectionHeader('Dream diary'),
                      _diary(),
                      const SizedBox(height: 20),
                      const SectionHeader('Recent learning'),
                      _timeline(),
                    ],
                  ),
                ),
              ],
            ),
          ),
  );
}
