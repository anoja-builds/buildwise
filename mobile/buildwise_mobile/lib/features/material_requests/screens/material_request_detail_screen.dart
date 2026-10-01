import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';
import '../services/material_request_service.dart';
import '../../procurement/screens/material_request_procurement_view.dart';

class MaterialRequestDetailScreen extends StatelessWidget {
  const MaterialRequestDetailScreen({
    super.key,
    required this.requestId,
    required this.service,
    this.canViewProcurementStatus = true,
  });
  final int requestId;
  final MaterialRequestService service;
  final bool canViewProcurementStatus;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: WorkspaceAppBar(
      title: Text(reference('MR', requestId)),
      subtitle: 'Material request status',
    ),
    body: ApiView<Map<String, dynamic>>(
      load: () => service.getRequest(requestId),
      builder: (context, r, refresh) => ListView(
        padding: const EdgeInsets.all(16),
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          RecordCard(
            title: 'Request Overview',
            status: r['status']?.toString(),
            children: [
              FieldRow('Project', '${r['projectName'] ?? 'Not recorded'}'),
              FieldRow('Required date', displayDate(r['requiredDate'])),
              FieldRow('Created', displayDate(r['createdAt'])),
              if (r['reason'] != null) Text('${r['reason']}'),
            ],
          ),
          const SizedBox(height: 16),
          if (canViewProcurementStatus && r['status'] == 'Draft')
            _SubmitDraftButton(
              requestId: requestId,
              service: service,
              onSaved: refresh,
            ),
          RecordCard(
            title: 'Requested Materials',
            children: [
              for (final item in r['items'] as List? ?? [])
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
                        'Quantity',
                        '${item['quantity']} ${item['materialUnit'] ?? ''}',
                      ),
                      if ((item['notes']?.toString() ?? '').isNotEmpty)
                        Text('${item['notes']}'),
                    ],
                  ),
                ),
            ],
          ),
          if (canViewProcurementStatus &&
              ['Approved', 'Ordered'].contains(r['status'])) ...[
            const SizedBox(height: 16),
            MaterialRequestProcurementView(materialRequestId: requestId),
          ],
        ],
      ),
    ),
  );
}

class _SubmitDraftButton extends StatefulWidget {
  const _SubmitDraftButton({
    required this.requestId,
    required this.service,
    required this.onSaved,
  });
  final int requestId;
  final MaterialRequestService service;
  final VoidCallback onSaved;
  @override
  State<_SubmitDraftButton> createState() => _SubmitDraftButtonState();
}

class _SubmitDraftButtonState extends State<_SubmitDraftButton> {
  bool _busy = false;
  @override
  Widget build(BuildContext context) => Padding(
    padding: const EdgeInsets.only(bottom: 16),
    child: AppButton(
      label: _busy ? 'Submitting...' : 'Submit Request',
      onPressed: _busy
          ? null
          : () async {
              setState(() => _busy = true);
              try {
                await widget.service.submitRequest(widget.requestId);
                if (mounted) widget.onSaved();
              } catch (error) {
                if (context.mounted) {
                  ScaffoldMessenger.of(context)
                      .showSnackBar(SnackBar(content: Text(error.toString())));
                }
              } finally {
                if (mounted) setState(() => _busy = false);
              }
            },
    ),
  );
}
