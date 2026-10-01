import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';
import 'quotation_comparison_screen.dart';

class ProcurementQueueScreen extends StatelessWidget {
  const ProcurementQueueScreen({
    super.key,
    required this.service,
    this.manager = false,
  });
  final ProcurementService service;
  final bool manager;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: const Text('Procurement Queue'),
      subtitle: 'Approved requests ready for supplier quotations',
    ),
    body: ui.ApiView<List<Json>>(
      load: service.getRequests,
      builder: (context, requests, refresh) {
        final queue = requests.where((r) => r['status'] == 'Approved').toList();
        return ListView(
          padding: const EdgeInsets.all(16),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            if (queue.isEmpty)
              const ui.EmptyStateWidget(
                title: 'No approved requests',
                message: 'Requests will appear after office approval.',
              ),
            for (final r in queue) ...[
              ui.RecordCard(
                title: ui.reference('MR', r['id'] as int),
                status: 'Approved',
                action: 'View Quotations',
                onTap: () async {
                  await Navigator.push(
                    context,
                    MaterialPageRoute(
                      builder: (_) => QuotationComparisonScreen(
                        requestId: r['id'] as int,
                        service: service,
                        manager: manager,
                      ),
                    ),
                  );
                  refresh();
                },
                children: [
                  ui.FieldRow(
                    'Project',
                    '${r['projectName'] ?? 'Not recorded'}',
                  ),
                  ui.FieldRow(
                    'Required date',
                    ui.displayDate(r['requiredDate']),
                  ),
                  Text(
                    '${r['itemsCount'] ?? (r['items'] as List? ?? []).length} requested items • ${r['quotationCount'] ?? 0} quotations received',
                  ),
                ],
              ),
              const SizedBox(height: 12),
            ],
            const ui.AdvisoryBanner(
              'Record and edit detailed supplier quotations in the web portal.',
              title: 'Quotation entry',
            ),
          ],
        );
      },
    ),
  );
}
