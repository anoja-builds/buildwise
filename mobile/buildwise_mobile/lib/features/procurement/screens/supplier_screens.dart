import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';
import 'quotation_comparison_screen.dart';
import 'purchase_order_detail_screen.dart';

class SuppliersScreen extends StatefulWidget {
  const SuppliersScreen({super.key, this.service, this.manager = false});
  final ProcurementService? service;
  final bool manager;
  @override
  State<SuppliersScreen> createState() => _SuppliersScreenState();
}

class _SuppliersScreenState extends State<SuppliersScreen> {
  late final api = widget.service ?? ProcurementService();
  String _status = 'All', _search = '';
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: const Text('Suppliers'),
      subtitle: 'Approved supplier directory',
    ),
    body: Column(
      children: [
        Padding(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 0),
          child: Column(
            children: [
              TextField(
                decoration: const InputDecoration(
                  hintText: 'Search suppliers or contacts',
                  prefixIcon: Icon(Icons.search),
                ),
                textInputAction: TextInputAction.search,
                onSubmitted: (value) => setState(() => _search = value.trim()),
              ),
              const SizedBox(height: 8),
              Align(
                alignment: Alignment.centerLeft,
                child: ui.FilterChips(
                  values: const ['All', 'Active', 'Inactive', 'Suspended'],
                  selected: _status,
                  onChanged: (value) => setState(() => _status = value),
                ),
              ),
            ],
          ),
        ),
        Expanded(
          child: ui.ApiView<List<Supplier>>(
            key: ValueKey('$_search/$_status'),
            load: () => api.getSuppliers(search: _search, status: _status),
            builder: (context, suppliers, refresh) => ListView(
              padding: const EdgeInsets.all(16),
              physics: const AlwaysScrollableScrollPhysics(),
              children: [
                if (suppliers.isEmpty)
                  const ui.EmptyStateWidget(
                    title: 'No suppliers found',
                    message: 'Try adjusting your search or status filter.',
                  ),
                for (final s in suppliers) ...[
                  ui.RecordCard(
                    title: s.name,
                    status: s.status,
                    onTap: () => Navigator.push(
                      context,
                      MaterialPageRoute(
                        builder: (_) => SupplierDetailScreen(
                          supplierId: s.id,
                          service: api,
                          manager: widget.manager,
                        ),
                      ),
                    ),
                    action: 'View Supplier',
                    children: [
                      if (s.contact != null) Text(s.contact!),
                      if (s.phone != null) Text(s.phone!),
                      if (s.email != null) Text(s.email!),
                    ],
                  ),
                  const SizedBox(height: 12),
                ],
              ],
            ),
          ),
        ),
      ],
    ),
  );
}

class SupplierDetailScreen extends StatelessWidget {
  const SupplierDetailScreen({
    super.key,
    required this.supplierId,
    required this.service,
    this.manager = false,
  });
  final int supplierId;
  final ProcurementService service;
  final bool manager;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: const Text('Supplier Detail'),
      subtitle: 'Supplier profile & procurement activity',
    ),
    body: ui.ApiView<(Supplier, List<PurchaseOrder>)>(
      load: () async => (
        await service.getSupplier(supplierId),
        (await service.getOrders())
            .where((o) => o.supplierId == supplierId)
            .toList(),
      ),
      builder: (context, data, refresh) {
        final s = data.$1;
        return ListView(
          padding: const EdgeInsets.all(16),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            ui.RecordCard(
              title: s.name,
              status: s.status,
              children: [
                ui.FieldRow('Contact person', s.contact ?? 'Not provided'),
                ui.FieldRow('Phone', s.phone ?? 'Not provided'),
                ui.FieldRow('Email', s.email ?? 'Not provided'),
                if (s.address != null) Text(s.address!),
                const SizedBox(height: 8),
                Text(
                  '${s.quotations.length} quotations • ${data.$2.length} purchase orders',
                ),
              ],
            ),
            const SizedBox(height: 16),
            const ui.SectionHeader(title: 'Recent Quotations'),
            const SizedBox(height: 12),
            if (s.quotations.isEmpty)
              const ui.EmptyStateWidget(
                title: 'No quotations yet',
                message: 'Recorded quotations will appear here.',
              ),
            for (final q in s.quotations) ...[
              ui.RecordCard(
                title:
                    '${ui.reference('QT', q['quotationId'] as int)} • ${ui.reference('MR', q['materialRequestId'] as int)}',
                status: q['status'] as String,
                action: 'View Quotations',
                onTap: () => Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => QuotationComparisonScreen(
                      requestId: q['materialRequestId'] as int,
                      service: service,
                      manager: manager,
                    ),
                  ),
                ),
                children: [
                  ui.FieldRow(
                    'Quotation date',
                    ui.displayDate(q['quotationDate']),
                  ),
                  ui.FieldRow('Total value', ui.money(q['totalAmount'] as num)),
                ],
              ),
              const SizedBox(height: 12),
            ],
            const ui.SectionHeader(title: 'Purchase Orders'),
            const SizedBox(height: 12),
            if (data.$2.isEmpty) const Text('No linked purchase orders.'),
            for (final o in data.$2) ...[
              ui.RecordCard(
                title: ui.reference('PO', o.id),
                status: o.status,
                action: 'View Purchase Order',
                onTap: () => Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => PurchaseOrderDetailScreen(
                      orderId: o.id,
                      service: service,
                    ),
                  ),
                ),
                children: [Text(ui.money(o.total))],
              ),
              const SizedBox(height: 12),
            ],
          ],
        );
      },
    ),
  );
}
