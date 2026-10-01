import 'package:flutter/material.dart';
import 'package:flutter/services.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';
import 'purchase_order_detail_screen.dart';

class ProcurementWorkflowScreen extends StatelessWidget {
  const ProcurementWorkflowScreen({
    super.key,
    required this.requestId,
    required this.service,
    this.manager = false,
    this.statusOnly = false,
  });
  final int requestId;
  final ProcurementService service;
  final bool manager, statusOnly;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: Text(
        statusOnly ? 'Recommendation Status' : 'AI Procurement Recommendation',
      ),
      subtitle: '${ui.reference('MR', requestId)} • Advisory Analysis',
    ),
    body: ui.ApiView<(ProcurementWorkflow?, List<Json>)>(
      load: () async => (
        await service.latestWorkflow(requestId),
        await service.getQuotations(requestId),
      ),
      builder: (context, data, refresh) {
        final w = data.$1;
        if (w == null) {
          return ListView(
            children: [
              ui.EmptyStateWidget(
                title: 'No evaluation yet',
                message: 'Start an evaluation from quotation comparison.',
                actionLabel: 'Refresh',
                onAction: refresh,
              ),
            ],
          );
        }
        final recommendation = w.recommendation;
        final quotation = data.$2
            .where((q) => q['id'] == recommendation?['recommendedQuotationId'])
            .firstOrNull;
        return ListView(
          padding: const EdgeInsets.all(16),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            if (!statusOnly) ...[
              const ui.AdvisoryBanner(
                'AI recommendations are advisory and require Procurement Manager review.',
              ),
              const SizedBox(height: 12),
              const ui.SectionHeader(
                title: 'Quotation & Supplier Analysis Agent',
              ),
              const SizedBox(height: 12),
            ],
            ui.RecordCard(
              title: statusOnly
                  ? ui.reference('MR', requestId)
                  : recommendation?['recommendedSupplierName']?.toString() ??
                        'Evaluation status',
              status: w.displayStatus,
              children: [
                ui.FieldRow('Material Request', ui.reference('MR', requestId)),
                if (recommendation?['recommendedSupplierName'] != null)
                  ui.FieldRow(
                    'Recommended supplier',
                    recommendation!['recommendedSupplierName'] as String,
                  ),
                if (recommendation?['recommendedQuotationId'] != null)
                  ui.FieldRow(
                    'Quotation',
                    ui.reference(
                      'QT',
                      recommendation!['recommendedQuotationId'] as int,
                    ),
                  ),
                if (quotation != null)
                  ui.FieldRow(
                    'Total',
                    ui.money(quotation['totalAmount'] as num),
                  ),
                ui.FieldRow(
                  'Human decision',
                  w.awaitingReview
                      ? 'Awaiting Manager Review'
                      : w.approvalStatus == 'Pending'
                      ? 'Not yet recorded'
                      : ui.statusLabel(w.approvalStatus),
                ),
                if (w.validation != null)
                  ui.FieldRow(
                    'Validation',
                    w.validation!['isValid'] == true ? 'Passed' : 'Failed',
                  ),
                ui.FieldRow('Last update', ui.displayDate(w.updatedAt)),
              ],
            ),
            const SizedBox(height: 12),
            if (!statusOnly && recommendation != null) ...[
              ui.RecordCard(
                title: 'Reasoning Summary',
                children: [
                  Text(
                    recommendation['rationale']?.toString() ??
                        'No summary returned.',
                  ),
                  for (final warning
                      in (recommendation['warnings'] as List? ?? []))
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text('Warning: $warning'),
                    ),
                  for (final error in (w.validation?['errors'] as List? ?? []))
                    Padding(
                      padding: const EdgeInsets.only(top: 8),
                      child: Text('Validation: $error'),
                    ),
                ],
              ),
              const SizedBox(height: 12),
              for (final alternative in records(
                recommendation['rankedAlternatives'],
              )) ...[
                ui.RecordCard(
                  title: 'Alternative: ${alternative['supplierName']}',
                  children: [
                    Text(ui.money(alternative['totalAmount'] as num)),
                    Text('${alternative['reason']}'),
                  ],
                ),
                const SizedBox(height: 12),
              ],
            ],
            if (w.outcome != null) ...[
              Text(w.outcome!),
              const SizedBox(height: 12),
            ],
            if (statusOnly) ...[
              ui.RecordCard(
                title: 'Status Timeline',
                children: [
                  // Only named execution steps are shown; raw agent traces are never rendered.
                  for (final step in w.steps)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 10),
                      child: Column(
                        crossAxisAlignment: CrossAxisAlignment.start,
                        children: [
                          Text(
                            '${step['stepName']} • ${step['status']}',
                            style: const TextStyle(fontWeight: FontWeight.w600),
                          ),
                          if (step['completedAt'] != null)
                            Text(ui.displayDate(step['completedAt'])),
                        ],
                      ),
                    ),
                  Text('Human decision: ${w.displayStatus}'),
                  const SizedBox(height: 8),
                  Text(
                    w.purchaseOrderId == null
                        ? 'No Purchase Order Created'
                        : 'Purchase Order Created: ${ui.reference('PO', w.purchaseOrderId!)}',
                  ),
                ],
              ),
            ] else
              TextButton(
                onPressed: () => Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => ProcurementWorkflowScreen(
                      requestId: requestId,
                      service: service,
                      manager: manager,
                      statusOnly: true,
                    ),
                  ),
                ),
                child: const Text('View Recommendation Status'),
              ),
            if (manager && w.awaitingReview)
              ui.AppButton(
                label: 'Review in Web Portal',
                expand: true,
                onPressed: () => Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => WebPortalScreen(requestId: requestId),
                  ),
                ),
              ),
            if (w.purchaseOrderId != null)
              TextButton(
                onPressed: () => Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => PurchaseOrderDetailScreen(
                      orderId: w.purchaseOrderId!,
                      service: service,
                    ),
                  ),
                ),
                child: const Text('View Purchase Order'),
              ),
            TextButton.icon(
              onPressed: refresh,
              icon: const Icon(Icons.refresh),
              label: const Text('Refresh status'),
            ),
          ],
        );
      },
    ),
  );
}

class WebPortalScreen extends StatelessWidget {
  const WebPortalScreen({super.key, this.requestId});
  final int? requestId;
  static const portalUrl = String.fromEnvironment('WEB_PORTAL_URL');
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(title: const Text('Procurement Workspace')),
    body: ListView(
      padding: const EdgeInsets.all(24),
      children: [
        const SizedBox(height: 64),
        const Icon(Icons.desktop_windows_outlined, size: 64),
        const SizedBox(height: 24),
        const Text(
          'Review in Web Portal',
          textAlign: TextAlign.center,
          style: TextStyle(fontSize: 20, fontWeight: FontWeight.w700),
        ),
        const SizedBox(height: 16),
        const Text(
          'Sign in to the BuildWise management portal with your Procurement Manager account to record the final decision. Return here and refresh to see the updated status.',
        ),
        if (requestId != null) ...[
          const SizedBox(height: 16),
          SelectableText('Material Request: ${ui.reference('MR', requestId!)}'),
        ],
        if (portalUrl.isNotEmpty) ...[
          const SizedBox(height: 16),
          SelectableText(portalUrl),
          ui.AppButton(
            label: 'Copy Portal Link',
            onPressed: () async {
              await Clipboard.setData(const ClipboardData(text: portalUrl));
              if (context.mounted) {
                ScaffoldMessenger.of(context).showSnackBar(
                  const SnackBar(content: Text('Portal link copied')),
                );
              }
            },
          ),
        ],
      ],
    ),
  );
}
