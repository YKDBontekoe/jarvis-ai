String normalizeOidcIssuer(String issuer) {
  var value = issuer.trim();
  while (value.length > 8 && value.endsWith('/')) {
    value = value.substring(0, value.length - 1);
  }
  return value;
}

bool oidcIssuersMatch(String configured, String discovered) =>
    normalizeOidcIssuer(configured) == normalizeOidcIssuer(discovered);

/// Origin + path only. Query and fragment must never be part of an OIDC redirect.
Uri oidcRedirectOrigin(Uri location) {
  final path = location.path.isEmpty ? '/' : location.path;
  return Uri.parse('${location.origin}$path');
}
