import 'dart:convert';
import '../../../core/api/api_client.dart';

class DeliveryService {
  DeliveryService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<List<dynamic>> getExpectedDeliveries() async {
    final response = await _apiClient.get('/deliveries/expected');
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load expected deliveries (${response.statusCode})');
    }
  }

  Future<void> receiveDelivery(int id, Map<String, dynamic> data) async {
    final response = await _apiClient.post(
      '/deliveries/$id/receive',
      body: data,
    );
    if (response.statusCode != 200) {
      throw Exception('Failed to reconcile delivery (${response.statusCode})');
    }
  }

  Future<void> uploadEvidence(int id, String imageUrl) async {
    final response = await _apiClient.post(
      '/deliveries/$id/evidence',
      body: {'imageUrl': imageUrl},
    );
    if (response.statusCode != 200) {
      throw Exception('Failed to upload evidence (${response.statusCode})');
    }
  }
}

