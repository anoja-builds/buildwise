import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/procurement_service.dart';
import '../widgets/procurement_status_tone.dart';

/// Quotation comparison and the QuotationSupplierAnalysisAgent decision.
///
/// This is the Component 2 human/agent boundary:
///   quotations arrive -> the agent ranks eligible suppliers (:8001)
///   -> a manager accepts or overrides at the approval boundary
///
/// The agent recommends. It never selects a supplier on its own, which is why
/// the decision controls sit here and not inside an agent result.
class QuotationComparisonScreen extends StatefulWidget {
  const QuotationComparisonScreen({super.key, this.service});

  final ProcurementService? service;

  @override
  State<QuotationComparisonScreen> createState() =>
      _QuotationComparisonScreenState();
}

class _QuotationComparisonScreenState extends State<QuotationComparisonScreen> {
  final _service = ProcurementService();
  List<Map<String, dynamic>> _requests = const [];
  int? _requestId;
  Map<String, dynamic>? _comparison;
  Map<String, dynamic>? _workflow;
  bool _loading = true;
  bool _running = false;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final requests = await (widget.service ?? _service).listApprovedMaterialRequests();
      if (!mounted) return;
      setState(() {
        _requests = requests;
        _requestId = requests.isEmpty ? null : (requests.first['id'] as num).toInt();
      });
      if (_requestId != null) await _compare();
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _compare() async {
    if (_requestId == null) return;
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await (widget.service ?? _service).compareQuotations(_requestId!);
      if (mounted) setState(() => _comparison = result);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Runs the QuotationSupplierAnalysisAgent over the selected request.
  Future<void> _runAgent() async {
    if (_requestId == null) return;
    setState(() {
      _running = true;
      _error = null;
    });
    try {
      final started = await (widget.service ?? _service).startWorkflow(_requestId!);
      final workflowId = (started['workflowId'] as num?)?.toInt();
      if (workflowId == null) {
        if (mounted) setState(() => _error = 'The workflow did not return a workflow id.');
        return;
      }
      final detail = await (widget.service ?? _service).getWorkflow(workflowId);
      if (mounted) setState(() => _workflow = detail);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _running = false);
    }
  }

  /// Records the manager's accept/override decision on the agent's ranking.
  Future<void> _decide(String decision) async {
    final nested = _workflow?['workflow'] as Map<String, dynamic>?;
    final rawId = _workflow?['id'] ?? nested?['id'];
    final workflowId = rawId is num ? rawId.toInt() : 0;
    if (workflowId <= 0) {
      if (mounted) setState(() => _error = 'This workflow has no id, so no decision can be recorded.');
      return;
    }
    try {
      await (widget.service ?? _service).recordWorkflowDecision(
        workflowId,
        decision: decision,
        comment: 'Recorded from the mobile approval boundary.',
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Workflow decision recorded: $decision.')),
      );
      final refreshed = await (widget.service ?? _service).getWorkflow(workflowId);
      if (mounted) setState(() => _workflow = refreshed);
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.toString().replaceFirst('Exception: ', ''))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final comparison = _comparison;
    final rows = (comparison?['rows'] as List<dynamic>?) ?? const [];
    final children = <Widget>[];

    if (_requests.isEmpty) {
      children.add(
        const AppCard(
          child: Text('No approved material requests to compare quotations for.'),
        ),
      );
    } else {
      children.addAll([
        AppDropdown(
          label: 'Approved material request',
          value: _requestId?.toString(),
          items: _requests.map((r) => r['id'].toString()).toList(),
          onChanged: (value) {
            setState(() {
              _requestId = value == null ? null : int.parse(value);
              _workflow = null;
            });
            _compare();
          },
        ),
        const SizedBox(height: 16),
        if (_running)
          const AppCard(
            child: Row(
              children: [
                SizedBox(
                  width: 18,
                  height: 18,
                  child: CircularProgressIndicator(strokeWidth: 2),
                ),
                SizedBox(width: 12),
                Text('QuotationSupplierAnalysisAgent is running…'),
              ],
            ),
          )
        else
          AppButton(
            label: 'Run Supplier Analysis Agent',
            expand: true,
            onPressed: _runAgent,
          ),
      ]);
      if (_workflow != null) {
        children.addAll([
          const SizedBox(height: 16),
          _WorkflowRecommendationCard(
            workflow: _workflow!,
            onAccept: () => _decide('Approved'),
            onOverride: () => _decide('RevisionRequested'),
          ),
        ]);
      }
      children.addAll([
        const SizedBox(height: 20),
        Text('Offers by material', style: Theme.of(context).textTheme.titleLarge),
        const SizedBox(height: 10),
      ]);
      if (rows.isEmpty) {
        children.add(
          const AppCard(
            child: Text('No quotations have been submitted for this request yet.'),
          ),
        );
      } else {
        for (final row in rows) {
          children.add(_ComparisonRow(row: row as Map<String, dynamic>));
        }
      }
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text('Quotation Comparison'),
        actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
      ),
      body: _loading && comparison == null
          ? const Center(child: CircularProgressIndicator())
          : _error != null
              ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
              : RefreshIndicator(
                  onRefresh: _load,
                  child: ListView(padding: const EdgeInsets.all(16), children: children),
                ),
    );
  }
}


/// The agent's ranking plus the human decision controls.
class _WorkflowRecommendationCard extends StatelessWidget {
  const _WorkflowRecommendationCard({
    required this.workflow,
    required this.onAccept,
    required this.onOverride,
  });

  final Map<String, dynamic> workflow;
  final VoidCallback onAccept;
  final VoidCallback onOverride;

  @override
  Widget build(BuildContext context) {
    final nested = workflow['workflow'] as Map<String, dynamic>?;
    final run = nested ?? workflow;
    final steps = (workflow['steps'] as List<dynamic>?) ?? const [];
    final status = run['status']?.toString() ?? 'Unknown';
    final approval = run['approvalStatus']?.toString() ?? 'Pending';
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Agent recommendation', style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 8),
          Text(run['objective']?.toString() ?? '', style: Theme.of(context).textTheme.bodySmall),
          const SizedBox(height: 10),
          Wrap(
            spacing: 8,
            runSpacing: 6,
            children: [
              StatusChip(label: status, tone: procurementStatusTone(status)),
              StatusChip(
                label: 'Approval: $approval',
                tone: procurementStatusTone(approval),
              ),
            ],
          ),
          if (steps.isNotEmpty) ...[
            const SizedBox(height: 12),
            Text('Agent steps', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 6),
            for (final step in steps.cast<Map<String, dynamic>>())
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: Text(
                  '• ${step['agentRole'] ?? 'Agent'} — ${step['toolName'] ?? 'tool'} '
                  '(${step['status'] ?? '—'})',
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
          ],
          const SizedBox(height: 14),
          Text(
            'Advisory only: the agent ranks eligible suppliers. An authorized manager '
            'accepts or overrides the recommendation, and only then is a purchase '
            'order created.',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 12),
          AppButton(label: 'Accept recommendation', expand: true, onPressed: onAccept),
          const SizedBox(height: 8),
          AppButton(
            label: 'Override / request revision',
            expand: true,
            variant: AppButtonVariant.secondary,
            onPressed: onOverride,
          ),
        ],
      ),
    );
  }
}

/// One requested material line with every supplier offer against it.
class _ComparisonRow extends StatelessWidget {
  const _ComparisonRow({required this.row});

  final Map<String, dynamic> row;

  @override
  Widget build(BuildContext context) {
    final offers = (row['offers'] as List<dynamic>?) ?? const [];
    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${row['materialName'] ?? 'Material'} — '
              'requested ${row['requestedQuantity'] ?? 0} ${row['unit'] ?? ''}',
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 10),
            if (offers.isEmpty)
              const Text('No offers for this line.')
            else
              for (final offer in offers.cast<Map<String, dynamic>>())
                Padding(
                  padding: const EdgeInsets.symmetric(vertical: 4),
                  child: Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(offer['supplierName']?.toString() ?? 'Supplier'),
                            // Eligibility is decided server-side; the chip
                            // restates that verdict rather than recomputing it.
                            Text(
                              '${offer['quantityOffered'] ?? 0} @ ${offer['unitPrice'] ?? 0} '
                              '= ${offer['lineTotal'] ?? 0}',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ],
                        ),
                      ),
                      StatusChip(
                        label: offer['coversFullQuantity'] == true ? 'Covers' : 'Partial',
                        tone: offer['coversFullQuantity'] == true
                            ? StatusTone.success
                            : StatusTone.warning,
                      ),
                    ],
                  ),
                ),
          ],
        ),
      ),
    );
  }
}

