import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';
import '../services/delivery_service.dart';
import 'delivery_detail_screen.dart';

class DeliveryListScreen extends StatefulWidget {
  const DeliveryListScreen({super.key, this.canReceive = false, this.service});
  final bool canReceive;
  final DeliveryService? service;
  @override
  State<DeliveryListScreen> createState() => _DeliveryListScreenState();
}

class _DeliveryListScreenState extends State<DeliveryListScreen> {
  late final _service = widget.service ?? DeliveryService();
  String _filter = 'Expected';
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: const WorkspaceAppBar(
      title: Text('Deliveries'),
      subtitle: 'Material receiving & tracking',
    ),
    body: Column(
      children: [
        Padding(
          padding: const EdgeInsets.all(16),
          child: FilterChips(
            values: const ['Expected', 'History'],
            selected: _filter,
            onChanged: (v) => setState(() => _filter = v),
          ),
        ),
        Expanded(
          child: ApiView<List<dynamic>>(
            key: ValueKey(_filter),
            load: _filter == 'Expected'
                ? _service.getExpectedDeliveries
                : _service.getDeliveryHistory,
            builder: (context, deliveries, refresh) => ListView(
              padding: const EdgeInsets.symmetric(horizontal: 16),
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                if (deliveries.isEmpty)
                  const EmptyStateWidget(
                    title: 'No deliveries',
                    message: 'Deliveries will appear here when available.',
                  ),
                for (final delivery in deliveries)
                  Padding(
                    padding: const EdgeInsets.only(bottom: 12),
                    child: RecordCard(
                      title:
                          delivery['deliveryReference']?.toString() ??
                          reference('DEL', delivery['id'] as int),
                      status: delivery['status']?.toString(),
                      action: 'View Delivery',
                      onTap: () async {
                        await Navigator.push(
                          context,
                          MaterialPageRoute(
                            builder: (_) => DeliveryDetailScreen(
                              deliveryId: delivery['id'] as int,
                              service: _service,
                              canReceive: widget.canReceive,
                            ),
                          ),
                        );
                        refresh();
                      },
                      children: [
                        Text(
                          '${delivery['supplierName'] ?? 'Supplier not recorded'}',
                        ),
                        FieldRow(
                          'Purchase order',
                          reference('PO', delivery['purchaseOrderId'] as int),
                        ),
                        if (delivery['projectName'] != null)
                          FieldRow('Project', '${delivery['projectName']}'),
                        FieldRow(
                          'Expected',
                          displayDate(delivery['expectedDate']),
                        ),
                      ],
                    ),
                  ),
              ],
            ),
          ),
        ),
      ],
    ),
  );
}
