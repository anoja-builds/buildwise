import 'dart:convert';

import '../../../core/api/api_client.dart';

class OperationsService {
  OperationsService({ApiClient? apiClient}) : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<Map<String, dynamic>> getDashboard() async {
    final response = await _apiClient.get('/dashboard');
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load role dashboard'));
    }
    return jsonDecode(response.body) as Map<String, dynamic>;
  }

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

  Future<List<Map<String, dynamic>>> listDeliveryIssues(int deliveryId) async {
    final response = await _apiClient.get('/deliveries/$deliveryId/issues');
    return _list(response, 'delivery issues');
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

  // ---------------------------------------------------------------------
  // AI agents.
  //
  // These are the same four analysis endpoints the web app uses, so a user on
  // either client gets the identical agent result. Each call returns the
  // backend's real response, including `agent`, `tool` and `executionSource`,
  // so the UI can show which agent actually answered rather than claiming
  // "AI analysis complete" from hardcoded copy. Re-running is safe: the agents
  // are read-only and never create or mutate a business record.
  //
  // These are slower than a normal read (the agents may call an LLM), so the
  // client timeout is raised for them.

  /// Agent 2 - RequestAnalysisAgent (:8002) over one material request.
  Future<Map<String, dynamic>> analyzeRequest(int materialRequestId) async {
    final response = await _apiClient.post(
      '/agent/analyze-request/$materialRequestId',
      timeout: _agentTimeout,
    );
    return _map(response, 'analyze material request');
  }

  /// Agent 3 - DeliveryDiscrepancyAgent (:8003) over one recorded delivery.
  Future<Map<String, dynamic>> analyzeDeliveryDiscrepancy(int deliveryId) async {
    final response = await _apiClient.post(
      '/deliveries/$deliveryId/discrepancy-analysis',
      timeout: _agentTimeout,
    );
    return _map(response, 'analyze delivery discrepancy');
  }

  /// Agent 4 - QualityRiskAnalysisAgent (:8004) over one completed inspection.
  Future<Map<String, dynamic>> analyzeQualityRisk(int inspectionId) async {
    final response = await _apiClient.post(
      '/quality-inspections/$inspectionId/risk-analysis',
      timeout: _agentTimeout,
    );
    return _map(response, 'analyze quality risk');
  }

  /// Agent 1 - QuotationSupplierAnalysisAgent (:8001). Starts the procurement
  /// workflow that filters and ranks eligible supplier quotations.
  Future<Map<String, dynamic>> startProcurementWorkflow(int materialRequestId) async {
    final response = await _apiClient.post(
      '/material-requests/$materialRequestId/procurement-workflow',
      timeout: _agentTimeout,
    );
    return _map(response, 'start procurement workflow');
  }

  /// One material request, including its items, for the detail view.
  Future<Map<String, dynamic>> getMaterialRequest(int id) async {
    final response = await _apiClient.get('/material-requests/$id');
    return _map(response, 'load material request');
  }

  /// Approve or reject a material request (ProcurementManager / Administrator).
  Future<Map<String, dynamic>> decideMaterialRequest(
    int id, {
    required String decision,
    String comments = '',
  }) async {
    final response = await _apiClient.post(
      '/material-requests/$id/decision',
      body: {'decision': decision, 'comments': comments},
    );
    return _map(response, 'record decision');
  }

  /// Agent workflow runs, newest first. The backend returns a paged envelope.
  Future<Map<String, dynamic>> listAgentWorkflows({int page = 1, int pageSize = 20}) async {
    final response = await _apiClient.get('/agent-workflows?page=$page&pageSize=$pageSize');
    return _map(response, 'load agent workflows');
  }

  /// One workflow with its per-step execution detail.
  Future<Map<String, dynamic>> getAgentWorkflow(int id) async {
    final response = await _apiClient.get('/agent-workflows/$id');
    return _map(response, 'load agent workflow');
  }

  /// Administrator-only: the user register.
  ///
  /// Returns a bare JSON array (not a paged envelope), so this is a list
  /// response — casting it to a map would throw at runtime.
  Future<List<dynamic>> listUsers({String? search}) async {
    final query = (search == null || search.isEmpty) ? '' : '?search=${Uri.encodeQueryComponent(search)}';
    final response = await _apiClient.get('/admin/users$query');
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load users'));
    }
    return jsonDecode(response.body) as List<dynamic>;
  }

  /// Administrator-only: recent audit trail. Also a bare JSON array.
  Future<List<dynamic>> listAuditLogs({int pageSize = 25}) async {
    final response = await _apiClient.get('/admin/audit-logs?pageSize=$pageSize');
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not load audit logs'));
    }
    return jsonDecode(response.body) as List<dynamic>;
  }

  /// Administrator-only: per-service health, including the four AI agents.
  Future<Map<String, dynamic>> getSystemHealth() async {
    final response = await _apiClient.get('/admin/health', timeout: _agentTimeout);
    return _map(response, 'load system health');
  }

  static const Duration _agentTimeout = Duration(seconds: 90);


  Future<List<Map<String, dynamic>>> listNonConformances() async {
    final response = await _apiClient.get('/quality-inspections/non-conformances');
    return _list(response, 'non-conformances');
  }

  /// Completes an inspection.
  ///
  /// [quantityCheck]..[defectsCheck] are the mandatory five-point checklist.
  /// They are required because the backend rejects a completion that omits any
  /// point: a completed inspection must state the outcome of every criterion
  /// rather than leave it implied by a blank field. `false` means the criterion
  /// was checked and failed; the parameters cannot be omitted at all.
  Future<Map<String, dynamic>> createInspection({
    required int deliveryId,
    required int materialId,
    required double inspected,
    required double accepted,
    required double rejected,
    required String reason,
    required bool quantityCheck,
    required bool visualConditionCheck,
    required bool moistureCheck,
    required bool packagingCheck,
    required bool defectsCheck,
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
      'quantityCheck': quantityCheck,
      'visualConditionCheck': visualConditionCheck,
      'moistureCheck': moistureCheck,
      'packagingCheck': packagingCheck,
      'defectsCheck': defectsCheck,
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
