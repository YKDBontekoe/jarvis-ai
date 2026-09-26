Future<Map<String, dynamic>?> completeAuthorizationCode(
  String issuer,
  String clientId,
  String redirectUri,
  List<String> scopes,
) async => null;

Future<void> beginAuthorizationCode(
  String issuer,
  String clientId,
  String redirectUri,
  List<String> scopes,
) async {
  throw UnsupportedError('Browser OIDC is available only in the web build.');
}

Future<Map<String, dynamic>> refreshAuthorizationTokens(
  String issuer,
  String clientId,
  String refreshToken,
) async {
  throw UnsupportedError('Browser OIDC is available only in the web build.');
}

String currentRedirectUri(String configuredRedirectUri) =>
    configuredRedirectUri;
