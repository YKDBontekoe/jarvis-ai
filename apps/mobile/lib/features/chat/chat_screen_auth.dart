part of 'chat_screen.dart';

// ignore_for_file: annotate_overrides

mixin _ChatScreenAuth on _ChatScreenController {
  Widget _signInScreen() {
    final busy = _authBusy || _signingOut;
    return Scaffold(
      body: Stack(
        children: [
          const Positioned.fill(child: _AmbientBackdrop()),
          SafeArea(
            child: Center(
              child: SingleChildScrollView(
                padding: const EdgeInsets.all(28),
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 400),
                  child: AutofillGroup(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        const JarvisOrb(size: 96, semanticLabel: 'Jarvis'),
                        const SizedBox(height: 32),
                        Text(
                          _creatingAccount
                              ? 'Create your account'
                              : 'Sign in to Jarvis',
                          textAlign: TextAlign.center,
                          style: JarvisType.displayOf(
                            context,
                          ).copyWith(fontSize: 42),
                        ),
                        const SizedBox(height: 10),
                        Text(
                          'Your private assistant for conversations, tasks, memory, and voice.',
                          textAlign: TextAlign.center,
                          style: Theme.of(context).textTheme.bodyLarge
                              ?.copyWith(
                                color: JarvisColors.of(context).inkSoft,
                              ),
                        ),
                        const SizedBox(height: 32),
                        TextField(
                          controller: _email,
                          enabled: !busy,
                          keyboardType: TextInputType.emailAddress,
                          textInputAction: TextInputAction.next,
                          autofillHints: const [AutofillHints.email],
                          autocorrect: false,
                          enableSuggestions: false,
                          decoration: const InputDecoration(labelText: 'Email'),
                        ),
                        const SizedBox(height: 12),
                        TextField(
                          controller: _password,
                          enabled: !busy,
                          obscureText: _obscurePassword,
                          textInputAction: TextInputAction.done,
                          autofillHints: [
                            _creatingAccount
                                ? AutofillHints.newPassword
                                : AutofillHints.password,
                          ],
                          onSubmitted: busy
                              ? null
                              : (_) => unawaited(_signIn()),
                          decoration: InputDecoration(
                            labelText: 'Password',
                            suffixIcon: IconButton(
                              tooltip: _obscurePassword
                                  ? 'Show password'
                                  : 'Hide password',
                              onPressed: () => setState(
                                () => _obscurePassword = !_obscurePassword,
                              ),
                              icon: Icon(
                                _obscurePassword
                                    ? PhosphorIconsRegular.eye
                                    : PhosphorIconsRegular.eyeSlash,
                              ),
                            ),
                          ),
                        ),
                        const SizedBox(height: 8),
                        Align(
                          alignment: Alignment.centerLeft,
                          child: Text(
                            'Use 8 or more characters with upper and lower case letters and a number.',
                            style: TextStyle(
                              fontSize: 12.5,
                              color: JarvisColors.of(context).muted,
                            ),
                          ),
                        ),
                        const SizedBox(height: 20),
                        SizedBox(
                          width: double.infinity,
                          child: FilledButton.icon(
                            onPressed: busy ? null : () => unawaited(_signIn()),
                            style: FilledButton.styleFrom(
                              backgroundColor: JarvisColors.of(context).ink,
                              minimumSize: const Size.fromHeight(54),
                              shape: RoundedRectangleBorder(
                                borderRadius: BorderRadius.circular(40),
                              ),
                            ),
                            icon: _authBusy
                                ? const SizedBox(
                                    width: 18,
                                    height: 18,
                                    child: CircularProgressIndicator(
                                      strokeWidth: 2,
                                    ),
                                  )
                                : Icon(
                                    _creatingAccount
                                        ? PhosphorIconsRegular.user
                                        : PhosphorIconsRegular.signIn,
                                  ),
                            label: Text(
                              _creatingAccount ? 'Create account' : 'Sign in',
                            ),
                          ),
                        ),
                        const SizedBox(height: 8),
                        TextButton(
                          onPressed: busy
                              ? null
                              : () => setState(() {
                                  _creatingAccount = !_creatingAccount;
                                  _error = null;
                                }),
                          child: Text(
                            _creatingAccount
                                ? 'Already have an account? Sign in'
                                : 'Need an account? Create one',
                          ),
                        ),
                        const SizedBox(height: 8),
                        Text(
                          'Your password is checked by Jarvis.',
                          textAlign: TextAlign.center,
                          style: TextStyle(
                            fontSize: 12.5,
                            color: JarvisColors.of(context).muted,
                          ),
                        ),
                        if (_error != null)
                          InlineNotice(
                            message: _error!,
                            tone: NoticeTone.danger,
                            margin: const EdgeInsets.only(top: 24),
                          ),
                      ],
                    ),
                  ),
                ),
              ),
            ),
          ),
        ],
      ),
    );
  }
}

/// Soft, blurred colour fields behind full-screen moments (sign-in).
class _AmbientBackdrop extends StatelessWidget {
  const _AmbientBackdrop();

  @override
  Widget build(BuildContext context) => const IgnorePointer(
    child: Stack(
      children: [
        Positioned(
          top: -120,
          left: -80,
          child: _Blob(size: 360, color: Color(0x1c7c6cff)),
        ),
        Positioned(
          bottom: -140,
          right: -100,
          child: _Blob(size: 420, color: Color(0x1638bdf8)),
        ),
        Positioned(
          top: 180,
          right: -60,
          child: _Blob(size: 220, color: Color(0x12f472b6)),
        ),
      ],
    ),
  );
}

class _Blob extends StatelessWidget {
  const _Blob({required this.size, required this.color});

  final double size;
  final Color color;

  @override
  Widget build(BuildContext context) => Container(
    width: size,
    height: size,
    decoration: BoxDecoration(
      shape: BoxShape.circle,
      gradient: RadialGradient(colors: [color, color.withValues(alpha: 0)]),
    ),
  );
}
