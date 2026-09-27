import 'dart:convert';
import 'dart:math';

import 'package:crypto/crypto.dart';
import 'package:dio/dio.dart';
import 'package:web/web.dart' as web;
import 'json_maps.dart';
import 'oidc_issuer.dart';

const _stateKey = 'jarvis_oidc_state';
const _verifierKey = 'jarvis_oidc_pkce_verifier';
const _openidDiscoverySuffix = '.well-known/openid-configuration';
final _http = Dio(
  BaseOptions(
    connectTimeout: const Duration(seconds: 10),
    receiveTimeout: const Duration(seconds: 20),
  ),
);

String currentRedirectUri(String configuredRedirectUri) {
  final redirectUri = configuredRedirectUri.isNotEmpty
      ? Uri.parse(configuredRedirectUri)
      : Uri.base.replace(query: null, fragment: null);
  final localDevelopmentHost =
      redirectUri.host == 'localhost' ||
      redirectUri.host == '127.0.0.1' ||
      redirectUri.host == '[::1]' ||
      redirectUri.host == '::1';
  if ((redirectUri.scheme != 'https' &&
          !(redirectUri.scheme == 'http' && localDevelopmentHost)) ||
      redirectUri.host.isEmpty ||
      redirectUri.userInfo.isNotEmpty ||
      redirectUri.hasQuery ||
      redirectUri.hasFragment) {
    throw StateError(
      'The web OIDC redirect must use HTTPS and contain no credentials, query, or fragment.',
    );
  }
  return redirectUri.toString();
}

Future<Map<String, dynamic>?> completeAuthorizationCode(
  String issuer,
  String clientId,
  String redirectUri,
  List<String> scopes,
) async {
  final callback = Uri.parse(web.window.location.href);
  final code = callback.queryParameters['code'];
  final returnedState = callback.queryParameters['state'];
  final error = callback.queryParameters['error'];
  if (code == null && error == null) return null;

  final session = web.window.sessionStorage;
  final expectedState = session.getItem(_stateKey);
  final verifier = session.getItem(_verifierKey);
  session.removeItem(_stateKey);
  session.removeItem(_verifierKey);
  web.window.history.replaceState(null, web.document.title, redirectUri);

  if (error != null) {
    if (expectedState == null || verifier == null || returnedState != expectedState)
      return null;
    throw StateError('The identity provider denied sign in.');
  }
  if (code == null ||
      expectedState == null ||
      verifier == null ||
      returnedState != expectedState) {
    return null;
  }

  final metadata = await _loadMetadata(issuer);
  final result = await _exchange(metadata['token_endpoint']!, {
    'grant_type': 'authorization_code',
    'client_id': clientId,
    'code': code,
    'redirect_uri': redirectUri,
    'code_verifier': verifier,
  });
  return result;
}

Future<void> beginAuthorizationCode(
  String issuer,
  String clientId,
  String redirectUri,
  List<String> scopes,
) async {
  final metadata = await _loadMetadata(issuer);
  final verifier = _randomUrlSafe(64);
  final state = _randomUrlSafe(32);
  final challenge = base64UrlEncode(
    sha256.convert(ascii.encode(verifier)).bytes,
  ).replaceAll('=', '');
  final session = web.window.sessionStorage;
  session.setItem(_stateKey, state);
  session.setItem(_verifierKey, verifier);

  final authorize = Uri.parse(metadata['authorization_endpoint']!).replace(
    queryParameters: {
      'client_id': clientId,
      'redirect_uri': redirectUri,
      'response_type': 'code',
      'scope': scopes.join(' '),
      'state': state,
      'code_challenge': challenge,
      'code_challenge_method': 'S256',
    },
  );
  web.window.location.assign(authorize.toString());
}

Future<Map<String, dynamic>> refreshAuthorizationTokens(
  String issuer,
  String clientId,
  String refreshToken,
) async {
  final metadata = await _loadMetadata(issuer);
  return _exchange(metadata['token_endpoint']!, {
    'grant_type': 'refresh_token',
    'client_id': clientId,
    'refresh_token': refreshToken,
  });
}

Future<Map<String, String>> _loadMetadata(String issuer) async {
  final uri = Uri.parse(issuer);
  if (uri.scheme != 'https' ||
      uri.host.isEmpty ||
      uri.userInfo.isNotEmpty ||
      uri.hasQuery ||
      uri.hasFragment) {
    throw StateError('The OIDC issuer must be a valid HTTPS URL.');
  }
  final path = uri.path;
  final issuerPath = path.endsWith('/') ? path : '$path/';
  final discovery = uri.replace(path: '$issuerPath$_openidDiscoverySuffix');
  final response = await _http.get<Map<String, dynamic>>(discovery.toString());
  final document = response.data;
  final discoveredIssuer = asJsonString(document?['issuer']);
  final authorizationEndpoint = asJsonString(document?['authorization_endpoint']);
  final tokenEndpoint = asJsonString(document?['token_endpoint']);
  if (discoveredIssuer == null ||
      !oidcIssuersMatch(discoveredIssuer, uri.toString()) ||
      authorizationEndpoint == null ||
      tokenEndpoint == null) {
    throw StateError('The OIDC discovery document is incomplete.');
  }
  final authorizationUri = Uri.parse(authorizationEndpoint);
  final tokenUri = Uri.parse(tokenEndpoint);
  if (authorizationUri.scheme != 'https' ||
      authorizationUri.host.isEmpty ||
      tokenUri.scheme != 'https' ||
      tokenUri.host.isEmpty ||
      authorizationUri.userInfo.isNotEmpty ||
      tokenUri.userInfo.isNotEmpty ||
      authorizationUri.hasFragment ||
      tokenUri.hasFragment) {
    throw StateError(
      'The OIDC provider must use HTTPS authorization and token endpoints.',
    );
  }
  return {
    'authorization_endpoint': authorizationEndpoint,
    'token_endpoint': tokenEndpoint,
  };
}

Future<Map<String, dynamic>> _exchange(
  String endpoint,
  Map<String, String> values,
) async {
  final response = await _http.post<Map<String, dynamic>>(
    endpoint,
    data: values,
    options: Options(contentType: Headers.formUrlEncodedContentType),
  );
  final data = response.data;
  if (data == null ||
      data['access_token'] is! String ||
      data['token_type'] is! String ||
      (data['token_type'] as String).toLowerCase() != 'bearer') {
    throw StateError(
      'The identity provider returned an incomplete bearer token response.',
    );
  }
  return data;
}

String _randomUrlSafe(int byteCount) {
  final random = Random.secure();
  final bytes = List<int>.generate(byteCount, (_) => random.nextInt(256));
  return base64UrlEncode(bytes).replaceAll('=', '');
}
