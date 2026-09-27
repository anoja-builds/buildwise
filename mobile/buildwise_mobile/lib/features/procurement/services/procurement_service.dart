import 'dart:convert';
import '../../../core/api/api_client.dart';

class ProcurementService {
  ProcurementService({ApiClient? apiClient})
      : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<List<dynamic>> getPurchaseOrders() async {
    final response = await _apiClient.get('/procurement/purchase-orders');
    if (response.statusCode == 200) {
      return json.decode(response.body);
    } else {
      throw Exception('Failed to load purchase orders (${response.statusCode})');
    }
  }
}

