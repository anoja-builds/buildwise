class InspectionRecord {
  const InspectionRecord(
    this.id,
    this.deliveryId,
    this.reference,
    this.status,
    this.decision,
    this.notes,
    this.items,
  );
  final int id;
  final int deliveryId;
  final String? reference;
  final Object status;
  final String? decision;
  final String? notes;
  final List<InspectionRecordItem> items;
  bool get completed => status == 'Completed' || status == 2;
  bool get editable => status == 'UnderInspection' || status == 1;

  factory InspectionRecord.fromJson(Map<String, dynamic> json) {
    try {
      final results = (json['items'] as List).cast<Map<String, dynamic>>();
      final deliveryItems = (json['deliveryItems'] as List)
          .cast<Map<String, dynamic>>();
      final status = json['status'];
      if (status is! String && status is! int) throw const FormatException();
      final decision = switch (json['overallDecision']) {
        0 => 'Accepted',
        1 => 'PartiallyAccepted',
        2 => 'Rejected',
        final String value => value,
        null => null,
        _ => throw const FormatException(),
      };
      return InspectionRecord(
        json['id'] as int,
        json['deliveryId'] as int,
        json['deliveryReference'] as String?,
        status as Object,
        decision,
        json['notes'] as String?,
        deliveryItems.map((item) {
          final id = item['deliveryItemId'] as int;
          final matches = results.where((r) => r['deliveryItemId'] == id);
          final result = matches.isEmpty ? null : matches.single;
          double quantity(dynamic value) {
            if (value is! num || !value.isFinite || value < 0) {
              throw const FormatException();
            }
            return value.toDouble();
          }

          return InspectionRecordItem(
            id,
            item['purchaseOrderItemId'] as int,
            quantity(item['receivedQuantity']),
            quantity(item['damagedQuantity']),
            result?['condition'] as String?,
            result == null ? null : quantity(result['acceptedQuantity']),
            result == null ? null : quantity(result['rejectedQuantity']),
            result?['remarks'] as String?,
          );
        }).toList(),
      );
    } on TypeError {
      throw const FormatException();
    } on StateError {
      throw const FormatException();
    }
  }
}

class InspectionRecordItem {
  const InspectionRecordItem(
    this.id,
    this.orderItemId,
    this.received,
    this.damaged,
    this.condition,
    this.accepted,
    this.rejected,
    this.remarks,
  );
  final int id;
  final int orderItemId;
  final double received;
  final double damaged;
  final String? condition;
  final double? accepted;
  final double? rejected;
  final String? remarks;
}

/// Matches CompleteInspectionItemDto; no user identity is sent.
class InspectionItemSubmission {
  const InspectionItemSubmission({
    required this.deliveryItemId,
    required this.acceptedQuantity,
    required this.rejectedQuantity,
    this.condition,
    this.remarks,
  });
  final int deliveryItemId;
  final double acceptedQuantity;
  final double rejectedQuantity;
  final String? condition;
  final String? remarks;
  Map<String, dynamic> toJson() => {
    'deliveryItemId': deliveryItemId,
    'condition': condition,
    'acceptedQuantity': acceptedQuantity,
    'rejectedQuantity': rejectedQuantity,
    'remarks': remarks,
  };
}
