import 'dart:convert';
import '../../../core/api/api_client.dart';

class MaterialRequestService {
  MaterialRequestService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<List<dynamic>> getRequests() async {
    final response = await _apiClient.get('/materialrequests');
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load material requests (${response.statusCode})');
    }
  }

  Future<void> createRequest(Map<String, dynamic> data) async {
    final response = await _apiClient.post(
      '/materialrequests',
      body: data,
    );
    if (response.statusCode != 201 && response.statusCode != 200) {
      throw Exception('Failed to create material request (${response.statusCode})');
    }
  }
}

