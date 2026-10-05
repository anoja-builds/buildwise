import 'package:flutter/material.dart';

import '../../../core/widgets/field_format.dart';
import '../../../core/widgets/field_messages.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';

/// The decision screen for one material request — the mobile counterpart of the
/// web app's `ReviewRequest`.
///
/// A manager approves a *purchase*, not an id. So this shows what the site team
/// actually asked for: project, priority, required date, reason, and every line
/// item with its quantity and unit. Only then are the three outcomes offered:
///
/// - **Approve** — optional comment; the request becomes Approved for procurement
/// - **Reject** — comment required; the request becomes Rejected
/// - **Request Revision** — comment required; sent back to the site team
///
/// The RequestAnalysisAgent (:8002) is advisory: it may flag urgency or bulk
/// risk, but it never records a decision. The human gate below is the only way
/// a request changes status.
class MaterialRequestReviewScreen extends StatefulWidget {
  const MaterialRequestReviewScreen({
    super.key,
    required this.detail,
    this.service,
    this.onDecided,
    this.canApprove = true,
  });

  /// The full request from `GET /material-requests/{id}`, including its items.
  final Map<String, dynamic> detail;
  final OperationsService? service;
  final VoidCallback? onDecided;
  final bool canApprove;

  @override
  State<MaterialRequestReviewScreen> createState() =>
      _MaterialRequestReviewScreenState();
}

class _MaterialRequestReviewScreenState
    extends State<MaterialRequestReviewScreen> {
  final _comment = TextEditingController();
  bool _deciding = false;
  bool _analyzing = false;
  String? _error;
  Map<String, dynamic>? _analysis;
  String? _outcome;

  @override
  void dispose() {
    _comment.dispose();
    super.dispose();
  }

  OperationsService get _api => widget.service ?? OperationsService();

  List<Map<String, dynamic>> get _items =>
      ((widget.detail['items'] as List<dynamic>?) ?? const [])
          .cast<Map<String, dynamic>>();

  /// Runs the RequestAnalysisAgent over this request. Advisory only.
  Future<void> _runAnalysis() async {
    final id = (widget.detail['id'] as num).toInt();
    setState(() {
      _analyzing = true;
      _error = null;
    });
    try {
      final result = await _api.analyzeRequest(id);
      // Ignore a response for a different request rather than showing another
      // request's flags on this one.
      final analysed = result['requestId'];
      if (!mounted) return;
      if (analysed != null && analysed != id) {
        setState(
          () => _error = 'The analysis response did not match this request.',
        );
        return;
      }
      if (mounted) setState(() => _analysis = result);
    } catch (e) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _analyzing = false);
    }
  }

  /// Records the decision, enforcing the server's comment rule up front so the
  /// manager never meets an unexplained 400.
  Future<void> _decide(String decision) async {
    final id = (widget.detail['id'] as num).toInt();
    final comment = _comment.text.trim();
    if (decision != 'Approved' && comment.isEmpty) {
      setState(
        () => _error = decision == 'Rejected'
            ? 'A comment is required when rejecting a request.'
            : 'Add a comment explaining what needs to be revised.',
      );
      return;
    }
    setState(() {
      _deciding = true;
      _error = null;
    });
    try {
      await _api.decideMaterialRequest(
        id,
        decision: decision,
        comments: comment.isEmpty ? null : comment,
      );
      if (!mounted) return;
      setState(() => _outcome = decision);
      widget.onDecided?.call();
    } catch (e) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _deciding = false);
    }
  }

  /// The confirmation shown once the decision is stored, so the manager sees the
  /// outcome and what happens next rather than returning to a silently changed
  /// list.
  Widget _confirmation(int? id) {
    final copy = switch (_outcome!) {
      'Approved' => (
        'Request #$id approved',
        'Procurement can begin: RFQ, quotations, analysis, then a purchase '
            'order.',
      ),
      'Rejected' => (
        'Request #$id rejected',
        'The site team can revise and resubmit it.',
      ),
      _ => (
        'Revision requested on #$id',
        'The request was sent back to the site team with your comments.',
      ),
    };
    return Scaffold(
      appBar: AppBar(title: const Text('Decision recorded')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          const SizedBox(height: 20),
          AppCard(
            child: Column(
              children: [
                const Icon(Icons.check_circle_outline, size: 44),
                const SizedBox(height: 12),
                Text(
                  copy.$1,
                  style: const TextStyle(fontWeight: FontWeight.w700),
                  textAlign: TextAlign.center,
                ),
                const SizedBox(height: 6),
                Text(copy.$2, textAlign: TextAlign.center),
              ],
            ),
          ),
          const SizedBox(height: 16),
          AppButton(
            label: 'Back to approvals',
            expand: true,
            onPressed: () => Navigator.of(context).pop(),
          ),
        ],
      ),
    );
  }

  Widget _detailRow(String label, String value) => Padding(
    padding: const EdgeInsets.only(bottom: 10),
    child: Row(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        SizedBox(
          width: 108,
          child: Text(label, style: Theme.of(context).textTheme.bodySmall),
        ),
        Expanded(child: Text(value)),
      ],
    ),
  );

  Widget _itemCard(Map<String, dynamic> item) => Padding(
    padding: const EdgeInsets.only(bottom: 8),
    child: AppCard(
      child: Row(
        mainAxisAlignment: MainAxisAlignment.spaceBetween,
        children: [
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text(
                  item['materialName']?.toString() ?? 'Material',
                  style: const TextStyle(fontWeight: FontWeight.w600),
                ),
                if (item['description'] != null &&
                    item['description'].toString().isNotEmpty)
                  Text(
                    item['description'].toString(),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
              ],
            ),
          ),
          Text(
            '${item['requestedQuantity'] ?? 0} ${item['unit'] ?? ''}',
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
        ],
      ),
    ),
  );

  Widget _analysisCard(Map<String, dynamic> analysis) {
    final flags = (analysis['flags'] as List<dynamic>?) ?? const [];
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(
            'Flags · ${analysis['status'] ?? 'Analyzed'}',
            style: const TextStyle(fontWeight: FontWeight.w700),
          ),
          const SizedBox(height: 6),
          if (flags.isEmpty)
            const Text('No planning risks flagged.')
          else
            ...flags.map(
              (flag) => Padding(
                padding: const EdgeInsets.only(top: 2),
                child: Text('• ${flag.toString().replaceAll('_', ' ')}'),
              ),
            ),
        ],
      ),
    );
  }

  @override
  Widget build(BuildContext context) {
    final id = (widget.detail['id'] as num?)?.toInt();
    if (_outcome != null) return _confirmation(id);

    return Scaffold(
      appBar: AppBar(title: Text('Material Request #$id')),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          _detailRow('Status', widget.detail['status']?.toString() ?? '—'),
          _detailRow(
            'Project',
            widget.detail['projectName']?.toString() ??
                'Project #${widget.detail['projectId']}',
          ),
          _detailRow(
            'Priority',
            FieldFormat.humanize(widget.detail['priority']?.toString()),
          ),
          _detailRow(
            'Required date',
            FieldFormat.date(widget.detail['requiredDate']),
          ),
          _detailRow('Reason', widget.detail['reason']?.toString() ?? '—'),
          const SizedBox(height: 16),
          const SectionHeader(title: 'Items'),
          const SizedBox(height: 8),
          if (_items.isEmpty)
            const AppCard(child: Text('No line items on this request.'))
          else
            ..._items.map(_itemCard),
          const SizedBox(height: 20),
          if (widget.canApprove) ...[
            const SectionHeader(title: 'Request analysis'),
            const SizedBox(height: 4),
            Text(
              'Advisory only — the agent flags planning risk but never approves or '
              'changes the request.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 10),
            AppButton(
              label: _analyzing
                  ? 'Analyzing…'
                  : _analysis == null
                  ? 'Analyze request'
                  : 'Re-run analysis',
              expand: true,
              variant: AppButtonVariant.secondary,
              onPressed: _analyzing ? null : _runAnalysis,
            ),
            if (_analysis != null) ...[
              const SizedBox(height: 10),
              _analysisCard(_analysis!),
            ],
            const SizedBox(height: 20),
          ],
          if (widget.canApprove &&
              [
                'PendingApproval',
                'UnderReview',
                'AwaitingProcurementApproval',
              ].contains(widget.detail['status'])) ...[
            const SectionHeader(title: 'Your decision'),
            const SizedBox(height: 8),
            AppTextField(
              label: 'Comment',
              controller: _comment,
              maxLines: 3,
              hint: 'Optional for Approve. Required for Reject or Revision.',
            ),
            if (_error != null) ...[
              const SizedBox(height: 10),
              Text(_error!, style: const TextStyle(color: Colors.red)),
            ],
            const SizedBox(height: 12),
            AppButton(
              label: 'Approve',
              expand: true,
              onPressed: _deciding ? null : () => _decide('Approved'),
            ),
            const SizedBox(height: 8),
            AppButton(
              label: 'Reject',
              expand: true,
              variant: AppButtonVariant.danger,
              onPressed: _deciding ? null : () => _decide('Rejected'),
            ),
            const SizedBox(height: 8),
            AppButton(
              label: 'Request revision',
              expand: true,
              variant: AppButtonVariant.secondary,
              onPressed: _deciding ? null : () => _decide('RevisionRequested'),
            ),
          ],
        ],
      ),
    );
  }
}
