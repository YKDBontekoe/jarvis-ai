import 'package:flutter/material.dart';

import 'jarvis_ui.dart';
import 'phosphor_icons.dart';

/// Asks before an editor closes with edits that were not saved.
///
/// [snapshot] describes every editable value as one string; the editor counts
/// as changed when it differs from the snapshot taken when it first opened.
/// Back, swipe-back and tapping outside offer Keep editing or Discard only when
/// something changed. Closing after a successful save (`Navigator.pop`) is never intercepted.
class UnsavedChangesGuard extends StatefulWidget {
  const UnsavedChangesGuard({
    required this.snapshot,
    required this.child,
    this.changes,
    this.enabled = true,
    this.onDiscard,
    super.key,
  });

  /// Describes the current values, e.g. the controllers' text joined together.
  final String Function() snapshot;

  /// Notifies when [snapshot] may have changed (usually the text controllers).
  final Listenable? changes;

  /// False while a save is running or after it finished.
  final bool enabled;

  /// Called when the owner chooses Discard, just before the editor closes.
  final Future<void> Function()? onDiscard;
  final Widget child;

  @override
  State<UnsavedChangesGuard> createState() => _UnsavedChangesGuardState();
}

class _UnsavedChangesGuardState extends State<UnsavedChangesGuard> {
  late final String _initial = widget.snapshot();

  bool get _dirty => widget.enabled && widget.snapshot() != _initial;

  Future<void> _confirmDiscard() async {
    final discard = await showJarvisConfirm(
      context,
      title: 'Discard your changes?',
      message: 'What you changed here has not been saved.',
      cancelLabel: 'Keep editing',
      confirmLabel: 'Discard',
      destructive: true,
      icon: PhosphorIconsRegular.trash,
    );
    if (!discard) return;
    await widget.onDiscard?.call();
    if (mounted) Navigator.of(context).pop();
  }

  @override
  Widget build(BuildContext context) => ListenableBuilder(
    listenable: widget.changes ?? _none,
    builder: (context, _) => PopScope(
      canPop: !_dirty,
      onPopInvokedWithResult: (didPop, _) {
        if (!didPop) _confirmDiscard();
      },
      child: widget.child,
    ),
  );
}

final _none = ValueNotifier<int>(0);
