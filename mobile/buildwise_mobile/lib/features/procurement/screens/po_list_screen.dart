import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';
import 'purchase_order_detail_screen.dart';

class PoListScreen extends StatefulWidget {
  const PoListScreen({super.key, this.service});
  final ProcurementService? service;
  @override
  State<PoListScreen> createState() => _PoListScreenState();
}

class _PoListScreenState extends State<PoListScreen> {
  late final api = widget.service ?? ProcurementService();
  String _status = 'All';
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: const Text('Purchase Orders'),
      subtitle: 'Procurement • Order progress',
    ),
    body: ui.ApiView<List<PurchaseOrder>>(
      load: api.getOrders,
      builder: (context, orders, refresh) {
        final filtered = orders
            .where((o) => _status == 'All' || o.status == _status)
            .toList();
        return ListView(
          padding: const EdgeInsets.all(16),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            ui.FilterChips(
              values: const [
                'All',
                'Created',
                'Confirmed',
                'InProgress',
                'Completed',
                'Cancelled',
              ],
              selected: _status,
              onChanged: (value) => setState(() => _status = value),
            ),
            const SizedBox(height: 12),
            if (filtered.isEmpty)
              const ui.EmptyStateWidget(
                title: 'No purchase orders found',
                message: 'Try another status filter or pull to refresh.',
              ),
            for (final o in filtered) ...[
              ui.RecordCard(
                title: ui.reference('PO', o.id),
                status: o.status,
                action: 'View Order Details',
                onTap: () async {
                  await Navigator.push(
                    context,
                    MaterialPageRoute(
                      builder: (_) => PurchaseOrderDetailScreen(
                        orderId: o.id,
                        service: api,
                      ),
                    ),
                  );
                  refresh();
                },
                children: [
                  ui.FieldRow('Supplier', o.supplier),
                  if (o.requestId != null)
                    ui.FieldRow(
                      'Material Request',
                      ui.reference('MR', o.requestId!),
                    ),
                  ui.FieldRow('Total amount', ui.money(o.total)),
                  if (o.expectedDate != null)
                    ui.FieldRow(
                      'Expected delivery',
                      ui.displayDate(o.expectedDate),
                    ),
                ],
              ),
              const SizedBox(height: 12),
            ],
          ],
        );
      },
    ),
  );
}
