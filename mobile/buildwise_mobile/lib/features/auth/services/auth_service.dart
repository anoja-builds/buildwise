import 'dart:convert';

import '../../../core/api/api_client.dart';

/// Shared BuildWise login (same /api/auth endpoints React uses). Not owned by
/// any single component — every role signs in here.
class AuthService {
  AuthService({ApiClient? apiClient}) : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<Map<String, dynamic>> login(String email, String password) async {
    final response = await _apiClient.post(
      '/auth/login',
      body: {'email': email, 'password': password},
    );

    if (response.statusCode != 200) {
      throw Exception(_extractError(response.body) ?? 'Invalid email or password.');
    }

    final data = jsonDecode(response.body) as Map<String, dynamic>;
    final user = data['user'] as Map<String, dynamic>;
    await _apiClient.saveSession(data['token'] as String, user);
    return user;
  }

  Future<bool> isSignedIn() => _apiClient.isSignedIn();

  Future<Map<String, dynamic>?> currentUser() => _apiClient.readUser();

  Future<void> logout() => _apiClient.signOut();

  String? _extractError(String body) {
    try {
      final data = jsonDecode(body) as Map<String, dynamic>;
      return data['error'] as String?;
    } catch (_) {
      return null;
    }
  }
}
