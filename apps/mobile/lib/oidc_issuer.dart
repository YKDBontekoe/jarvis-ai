String normalizeOidcIssuer(String issuer) {
  var value = issuer.trim();
  while (value.length > 8 && value.endsWith('/')) {
    value = value.substring(0, value.length - 1);
  }
  return value;
}

bool oidcIssuersMatch(String configured, String discovered) =>
    normalizeOidcIssuer(configured) == normalizeOidcIssuer(discovered);
