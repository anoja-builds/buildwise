import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../../../core/widgets/structured_result.dart';
import '../../../core/widgets/field_format.dart';
import '../services/procurement_service.dart';
import 'quotation_entry_screen.dart';
import '../widgets/project_budget_card.dart';
import 'purchase_orders_screen.dart';
import '../widgets/procurement_status_tone.dart';

/// Quotation comparison and the QuotationSupplierAnalysisAgent decision.
///
/// Reached by the Procurement Officer and Procurement Manager
/// (`BuildWiseRoles.procurementDesk`), using the same endpoints as the React
/// comparison page. Suppliers are external parties contacted by email: they send
/// their quotation back to the officer, who records it in BuildWise.
///
/// This is the Component 2 human/agent boundary:
///   quotations arrive -> the agent ranks eligible suppliers (:8001)
///   -> a manager accepts or overrides at the approval boundary
///
/// The agent recommends. It never selects a supplier on its own, which is why
/// the decision controls sit here and not inside an agent result.
class QuotationComparisonScreen extends StatefulWidget {
  const QuotationComparisonScreen({
    super.key,
    this.service,
    this.canApprove = true,
    this.canRunAgent = true,
    this.autoRefresh = true,
  });

  final ProcurementService? service;

  /// Whether this session may record a human procurement decision.
  /// Procurement officers may run the agent; approval requires a manager
  /// or administrator under the backend's ProcurementDecisionOnly policy.
  final bool canApprove;
  final bool canRunAgent;
  final bool autoRefresh;

  @override
  State<QuotationComparisonScreen> createState() =>
      _QuotationComparisonScreenState();
}

class _QuotationComparisonScreenState extends State<QuotationComparisonScreen> {
  final _service = ProcurementService();
  Timer? _refreshTimer;
  List<Map<String, dynamic>> _requests = const [];
  int? _requestId;
  Map<String, dynamic>? _comparison;
  Map<String, dynamic>? _workflow;
  Map<String, dynamic>? _requestDetail;
  bool _loading = true;
  bool _running = false;
  bool _deciding = false;
  bool _analysisTab = false;
  final _comment = TextEditingController();

  @override
  void dispose() {
    _refreshTimer?.cancel();
    _comment.dispose();
    super.dispose();
  }

  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.autoRefresh) {
      _refreshTimer = Timer.periodic(
        const Duration(seconds: 15),
        (_) => _refreshLatest(),
      );
    }
  }

  Future<void> _refreshLatest() async {
    final id = _requestId;
    if (id == null || _running || _deciding || _loading) return;
    try {
      final api = widget.service ?? _service;
      final comparison = await api.compareQuotations(id);
      final workflow = await api.getLatestWorkflow(id);
      final detail = await api.getMaterialRequest(id);
      if (mounted && _requestId == id) {
        setState(() {
          _comparison = comparison;
          _workflow = workflow;
          _requestDetail = detail;
        });
      }
    } catch (_) {
      /* Keep current data when a background refresh fails. */
    }
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final requests = await (widget.service ?? _service)
          .listApprovedMaterialRequests();
      if (!mounted) return;
      setState(() {
        _requests = requests;
        final previous = _requestId;
        _requestId = requests.any((r) => r['id'] == previous)
            ? previous
            : requests.isEmpty
            ? null
            : (requests.first['id'] as num).toInt();
      });
      if (_requestId != null) await _compare();
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
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
      final requestId = _requestId!;
      final result = await (widget.service ?? _service).compareQuotations(
        requestId,
      );
      final workflow = await (widget.service ?? _service).getLatestWorkflow(
        requestId,
      );
      final detail = await (widget.service ?? _service).getMaterialRequest(
        requestId,
      );
      if (mounted && _requestId == requestId) {
        setState(() {
          _comparison = result;
          _workflow = workflow;
          _requestDetail = detail;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Runs the QuotationSupplierAnalysisAgent over the selected request.
  Future<void> _runAgent() async {
    if (_requestId == null || !widget.canRunAgent) return;
    setState(() {
      _running = true;
      _error = null;
    });
    try {
      final started = await (widget.service ?? _service).startWorkflow(
        _requestId!,
      );
      final workflowId = (started['workflowId'] as num?)?.toInt();
      if (workflowId == null) {
        if (mounted) {
          setState(() => _error = 'The workflow did not return a workflow id.');
        }
        return;
      }
      final detail = await (widget.service ?? _service).getWorkflow(workflowId);
      if (mounted) {
        setState(() {
          _workflow = detail;
          _analysisTab = true;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _running = false);
    }
  }

  /// Records the manager's accept/override decision on the agent's ranking.
  Future<void> _decide(String decision) async {
    if (!widget.canApprove || _deciding) return;
    if (decision == 'RevisionRequested' && _comment.text.trim().isEmpty) {
      setState(() => _error = 'Add a comment explaining what needs revision.');
      return;
    }
    setState(() {
      _deciding = true;
      _error = null;
    });
    final nested = _workflow?['workflow'] as Map<String, dynamic>?;
    final rawId = _workflow?['id'] ?? nested?['id'];
    final workflowId = rawId is num ? rawId.toInt() : 0;
    if (workflowId <= 0) {
      if (mounted) {
        setState(() {
          _deciding = false;
          _error = 'This workflow has no id, so no decision can be recorded.';
        });
      }
      return;
    }
    try {
      await (widget.service ?? _service).recordWorkflowDecision(
        workflowId,
        decision: decision,
        comment: _comment.text.trim(),
      );
      if (!mounted) return;
      _comment.clear();
      final refreshed = await (widget.service ?? _service).getWorkflow(
        workflowId,
      );
      if (!mounted) return;
      setState(() => _workflow = refreshed);
      final run = refreshed['workflow'] as Map<String, dynamic>? ?? refreshed;
      final poId = run['purchaseOrderId'];
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(
          content: Text(
            decision == 'Approve' && poId != null
                ? 'Purchase Order #$poId created successfully.'
                : 'Decision recorded: $decision.',
          ),
        ),
      );
      await _compare();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.toString().replaceFirst('Exception: ', ''))),
      );
    } finally {
      if (mounted) setState(() => _deciding = false);
    }
  }

  Future<void> _removeQuotation(int id) async {
    if (!widget.canRunAgent) return;
    try {
      await (widget.service ?? _service).deleteQuotation(id);
      await _compare();
    } catch (error) {
      if (mounted) {
        setState(
          () => _error = error.toString().replaceFirst('Exception: ', ''),
        );
      }
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
          child: Text(
            'No approved material requests to compare quotations for.',
          ),
        ),
      );
    } else {
      children.addAll([
        AppDropdown(
          label: 'Approved material request',
          value: _requestId?.toString(),
          items: _requests.map((r) => r['id'].toString()).toList(),
          itemLabels: _requests.map(FieldFormat.materialRequestLabel).toList(),
          onChanged: (value) {
            setState(() {
              _requestId = value == null ? null : int.parse(value);
              _workflow = null;
              _comparison = null;
              _requestDetail = null;
            });
            _compare();
          },
        ),
        const SizedBox(height: 16),
        if (_analysisTab && _running)
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
        else if (_analysisTab && widget.canRunAgent)
          AppButton(
            label: _workflow == null ? 'Run AI Analysis' : 'Re-run AI Analysis',
            expand: true,
            onPressed: _runAgent,
          )
        else if (_analysisTab)
          const AppCard(
            child: Text(
              'The Procurement Officer runs AI analysis. The Procurement Manager reviews the recommendation and records the decision.',
            ),
          ),
      ]);
      children.insert(
        2,
        Wrap(
          spacing: 8,
          children: [
            TextButton(
              onPressed: () => setState(() => _analysisTab = false),
              child: const Text('Quotations'),
            ),
            TextButton(
              onPressed: () => setState(() => _analysisTab = true),
              child: const Text('Comparison & AI Recommendation'),
            ),
          ],
        ),
      );
      if (_workflow != null && _analysisTab) {
        children.addAll([
          const SizedBox(height: 16),
          _WorkflowRecommendationCard(
            workflow: _workflow!,
            canDecide: widget.canApprove && !_deciding,
            comment: _comment,
            onReject: () => _decide('Reject'),
            onAccept: () => _decide('Approve'),
            onOverride: () => _decide('RevisionRequested'),
          ),
        ]);
      }
      if (_requestDetail != null) {
        final detail = _requestDetail!;
        children.insert(
          2,
          AppCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'MATERIAL REQUEST #${detail['id']}',
                  style: Theme.of(context).textTheme.labelLarge,
                ),
                Text(
                  detail['projectName']?.toString() ?? '',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                Text(detail['reason']?.toString() ?? ''),
                Text('Required by ${detail['requiredDate'] ?? ''}'),
                StatusChip(
                  label: detail['status']?.toString() ?? 'Approved',
                  tone: procurementStatusTone(
                    detail['status']?.toString() ?? 'Approved',
                  ),
                ),
                for (final item in ((detail['items'] as List?) ?? const []))
                  Text(
                    '${item['materialName'] ?? 'Material'} · ${item['requestedQuantity'] ?? item['quantity'] ?? 0} ${item['unit'] ?? ''}',
                  ),
              ],
            ),
          ),
        );
        final projectId = (detail['projectId'] as num?)?.toInt();
        if (projectId != null && _analysisTab) {
          children.add(
            Padding(
              padding: const EdgeInsets.only(top: 16),
              child: ProjectBudgetCard(
                key: ValueKey(projectId),
                projectId: projectId,
                service: widget.service ?? _service,
                canEdit: widget.canApprove,
              ),
            ),
          );
        }
      }
      final quotations = (comparison?['quotations'] as List?) ?? const [];
      if (quotations.isNotEmpty && !_analysisTab) {
        children.add(
          AppCard(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  'Quotation details',
                  style: Theme.of(context).textTheme.titleLarge,
                ),
                for (final quotation in quotations) ...[
                  const Divider(),
                  Text(
                    quotation['supplierName']?.toString() ?? 'Supplier',
                    style: const TextStyle(fontWeight: FontWeight.w700),
                  ),
                  Text('Supplier status: ${quotation['supplierStatus'] ?? ''}'),
                  Text('Total: ${quotation['totalAmount'] ?? 0} LKR'),
                  Text('Status: ${quotation['status'] ?? ''}'),
                  if (widget.canRunAgent &&
                      [
                        'Submitted',
                        'UnderReview',
                      ].contains(quotation['status']))
                    TextButton(
                      onPressed: () =>
                          _removeQuotation((quotation['id'] as num).toInt()),
                      child: const Text('Remove'),
                    ),
                ],
              ],
            ),
          ),
        );
      }
      if (!_analysisTab) {
        children.addAll([
          const SizedBox(height: 20),
          Text(
            'Offers by material',
            style: Theme.of(context).textTheme.titleLarge,
          ),
          const SizedBox(height: 10),
        ]);
        if (rows.isEmpty) {
          children.add(
            const AppCard(
              child: Text(
                'No quotations have been submitted for this request yet.',
              ),
            ),
          );
        } else {
          for (final row in rows) {
            children.add(_ComparisonRow(row: row as Map<String, dynamic>));
          }
        }
      }
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text('Quotation Comparison'),
        actions: [
          if (widget.canRunAgent)
            TextButton(
              onPressed: () async {
                await Navigator.of(context).push(
                  MaterialPageRoute<void>(
                    builder: (_) => QuotationEntryScreen(
                      service: widget.service,
                      initialRequestId: _requestId,
                    ),
                  ),
                );
                if (mounted) await _load();
              },
              child: const Text('Record quotation'),
            ),
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ],
      ),
      body: _loading && comparison == null
          ? const Center(child: CircularProgressIndicator())
          : _error != null && comparison == null
          ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
          : RefreshIndicator(
              onRefresh: _load,
              child: ListView(
                padding: const EdgeInsets.all(16),
                children: [
                  if (_error != null)
                    Padding(
                      padding: const EdgeInsets.only(bottom: 12),
                      child: Text(
                        _error!,
                        style: TextStyle(
                          color: Theme.of(context).colorScheme.error,
                        ),
                      ),
                    ),
                  ...children,
                ],
              ),
            ),
    );
  }
}

/// The agent's ranking plus the human decision controls.
class _WorkflowRecommendationCard extends StatelessWidget {
  const _WorkflowRecommendationCard({
    required this.workflow,
    required this.canDecide,
    required this.onAccept,
    required this.onOverride,
    required this.onReject,
    required this.comment,
  });

  final Map<String, dynamic> workflow;

  /// Whether the decision buttons may be drawn. Mirrors the API's
  /// ProcurementDecisionOnly policy so a reader never taps a 403.
  final bool canDecide;
  final VoidCallback onAccept;
  final VoidCallback onOverride;
  final VoidCallback onReject;
  final TextEditingController comment;

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
          Text(
            'Agent recommendation',
            style: Theme.of(context).textTheme.titleMedium,
          ),
          const SizedBox(height: 8),
          Text(
            run['objective']?.toString() ?? '',
            style: Theme.of(context).textTheme.bodySmall,
          ),
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
          if (run['recommendation'] is Map) ...[
            const SizedBox(height: 12),
            Text(
              'Recommended supplier: ${(run['recommendation'] as Map)['recommendedSupplierName']?.toString() ?? 'None'}',
            ),
            Text((run['recommendation'] as Map)['rationale']?.toString() ?? ''),
          ],
          StructuredResult(
            label: 'Recommendation and ranking',
            value: run['recommendation'],
          ),
          StructuredResult(label: 'Validation', value: run['validation']),
          StructuredResult(label: 'Planning', value: run['planning']),
          if (run['purchaseOrderId'] != null)
            TextButton(
              onPressed: () => Navigator.of(context).push(
                MaterialPageRoute<void>(
                  builder: (_) => PurchaseOrdersScreen(
                    initialOrderId: (run['purchaseOrderId'] as num).toInt(),
                  ),
                ),
              ),
              child: Text('View Purchase Order #${run['purchaseOrderId']}'),
            ),
          if (steps.isNotEmpty) ...[
            const SizedBox(height: 12),
            Text('Agent steps', style: Theme.of(context).textTheme.labelLarge),
            const SizedBox(height: 6),
            for (final step in steps.cast<Map<String, dynamic>>())
              Padding(
                padding: const EdgeInsets.symmetric(vertical: 3),
                child: Text(
                  '• ${step['agentRole'] ?? 'Agent'} — ${step['stepName'] ?? 'Step'} '
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
          if (canDecide &&
              status == 'AwaitingApproval' &&
              approval == 'Pending') ...[
            AppTextField(
              label: 'Comment',
              controller: comment,
              maxLines: 3,
              hint: 'Optional for approval; required for revision.',
            ),
            const SizedBox(height: 8),
            AppButton(label: 'Approve', expand: true, onPressed: onAccept),
            const SizedBox(height: 8),
            AppButton(
              label: 'Reject',
              expand: true,
              variant: AppButtonVariant.danger,
              onPressed: onReject,
            ),
            const SizedBox(height: 8),
            AppButton(
              label: 'Request Revision',
              expand: true,
              variant: AppButtonVariant.secondary,
              onPressed: onOverride,
            ),
          ] else
            // Read-only sessions see why the decision is absent rather than a
            // row of buttons that the API would reject.
            Text(
              approval != 'Pending'
                  ? 'This workflow has already been decided: $approval.'
                  : !canDecide
                  ? 'Sign in as Procurement Manager to Approve, Reject, or Request Revision on this workflow. Officers can view status only.'
                  : 'This workflow is not yet awaiting approval (current status: $status).',
              style: Theme.of(context).textTheme.bodySmall,
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
                            Text(
                              offer['supplierName']?.toString() ?? 'Supplier',
                            ),
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
                        label: offer['coversFullQuantity'] == true
                            ? 'Covers'
                            : 'Partial',
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
