import 'package:flutter/material.dart';

import '../../core/widgets/widgets.dart';
import '../../features/material_requests/services/material_request_service.dart';
import '../../features/deliveries/services/delivery_service.dart';
import '../../features/deliveries/screens/delivery_list_screen.dart';
import '../../features/quality/services/quality_api_service.dart';
import '../../features/quality/screens/inspection_record_screen.dart';

/// Home summaries use the same API records as the corresponding operational lists.
class SiteEngineerHomeScreen extends StatelessWidget {
  const SiteEngineerHomeScreen({super.key, required this.requests});
  final MaterialRequestService requests;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: const WorkspaceAppBar(
      title: Text('Site Workspace'),
      subtitle: 'Site Engineer · BuildWise',
    ),
    body: ApiView<(List<dynamic>, List<dynamic>)>(
      load: () async => (
        await requests.getRequests(),
        await DeliveryService().getExpectedDeliveries(),
      ),
      builder: (context, data, refresh) => ListView(
        padding: const EdgeInsets.all(16),
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          RecordCard(
            title: 'Material Requests',
            action: 'View Requests',
            onTap: () => Navigator.pop(context),
            children: [
              FieldRow(
                'Active Requests',
                '${data.$1.where((r) => !['Rejected', 'Ordered'].contains(r['status'])).length}',
              ),
              if (data.$1.isNotEmpty)
                FieldRow('Latest', reference('MR', data.$1.first['id'] as int)),
            ],
          ),
          const SizedBox(height: 16),
          RecordCard(
            title: 'Material Deliveries',
            action: 'View Deliveries',
            onTap: () => Navigator.push(
              context,
              MaterialPageRoute(
                builder: (_) => const DeliveryListScreen(canReceive: true),
              ),
            ),
            children: [FieldRow('Incoming', '${data.$2.length}')],
          ),
          const SizedBox(height: 20),
          const SectionHeader(title: 'Recent Request Activity'),
          for (final r in data.$1.take(5))
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: RecordCard(
                title: reference('MR', r['id'] as int),
                status: r['status']?.toString(),
                children: [
                  Text('${r['reason'] ?? ''}'),
                  FieldRow('Created', displayDate(r['createdAt'])),
                ],
              ),
            ),
        ],
      ),
    ),
  );
}

class QualityHomeScreen extends StatelessWidget {
  const QualityHomeScreen({super.key, required this.service});
  final QualityApiService service;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: const WorkspaceAppBar(
      title: Text('Quality Workspace'),
      subtitle: 'Inspector Terminal · BuildWise',
    ),
    body: ApiView<(int, List<Map<String, dynamic>>, List<Map<String, dynamic>>)>(
      load: () async => (
        (await service.getPendingDeliveries()).length,
        await service.getHistory(),
        await service.getNonConformances(),
      ),
      builder: (context, data, refresh) => ListView(
        padding: const EdgeInsets.all(16),
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          RecordCard(
            title: 'Pending Inspections',
            action: 'View Pending Inspections',
            onTap: () => Navigator.pop(context),
            children: [FieldRow('Awaiting QA Check', '${data.$1}')],
          ),
          const SizedBox(height: 16),
          RecordCard(
            title: 'Inspection Progress',
            children: [
              FieldRow(
                'In progress',
                '${data.$2.where((r) => r['status'] == 'UnderInspection' || r['status'] == 1).length}',
              ),
              FieldRow(
                'Completed',
                '${data.$2.where((r) => r['status'] == 'Completed' || r['status'] == 2).length}',
              ),
            ],
          ),
          const SizedBox(height: 20),
          RecordCard(
            title: 'Open Non-Conformances',
            children: [
              FieldRow(
                'Open records',
                '${data.$3.where((r) => r['status'] == 'Open' || r['status'] == 0).length}',
              ),
              for (final ncr
                  in data.$3
                      .where((r) => r['status'] == 'Open' || r['status'] == 0)
                      .take(3))
                Padding(
                  padding: const EdgeInsets.only(top: 8),
                  child: Text(
                    '${reference('NCR', ncr['id'] as int)} · ${ncr['issueDescription']}',
                  ),
                ),
            ],
          ),
          const SizedBox(height: 20),
          const SectionHeader(title: 'Recent Inspection Outcomes'),
          for (final r
              in data.$2
                  .where((r) => r['status'] == 'Completed' || r['status'] == 2)
                  .take(3))
            Padding(
              padding: const EdgeInsets.only(top: 12),
              child: RecordCard(
                title: reference('INS', r['id'] as int),
                status: r['overallDecision']?.toString(),
                action: 'View Inspection',
                onTap: () => Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) => InspectionRecordScreen(
                      inspectionId: r['id'] as int,
                      service: service,
                    ),
                  ),
                ),
                children: [
                  FieldRow(
                    'Delivery',
                    '${r['deliveryReference'] ?? r['deliveryId']}',
                  ),
                  FieldRow('Inspected', displayDate(r['inspectionDate'])),
                ],
              ),
            ),
        ],
      ),
    ),
  );
}
