import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';

import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/feature_kit.dart';
import '../../ui/jarvis_ui.dart';

/// Language for replies and the base currency for new budgets and trips. Both
/// are only what the owner picks here: nothing is guessed from the phone, and
/// nothing is converted.
class LocaleScreen extends StatefulWidget {
  const LocaleScreen({required this.http, super.key});

  final Dio http;

  @override
  State<LocaleScreen> createState() => _LocaleScreenState();
}

class _LocaleScreenState extends State<LocaleScreen> {
  Map<String, dynamic>? _view;
  bool _loading = true;
  bool _busy = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    unawaited(_load());
  }

  Future<void> _load() async {
    try {
      final response = await widget.http.get<dynamic>(
        '/api/v1/preferences/locale',
      );
      if (!mounted) return;
      setState(() {
        _view = jsonObject(response.data);
        _loading = false;
        _error = null;
      });
    } on DioException catch (error) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error =
            firstProblemMessage(error.response?.data) ??
            'Could not load language and currency.';
      });
    }
  }

  Future<void> _save(Map<String, Object?> body) async {
    setState(() => _busy = true);
    try {
      final response = await widget.http.put<dynamic>(
        '/api/v1/preferences/locale',
        data: body,
      );
      if (mounted) setState(() => _view = jsonObject(response.data) ?? _view);
    } on DioException catch (error) {
      if (mounted) {
        ScaffoldMessenger.maybeOf(context)
          ?..hideCurrentSnackBar()
          ..showSnackBar(
            SnackBar(
              content: Text(
                firstProblemMessage(error.response?.data) ??
                    'That did not work.',
              ),
            ),
          );
      }
    } finally {
      if (mounted) setState(() => _busy = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final languages = jsonMaps(_view?['languages']);
    final currencies = jsonStrings(_view?['currencies']);
    final language = asJsonString(_view?['language']);
    final currency = asJsonString(_view?['baseCurrency']);
    return Scaffold(
      appBar: AppBar(title: const PageTitle('Language and currency')),
      body: _loading
          ? const SkeletonList()
          : _error != null
          ? ErrorState(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.fromLTRB(16, 4, 16, 32),
              children: [
                ContentWidth(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.stretch,
                    children: [
                      HeroCard(
                        key: const Key('locale-hero'),
                        label: 'YOUR CHOICES',
                        title: [
                          asJsonString(_view?['languageName']) ??
                              'No language set',
                          currency ?? 'no currency',
                        ].join(' · '),
                        subtitle: asJsonString(_view?['explanation']),
                      ),
                      const GroupLabel('Reply language'),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          ChoiceChip(
                            key: const Key('locale-language-none'),
                            label: const Text('Follow my message'),
                            selected: language == null,
                            onSelected: _busy
                                ? null
                                : (_) => unawaited(_save({'language': ''})),
                          ),
                          for (final option in languages)
                            ChoiceChip(
                              key: Key('locale-language-${option['code']}'),
                              label: Text(asJsonString(option['name']) ?? ''),
                              selected:
                                  language == asJsonString(option['code']),
                              onSelected: _busy
                                  ? null
                                  : (_) => unawaited(
                                      _save({'language': option['code']}),
                                    ),
                            ),
                        ],
                      ),
                      const GroupLabel('Base currency'),
                      Wrap(
                        spacing: 8,
                        runSpacing: 8,
                        children: [
                          ChoiceChip(
                            key: const Key('locale-currency-none'),
                            label: const Text('Not set'),
                            selected: currency == null,
                            onSelected: _busy
                                ? null
                                : (_) => unawaited(_save({'currency': ''})),
                          ),
                          for (final code in currencies)
                            ChoiceChip(
                              key: Key('locale-currency-$code'),
                              label: Text(code),
                              selected: currency == code,
                              onSelected: _busy
                                  ? null
                                  : (_) => unawaited(_save({'currency': code})),
                            ),
                        ],
                      ),
                      Padding(
                        padding: const EdgeInsets.only(top: 12),
                        child: Text(
                          'A profile that sets its own reply language wins over this.',
                          style: TextStyle(
                            color: JarvisColors.of(context).muted,
                          ),
                        ),
                      ),
                    ],
                  ),
                ),
              ],
            ),
    );
  }
}
