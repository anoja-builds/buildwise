/// Matches PendingInspectionDeliveryDto. The shared API emits enum names;
/// integer values remain supported for compatibility with earlier responses.
class PendingInspectionDelivery {
  const PendingInspectionDelivery({
    required this.deliveryId,
    required this.deliveryReference,
    required this.status,
    required this.items,
  });

  final int deliveryId;
  final String? deliveryReference;
  final Object status;
  final List<InspectionDeliveryItem> items;

  String get displayReference => deliveryReference?.trim().isNotEmpty == true
      ? deliveryReference!.trim()
      : 'Delivery #$deliveryId';

  // Display labels only; eligibility is decided by ASP.NET.
  String get statusLabel => switch (status) {
    0 || 'Scheduled' => 'Scheduled',
    1 || 'InTransit' => 'In Transit',
    2 || 'Arrived' => 'Arrived',
    3 || 'ReceivingInProgress' => 'Receiving In Progress',
    4 || 'Received' => 'Received',
    5 || 'PartiallyReceived' => 'Partially Received',
    6 || 'DiscrepancyReported' => 'Discrepancy Reported',
    _ => 'Unknown status ($status)',
  };

  factory PendingInspectionDelivery.fromJson(Map<String, dynamic> json) {
    if (json case {
      'deliveryId': int deliveryId,
      'status': Object status,
      'items': List<dynamic> items,
    }) {
      if (status is! int && status is! String) {
        throw const FormatException('Invalid delivery status.');
      }
      final reference = json['deliveryReference'];
      if (reference != null && reference is! String) {
        throw const FormatException('Invalid delivery reference.');
      }
      return PendingInspectionDelivery(
        deliveryId: deliveryId,
        deliveryReference: reference as String?,
        status: status,
        items: List.unmodifiable(
          items.map((item) {
            if (item is! Map<String, dynamic>) {
              throw const FormatException('Invalid delivery item.');
            }
            return InspectionDeliveryItem.fromJson(item);
          }),
        ),
      );
    }
    throw const FormatException('Invalid pending inspection delivery.');
  }
}

/// Matches InspectionDeliveryItemDto. Decimal JSON values can decode as either
/// int or double, so accept num before converting for this read-only view.
class InspectionDeliveryItem {
  const InspectionDeliveryItem({
    required this.deliveryItemId,
    required this.purchaseOrderItemId,
    required this.receivedQuantity,
  });

  final int deliveryItemId;
  final int purchaseOrderItemId;
  final double receivedQuantity;

  factory InspectionDeliveryItem.fromJson(Map<String, dynamic> json) {
    if (json case {
      'deliveryItemId': int deliveryItemId,
      'purchaseOrderItemId': int purchaseOrderItemId,
      'receivedQuantity': num receivedQuantity,
    }) {
      if (receivedQuantity.isFinite) {
        return InspectionDeliveryItem(
          deliveryItemId: deliveryItemId,
          purchaseOrderItemId: purchaseOrderItemId,
          receivedQuantity: receivedQuantity.toDouble(),
        );
      }
    }
    throw const FormatException('Invalid inspection delivery item.');
  }
}
