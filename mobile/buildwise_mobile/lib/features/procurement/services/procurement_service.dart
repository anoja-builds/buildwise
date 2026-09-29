import 'dart:convert';

import '../../../core/api/api_client.dart';

class ProcurementService {
  ProcurementService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<List<dynamic>> getPurchaseOrders() async {
    final orders = <dynamic>[];
    var page = 1;
    while (true) {
      final response = await _apiClient.get(
        '/purchase-orders?page=$page&pageSize=50',
      );
      if (response.statusCode != 200) {
        throw Exception(
          'Failed to load purchase orders (${response.statusCode})',
        );
      }
      final data = json.decode(response.body) as Map<String, dynamic>;
      final items = data['items'] as List<dynamic>;
      orders.addAll(items);
      if (orders.length >= (data['total'] as int)) return orders;
      if (items.isEmpty) {
        throw const FormatException('Incomplete purchase order response.');
      }
      page++;
    }
  }
}
