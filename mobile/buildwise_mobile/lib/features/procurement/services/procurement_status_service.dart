import 'dart:convert';

import '../../../core/api/api_client.dart';

/// The only procurement states the Site Engineer's Flutter view is allowed
/// to show (spec §9) — no supplier names, prices, or quotation detail.
enum ProcurementStatus {
  notStarted,
  quotationsInProgress,
  awaitingApproval,
  purchaseOrderCreated,
  rejected;

  static ProcurementStatus fromApi(String value) => switch (value) {
    'QuotationsInProgress' => ProcurementStatus.quotationsInProgress,
    'AwaitingApproval' => ProcurementStatus.awaitingApproval,
    'PurchaseOrderCreated' => ProcurementStatus.purchaseOrderCreated,
    'Rejected' => ProcurementStatus.rejected,
    _ => ProcurementStatus.notStarted,
  };
}

class ProcurementStatusInfo {
  const ProcurementStatusInfo({
    required this.materialRequestId,
    required this.status,
    this.purchaseOrderId,
  });

  final int materialRequestId;
  final ProcurementStatus status;
  final int? purchaseOrderId;

  factory ProcurementStatusInfo.fromJson(Map<String, dynamic> json) =>
      ProcurementStatusInfo(
        materialRequestId: json['materialRequestId'] as int,
        status: ProcurementStatus.fromApi(json['status'] as String),
        purchaseOrderId: json['purchaseOrderId'] as int?,
      );
}

/// Talks to BuildWise.Api's read-only, JWT-protected procurement-status
/// endpoint via the shared [ApiClient] (attaches the signed-in Site
/// Engineer's bearer token automatically).
class ProcurementStatusService {
  ProcurementStatusService({ApiClient? apiClient})
    : _apiClient = apiClient ?? ApiClient();

  final ApiClient _apiClient;

  Future<ProcurementStatusInfo> getStatus(int materialRequestId) async {
    final response = await _apiClient.get(
      '/material-requests/$materialRequestId/procurement-status',
    );

    if (response.statusCode != 200) {
      throw Exception(
        'Could not load procurement status (${response.statusCode}).',
      );
    }

    return ProcurementStatusInfo.fromJson(
      jsonDecode(response.body) as Map<String, dynamic>,
    );
  }
}
