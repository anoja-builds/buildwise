import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/field_format.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/procurement_service.dart';
import '../widgets/procurement_status_tone.dart';

class _RfqEmailDialog extends StatefulWidget {
  const _RfqEmailDialog();
  @override
  State<_RfqEmailDialog> createState() => _RfqEmailDialogState();
}

class _RfqEmailDialogState extends State<_RfqEmailDialog> {
  final _email = TextEditingController();
  final _message = TextEditingController();
  String? _error;
  @override
  void dispose() {
    _email.dispose();
    _message.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) => AlertDialog(
    title: const Text('Send RFQ email'),
    content: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        children: [
          AppTextField(
            label: 'Recipient email',
            controller: _email,
            keyboardType: TextInputType.emailAddress,
          ),
          AppTextField(
            label: 'Custom message',
            controller: _message,
            maxLines: 3,
          ),
          if (_error != null)
            Text(_error!, style: const TextStyle(color: Colors.red)),
        ],
      ),
    ),
    actions: [
      TextButton(
        onPressed: () => Navigator.pop(context),
        child: const Text('Cancel'),
      ),
      TextButton(
        onPressed: () {
          if (!RegExp(r'^[^\s@]+@[^\s@]+\.[^\s@]+$')
              .hasMatch(_email.text.trim())) {
            setState(() => _error = 'Enter a valid recipient email.');
            return;
          }
          Navigator.pop(context, {
            'email': _email.text.trim(),
            'message': _message.text.trim(),
          });
        },
        child: const Text('Send email'),
      ),
    ],
  );
}

/// RFQ register — mirrors the web app's RFQs page.
///
/// Reached by the Procurement Officer and Procurement Manager
/// (`BuildWiseRoles.procurementDesk`). It points at the same endpoints the
/// React page uses, so the two clients cannot drift in behaviour.
///
/// An RFQ can only be raised against an **Approved** material request, and at
/// least one active supplier must be invited, because the whole point is to open
/// a quotation window with real suppliers. Suppliers themselves are external
/// parties: BuildWise emails them, they never log in.
class RfqScreen extends StatefulWidget {
  const RfqScreen({
    super.key,
    this.service,
    this.canIssue = true,
    this.canClose = true,
  });

  final ProcurementService? service;
  final bool canIssue;
  final bool canClose;

  @override
  State<RfqScreen> createState() => _RfqScreenState();
}

class _RfqScreenState extends State<RfqScreen> {
  final _service = ProcurementService();
  List<Map<String, dynamic>> _rfqs = const [];
  bool _loading = true;
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
      final rfqs = await (widget.service ?? _service).listRfqs();
      if (mounted) setState(() => _rfqs = rfqs);
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _issue() async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _IssueRfqSheet(service: widget.service ?? _service),
    );
    if (created == true) _load();
  }

  Future<void> _email(Map<String, dynamic> rfq) async {
    final result = await showDialog<Map<String, String>>(
      context: context,
      builder: (_) => const _RfqEmailDialog(),
    );
    if (result == null) return;
    try {
      final response = await (widget.service ?? _service).sendRfqEmail(
        (rfq['id'] as num).toInt(),
        recipientEmail: result['email']!,
        customMessage: result['message'],
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              response['message']?.toString() ?? 'Email request processed.',
            ),
          ),
        );
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(e.toString().replaceFirst('Exception: ', ''))),
        );
      }
    }
  }

  Future<void> _close(Map<String, dynamic> rfq) async {
    final id = (rfq['id'] as num).toInt();
    try {
      await (widget.service ?? _service).closeRfq(
        id,
        'Closed from the mobile RFQ workspace.',
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text('RFQ #$id closed.')));
      _load();
    } catch (e) {
      if (!mounted) return;
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text(e.toString().replaceFirst('Exception: ', ''))),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    final body = _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
        : _rfqs.isEmpty
        ? const EmptyStateWidget(
            title: 'No RFQs yet',
            message: 'Issue an RFQ from an approved material request.',
          )
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView.separated(
              padding: const EdgeInsets.all(16),
              itemCount: _rfqs.length,
              separatorBuilder: (_, _) => const SizedBox(height: 10),
              itemBuilder: (_, index) => _RfqCard(
                rfq: _rfqs[index],
                onClose: widget.canClose ? () => _close(_rfqs[index]) : null,
                onEmail: widget.canClose ? () => _email(_rfqs[index]) : null,
              ),
            ),
          );
    return Scaffold(
      appBar: AppBar(
        title: const Text('RFQs'),
        actions: [
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ],
      ),
      floatingActionButton: widget.canIssue
          ? FloatingActionButton.extended(
              onPressed: _issue,
              icon: const Icon(Icons.add),
              label: const Text('Issue RFQ'),
            )
          : null,
      body: body,
    );
  }
}

class _RfqCard extends StatelessWidget {
  const _RfqCard({required this.rfq, required this.onClose, this.onEmail});

  final Map<String, dynamic> rfq;
  final VoidCallback? onClose;
  final VoidCallback? onEmail;

  @override
  Widget build(BuildContext context) {
    final suppliers = (rfq['suppliers'] as List<dynamic>?) ?? const [];
    final status = rfq['status']?.toString() ?? 'Unknown';
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'RFQ #${rfq['id']}',
                style: Theme.of(context).textTheme.titleMedium,
              ),
              StatusChip(label: status, tone: procurementStatusTone(status)),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            rfq['projectName']?.toString() ?? '',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 4),
          Text(
            'Responses by ${rfq['requiredResponseDate'] ?? '—'} · '
            '${suppliers.length} supplier(s) invited',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          if (suppliers.isNotEmpty) ...[
            const SizedBox(height: 8),
            Wrap(
              spacing: 6,
              runSpacing: 6,
              children: [
                for (final entry in suppliers.cast<Map<String, dynamic>>())
                  StatusChip(
                    label:
                        '${entry['supplierName'] ?? 'Supplier'} · ${entry['invitationStatus'] ?? 'Invited'}',
                    tone: entry['invitationStatus']?.toString() == 'Accepted'
                        ? StatusTone.success
                        : StatusTone.neutral,
                  ),
              ],
            ),
          ],
          if (onEmail != null)
            TextButton(onPressed: onEmail, child: const Text('Send RFQ email')),
          // Only an Issued RFQ can still be closed; closing a Draft or an
          // already-Closed one is not a valid transition, so the control is
          // withheld rather than shown and failing.
          if (status == 'Issued' && onClose != null) ...[
            const SizedBox(height: 12),
            AppButton(
              label: 'Close RFQ',
              expand: true,
              variant: AppButtonVariant.secondary,
              onPressed: onClose,
            ),
          ],
        ],
      ),
    );
  }
}

/// Issues an RFQ against an approved request and invites chosen suppliers.
class _IssueRfqSheet extends StatefulWidget {
  const _IssueRfqSheet({required this.service});

  final ProcurementService service;

  @override
  State<_IssueRfqSheet> createState() => _IssueRfqSheetState();
}

class _IssueRfqSheetState extends State<_IssueRfqSheet> {
  List<Map<String, dynamic>> _requests = const [];
  List<Map<String, dynamic>> _suppliers = const [];
  final Set<int> _selectedSuppliers = {};
  final _notes = TextEditingController();
  DateTime _responseDate = DateTime.now().add(const Duration(days: 7));
  int? _requestId;
  bool _loading = true;
  bool _submitting = false;
  String? _error;

  /// The same rule the web RFQ form enforces: suppliers must be given time to
  /// respond, so the response date has to fall after today.
  bool get _responseDateIsFuture {
    final now = DateTime.now();
    return _responseDate.isAfter(DateTime(now.year, now.month, now.day));
  }

  String get _responseDateHint => _responseDateIsFuture
      ? 'Suppliers must respond by this date (must be in the future).'
      : 'Response date must be after today.';

  @override
  void initState() {
    super.initState();
    _load();
  }

  @override
  void dispose() {
    _notes.dispose();
    super.dispose();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      // Awaited separately rather than through Future.wait: the two calls
      // return different shapes (a list and a map), and Future.wait would
      // collapse both to Object and lose the element types.
      final requests = await widget.service.listApprovedMaterialRequests();
      final supplierPage = await widget.service.listSuppliers();
      final suppliers = (supplierPage['items'] as List<dynamic>?) ?? const [];
      if (!mounted) return;
      setState(() {
        _requests = requests;
        // Only Active suppliers can be invited; a Suspended supplier must not
        // be asked to quote.
        _suppliers = suppliers
            .cast<Map<String, dynamic>>()
            .where((s) => s['status']?.toString() == 'Active')
            .toList();
        _requestId = requests.isEmpty
            ? null
            : (requests.first['id'] as num).toInt();
      });
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _submit() async {
    if (_requestId == null) return;
    final request = _requests
        .where((row) => row['id'] == _requestId)
        .firstOrNull;
    final required = DateTime.tryParse(
      request?['requiredDate']?.toString() ?? '',
    );
    if (required != null &&
        DateUtils.dateOnly(_responseDate)
            .isAfter(DateUtils.dateOnly(required))) {
      setState(
        () => _error =
            'Response date cannot be later than the material required date.',
      );
      return;
    }
    if (_selectedSuppliers.isEmpty) {
      setState(() => _error = 'Select at least one active supplier.');
      return;
    }
    if (!_responseDateIsFuture) {
      setState(
        () => _error =
            'Required response date must be a future date (after today).',
      );
      return;
    }
    setState(() {
      _submitting = true;
      _error = null;
    });
    try {
      await widget.service.createRfq(
        materialRequestId: _requestId!,
        requiredResponseDate: _responseDate.toIso8601String().substring(0, 10),
        supplierIds: _selectedSuppliers.toList(),
        notes: _notes.text.trim(),
      );
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final ready =
        _requestId != null &&
        _selectedSuppliers.isNotEmpty &&
        _responseDateIsFuture;
    return Padding(
      padding: EdgeInsets.only(
        bottom: MediaQuery.of(context).viewInsets.bottom,
      ),
      child: SingleChildScrollView(
        child: Padding(
          padding: const EdgeInsets.all(20),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            mainAxisSize: MainAxisSize.min,
            children: [
              Text('Issue RFQ', style: Theme.of(context).textTheme.titleLarge),
              const SizedBox(height: 4),
              Text(
                'RFQs can only be created for Approved material requests.',
                style: Theme.of(context).textTheme.bodySmall,
              ),
              const SizedBox(height: 16),
              if (_loading)
                const Center(
                  child: Padding(
                    padding: EdgeInsets.all(24),
                    child: CircularProgressIndicator(),
                  ),
                )
              else ...[
                if (_requests.isEmpty)
                  const AppCard(
                    child: Text(
                      'No approved material requests to raise an RFQ against.',
                    ),
                  )
                else
                  AppDropdown(
                    label: 'Approved material request',
                    value: _requestId?.toString(),
                    items: _requests.map((r) => r['id'].toString()).toList(),
                    // Label each option with the material, not the bare id.
                    itemLabels: _requests
                        .map(FieldFormat.materialRequestLabel)
                        .toList(),
                    onChanged: (value) => setState(
                      () =>
                          _requestId = value == null ? null : int.parse(value),
                    ),
                  ),
                const SizedBox(height: 12),
                ListTile(
                  contentPadding: EdgeInsets.zero,
                  title: const Text('Required response date'),
                  subtitle: Text(
                    _responseDateHint,
                    style: TextStyle(
                      fontSize: 12,
                      color: _responseDateIsFuture
                          ? Theme.of(context).textTheme.bodySmall?.color
                          : Colors.red,
                    ),
                  ),
                  trailing: Text(
                    _responseDate.toIso8601String().substring(0, 10),
                    style: Theme.of(context).textTheme.bodyMedium,
                  ),
                  onTap: () async {
                    final picked = await showDatePicker(
                      context: context,
                      firstDate: DateTime.now(),
                      lastDate: DateTime.now().add(const Duration(days: 365)),
                      initialDate: _responseDate,
                    );
                    if (picked != null) setState(() => _responseDate = picked);
                  },
                ),
                const SizedBox(height: 8),
                AppTextField(label: 'Notes', controller: _notes, maxLines: 2),
                const SizedBox(height: 16),
                Text(
                  'Invite active suppliers',
                  style: Theme.of(context).textTheme.titleSmall,
                ),
                const SizedBox(height: 8),
                if (_suppliers.isEmpty)
                  const AppCard(child: Text('No active suppliers available.'))
                else
                  ..._suppliers.map((supplier) {
                    final id = (supplier['id'] as num).toInt();
                    return CheckboxListTile(
                      contentPadding: EdgeInsets.zero,
                      value: _selectedSuppliers.contains(id),
                      title: Text(supplier['name']?.toString() ?? 'Supplier'),
                      onChanged: (checked) => setState(() {
                        if (checked == true) {
                          _selectedSuppliers.add(id);
                        } else {
                          _selectedSuppliers.remove(id);
                        }
                      }),
                    );
                  }),
                if (_error != null) ...[
                  const SizedBox(height: 10),
                  Text(_error!, style: const TextStyle(color: Colors.red)),
                ],
                const SizedBox(height: 16),
                AppButton(
                  label: _submitting ? 'Issuing…' : 'Issue RFQ',
                  expand: true,
                  onPressed: (ready && !_submitting) ? _submit : null,
                ),
                const SizedBox(height: 8),
                AppButton(
                  label: 'Cancel',
                  expand: true,
                  variant: AppButtonVariant.secondary,
                  onPressed: () => Navigator.of(context).pop(false),
                ),
              ],
            ],
          ),
        ),
      ),
    );
  }
}
