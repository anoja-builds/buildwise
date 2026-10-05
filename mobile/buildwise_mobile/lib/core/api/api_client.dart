import 'dart:convert';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

/// Compile-time override, set with `--dart-define=API_BASE_URL=...`.
///
/// This is still the right tool for a physical phone or a remote server, where
/// the host cannot be inferred. It is no longer required for normal local use:
/// when it is absent the URL is derived from the platform (see
/// [defaultApiBaseUrl]).
const String _apiBaseUrlOverride = String.fromEnvironment('API_BASE_URL');

/// Resolves the API origin for the current platform.
///
/// Previously this was hard-coded to `10.0.2.2`, which is only meaningful on
/// the Android emulator. Launched in a browser that address does not resolve, so
/// every request failed with an opaque "Failed to fetch" and login was
/// impossible. Deriving the origin keeps the app working out of the box on web
/// and desktop while preserving the emulator behaviour on Android.
String defaultApiBaseUrl() {
  if (kIsWeb) {
    // The app is served from the same machine as the API in local development,
    // so reuse the browser's host and only swap the port.
    final host = Uri.base.host.isEmpty ? 'localhost' : Uri.base.host;
    final scheme = Uri.base.scheme.isEmpty ? 'http' : Uri.base.scheme;
    return '$scheme://$host:5078/api';
  }
  // Android emulator: 10.0.2.2 is the host machine's loopback.
  return 'http://10.0.2.2:5078/api';
}

/// Base URL for BuildWise.Api.
///
/// Override per device with `--dart-define=API_BASE_URL=...`; otherwise it is
/// derived from the platform (see docs/component2_setup_guide.md).
String get apiBaseUrl =>
    _apiBaseUrlOverride.isNotEmpty ? _apiBaseUrlOverride : defaultApiBaseUrl();

/// Thin shared HTTP client: attaches the signed-in user's JWT (from secure
/// storage) to every request and centralizes the auth-session shape so every
/// feature service talks to the same backend the same way.
class ApiClient {
  ApiClient({FlutterSecureStorage? storage, http.Client? client})
    : _storage = storage ?? const FlutterSecureStorage(),
      _client = client ?? http.Client();

  final FlutterSecureStorage _storage;
  final http.Client _client;

  static const _tokenKey = 'buildwise.jwt';
  static const _userKey = 'buildwise.user';

  Future<void> saveSession(String token, Map<String, dynamic> user) async {
    await _storage.write(key: _tokenKey, value: token);
    await _storage.write(key: _userKey, value: jsonEncode(user));
  }

  Future<String?> readToken() => _storage.read(key: _tokenKey);

  Future<Map<String, dynamic>?> readUser() async {
    final raw = await _storage.read(key: _userKey);
    if (raw == null) return null;
    return jsonDecode(raw) as Map<String, dynamic>;
  }

  Future<bool> isSignedIn() async => (await readToken()) != null;

  Future<void> signOut() async {
    await _storage.delete(key: _tokenKey);
    await _storage.delete(key: _userKey);
  }

  Future<Map<String, String>> _authHeaders({bool json = false}) async {
    final token = await readToken();
    return {
      if (json) 'Content-Type': 'application/json',
      if (token != null) 'Authorization': 'Bearer $token',
    };
  }

  /// Shared timeout for ordinary reads and writes.
  static const Duration _defaultTimeout = Duration(seconds: 10);

  /// [timeout] exists because the AI agent endpoints call a Python service that
  /// may reach an LLM, which regularly outlives the normal request budget. A
  /// 10s cap would surface a misleading "Could not analyze…" error on a call
  /// that is still legitimately running.
  Future<http.Response> get(String path, {Duration? timeout}) async {
    final headers = await _authHeaders();
    return _client
        .get(Uri.parse('$apiBaseUrl$path'), headers: headers)
        .timeout(timeout ?? _defaultTimeout);
  }

  Future<http.Response> put(
    String path, {
    Map<String, dynamic>? body,
    Duration? timeout,
  }) async {
    final headers = await _authHeaders(json: true);
    return _client
        .put(Uri.parse('$apiBaseUrl$path'), headers: headers, body: body == null ? null : jsonEncode(body))
        .timeout(timeout ?? _defaultTimeout);
  }

  Future<http.Response> post(
    String path, {
    Map<String, dynamic>? body,
    Duration? timeout,
  }) async {
    final headers = await _authHeaders(json: true);
    return _client
        .post(
          Uri.parse('$apiBaseUrl$path'),
          headers: headers,
          body: body == null ? null : jsonEncode(body),
        )
        .timeout(timeout ?? _defaultTimeout);
  }
}
