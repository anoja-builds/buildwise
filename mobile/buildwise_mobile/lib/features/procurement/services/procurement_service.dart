import 'dart:convert';

import '../../../core/api/api_client.dart';
import '../../../core/api/api_exception.dart';
import '../models/procurement_models.dart';

class ProcurementService {
  ProcurementService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<dynamic> _get(String path) async {
    final response = await _apiClient.get(path);
    ApiException.check(response, accepted: const [200, 204]);
    if (response.body.trim().isEmpty) return null;
    return jsonDecode(utf8.decode(response.bodyBytes));
  }

  Future<List<Json>> _pages(String path) async {
    final result = <Json>[];
    var page = 1;
    while (true) {
      final data = await _get(
        '$path${path.contains('?') ? '&' : '?'}page=$page&pageSize=50',
      ) as Json;
      final items = records(data['items']);
      result.addAll(items);
      if (result.length >= (data['total'] as int)) return result;
      if (items.isEmpty) {
        throw const FormatException('Incomplete paginated response.');
      }
      page++;
    }
  }

  Future<List<Supplier>> getSuppliers({
    String search = '',
    String status = 'All',
  }) async {
    final query = Uri(
      queryParameters: {
        'search': search,
        if (status != 'All') 'status': status,
      },
    ).query;
    return (await _pages('/suppliers?$query')).map(Supplier.fromJson).toList();
  }

  Future<Supplier> getSupplier(int id) async =>
      Supplier.fromJson(await _get('/suppliers/$id') as Json);
  Future<List<Json>> getRequests() async =>
      records(await _get('/materialrequests'));
  Future<Json> getRequest(int id) async =>
      await _get('/materialrequests/$id') as Json;
  Future<List<Json>> getQuotations(int requestId) async =>
      records(await _get('/material-requests/$requestId/quotations'));
  Future<QuotationComparison> compare(int requestId) async =>
      QuotationComparison.fromJson(
        await _get('/material-requests/$requestId/quotations/compare') as Json,
      );
  Future<ProcurementWorkflow?> latestWorkflow(int requestId) async {
    final data = await _get(
      '/material-requests/$requestId/procurement-workflow',
    );
    return data == null ? null : ProcurementWorkflow.fromJson(data as Json);
  }

  Future<ProcurementWorkflow> getWorkflow(int id) async =>
      ProcurementWorkflow.fromJson(
        await _get('/procurement-workflow/$id') as Json,
      );
  Future<List<Json>> getHistory(int id) async =>
      records(await _get('/procurement-workflow/$id/history'));
  Future<int> startWorkflow(int requestId) async {
    final response = await _apiClient.post(
      '/material-requests/$requestId/procurement-workflow',
      body: {},
      timeout: const Duration(seconds: 120),
    );
    ApiException.check(response);
    return (jsonDecode(response.body) as Json)['workflowId'] as int;
  }

  Future<List<PurchaseOrder>> getOrders() async => (await getPurchaseOrders())
      .map((e) => PurchaseOrder.fromJson(e as Json))
      .toList();
  Future<PurchaseOrder> getOrder(int id) async =>
      PurchaseOrder.fromJson(await _get('/purchase-orders/$id') as Json);
  Future<List<Json>> getDeliveries() async =>
      records(await _get('/deliveries/history'));

  Future<ProcurementOverview> getOverview() async {
    final requests = await getRequests();
    final orders = await getOrders();
    final workflows = <ProcurementWorkflow>[];
    // There is no aggregate workflow endpoint; keep concurrent reads bounded.
    final relevant = requests
        .where((r) => ['Approved', 'Ordered'].contains(r['status']))
        .toList();
    for (var i = 0; i < relevant.length; i += 4) {
      final batch = relevant.skip(i).take(4);
      final results = await Future.wait(
        batch.map((r) => latestWorkflow(r['id'] as int)),
      );
      workflows.addAll(results.whereType<ProcurementWorkflow>());
    }
    return ProcurementOverview(requests, workflows, orders);
  }

  Future<List<dynamic>> getPurchaseOrders() async {
    final orders = <dynamic>[];
    var page = 1;
    while (true) {
      final response = await _apiClient.get(
        '/purchase-orders?page=$page&pageSize=50',
      );
      ApiException.check(response);
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
