import 'dart:convert';

import '../../../core/api/api_client.dart';

/// Client for the external Supplier portal (`/api/supplier-portal/*`).
///
/// Every endpoint is scoped server-side to the supplier bound to the signed-in
/// user's JWT, so this client never sends a supplier id. Keep it that way: a
/// `supplierId` parameter here would be a security smell, not a feature.
class SupplierPortalService {
  SupplierPortalService({ApiClient? apiClient}) : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<Map<String, dynamic>> getProfile() async {
    final response = await _apiClient.get('/supplier-portal/profile');
    return _object(response, 'supplier profile');
  }

  Future<List<Map<String, dynamic>>> listRfqs() async {
    final response = await _apiClient.get('/supplier-portal/rfqs');
    return _list(response, 'RFQs');
  }

  Future<List<Map<String, dynamic>>> getRfqItems(int rfqId) async {
    final response = await _apiClient.get('/supplier-portal/rfqs/$rfqId/items');
    return _list(response, 'RFQ lines');
  }

  Future<List<Map<String, dynamic>>> listQuotations() async {
    final response = await _apiClient.get('/supplier-portal/quotations');
    return _list(response, 'quotations');
  }

  Future<List<Map<String, dynamic>>> listPurchaseOrders() async {
    final response = await _apiClient.get('/supplier-portal/purchase-orders');
    return _list(response, 'purchase orders');
  }

  /// Submits a quotation against an RFQ this supplier was invited to.
  /// The API computes the total from quantity x unit price; any total sent by
  /// the client is ignored.
  Future<Map<String, dynamic>> submitQuotation(int rfqId, Map<String, dynamic> payload) async {
    final response = await _apiClient.post(
      '/supplier-portal/rfqs/$rfqId/quotations',
      body: payload,
    );
    return _object(response, 'quotation');
  }

  Map<String, dynamic> _object(dynamic response, String what) {
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load $what'));
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

  List<Map<String, dynamic>> _list(dynamic response, String what) {
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load $what'));
    }
    final decoded = jsonDecode(response.body);
    if (decoded is! List) return const [];
    return decoded.cast<Map<String, dynamic>>();
  }

  String _error(dynamic response, String fallback) {
    try {
      final data = jsonDecode(response.body);
      if (data is Map && data['message'] is String) return data['message'] as String;
    } catch (_) {
      // no JSON body
    }
    return fallback;
  }
}