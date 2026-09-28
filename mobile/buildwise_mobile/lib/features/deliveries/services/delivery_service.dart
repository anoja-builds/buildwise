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

  Future<Map<String, dynamic>> getDeliveryById(int id) async {
    final response = await _apiClient.get('/deliveries/$id');
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load delivery details (${response.statusCode})');
    }
  }

  Future<List<dynamic>> getDeliveryHistory() async {
    final response = await _apiClient.get('/deliveries/history');
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load delivery history (${response.statusCode})');
    }
  }

  Future<Map<String, dynamic>> receiveDelivery(int id, Map<String, dynamic> data) async {
    final response = await _apiClient.post(
      '/deliveries/$id/receive',
      body: data,
    );
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      final errorText = response.body;
      throw Exception(errorText.isNotEmpty ? errorText : 'Failed to reconcile delivery (${response.statusCode})');
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

  Future<Map<String, dynamic>> analyzeDiscrepancies(int deliveryId) async {
    final response = await _apiClient.post(
      '/deliveries/$deliveryId/discrepancy-analysis',
      timeout: const Duration(seconds: 30),
    );
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      final errorText = response.body;
      throw Exception(errorText.isNotEmpty ? errorText : 'Discrepancy analysis failed (${response.statusCode})');
    }
  }

  Future<List<dynamic>> getDiscrepancyHistory(int deliveryId) async {
    final response = await _apiClient.get('/deliveries/$deliveryId/discrepancy-history');
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load discrepancy history (${response.statusCode})');
    }
  }
}
