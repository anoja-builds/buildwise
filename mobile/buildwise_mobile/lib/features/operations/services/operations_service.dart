import 'dart:convert';

import '../../../core/api/api_client.dart';

class OperationsService {
  OperationsService({ApiClient? apiClient}) : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<List<Map<String, dynamic>>> listMyRequests() async {
    final response = await _apiClient.get('/material-requests/my');
    return _list(response, 'material requests');
  }

  Future<List<Map<String, dynamic>>> listRequests({String? status}) async {
    final query = status == null ? '' : '?status=$status';
    final response = await _apiClient.get('/material-requests$query');
    return _list(response, 'material requests');
  }

  Future<Map<String, dynamic>> getProcurementStatus(int materialRequestId) async {
    final response = await _apiClient.get('/material-requests/$materialRequestId/procurement-status');
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load procurement status'));
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

  Future<Map<String, dynamic>> createRequest({
    required int projectId,
    required String requiredDate,
    required int materialId,
    required double quantity,
    required String reason,
    String priority = 'Normal',
    String? siteNotes,
    String? description,
    String? unit,
    String? itemRequiredDate,
  }) async {
    final response = await _apiClient.post('/material-requests', body: {
      'projectId': projectId,
      'requiredDate': requiredDate,
      'priority': priority,
      'reason': reason,
      'siteNotes': siteNotes,
      'status': 'Draft',
      'items': [
        {
          'materialId': materialId,
          'requestedQuantity': quantity,
          'unit': unit,
          'description': description,
          'requiredDate': itemRequiredDate,
          'notes': description,
        },
      ],
    });
    return _map(response, 'create material request');
  }

  Future<List<Map<String, dynamic>>> getMaterialRequestHistory(int id) async {
    final response = await _apiClient.get('/material-requests/$id/history');
    return _list(response, 'material request history');
  }

  Future<Map<String, dynamic>> reviseRequest(int id, Map<String, dynamic> payload) async {
    final response = await _apiClient.post('/material-requests/$id/revise', body: payload);
    return _map(response, 'revise material request');
  }

  Future<List<Map<String, dynamic>>> listConfirmedOrders() async {
    final response = await _apiClient.get('/deliveries/confirmed-orders');
    return _list(response, 'confirmed purchase orders');
  }

  Future<List<Map<String, dynamic>>> listDeliveries() async {
    final response = await _apiClient.get('/deliveries');
    return _list(response, 'deliveries');
  }

  Future<Map<String, dynamic>> recordDelivery({
    required int purchaseOrderId,
    required String reference,
    required int materialId,
    required double received,
    required double damaged,
  }) async {
    final response = await _apiClient.post('/deliveries', body: {
      'purchaseOrderId': purchaseOrderId,
      'deliveryReference': reference,
      'status': 'Arrived',
      'items': [
        {'materialId': materialId, 'receivedQuantity': received, 'damagedQuantity': damaged},
      ],
    });
    return _map(response, 'record delivery');
  }

  Future<List<Map<String, dynamic>>> listInspections({String? status}) async {
    final query = status == null ? '' : '?status=$status';
    final response = await _apiClient.get('/quality-inspections$query');
    return _list(response, 'quality inspections');
  }

  Future<List<Map<String, dynamic>>> listNonConformances() async {
    final response = await _apiClient.get('/quality-inspections/non-conformances');
    return _list(response, 'non-conformances');
  }

  Future<Map<String, dynamic>> createInspection({
    required int deliveryId,
    required int materialId,
    required double inspected,
    required double accepted,
    required double rejected,
    required String reason,
    String? criteria,
    String? observedResult,
    String? notes,
    List<Map<String, dynamic>> evidence = const [],
  }) async {
    final response = await _apiClient.post('/quality-inspections', body: {
      'deliveryId': deliveryId,
      'inspectionCriteria': criteria,
      'observedResult': observedResult,
      'notes': notes,
      'evidence': evidence,
      'items': [{
        'materialId': materialId,
        'inspectedQuantity': inspected,
        'acceptedQuantity': accepted,
        'rejectedQuantity': rejected,
        'rejectionReason': reason,
      }],
    });
    return _map(response, 'complete inspection');
  }

  Future<Map<String, dynamic>> transitionNonConformance(int id, Map<String, dynamic> payload) async {
    final response = await _apiClient.post('/quality-inspections/non-conformances/$id/transition', body: payload);
    return _map(response, 'update non-conformance');
  }

  Future<List<Map<String, dynamic>>> listNotifications({bool unreadOnly = false}) async {
    final response = await _apiClient.get('/notifications?unreadOnly=$unreadOnly');
    return _list(response, 'notifications');
  }

  Future<void> markNotificationRead(int id) async {
    final response = await _apiClient.put('/notifications/$id/read');
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not mark notification read'));
    }
  }

  List<Map<String, dynamic>> _list(dynamic response, String label) {
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load $label'));
    }
    final decoded = jsonDecode(response.body) as List<dynamic>;
    return decoded.cast<Map<String, dynamic>>();
  }

  Map<String, dynamic> _map(dynamic response, String label) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not $label'));
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

  String _error(dynamic response, String fallback) {
    try {
      final body = jsonDecode(response.body as String) as Map<String, dynamic>;
      return (body['message'] ?? body['error'] ?? fallback).toString();
    } catch (_) {
      return '$fallback (${response.statusCode})';
    }
  }
}
