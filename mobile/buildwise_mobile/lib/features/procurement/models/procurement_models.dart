typedef Json = Map<String, dynamic>;
List<Json> records(Object? value) => (value as List? ?? [])
    .map((e) => Map<String, dynamic>.from(e as Map))
    .toList();

class Supplier {
  Supplier.fromJson(Json json)
    : id = json['id'] as int,
      name = json['name'] as String,
      status = json['status'] as String,
      contact = json['contactPerson'] as String?,
      phone = json['phone'] as String?,
      email = json['email'] as String?,
      address = json['address'] as String?,
      quotations = records(json['quotationHistory']);
  final int id;
  final String name, status;
  final String? contact, phone, email, address;
  final List<Json> quotations;
}

class QuotationComparison {
  QuotationComparison.fromJson(Json json)
    : requestId = json['materialRequestId'] as int,
      project = json['projectName'] as String,
      rows = records(json['rows']),
      quotations = records(json['quotations']);
  final int requestId;
  final String project;
  final List<Json> rows, quotations;
  // Coverage uses the server's per-offer flags, never local price calculations.
  bool fullyCovered(int quotationId) =>
      rows.isNotEmpty &&
      rows.every(
        (row) => records(row['offers']).any(
          (offer) =>
              offer['quotationId'] == quotationId &&
              offer['coversFullQuantity'] == true,
        ),
      );
}

class ProcurementWorkflow {
  ProcurementWorkflow.fromJson(Json json)
    : id = json['id'] as int,
      requestId = json['materialRequestId'] as int,
      status = json['status'] as String,
      approvalStatus = json['approvalStatus'] as String,
      recommendation = json['recommendation'] as Json?,
      validation = json['validation'] as Json?,
      outcome = json['finalOutcome'] as String?,
      purchaseOrderId = json['purchaseOrderId'] as int?,
      steps = records(json['steps']),
      updatedAt = json['updatedAt'] as String?;
  final int id, requestId;
  final String status, approvalStatus;
  final Json? recommendation, validation;
  final String? outcome, updatedAt;
  final int? purchaseOrderId;
  final List<Json> steps;
  bool get awaitingReview =>
      status == 'AwaitingApproval' && approvalStatus == 'Pending';
  String get displayStatus {
    if (approvalStatus == 'Rejected') return 'Rejected';
    if (approvalStatus == 'RevisionRequested') return 'Revision Requested';
    if (approvalStatus == 'Approved') return 'Approved';
    if (status == 'Failed') return 'Failed';
    if (awaitingReview) return 'Awaiting Manager Review';
    return status;
  }
}

class PurchaseOrder {
  PurchaseOrder.fromJson(Json json)
    : id = json['id'] as int,
      supplierId = json['supplierId'] as int?,
      supplier = json['supplierName'] as String,
      status = json['status'] as String,
      total = json['totalAmount'] as num,
      requestId = (json['materialRequestId'] as int? ?? 0) > 0
          ? json['materialRequestId'] as int
          : null,
      createdAt = json['createdAt'] as String?,
      orderDate = json['orderDate'] as String?,
      expectedDate = json['expectedDeliveryDate'] as String?,
      items = records(json['items']);
  final int id;
  final int? supplierId, requestId;
  final String supplier, status;
  final num total;
  final String? createdAt, orderDate, expectedDate;
  final List<Json> items;
}

class ProcurementOverview {
  const ProcurementOverview(this.requests, this.workflows, this.orders);
  final List<Json> requests;
  final List<ProcurementWorkflow> workflows;
  final List<PurchaseOrder> orders;
}
