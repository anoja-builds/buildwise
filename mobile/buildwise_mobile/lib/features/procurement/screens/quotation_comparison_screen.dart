import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart' as ui;
import '../models/procurement_models.dart';
import '../services/procurement_service.dart';
import 'procurement_workflow_screen.dart';

class QuotationComparisonScreen extends StatefulWidget {
  const QuotationComparisonScreen({
    super.key,
    required this.requestId,
    required this.service,
    this.manager = false,
  });
  final int requestId;
  final ProcurementService service;
  final bool manager;
  @override
  State<QuotationComparisonScreen> createState() =>
      _QuotationComparisonScreenState();
}

class _QuotationComparisonScreenState extends State<QuotationComparisonScreen> {
  bool _running = false;
  String? _error;
  Future<void> _evaluate() async {
    if (_running) return;
    setState(() {
      _running = true;
      _error = null;
    });
    try {
      await widget.service.startWorkflow(widget.requestId);
      if (!mounted) return;
      await Navigator.push(
        context,
        MaterialPageRoute(
          builder: (_) => ProcurementWorkflowScreen(
            requestId: widget.requestId,
            service: widget.service,
            manager: widget.manager,
          ),
        ),
      );
    } catch (e) {
      if (mounted) setState(() => _error = e.toString());
    } finally {
      if (mounted) setState(() => _running = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: ui.WorkspaceAppBar(
      title: const Text('Quotation Comparison'),
      subtitle: ui.reference('MR', widget.requestId),
    ),
    body: ui.ApiView<QuotationComparison>(
      load: () => widget.service.compare(widget.requestId),
      builder: (context, data, refresh) => ListView(
        padding: const EdgeInsets.all(16),
        physics: const AlwaysScrollableScrollPhysics(),
        children: [
          ui.RecordCard(
            title: 'Material Request ${ui.reference('MR', data.requestId)}',
            children: [
              Text(data.project),
              Text('${data.rows.length} requested items'),
            ],
          ),
          const SizedBox(height: 12),
          if (data.quotations.isEmpty)
            const ui.EmptyStateWidget(
              title: 'No quotations yet',
              message: 'Record supplier quotations in the web portal.',
            ),
          for (final q in data.quotations) ...[
            ui.RecordCard(
              title: q['supplierName'] as String,
              status: q['status'] as String,
              children: [
                ui.FieldRow(
                  'Quotation total',
                  ui.money(q['totalAmount'] as num),
                ),
                Text(
                  data.fullyCovered(q['id'] as int)
                      ? 'Full Coverage'
                      : 'Partial Coverage',
                ),
                Text('Valid until ${ui.displayDate(q['validUntil'])}'),
                Text('Supplier: ${q['supplierStatus']}'),
              ],
            ),
            const SizedBox(height: 12),
          ],
          if (_error != null)
            Padding(
              padding: const EdgeInsets.only(bottom: 12),
              child: Text(
                _error!,
                style: TextStyle(color: Theme.of(context).colorScheme.error),
              ),
            ),
          if (!widget.manager)
            ui.AppButton(
              label: _running ? 'Running AI Evaluation…' : 'Run AI Evaluation',
              expand: true,
              onPressed: _running || data.quotations.isEmpty ? null : _evaluate,
            ),
          TextButton(
            onPressed: () => Navigator.push(
              context,
              MaterialPageRoute(
                builder: (_) => ProcurementWorkflowScreen(
                  requestId: widget.requestId,
                  service: widget.service,
                  manager: widget.manager,
                ),
              ),
            ),
            child: const Text('View Latest Recommendation'),
          ),
          const ui.AdvisoryBanner(
            'AI recommendations require Procurement Manager review. Final decisions are made in the web portal.',
          ),
        ],
      ),
    ),
  );
}
