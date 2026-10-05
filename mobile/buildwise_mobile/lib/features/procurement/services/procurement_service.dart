import 'dart:convert';

import '../../../core/api/api_client.dart';

/// Internal procurement client, mirroring the web app's `procurementApi`.
///
/// Unlike the web client this has **no mock fallback data**. The web app ships
/// hardcoded demo suppliers/quotations so its screens stay populated when the
/// API is down; on mobile that fallback would be worse than useless, because a
/// demo would show invented suppliers as if they were real records. Every value
/// rendered here comes from the live API or the screen shows an error.
class ProcurementService {
  ProcurementService({ApiClient? apiClient}) : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  /// Agents may call an LLM, so agent-backed calls get a longer budget than an
  /// ordinary read.
  static const Duration _agentTimeout = Duration(seconds: 90);

  // ------------------------------------------------------------ suppliers

  /// Active + suspended suppliers. Commercial detail, so this is only
  /// meaningful for the roles the API grants it to.
  Future<Map<String, dynamic>> listSuppliers({int pageSize = 100}) async {
    final response = await _apiClient.get('/suppliers?page=1&pageSize=$pageSize');
    return _object(response, 'load suppliers');
  }

  // ---------------------------------------------------- material requests

  /// Approved requests — the only ones an RFQ can be raised against.
  Future<List<Map<String, dynamic>>> listApprovedMaterialRequests() async {
    final response = await _apiClient.get('/material-requests?status=Approved');
    return _list(response, 'load approved requests');
  }

  Future<Map<String, dynamic>> getMaterialRequest(int id) async {
    final response = await _apiClient.get('/material-requests/$id');
    return _object(response, 'load material request');
  }

  // ---------------------------------------------------------- quotations

  Future<List<Map<String, dynamic>>> listQuotationsForRequest(int requestId) async {
    final response = await _apiClient.get('/material-requests/$requestId/quotations');
    return _list(response, 'load quotations');
  }

  /// Side-by-side comparison of every offer against the requested quantity.
  ///
  /// `coversFullQuantity` comes from the API, so the mobile client does not
  /// re-derive the eligibility rule the QuotationSupplierAnalysisAgent uses.
  Future<Map<String, dynamic>> compareQuotations(int requestId) async {
    final response = await _apiClient.get('/material-requests/$requestId/quotations/compare');
    return _object(response, 'compare quotations');
  }


  // ---------------------------------------------------------------- RFQs

  Future<List<Map<String, dynamic>>> listRfqs({String? status}) async {
    final query = (status == null || status.isEmpty) ? '' : '?status=$status';
    final response = await _apiClient.get('/rfqs$query');
    return _list(response, 'load RFQs');
  }

  /// Issues an RFQ against an approved request and invites the chosen suppliers.
  Future<Map<String, dynamic>> createRfq({
    required int materialRequestId,
    required String requiredResponseDate,
    List<int> supplierIds = const [],
    String notes = '',
  }) async {
    final response = await _apiClient.post('/rfqs', body: {
      'materialRequestId': materialRequestId,
      'requiredResponseDate': requiredResponseDate,
      'supplierIds': supplierIds,
      'notes': notes,
    });
    return _object(response, 'issue RFQ');
  }

  Future<Map<String, dynamic>> closeRfq(int id, String reason) async {
    final response = await _apiClient.post('/rfqs/$id/close', body: {'reason': reason});
    return _object(response, 'close RFQ');
  }

  // ------------------------------------------------------------ workflow

  /// Starts the QuotationSupplierAnalysisAgent workflow (:8001) for a request.
  Future<Map<String, dynamic>> startWorkflow(int requestId) async {
    final response = await _apiClient.post(
      '/material-requests/$requestId/procurement-workflow',
      timeout: _agentTimeout,
    );
    return _object(response, 'start procurement workflow');
  }

  Future<Map<String, dynamic>> getWorkflow(int workflowId) async {
    final response = await _apiClient.get('/procurement-workflow/$workflowId');
    return _object(response, 'load workflow');
  }

  /// The human decision at the approval boundary. The agent recommends a
  /// supplier; an authorized manager accepts or overrides it here.
  Future<Map<String, dynamic>> recordWorkflowDecision(
    int workflowId, {
    required String decision,
    String comment = '',
  }) async {
    final response = await _apiClient.post(
      '/procurement-workflow/$workflowId/decision',
      body: {'decision': decision, 'comment': comment},
    );
    return _object(response, 'record workflow decision');
  }

  // ------------------------------------------------------ purchase orders

  Future<Map<String, dynamic>> listPurchaseOrders({int page = 1, int pageSize = 20}) async {
    final response = await _apiClient.get('/purchase-orders?page=$page&pageSize=$pageSize');
    return _object(response, 'load purchase orders');
  }

  Future<Map<String, dynamic>> createPurchaseOrderFromWorkflow(int workflowId) async {
    final response = await _apiClient.post(
      '/procurement-workflow/$workflowId/purchase-order',
      timeout: _agentTimeout,
    );
    return _object(response, 'create purchase order');
  }

  // ------------------------------------------------------------- helpers

  Map<String, dynamic> _object(dynamic response, String label) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not $label'));
    }
    final decoded = jsonDecode(response.body);
    // A few endpoints answer 200 with an empty body; treat that as an empty
    // result instead of crashing on a null cast.
    if (decoded == null) return <String, dynamic>{};
    return decoded as Map<String, dynamic>;
  }

  List<Map<String, dynamic>> _list(dynamic response, String label) {
    if (response.statusCode != 200) {
      throw Exception(_error(response, 'Could not $label'));
    }
    final decoded = jsonDecode(response.body);
    if (decoded is! List) return const [];
    return decoded.cast<Map<String, dynamic>>();
  }

  String _error(dynamic response, String fallback) {
    try {
      final data = jsonDecode(response.body);
      if (data is Map && data['message'] is String) return data['message'] as String;
      if (data is Map && data['error'] is String) return data['error'] as String;
    } catch (_) {
      // no JSON body
    }
    return '$fallback (${response.statusCode})';
  }
}
