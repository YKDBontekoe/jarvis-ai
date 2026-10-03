import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../http_urls.dart';
import '../../json_maps.dart';
import '../../theme.dart';
import '../../ui/jarvis_ui.dart';
import '../../ui/phosphor_icons.dart';

/// Signs the Jarvis server in to ChatGPT with a one-time code, so nobody needs a shell on the server. Shows
/// nothing once the server is signed in, so it can sit on Home as well as in Settings → Models.
class CodexSignInCard extends StatefulWidget {
  const CodexSignInCard({
    required this.http,
    this.pollInterval = const Duration(seconds: 3),
    super.key,
  });

  final Dio http;
  final Duration pollInterval;

  @override
  State<CodexSignInCard> createState() => _CodexSignInCardState();
}

class _CodexSignInCardState extends State<CodexSignInCard> {
  static const _path = '/api/v1/settings/models/codex/sign-in';

  bool? _signedIn;
  bool _starting = false;
  bool _justSignedIn = false;
  Map<String, dynamic>? _pending;
  String? _error;
  Timer? _poll;

  @override
  void initState() {
    super.initState();
    _refresh();
  }

  @override
  void dispose() {
    _poll?.cancel();
    super.dispose();
  }

  Future<void> _refresh() async {
    try {
      final response = await widget.http.get<dynamic>(_path);
      if (!mounted) return;
      _apply(jsonObject(response.data) ?? const {});
    } on DioException {
      // The card stays hidden when the status cannot be read; chat shows its own errors.
    }
  }

  void _apply(Map<String, dynamic> status) {
    final signedIn = status['signedIn'] == true;
    final pending = jsonObject(status['pending']);
    setState(() {
      if (signedIn && _pending != null) _justSignedIn = true;
      _signedIn = signedIn;
      _pending = signedIn ? null : pending;
      _error = asJsonString(status['error']) ?? _error;
    });
    _poll?.cancel();
    if (!signedIn && pending != null) {
      _poll = Timer(widget.pollInterval, _refresh);
    }
  }

  Future<void> _start() async {
    setState(() {
      _starting = true;
      _error = null;
    });
    try {
      final response = await widget.http.post<dynamic>(_path);
      if (!mounted) return;
      _apply(jsonObject(response.data) ?? const {});
      final url = parseHttpUrl(asJsonString(_pending?['verificationUrl']));
      if (url != null) await launchHttpUrl(url);
    } on DioException catch (error) {
      if (!mounted) return;
      setState(
        () => _error =
            asJsonString(jsonObject(error.response?.data)?['message']) ??
            'Jarvis could not start the sign-in. Try again.',
      );
    } finally {
      if (mounted) setState(() => _starting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final colors = JarvisColors.of(context);
    if (_justSignedIn) {
      return InlineNotice(
        key: const Key('codex-signed-in'),
        message: 'Jarvis is signed in to ChatGPT. You can start chatting.',
        tone: NoticeTone.success,
        margin: const EdgeInsets.only(bottom: 16),
      );
    }
    if (_signedIn != false) return const SizedBox.shrink();
    final pending = _pending;
    final code = asJsonString(pending?['userCode']);
    final url = asJsonString(pending?['verificationUrl']);
    return Padding(
      padding: const EdgeInsets.only(bottom: 16),
      child: SurfaceCard(
        key: const Key('codex-sign-in'),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: [
            Row(
              children: [
                const IconBadge(icon: PhosphorIconsRegular.signIn),
                const SizedBox(width: 12),
                Expanded(
                  child: Text(
                    'Sign Jarvis in to ChatGPT',
                    style: Theme.of(context).textTheme.titleMedium,
                  ),
                ),
              ],
            ),
            const SizedBox(height: 10),
            Text(
              code == null
                  ? 'Jarvis thinks with your ChatGPT account. Sign in once and Jarvis can answer.'
                  : 'Open the sign-in page, then enter this code. This page updates by itself when you are done.',
              style: TextStyle(color: colors.inkSoft, height: 1.4),
            ),
            if (code != null) ...[
              const SizedBox(height: 14),
              Center(
                child: SelectableText(
                  code,
                  key: const Key('codex-code'),
                  style: const TextStyle(
                    fontSize: 28,
                    fontWeight: FontWeight.w700,
                    letterSpacing: 2,
                    fontFamily: 'monospace',
                  ),
                ),
              ),
              const SizedBox(height: 10),
              Wrap(
                alignment: WrapAlignment.center,
                spacing: 8,
                runSpacing: 8,
                children: [
                  FilledButton.icon(
                    onPressed: url == null
                        ? null
                        : () => launchHttpUrl(Uri.parse(url)),
                    icon: const Icon(
                      PhosphorIconsRegular.arrowSquareOut,
                      size: 18,
                    ),
                    label: const Text('Open sign-in page'),
                  ),
                  TextButton.icon(
                    onPressed: () =>
                        Clipboard.setData(ClipboardData(text: code)),
                    icon: const Icon(PhosphorIconsRegular.copy, size: 18),
                    label: const Text('Copy code'),
                  ),
                ],
              ),
            ] else ...[
              const SizedBox(height: 14),
              FilledButton(
                key: const Key('codex-sign-in-start'),
                onPressed: _starting ? null : _start,
                child: Text(
                  _starting ? 'Getting a code…' : 'Sign in to ChatGPT',
                ),
              ),
            ],
            if (_error != null) ...[
              const SizedBox(height: 10),
              Text(_error!, style: TextStyle(color: colors.danger)),
            ],
          ],
        ),
      ),
    );
  }
}
