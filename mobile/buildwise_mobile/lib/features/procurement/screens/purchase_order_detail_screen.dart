import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';

class PurchaseOrderDetailScreen extends StatelessWidget {
  const PurchaseOrderDetailScreen({
    super.key,
    required this.orderId,
    required this.service,
  });
  final int orderId;
  final ProcurementService service;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: const Text('Purchase Order Detail'),
      subtitle: ui.reference('PO', orderId),
    ),
    body: ui.ApiView<PurchaseOrder>(
      load: () => service.getOrder(orderId),
      builder: (context, o, refresh) => ListView(
        padding: const EdgeInsets.all(16),
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          ui.RecordCard(
            title: ui.reference('PO', o.id),
            status: o.status,
            children: [
              ui.FieldRow('Supplier', o.supplier),
              if (o.requestId != null)
                ui.FieldRow(
                  'Material Request',
                  ui.reference('MR', o.requestId!),
                ),
              ui.FieldRow('Total', ui.money(o.total)),
              ui.FieldRow('Created date', ui.displayDate(o.createdAt)),
            ],
          ),
          const SizedBox(height: 16),
          const ui.SectionHeader(title: 'Material Summary'),
          const SizedBox(height: 12),
          for (final item in o.items) ...[
            ui.RecordCard(
              title: '${item['materialName']}',
              children: [
                ui.FieldRow(
                  'Quantity',
                  '${item['orderedQuantity']} ${item['unit']}',
                ),
                ui.FieldRow('Unit price', ui.money(item['unitPrice'] as num)),
                ui.FieldRow('Line total', ui.money(item['lineTotal'] as num)),
              ],
            ),
            const SizedBox(height: 12),
          ],
          ui.RecordCard(
            title: 'Delivery',
            children: [
              ui.FieldRow('Expected date', ui.displayDate(o.expectedDate)),
            ],
          ),
          const SizedBox(height: 12),
          ui.RecordCard(
            title: 'Order Status',
            status: o.status,
            children: [ui.FieldRow('Order date', ui.displayDate(o.orderDate))],
          ),
        ],
      ),
    ),
  );
}
