import 'dart:convert';

import '../../../core/api/api_client.dart';
import '../../../core/api/api_exception.dart';

class DeliveryService {
  DeliveryService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();
  final ApiClient _apiClient;
  Future<dynamic> _get(String path) async {
    final response = await _apiClient.get(path);
    ApiException.check(response);
    return jsonDecode(utf8.decode(response.bodyBytes));
  }

  Future<List<dynamic>> getExpectedDeliveries() async =>
      await _get('/deliveries/expected') as List<dynamic>;
  Future<Map<String, dynamic>> getDeliveryById(int id) async =>
      await _get('/deliveries/$id') as Map<String, dynamic>;
  Future<List<dynamic>> getDeliveryHistory() async =>
      await _get('/deliveries/history') as List<dynamic>;
  Future<Map<String, dynamic>> receiveDelivery(
    int id,
    Map<String, dynamic> data,
  ) async {
    final response = await _apiClient.post(
      '/deliveries/$id/receive',
      body: data,
    );
    ApiException.check(response);
    return jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
  }

  Future<void> uploadEvidence(int id, String imageUrl) async {
    ApiException.check(
      await _apiClient.post(
        '/deliveries/$id/evidence',
        body: {'imageUrl': imageUrl},
      ),
    );
  }

  Future<Map<String, dynamic>> analyzeDiscrepancies(int deliveryId) async {
    final response = await _apiClient.post(
      '/deliveries/$deliveryId/discrepancy-analysis',
      timeout: const Duration(seconds: 120),
    );
    ApiException.check(response);
    return jsonDecode(utf8.decode(response.bodyBytes)) as Map<String, dynamic>;
  }

  Future<List<dynamic>> getDiscrepancyHistory(int deliveryId) async =>
      await _get('/deliveries/$deliveryId/discrepancy-history')
          as List<dynamic>;
}
