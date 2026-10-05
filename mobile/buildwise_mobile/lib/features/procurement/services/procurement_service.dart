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
  ProcurementService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  /// Agents may call an LLM, so agent-backed calls get a longer budget than an
  /// ordinary read.
  static const Duration _agentTimeout = Duration(seconds: 90);

  // ------------------------------------------------------------ suppliers

  /// Active + suspended suppliers. Commercial detail, so this is only
  /// meaningful for the roles the API grants it to.
  Future<Map<String, dynamic>> listSuppliers({int pageSize = 100}) async {
    final response = await _apiClient.get(
      '/suppliers?page=1&pageSize=$pageSize',
    );
    final result = _object(response, 'load suppliers');
    final rows = [...(result['items'] as List<dynamic>? ?? const [])];
    final total = (result['total'] as num?)?.toInt() ?? rows.length;
    final actualPageSize = (result['pageSize'] as num?)?.toInt() ?? pageSize;
    for (var page = 2; rows.length < total; page++) {
      final next = _object(
        await _apiClient.get('/suppliers?page=$page&pageSize=$actualPageSize'),
        'load suppliers',
      );
      final batch = next['items'] as List<dynamic>? ?? const [];
      if (batch.isEmpty) break;
      rows.addAll(batch);
    }
    return {...result, 'items': rows};
  }

  /// Registers a supplier contact record.
  ///
  /// Mirrors the web app's `procurementApi.createSupplier`: same endpoint, same
  /// `CreateSupplierDto` field names. A supplier is still an external party —
  /// this records how to reach them for an RFQ, it does not create a login.
  Future<Map<String, dynamic>> createSupplier({
    required String name,
    String contactPerson = '',
    String email = '',
    String phone = '',
    String address = '',
  }) async {
    final response = await _apiClient.post(
      '/suppliers',
      body: {
        'name': name,
        'contactPerson': contactPerson,
        'email': email,
        'phone': phone,
        'address': address,
      },
    );
    return _object(response, 'add supplier');
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

  Future<Map<String, dynamic>> getProjectBudget(int projectId) async {
    final response = await _apiClient.get('/projects/$projectId/budget');
    return _object(response, 'load project budget');
  }

  Future<Map<String, dynamic>> updateProjectBudget(
    int projectId,
    double? amount,
  ) async {
    final response = await _apiClient.put(
      '/projects/$projectId/budget',
      body: {'materialBudgetAmount': amount},
    );
    return _object(response, 'save project budget');
  }

  // ---------------------------------------------------------- quotations

  Future<List<Map<String, dynamic>>> listQuotationsForRequest(
    int requestId,
  ) async {
    final response = await _apiClient.get(
      '/material-requests/$requestId/quotations',
    );
    return _list(response, 'load quotations');
  }

  /// Records what a supplier quoted, for every line the officer chooses to price.
  ///
  /// Mirrors the web app's `procurementApi.createQuotation` exactly — same
  /// endpoint, same `CreateQuotationDto` shape — so a quotation keyed in on a
  /// phone is indistinguishable from one keyed in on the web.
  ///
  /// The API, not this client, decides whether the supplier is eligible. The
  /// validation here only stops obviously invalid input reaching the server.
  Future<Map<String, dynamic>> createQuotation(
    int requestId, {
    required int supplierId,
    required String quotationDate,
    required String validUntil,
    required List<Map<String, dynamic>> items,
    String? promisedDeliveryDate,
    int? rfqId,
    double transportCharge = 0,
    String? paymentTerms,
  }) async {
    final response = await _apiClient.post(
      '/material-requests/$requestId/quotations',
      body: {
        'supplierId': supplierId,
        'quotationDate': quotationDate,
        'validUntil': validUntil,
        'promisedDeliveryDate': promisedDeliveryDate,
        'rfqId': rfqId,
        'transportCharge': transportCharge,
        'paymentTerms': paymentTerms,
        'items': items,
      },
    );
    return _object(response, 'save quotation');
  }

  Future<void> deleteQuotation(int id) async {
    final response = await _apiClient.delete('/quotations/$id');
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not delete quotation'));
    }
  }

  /// Side-by-side comparison of every offer against the requested quantity.
  ///
  /// `coversFullQuantity` comes from the API, so the mobile client does not
  /// re-derive the eligibility rule the QuotationSupplierAnalysisAgent uses.
  Future<Map<String, dynamic>> compareQuotations(int requestId) async {
    final response = await _apiClient.get(
      '/material-requests/$requestId/quotations/compare',
    );
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
    final response = await _apiClient.post(
      '/rfqs',
      body: {
        'materialRequestId': materialRequestId,
        'requiredResponseDate': requiredResponseDate,
        'supplierIds': supplierIds,
        'notes': notes,
      },
    );
    return _object(response, 'issue RFQ');
  }

  Future<Map<String, dynamic>> closeRfq(int id, String reason) async {
    final response = await _apiClient.post(
      '/rfqs/$id/close',
      body: {'reason': reason},
    );
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

  Future<Map<String, dynamic>?> getLatestWorkflow(int requestId) async {
    final response = await _apiClient.get(
      '/material-requests/$requestId/procurement-workflow',
    );
    if (response.statusCode == 204) return null;
    return _object(response, 'load latest workflow');
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

  /// Purchase orders created from an approved procurement decision.
  ///
  /// Mirrors the web app's Purchase Orders list: same endpoint, same status
  /// filter. A purchase order only ever exists because a manager approved a
  /// workflow at the human gate — the quotation agent recommends, and
  /// [createPurchaseOrderFromWorkflow] is the step that turns an approved
  /// workflow into a real order. Nothing on this screen creates one directly.
  Future<List<Map<String, dynamic>>> listPurchaseOrders({
    int page = 1,
    int pageSize = 20,
    String? status,
    String? search,
  }) async {
    final query = <String>['page=$page', 'pageSize=$pageSize'];
    if (status != null && status.isNotEmpty) query.add('status=$status');
    if (search != null && search.isNotEmpty) {
      query.add('search=${Uri.encodeQueryComponent(search)}');
    }
    final response = await _apiClient.get(
      '/purchase-orders?${query.join('&')}',
    );
    return _list(response, 'load purchase orders');
  }

  /// The paged envelope the web list renders, kept for the count and paging.
  Future<Map<String, dynamic>> listPurchaseOrdersPage({
    int page = 1,
    int pageSize = 20,
    String? status,
    String? search,
  }) async {
    final query = <String>['page=$page', 'pageSize=$pageSize'];
    if (status != null && status.isNotEmpty) query.add('status=$status');
    if (search != null && search.isNotEmpty) {
      query.add('search=${Uri.encodeQueryComponent(search)}');
    }
    final response = await _apiClient.get(
      '/purchase-orders?${query.join('&')}',
    );
    return _object(response, 'load purchase orders');
  }

  Future<Map<String, dynamic>> getPurchaseOrder(int id) async {
    final response = await _apiClient.get('/purchase-orders/$id');
    return _object(response, 'load purchase order');
  }

  Future<Map<String, dynamic>> createPurchaseOrderFromWorkflow(
    int workflowId,
  ) async {
    final response = await _apiClient.post(
      '/procurement-workflow/$workflowId/purchase-order',
      timeout: _agentTimeout,
    );
    return _object(response, 'create purchase order');
  }

  Future<void> updatePurchaseOrderStatus(int id, String status) async {
    final response = await _apiClient.patch(
      '/purchase-orders/$id/status',
      body: {'status': status},
    );
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not update purchase order'));
    }
  }

  Future<Map<String, dynamic>> getSupplier(int id) async =>
      _object(await _apiClient.get('/suppliers/$id'), 'load supplier');

  Future<Map<String, dynamic>> updateSupplier(
    int id,
    Map<String, dynamic> data,
  ) async => _object(
    await _apiClient.put('/suppliers/$id', body: data),
    'update supplier',
  );

  Future<void> updateSupplierStatus(int id, String status) async {
    final response = await _apiClient.patch(
      '/suppliers/$id/status',
      body: {'status': status},
    );
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not update supplier status'));
    }
  }

  Future<Map<String, dynamic>> sendRfqEmail(
    int id, {
    required String recipientEmail,
    String? customMessage,
  }) async => _object(
    await _apiClient.post(
      '/rfqs/$id/send-email',
      body: {'recipientEmail': recipientEmail, 'customMessage': customMessage},
    ),
    'send RFQ email',
  );

  // ------------------------------------------------------------- helpers

  Map<String, dynamic> _object(dynamic response, String label) {
    if (response.statusCode < 200 || response.statusCode >= 300) {
      throw Exception(_error(response, 'Could not $label'));
    }
    final decoded = response.body.trim().isEmpty
        ? null
        : jsonDecode(response.body);
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
    final rows = decoded is List
        ? decoded
        : decoded is Map
        ? decoded['items']
        : null;
    if (rows is! List) {
      throw Exception('Invalid response while trying to $label.');
    }
    return rows.cast<Map<String, dynamic>>();
  }

  String _error(dynamic response, String fallback) {
    try {
      final data = jsonDecode(response.body);
      if (data is String && data.trim().isNotEmpty) return data.trim();
      if (data is Map && data['errors'] is Map) {
        final messages = (data['errors'] as Map).values
            .expand((value) => value is List ? value : [value])
            .where(
              (value) => value != null && value.toString().trim().isNotEmpty,
            )
            .map((value) => value.toString())
            .join(' ');
        if (messages.isNotEmpty) return messages;
      }
      if (data is Map && data['message'] is String) {
        return data['message'] as String;
      }
      if (data is Map && data['error'] is String) {
        return data['error'] as String;
      }
      if (data is Map && data['detail'] is String) {
        return data['detail'] as String;
      }
      if (data is Map && data['title'] is String) {
        return data['title'] as String;
      }
    } catch (_) {
      // no JSON body
    }
    return '$fallback (${response.statusCode})';
  }
}
