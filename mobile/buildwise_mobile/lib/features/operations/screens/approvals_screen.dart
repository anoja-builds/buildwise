import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/field_format.dart';
import '../../../core/widgets/field_messages.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
import 'material_request_review_screen.dart';

/// Material request approval queue for the Site Manager, Procurement Manager and
/// Administrator â€” the human decision gate on the way to procurement.
///
/// This mirrors the web app's Material Requests page, pointed at the identical
/// endpoints (`GET /material-requests?status=PendingApproval` then
/// `POST /material-requests/{id}/decision`). The API enforces the
/// `MaterialRequestApprovalOnly` policy; the shell only decides whether to draw
/// the controls, so a non-approver never taps a button that would 403.
///
/// The decision is always a human one. The RequestAnalysisAgent (:8002) is
/// offered per request and stays advisory: it highlights urgency and quantity
/// risk but never approves anything.
/// The full material request register for an approver â€” the mobile counterpart
/// of the web app's Material Requests page.
///
/// Like the web, an approver loads **every** request (`status=all`), not just
/// the pending ones: a decision has to stay visible after it is made, and the
/// Procurement Officer's work starts once a request is Approved, so a
/// pending-only queue would hide exactly the rows the approver acts on. The
/// status filter below narrows the same complete list.
///
/// Who raised each request is shown, because an approver works a queue raised by
/// other people. The decision itself lives on
/// [MaterialRequestReviewScreen] â€” list, then review, then decide.
class ApprovalsScreen extends StatefulWidget {
  const ApprovalsScreen({super.key, this.service});

  final OperationsService? service;

  @override
  State<ApprovalsScreen> createState() => _ApprovalsScreenState();
}

class _ApprovalsScreenState extends State<ApprovalsScreen>
    with WidgetsBindingObserver {
  Timer? _refreshTimer;
  final _service = OperationsService();

  /// The same status vocabulary the web filter offers.
  static const List<String?> _statuses = [
    null, // All statuses
    'PendingApproval',
    'UnderReview',
    'AwaitingProcurementApproval',
    'Approved',
    'Rejected',
    'RevisionRequested',
    'Fulfilled',
  ];

  List<Map<String, dynamic>> _requests = const [];
  String? _status;
  String _query = '';
  String _priority = 'all';
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _refreshTimer = Timer.periodic(const Duration(seconds: 30), (_) {
      if (WidgetsBinding.instance.lifecycleState == AppLifecycleState.resumed) {
        _load(background: true);
      }
    });
    _load();
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed) _load(background: true);
  }

  OperationsService get _api => widget.service ?? _service;

  /// Loads the complete register once, then filters in memory.
  ///
  /// The web does the same: one `status=all` fetch, with the status and
  /// priority filters applied client-side. Re-querying the server per keystroke
  /// would refetch the whole portfolio on every character typed.
  Future<void> _load({bool background = false}) async {
    if (!mounted) return;
    if (!background) {
      setState(() {
        _loading = true;
        _error = null;
      });
    }
    try {
      final rows = await _api.listRequests(status: 'all');
      if (mounted) {
        setState(() {
          _requests = rows;
          _error = null;
        });
      }
    } catch (e) {
      if (mounted && !background) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted && !background) setState(() => _loading = false);
    }
  }

  List<Map<String, dynamic>> get _filtered {
    final q = _query.trim().toLowerCase();
    return _requests.where((request) {
      if (_priority != 'all' && request['priority']?.toString() != _priority) {
        return false;
      }
      if (_status != null && request['status']?.toString() != _status) {
        return false;
      }
      if (q.isEmpty) return true;
      // Search the same fields the web searches: id, project, material, reason.
      final id = (request['id'] as num?)?.toInt().toString() ?? '';
      final project = request['projectName']?.toString().toLowerCase() ?? '';
      final names = ((request['materialNames'] as List<dynamic>?) ?? const [])
          .map((value) => value.toString().toLowerCase())
          .join(' ');
      final reason = request['reason']?.toString().toLowerCase() ?? '';
      return id.contains(q) ||
          project.contains(q) ||
          names.contains(q) ||
          reason.contains(q);
    }).toList();
  }

  /// Requests actually awaiting a decision, for the headline count.
  int get _pendingCount => _requests
      .where((r) => r['status']?.toString() == 'PendingApproval')
      .length;

  /// Opens the request detail, where the decision is actually recorded.
  ///
  /// A manager approves a *purchase*, not an id, so the line items, quantity,
  /// reason and priority have to be on screen before the buttons. This is the
  /// same two-step the web app's `ReviewRequest` uses: list -> review -> decide.
  Future<void> _openReview(Map<String, dynamic> request) async {
    final id = (request['id'] as num).toInt();
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final detail = await _api.getMaterialRequestDetail(id);
      if (!mounted) return;
      setState(() => _loading = false);
      await Navigator.of(context).push<void>(
        MaterialPageRoute<void>(
          builder: (_) => MaterialRequestReviewScreen(
            detail: detail,
            service: widget.service,
            onDecided: _load,
          ),
        ),
      );
    } catch (e) {
      if (!mounted) return;
      setState(() {
        _loading = false;
        _error = FieldMessages.friendly(e.toString());
      });
    }
  }

  @override
  Widget build(BuildContext context) {
    final rows = _filtered;
    return Scaffold(
      appBar: AppBar(
        title: const Text('Material Requests'),
        actions: [
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
          ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
          : Column(
              children: [
                _filters(rows.length),
                const Divider(height: 1),
                Expanded(
                  child: rows.isEmpty
                      ? EmptyStateWidget(
                          title: _query.isEmpty && _status == null
                              ? 'No material requests yet'
                              : 'No matching requests',
                          message: _query.isEmpty && _status == null
                              ? 'Site teams submit material requests; they '
                                    'appear here for review.'
                              : 'No request matches the current filter.',
                        )
                      : RefreshIndicator(
                          onRefresh: _load,
                          child: ListView.separated(
                            padding: const EdgeInsets.all(16),
                            itemCount: rows.length,
                            separatorBuilder: (_, _) =>
                                const SizedBox(height: 10),
                            itemBuilder: (_, index) => _rowCard(rows[index]),
                          ),
                        ),
                ),
              ],
            ),
    );
  }

  /// Search + status filter, matching the web page's controls.
  Widget _filters(int shown) => Padding(
    padding: const EdgeInsets.fromLTRB(16, 12, 16, 12),
    child: Column(
      children: [
        AppTextField(
          label: 'Search',
          hint: 'Request id, project, material or reason',
          onChanged: (value) => setState(() => _query = value),
        ),
        const SizedBox(height: 10),
        AppDropdown(
          label: 'Status',
          value: _status ?? 'all',
          items: _statuses.map((s) => s ?? 'all').toList(),
          itemLabels: _statuses
              .map(
                (s) => s == null
                    ? 'All statuses'
                    : 'Status: ${FieldFormat.humanize(s)}',
              )
              .toList(),
          onChanged: (value) =>
              setState(() => _status = value == 'all' ? null : value),
        ),
        const SizedBox(height: 10),
        AppDropdown(
          label: 'Priority',
          value: _priority,
          items: const ['all', 'Low', 'Normal', 'High', 'Urgent'],
          itemLabels: const [
            'All priorities',
            'Low',
            'Normal',
            'High',
            'Urgent',
          ],
          onChanged: (value) => setState(() => _priority = value ?? 'all'),
        ),
        const SizedBox(height: 10),
        // The pending count is stated separately from the filtered total so
        // it stays visible while the approver narrows the list.
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              'Showing $shown of ${_requests.length}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            Text(
              '$_pendingCount awaiting decision',
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(fontWeight: FontWeight.w700),
            ),
          ],
        ),
      ],
    ),
  );

  /// One request row: material, project, requester, priority, item count,
  /// status, and a tap target into the review screen.
  Widget _rowCard(Map<String, dynamic> request) {
    final id = (request['id'] as num).toInt();
    final status = request['status']?.toString();
    final requester = request['requestedByName']?.toString();
    final count = (request['itemCount'] as num?)?.toInt() ?? 0;
    return AppCard(
      // The decision belongs on a detail screen, not the list row, so the
      // manager reads what they are approving before choosing. Mirrors the
      // web's list -> ReviewRequest -> decide flow.
      onTap: _loading ? null : () => _openReview(request),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: [
              Expanded(
                // Lead with the material, not the request number: an approver
                // decides on what is being bought.
                child: Text(
                  FieldFormat.materialRequestLabel(request),
                  style: const TextStyle(fontWeight: FontWeight.w700),
                ),
              ),
              const SizedBox(width: 10),
              StatusChip(
                label: FieldFormat.humanize(status),
                tone: FieldFormat.statusTone(status),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            '${request['projectName'] ?? 'Project'} · required '
            '${FieldFormat.date(request['requiredDate'])}',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 6,
            runSpacing: 6,
            children: [
              // Who raised it. An approver works a queue raised by other
              // people, so the material alone does not say who to ask.
              if (requester != null && requester.isNotEmpty)
                _tag(Icons.person_outline, 'Requested by $requester'),
              _tag(
                Icons.flag_outlined,
                FieldFormat.humanize(request['priority']?.toString()),
              ),
              if (count > 0)
                _tag(
                  Icons.inventory_2_outlined,
                  count == 1 ? '1 item' : '$count items',
                ),
            ],
          ),
          const SizedBox(height: 8),
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'Request #$id',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              Text(
                'Review →',
                style: Theme.of(context).textTheme.labelLarge?.copyWith(
                  color: Theme.of(context).colorScheme.primary,
                  fontWeight: FontWeight.w700,
                ),
              ),
            ],
          ),
        ],
      ),
    );
  }

  Widget _tag(IconData icon, String label) => Container(
    padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 4),
    decoration: BoxDecoration(
      color: Theme.of(context).colorScheme.surfaceContainerHighest,
      borderRadius: BorderRadius.circular(999),
    ),
    child: Row(
      mainAxisSize: MainAxisSize.min,
      children: [
        Icon(icon, size: 13, color: Theme.of(context).colorScheme.outline),
        const SizedBox(width: 4),
        Text(label, style: Theme.of(context).textTheme.labelSmall),
      ],
    ),
  );
}
