import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';
import 'procurement_queue_screen.dart';
import 'procurement_workflow_screen.dart';
import 'po_list_screen.dart';
import 'quotation_comparison_screen.dart';

class ProcurementHomeScreen extends StatelessWidget {
  const ProcurementHomeScreen({super.key, this.manager = false, this.service});
  final bool manager;
  final ProcurementService? service;
  @override
  Widget build(BuildContext context) {
    final api = service ?? ProcurementService();
    void open(Widget page) =>
        Navigator.of(context).push(MaterialPageRoute(builder: (_) => page));
    return Scaffold(
      appBar: ui.WorkspaceAppBar(
        title: Text(manager ? 'Procurement Review' : 'Procurement Workspace'),
        subtitle:
            '${manager ? 'Procurement Manager' : 'Procurement Officer'} • BuildWise',
      ),
      body: ui.ApiView<ProcurementOverview>(
        load: api.getOverview,
        builder: (context, data, refresh) {
          final awaiting = data.workflows
              .where((w) => w.awaitingReview)
              .toList();
          final activeOrders = data.orders
              .where((o) => !['Completed', 'Cancelled'].contains(o.status))
              .length;
          Widget metric(String label, int count, VoidCallback tap) =>
              ui.AppCard(
                onTap: tap,
                child: Row(
                  children: [
                    Expanded(
                      child: Text(
                        label,
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                    ),
                    const SizedBox(width: 16),
                    Text(
                      '$count',
                      style: const TextStyle(
                        fontSize: 32,
                        fontWeight: FontWeight.w800,
                      ),
                    ),
                  ],
                ),
              );
          void queue() =>
              open(ProcurementQueueScreen(service: api, manager: manager));
          return ListView(
            padding: const EdgeInsets.all(16),
            physics: const AlwaysScrollableScrollPhysics(),
            children: [
              metric(
                manager
                    ? 'Recommendations Awaiting Review'
                    : 'Approved Requests Awaiting Quotations',
                manager
                    ? awaiting.length
                    : data.requests
                          .where(
                            (r) =>
                                r['status'] == 'Approved' &&
                                r['quotationCount'] == 0,
                          )
                          .length,
                queue,
              ),
              const SizedBox(height: 12),
              metric(
                manager
                    ? 'Approved Recommendations'
                    : 'Active Quotation Evaluations',
                data.workflows
                    .where(
                      (w) => manager
                          ? w.approvalStatus == 'Approved'
                          : [
                              'Pending',
                              'Running',
                              'AwaitingApproval',
                            ].contains(w.status),
                    )
                    .length,
                queue,
              ),
              const SizedBox(height: 12),
              metric(
                manager
                    ? 'Active Purchase Orders'
                    : 'Purchase Orders In Progress',
                activeOrders,
                () => open(PoListScreen(service: api)),
              ),
              const SizedBox(height: 16),
              ui.SectionHeader(
                title: manager
                    ? 'AI Recommendations Requiring Review'
                    : 'Recent Procurement Activity',
                actionLabel: 'Queue',
                onAction: queue,
              ),
              if (manager && awaiting.isEmpty)
                const ui.EmptyStateWidget(
                  title: 'No recommendations awaiting review',
                  message: 'Pull to refresh after new evaluations.',
                ),
              if (manager)
                for (final w in awaiting) ...[
                  ui.RecordCard(
                    title: ui.reference('MR', w.requestId),
                    status: 'AwaitingApproval',
                    action: 'View Recommendation',
                    onTap: () async {
                      await Navigator.push(
                        context,
                        MaterialPageRoute(
                          builder: (_) => ProcurementWorkflowScreen(
                            requestId: w.requestId,
                            manager: true,
                            service: api,
                          ),
                        ),
                      );
                      refresh();
                    },
                    children: [
                      Text(
                        data.requests
                                .where((r) => r['id'] == w.requestId)
                                .firstOrNull?['projectName']
                                ?.toString() ??
                            'Project not recorded',
                      ),
                      ui.FieldRow(
                        'Recommended supplier',
                        w.recommendation?['recommendedSupplierName']
                                ?.toString() ??
                            'Not available',
                      ),
                    ],
                  ),
                  const SizedBox(height: 12),
                ],
              if (!manager)
                for (final r
                    in data.requests
                        .where(
                          (r) => ['Approved', 'Ordered'].contains(r['status']),
                        )
                        .take(5)) ...[
                  ui.RecordCard(
                    title: ui.reference('MR', r['id'] as int),
                    status: r['status'] as String,
                    onTap: () => open(
                      r['status'] == 'Ordered'
                          ? ProcurementWorkflowScreen(
                              requestId: r['id'] as int,
                              service: api,
                              statusOnly: true,
                            )
                          : QuotationComparisonScreen(
                              requestId: r['id'] as int,
                              service: api,
                            ),
                    ),
                    action: 'View Procurement',
                    children: [
                      Text('${r['projectName'] ?? 'Project not recorded'}'),
                      const SizedBox(height: 4),
                      Text('${r['quotationCount']} quotations received'),
                    ],
                  ),
                  const SizedBox(height: 12),
                ],
              if (!manager && data.requests.isEmpty)
                const ui.EmptyStateWidget(
                  title: 'No procurement activity',
                  message: 'Approved requests will appear here.',
                ),
            ],
          );
        },
      ),
    );
  }
}
