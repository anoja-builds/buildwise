/// Matches PendingInspectionDeliveryDto; ASP.NET currently serializes enums
/// as integers. Status is kept as the server value, not inferred on the client.
class PendingInspectionDelivery {
  const PendingInspectionDelivery({
    required this.deliveryId,
    required this.deliveryReference,
    required this.status,
    required this.items,
  });

  final int deliveryId;
  final String? deliveryReference;
  final int status;
  final List<InspectionDeliveryItem> items;

  factory PendingInspectionDelivery.fromJson(Map<String, dynamic> json) {
    if (json case {
      'deliveryId': int deliveryId,
      'status': int status,
      'items': List<dynamic> items,
    }) {
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
