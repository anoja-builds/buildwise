import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';
import '../services/delivery_service.dart';
import 'receive_delivery_screen.dart';

class DeliveryDetailScreen extends StatelessWidget {
  const DeliveryDetailScreen({
    super.key,
    required this.deliveryId,
    required this.service,
    this.canReceive = false,
  });
  final int deliveryId;
  final DeliveryService service;
  final bool canReceive;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: const WorkspaceAppBar(title: Text('Delivery Detail')),
    body: ApiView<Map<String, dynamic>>(
      load: () => service.getDeliveryById(deliveryId),
      builder: (context, d, refresh) => ListView(
        padding: const EdgeInsets.all(16),
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          RecordCard(
            title: '${d['deliveryReference'] ?? reference('DEL', deliveryId)}',
            status: d['status']?.toString(),
            children: [
              FieldRow('Supplier', '${d['supplierName'] ?? 'Not recorded'}'),
              FieldRow(
                'Purchase order',
                reference('PO', d['purchaseOrderId'] as int),
              ),
              if (d['projectName'] != null)
                FieldRow('Project', '${d['projectName']}'),
              FieldRow('Expected date', displayDate(d['expectedDate'])),
              if (d['receivedAt'] != null)
                FieldRow('Received', displayDate(d['receivedAt'])),
              if (d['notes'] != null) Text('${d['notes']}'),
            ],
          ),
          const SizedBox(height: 16),
          RecordCard(
            title: 'Delivery Items',
            children: [
              for (final item in d['items'] as List? ?? [])
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 8),
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        '${item['materialName']}',
                        style: const TextStyle(fontWeight: FontWeight.w600),
                      ),
                      FieldRow(
                        'Ordered',
                        '${item['orderedQuantity']} ${item['materialUnit'] ?? ''}',
                      ),
                      if (item['outstandingQuantity'] != null)
                        FieldRow(
                          'Outstanding',
                          '${item['outstandingQuantity']}',
                        ),
                      if (item['receivedQuantity'] != null)
                        FieldRow('Received', '${item['receivedQuantity']}'),
                      if (item['damagedQuantity'] != null)
                        FieldRow('Damaged', '${item['damagedQuantity']}'),
                    ],
                  ),
                ),
            ],
          ),
          if (canReceive &&
              [
                'Scheduled',
                'InTransit',
                'Arrived',
                'ReceivingInProgress',
              ].contains(d['status'])) ...[
            const SizedBox(height: 20),
            AppButton(
              label: 'Receive Delivery',
              expand: true,
              onPressed: () async {
                await Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) =>
                        ReceiveDeliveryScreen(delivery: d, service: service),
                  ),
                );
                refresh();
              },
            ),
          ],
        ],
      ),
    ),
  );
}
