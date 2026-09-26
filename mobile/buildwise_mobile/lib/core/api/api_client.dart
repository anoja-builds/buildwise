import 'dart:convert';

import 'package:flutter/foundation.dart' show kIsWeb;
import 'package:flutter_secure_storage/flutter_secure_storage.dart';
import 'package:http/http.dart' as http;

/// Base URL for BuildWise.Api. Uses the browser host on web and the Android
/// emulator's host alias elsewhere; override per device with
/// `--dart-define=API_BASE_URL=...` (see docs/component2_setup_guide.md).
const String _defaultApiBaseUrl = kIsWeb
    ? 'http://localhost:5078/api'
    : 'http://10.0.2.2:5078/api';

const String apiBaseUrl = String.fromEnvironment(
  'API_BASE_URL',
  defaultValue: _defaultApiBaseUrl,
);

/// Thin shared HTTP client: attaches the signed-in user's JWT (from secure
/// storage) to every request and centralizes the auth-session shape so every
/// feature service talks to the same backend the same way.
class ApiClient {
  ApiClient({FlutterSecureStorage? storage, http.Client? client})
    : _storage = storage ?? const FlutterSecureStorage(),
      _client = client ?? http.Client(),
      _ownsClient = client == null;

  final FlutterSecureStorage _storage;
  final http.Client _client;
  final bool _ownsClient;

  Uri _uri(String path) => Uri.parse(
    '${apiBaseUrl.replaceFirst(RegExp(r'/+$'), '')}/'
    '${path.replaceFirst(RegExp(r'^/+'), '')}',
  );

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

  Future<http.Response> get(String path) async {
    final headers = await _authHeaders();
    return _client
        .get(_uri(path), headers: headers)
        .timeout(const Duration(seconds: 10));
  }

  Future<http.Response> post(
    String path, {
    Map<String, dynamic>? body,
    Duration timeout = const Duration(seconds: 10),
  }) async {
    final headers = await _authHeaders(json: true);
    return _client
        .post(
          _uri(path),
          headers: headers,
          body: body == null ? null : jsonEncode(body),
        )
        .timeout(timeout);
  }

  /// An injected HTTP client remains owned by its caller.
  void close() {
    if (_ownsClient) _client.close();
  }
}
