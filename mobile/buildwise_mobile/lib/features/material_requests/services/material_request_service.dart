import 'dart:convert';

import '../../../core/api/api_client.dart';
import '../../../core/api/api_exception.dart';

class MaterialRequestService {
  MaterialRequestService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();
  final ApiClient _apiClient;
  Future<dynamic> _get(String path) async {
    final response = await _apiClient.get(path);
    ApiException.check(response);
    return jsonDecode(utf8.decode(response.bodyBytes));
  }

  Future<Map<String, dynamic>> getOptions() async =>
      await _get('/materialrequests/options') as Map<String, dynamic>;
  Future<List<dynamic>> getRequests() async =>
      await _get('/materialrequests') as List<dynamic>;
  Future<Map<String, dynamic>> getRequest(int id) async =>
      await _get('/materialrequests/$id') as Map<String, dynamic>;
  Future<void> submitRequest(int id) async {
    ApiException.check(
      await _apiClient.post('/materialrequests/$id/submit', body: {}),
      accepted: const [200, 204],
    );
  }

  Future<void> createRequest(Map<String, dynamic> data) async {
    ApiException.check(
      await _apiClient.post('/materialrequests', body: data),
      accepted: const [200, 201],
    );
  }
}
