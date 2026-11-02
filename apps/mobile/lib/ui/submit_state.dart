import 'package:flutter/material.dart';

import 'jarvis_ui.dart';

/// Lets an editor run its own save request: the editor stays open and keeps
/// every typed value until the request succeeds, shows the failure beside the
/// input, and ignores repeated taps while a save is running.
mixin SubmitState<T extends StatefulWidget> on State<T> {
  bool submitting = false;
  String? submitError;

  /// Runs [action], which returns null on success or a message on failure.
  /// Returns true only when it succeeded (the caller then closes the editor).
  Future<bool> submit(Future<String?> Function() action) async {
    if (submitting) return false;
    setState(() {
      submitting = true;
      submitError = null;
    });
    String? error;
    try {
      error = await action();
    } catch (_) {
      error = 'Something went wrong. Try again.';
    }
    if (!mounted) return error == null;
    setState(() {
      submitting = false;
      submitError = error;
    });
    return error == null;
  }

  /// The failure notice to place above the editor's buttons.
  Widget submitErrorNotice() => submitError == null
      ? const SizedBox.shrink()
      : Padding(
          padding: const EdgeInsets.only(top: 12),
          child: InlineNotice(
            key: const Key('submit-error'),
            tone: NoticeTone.danger,
            message: submitError!,
          ),
        );
}

/// A filled button that shows a spinner and ignores taps while saving.
class SubmitButton extends StatelessWidget {
  const SubmitButton({
    required this.label,
    required this.busy,
    required this.onPressed,
    super.key,
  });

  final String label;
  final bool busy;
  final VoidCallback? onPressed;

  @override
  Widget build(BuildContext context) => FilledButton(
    onPressed: busy ? null : onPressed,
    child: busy
        ? const SizedBox.square(
            dimension: 18,
            child: CircularProgressIndicator(strokeWidth: 2),
          )
        : Text(label),
  );
}
