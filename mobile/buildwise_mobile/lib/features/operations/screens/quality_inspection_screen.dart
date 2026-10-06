import 'dart:convert';

import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
import '../widgets/agent_analysis_panels.dart';
import '../../procurement/services/notification_service.dart';

class QualityInspectionScreen extends StatefulWidget {
  const QualityInspectionScreen({
    super.key,
    this.service,
    this.readOnly = false,
    this.canManageNcrs = true,
    this.ncrOnly = false,
  });
  final OperationsService? service;

  /// Hides the "New Inspection" form for roles that may only read quality
  /// records. The API enforces the same split through QualityControlOnly.
  final bool readOnly;

  /// Whether this session may move an NCR to Resolved/Closed. The API only
  /// allows that under ProcurementDecisionOnly (Procurement Manager, Site
  /// Manager, Administrator), so a read-only session must not be shown a
  /// button that would 403.
  final bool canManageNcrs;
  final bool ncrOnly;

  @override
  State<QualityInspectionScreen> createState() =>
      _QualityInspectionScreenState();
}

class _QualityInspectionScreenState extends State<QualityInspectionScreen> {
  final _service = OperationsService();
  List<Map<String, dynamic>> _deliveries = const [];
  List<Map<String, dynamic>> _inspections = const [];
  List<Map<String, dynamic>> _ncrs = const [];
  Map<String, dynamic>? _selected;
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
      final service = widget.service ?? _service;
      final values = await Future.wait([
        service.listDeliveries(),
        service.listNonConformances(),
        service.listInspections(),
      ]);
      if (mounted) {
        setState(() {
          _deliveries = values[0];
          _ncrs = values[1];
          _inspections = values[2];
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

  /// Runs the QualityRiskAnalysisAgent (:8004) over one completed inspection.
  /// The agent is read-only: it reports risk and an NCR recommendation but
  /// never creates or changes the inspection or the NCR.
  Future<void> _analyzeInspection(Map<String, dynamic> inspection) async {
    final id = (inspection['id'] as num).toInt();
    await showAiAnalysisSheet(
      context,
      title: 'AI Quality Risk Analysis — INS-$id',
      run: () => (widget.service ?? _service).analyzeQualityRisk(id),
      builder: buildQualityRiskPanel,
    );
  }

  @override
  Widget build(BuildContext context) {
    final openNcrs = _ncrs.where((n) {
      final s = n['status']?.toString() ?? '';
      return s != 'Closed';
    }).length;
    final highNcrs = _ncrs.where((n) {
      final sev = n['severity']?.toString() ?? '';
      return sev == 'High' || sev == 'Critical';
    }).length;
    final resolvedNcrs = _ncrs.where((n) {
      final s = n['status']?.toString() ?? '';
      return s == 'Resolved' || s == 'Closed' || s == 'AcceptedException';
    }).length;
    final flaggedItems = _inspections.fold<int>(0, (sum, ins) {
      final items = ins['items'] as List<dynamic>? ?? [];
      return sum +
          items
              .where(
                (i) =>
                    ((i as Map<String, dynamic>)['rejectedQuantity'] as num? ??
                        0) >
                    0,
              )
              .length;
    });

    return Scaffold(
      appBar: AppBar(
        title: Text(
          widget.ncrOnly ? 'Non-Conformance Reports' : 'Quality Inspections',
        ),
        actions: [
          IconButton(onPressed: _load, icon: const Icon(Icons.refresh)),
        ],
      ),
      body: _loading
          ? const Center(child: CircularProgressIndicator())
          : _error != null
          ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
          : ListView(
              padding: const EdgeInsets.all(16),
              children: [
                // ── Stats summary ─────────────────────────────────────
                AppCard(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      Text(
                        'Quality Inspections & NCRs',
                        style: Theme.of(context).textTheme.titleMedium,
                      ),
                      const SizedBox(height: 4),
                      Text(
                        'Track site inspection results, defect rates and active NCRs.',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      const SizedBox(height: 14),
                      Row(
                        children: [
                          _StatTile(
                            label: 'Open NCRs',
                            value: '$openNcrs',
                            color: openNcrs > 0
                                ? Colors.orange.shade700
                                : Colors.green.shade700,
                          ),
                          const SizedBox(width: 10),
                          _StatTile(
                            label: 'High/Critical',
                            value: '$highNcrs',
                            color: highNcrs > 0
                                ? Colors.red.shade700
                                : Colors.grey,
                          ),
                          const SizedBox(width: 10),
                          _StatTile(
                            label: 'Resolved',
                            value: '$resolvedNcrs',
                            color: Colors.green.shade700,
                          ),
                          const SizedBox(width: 10),
                          _StatTile(
                            label: 'Flagged Items',
                            value: '$flaggedItems',
                            color: flaggedItems > 0
                                ? Colors.orange.shade700
                                : Colors.grey,
                          ),
                        ],
                      ),
                    ],
                  ),
                ),
                const SizedBox(height: 16),

                // ── New inspection form ───────────────────────────────
                if (!widget.readOnly && !widget.ncrOnly) ...[
                  Text(
                    'New Inspection',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 4),
                  Text(
                    'DEL received → Inspector records result → AI quality risk analysis → NCR if rejected',
                    style: Theme.of(context).textTheme.bodySmall
                        ?.copyWith(fontStyle: FontStyle.italic),
                  ),
                  const SizedBox(height: 12),
                  AppDropdown(
                    label: 'Select Delivery',
                    value: _selected == null
                        ? null
                        : _selected!['id'].toString(),
                    items: _deliveries.map((d) => d['id'].toString()).toList(),
                    onChanged: (value) => setState(() {
                      _selected = _deliveries.firstWhere(
                        (d) => d['id'].toString() == value,
                      );
                    }),
                  ),
                  if (_selected != null) ...[
                    const SizedBox(height: 8),
                    AppCard(
                      child: Row(
                        children: [
                          const Icon(Icons.local_shipping_outlined, size: 16),
                          const SizedBox(width: 6),
                          Text(
                            'DEL-${_selected!['id']}  ·  ${_selected!['deliveryReference'] ?? 'no ref'}',
                            style: Theme.of(context).textTheme.bodySmall,
                          ),
                        ],
                      ),
                    ),
                    const SizedBox(height: 8),
                    AppCard(
                      child: _InspectionForm(
                        key: ValueKey(_selected!['id']),
                        delivery: _selected!,
                        service: widget.service ?? _service,
                        onSaved: _load,
                      ),
                    ),
                  ],
                  const SizedBox(height: 24),
                ],

                // ── Inspection history ────────────────────────────────
                if (!widget.ncrOnly) ...[
                  Text(
                    'Inspection History',
                    style: Theme.of(context).textTheme.titleLarge,
                  ),
                  const SizedBox(height: 10),
                  if (_inspections.isEmpty)
                    const AppCard(child: Text('No inspections recorded yet.'))
                  else
                    ..._inspections.map(
                      (inspection) => Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: _InspectionHistoryCard(
                          inspection: inspection,
                          onAnalyze: () => _analyzeInspection(inspection),
                        ),
                      ),
                    ),
                  const SizedBox(height: 24),

                  // ── Active NCRs ───────────────────────────────────────
                ],
                Row(
                  children: [
                    Expanded(
                      child: Text(
                        'Active Non-Conformance Reports',
                        style: Theme.of(context).textTheme.titleLarge,
                      ),
                    ),
                    const SizedBox(width: 8),
                    if (openNcrs > 0)
                      StatusChip(
                        label: '$openNcrs open',
                        tone: StatusTone.danger,
                      ),
                  ],
                ),
                const SizedBox(height: 10),
                if (_ncrs.isEmpty)
                  const AppCard(
                    child: Text(
                      'No active NCRs. Rejected quantities generate one automatically.',
                    ),
                  )
                else
                  ..._ncrs.map(
                    (ncr) => Padding(
                      padding: const EdgeInsets.only(bottom: 10),
                      child: AppCard(
                        child: _NcrCard(
                          ncr: ncr,
                          service: widget.service ?? _service,
                          onUpdated: _load,
                          canManageNcrs: widget.canManageNcrs,
                        ),
                      ),
                    ),
                  ),
              ],
            ),
    );
  }
}

/// Small coloured stat tile used in the summary header.
class _StatTile extends StatelessWidget {
  const _StatTile({
    required this.label,
    required this.value,
    required this.color,
  });
  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) => Expanded(
    child: Column(
      children: [
        Text(
          value,
          style: TextStyle(
            fontSize: 20,
            fontWeight: FontWeight.bold,
            color: color,
          ),
        ),
        const SizedBox(height: 2),
        Text(
          label,
          style: Theme.of(context).textTheme.labelSmall,
          textAlign: TextAlign.center,
        ),
      ],
    ),
  );
}

class _NcrCard extends StatefulWidget {
  const _NcrCard({
    required this.ncr,
    required this.service,
    required this.onUpdated,
    this.canManageNcrs = true,
  });
  final Map<String, dynamic> ncr;
  final OperationsService service;
  final VoidCallback onUpdated;
  final bool canManageNcrs;

  @override
  State<_NcrCard> createState() => _NcrCardState();
}

class _NcrCardState extends State<_NcrCard> {
  bool _transitioning = false;
  final _resolution = TextEditingController();

  @override
  void initState() {
    super.initState();
    _resolution.text = widget.ncr['resolution']?.toString() ?? '';
  }

  @override
  void dispose() {
    _resolution.dispose();
    super.dispose();
  }

  Future<void> _transition(String newStatus) async {
    if (['Resolved', 'Closed', 'AcceptedException'].contains(newStatus) &&
        _resolution.text.trim().isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('A resolution is required for this NCR transition.'),
        ),
      );
      return;
    }
    setState(() => _transitioning = true);
    try {
      await widget.service.transitionNonConformance(
        (widget.ncr['id'] as num).toInt(),
        {'status': newStatus, 'resolution': _resolution.text.trim()},
      );
      if (mounted) widget.onUpdated();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              'Could not update NCR: ${e.toString().replaceFirst('Exception: ', '')}',
            ),
          ),
        );
      }
    } finally {
      if (mounted) setState(() => _transitioning = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final ncr = widget.ncr;
    final sev = ncr['severity']?.toString() ?? '';
    final status = ncr['status']?.toString() ?? '';
    final isResolved =
        status == 'Resolved' ||
        status == 'Closed' ||
        status == 'AcceptedException';
    final sevColor = (sev == 'Critical' || sev == 'High')
        ? Colors.red.shade700
        : Colors.orange.shade700;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              ncr['ncrNumber']?.toString() ?? 'NCR',
              style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15),
            ),
            StatusChip(
              label: isResolved ? 'Resolved' : '$sev Severity',
              tone: isResolved
                  ? StatusTone.success
                  : (sev == 'Critical' || sev == 'High'
                        ? StatusTone.danger
                        : StatusTone.warning),
            ),
          ],
        ),
        const SizedBox(height: 6),
        if (ncr['materialName'] != null)
          Text(
            'Material: ${ncr['materialName']}',
            style: Theme.of(context).textTheme.bodySmall
                ?.copyWith(fontWeight: FontWeight.w600),
          ),
        const SizedBox(height: 4),
        Text(
          ncr['issueDescription']?.toString() ?? 'Quality issue',
          style: Theme.of(context).textTheme.bodySmall,
        ),
        if ((ncr['correctiveActionPlan'] ?? ncr['correctiveAction']) !=
            null) ...[
          const SizedBox(height: 6),
          Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: sevColor.withValues(alpha: 0.08),
              borderRadius: BorderRadius.circular(6),
              border: Border.all(color: sevColor.withValues(alpha: 0.25)),
            ),
            child: Row(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Icon(Icons.build_outlined, size: 14, color: sevColor),
                const SizedBox(width: 6),
                Expanded(
                  child: Text(
                    ncr['correctiveAction'].toString(),
                    style: Theme.of(context).textTheme.bodySmall,
                  ),
                ),
              ],
            ),
          ),
        ],
        const SizedBox(height: 8),
        Row(
          children: [
            Icon(
              Icons.calendar_today_outlined,
              size: 12,
              color: Colors.grey.shade600,
            ),
            const SizedBox(width: 4),
            Text(
              '$status  ·  Created ${_fmtDate(ncr['createdAt']?.toString())}',
              style: Theme.of(context).textTheme.labelSmall,
            ),
          ],
        ),
        if (!['Closed', 'AcceptedException'].contains(status) &&
            widget.canManageNcrs) ...[
          const SizedBox(height: 10),
          AppTextField(
            label: 'Resolution',
            controller: _resolution,
            maxLines: 2,
          ),
          const SizedBox(height: 8),
          Wrap(
            spacing: 8,
            runSpacing: 8,
            children: [
              for (final next in switch (status) {
                'Open' => ['UnderReview', 'CorrectiveActionRequired'],
                'UnderReview' => [
                  'CorrectiveActionRequired',
                  'AcceptedException',
                ],
                'CorrectiveActionRequired' => [
                  'UnderReview',
                  'Resolved',
                  'AcceptedException',
                ],
                'Resolved' => ['Closed'],
                _ => <String>[],
              })
                AppButton(
                  label: _humanize(next),
                  onPressed: _transitioning ? null : () => _transition(next),
                ),
            ],
          ),
        ],
      ],
    );
  }
}

String _fmtDate(String? iso) {
  if (iso == null) return '—';
  try {
    final d = DateTime.parse(iso);
    return '${d.day}/${d.month}/${d.year}';
  } catch (_) {
    return iso.split('T').first;
  }
}

/// One completed inspection plus the action that runs the quality agent over it.
///
/// The decision badge reflects the backend's authoritative record. The three-way
/// classification is backend-derived: Accepted, Partially Accepted, or Rejected
/// (when every line has AcceptedQuantity = 0).
class _InspectionHistoryCard extends StatelessWidget {
  const _InspectionHistoryCard({
    required this.inspection,
    required this.onAnalyze,
  });

  final Map<String, dynamic> inspection;
  final VoidCallback onAnalyze;

  @override
  Widget build(BuildContext context) {
    final id = (inspection['id'] as num?)?.toInt() ?? 0;
    final decision = inspection['overallDecision']?.toString();
    final items = inspection['items'] as List<dynamic>? ?? const [];
    final rejected = items.fold<num>(
      0,
      (sum, item) =>
          sum +
          ((item as Map<String, dynamic>)['rejectedQuantity'] as num? ?? 0),
    );
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text('INS-$id', style: Theme.of(context).textTheme.titleMedium),
              StatusChip(
                label: _humanize(decision),
                tone: switch (decision) {
                  'Accepted' => StatusTone.success,
                  'PartiallyAccepted' => StatusTone.warning,
                  'Rejected' => StatusTone.danger,
                  _ => StatusTone.neutral,
                },
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            'Delivery DEL-${inspection['deliveryId']} · '
            '${_humanize(inspection['status'])} · $rejected rejected',
            style: Theme.of(context).textTheme.bodySmall,
          ),
          const SizedBox(height: 8),
          _ChecklistRow(inspection: inspection),
          if (inspection['notes']?.toString().trim().isNotEmpty == true) ...[
            const SizedBox(height: 8),
            const Text(
              'Inspector Comments / Notes',
              style: TextStyle(fontWeight: FontWeight.w600),
            ),
            Text(inspection['notes'].toString()),
          ],
          for (final file
              in (inspection['evidence'] as List<dynamic>? ?? const []))
            _SavedInspectionEvidence(
              file: Map<String, dynamic>.from(file as Map),
            ),
          const SizedBox(height: 12),
          AppButton(
            label: 'Run AI Analysis',
            expand: true,
            variant: AppButtonVariant.secondary,
            onPressed: onAnalyze,
          ),
        ],
      ),
    );
  }
}

String _humanize(String? value) {
  if (value == null || value.isEmpty) return '—';
  final text = value.replaceAllMapped(
    RegExp('([a-z0-9])([A-Z])'),
    (match) => '${match.group(1)} ${match.group(2)}',
  );
  return '${text[0].toUpperCase()}${text.substring(1)}';
}

class _SavedInspectionEvidence extends StatelessWidget {
  const _SavedInspectionEvidence({required this.file});
  final Map<String, dynamic> file;
  @override
  Widget build(BuildContext context) {
    final url = file['fileUrl']?.toString() ?? '';
    final name = file['fileName']?.toString() ?? 'Inspection evidence';
    Widget? photo;
    try {
      if (url.startsWith('data:image/')) {
        photo = Image.memory(
          base64Decode(url.substring(url.indexOf(',') + 1)),
          fit: BoxFit.contain,
          errorBuilder: (_, _, _) => const Text('Image unavailable'),
        );
      } else if (Uri.tryParse(url)?.scheme == 'https' ||
          Uri.tryParse(url)?.scheme == 'http') {
        if ((file['contentType']?.toString() ?? '').startsWith('image/') ||
            RegExp(
              r'\.(png|jpe?g|webp|gif)(\?|$)',
              caseSensitive: false,
            ).hasMatch(url)) {
          photo = Image.network(
            url,
            fit: BoxFit.contain,
            errorBuilder: (_, _, _) => const Text('Image unavailable'),
          );
        }
      }
    } catch (_) {
      photo = const Text('Image unavailable');
    }
    return Padding(
      padding: const EdgeInsets.only(top: 8),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text(name),
          if (photo != null)
            GestureDetector(
              onTap: () => showDialog<void>(
                context: context,
                builder: (_) => Dialog(
                  child: Padding(
                    padding: const EdgeInsets.all(16),
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: [
                        Text(name),
                        SizedBox(height: 300, child: photo),
                        TextButton(
                          onPressed: () => Navigator.pop(context),
                          child: const Text('Close'),
                        ),
                      ],
                    ),
                  ),
                ),
              ),
              child: SizedBox(height: 88, width: 120, child: photo),
            )
          else if (url.isNotEmpty)
            SelectableText(url),
        ],
      ),
    );
  }
}

class _InspectionForm extends StatefulWidget {
  const _InspectionForm({
    super.key,
    required this.delivery,
    required this.service,
    required this.onSaved,
  });
  final Map<String, dynamic> delivery;
  final OperationsService service;
  final Future<void> Function() onSaved;

  @override
  State<_InspectionForm> createState() => _InspectionFormState();
}

class _InspectionFormState extends State<_InspectionForm> {
  final _inspected = TextEditingController();
  final _accepted = TextEditingController();
  final _rejected = TextEditingController(text: '0');
  final _reason = TextEditingController();
  final _criteria = TextEditingController(
    text: 'Visual check for damage, moisture, and packaging integrity',
  );
  final _observed = TextEditingController(
    text: 'Material checked against the inspection criteria',
  );
  final _notes = TextEditingController();
  final _evidenceUrl = TextEditingController();
  final _evidenceName = TextEditingController();
  EvidencePhoto? _evidencePhoto;
  int? _materialId;
  List<Map<String, dynamic>> get _deliveryItems =>
      (widget.delivery['items'] as List<dynamic>? ?? const [])
          .cast<Map<String, dynamic>>();
  double get _receivedQuantity => _deliveryItems
      .where((item) => item['materialId'] == _materialId)
      .fold(
        0.0,
        (sum, item) => sum + (item['receivedQuantity'] as num? ?? 0).toDouble(),
      );
  bool _submitting = false;
  String? _error;

  static const List<(String, String)> _checkPoints = [
    ('Quantity', 'quantity'),
    ('Visual condition', 'visual'),
    ('Moisture', 'moisture'),
    ('Packaging', 'packaging'),
    ('Defects', 'defects'),
  ];

  final Map<String, bool> _checks = {
    'quantity': true,
    'visual': true,
    'moisture': true,
    'packaging': true,
    'defects': true,
  };

  @override
  void initState() {
    super.initState();
    final items = widget.delivery['items'] as List<dynamic>? ?? const [];
    _materialId = items.isEmpty
        ? null
        : (items.first as Map<String, dynamic>)['materialId'] as int?;
    final quantity = items.isEmpty
        ? 0
        : ((items.first as Map<String, dynamic>)['receivedQuantity'] as num? ??
              0);
    _inspected.text = quantity.toString();
    _syncAccepted();
  }

  void _syncAccepted() {
    final inspected = double.tryParse(_inspected.text) ?? 0.0;
    final rejected = double.tryParse(_rejected.text) ?? 0.0;
    if (!inspected.isFinite || !rejected.isFinite) {
      _accepted.text = '0';
      return;
    }
    final calculated = (inspected - rejected).clamp(
      0.0,
      inspected.isFinite && inspected > 0 ? inspected : 0.0,
    );
    final text = calculated % 1 == 0
        ? calculated.toInt().toString()
        : calculated.toString();
    if (_accepted.text != text) {
      _accepted.text = text;
    }
  }

  @override
  void dispose() {
    _inspected.dispose();
    _accepted.dispose();
    _rejected.dispose();
    _reason.dispose();
    _criteria.dispose();
    _observed.dispose();
    _notes.dispose();
    _evidenceUrl.dispose();
    _evidenceName.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final inspected = double.tryParse(_inspected.text) ?? -1;
    final accepted = double.tryParse(_accepted.text) ?? -1;
    final rejected = double.tryParse(_rejected.text) ?? -1;

    if (!inspected.isFinite ||
        !accepted.isFinite ||
        !rejected.isFinite ||
        inspected <= 0 ||
        accepted < 0 ||
        rejected < 0 ||
        rejected > inspected ||
        ((accepted + rejected - inspected).abs() > 0.000000001)) {
      setState(
        () => _error = 'Inspected must be positive, and Accepted + Rejected must exactly equal Inspected.',
      );
      return;
    }

    final hasChecklistFailure = _checks.values.any((pass) => !pass);
    if (rejected > 0 && _reason.text.trim().isEmpty) {
      setState(
        () => _error =
            'A rejection reason is required when material is rejected.',
      );
      return;
    }

    if (hasChecklistFailure && _notes.text.trim().isEmpty) {
      setState(
        () => _error = 'Notes are required when any checklist item fails.',
      );
      return;
    }
    if (_evidencePhoto != null && _evidencePhoto!.sizeBytes > 10000000) {
      setState(() => _error = 'Evidence files must be 10 MB or less.');
      return;
    }
    final items = widget.delivery['items'] as List<dynamic>? ?? const [];
    if (items.isEmpty) {
      setState(() => _error = 'The selected delivery has no item lines.');
      return;
    }
    final first = _deliveryItems
        .where((item) => item['materialId'] == _materialId)
        .firstOrNull;
    if (first == null) {
      setState(() => _error = 'Material must belong to the selected delivery.');
      return;
    }
    if ((widget.delivery['id'] as num? ?? 0) <= 0 ||
        (first['materialId'] as num? ?? 0) <= 0) {
      setState(() => _error = 'Select a delivery with a valid material item.');
      return;
    }
    final receivedQty = _receivedQuantity;
    if (inspected > receivedQty) {
      setState(
        () => _error =
            'Inspected quantity ($inspected) cannot exceed received quantity ($receivedQty).',
      );
      return;
    }

    setState(() {
      _submitting = true;
      _error = null;
    });

    try {
      final evidenceList = <Map<String, dynamic>>[];
      if (_evidencePhoto != null) {
        evidenceList.add({
          ..._evidencePhoto!.toPayload(),
          if (_evidenceName.text.trim().isNotEmpty)
            'fileName': _evidenceName.text.trim(),
        });
      } else if (_evidenceUrl.text.trim().isNotEmpty) {
        evidenceList.add({
          'fileName': _evidenceName.text.trim().isEmpty
              ? 'inspection-evidence.jpg'
              : _evidenceName.text.trim(),
          'fileUrl': _evidenceUrl.text.trim(),
          'contentType': 'image/jpeg',
          'fileSizeBytes': 0,
        });
      }

      await widget.service.createInspection(
        deliveryId: (widget.delivery['id'] as num).toInt(),
        materialId: (first['materialId'] as num).toInt(),
        inspected: inspected,
        accepted: accepted,
        rejected: rejected,
        reason: _reason.text.trim(),
        criteria: _criteria.text.trim(),
        observedResult: _observed.text.trim(),
        notes: _notes.text.trim(),
        quantityCheck: _checks['quantity']!,
        visualConditionCheck: _checks['visual']!,
        moistureCheck: _checks['moisture']!,
        packagingCheck: _checks['packaging']!,
        defectsCheck: _checks['defects']!,
        evidence: evidenceList,
      );

      if (mounted) {
        final messenger = ScaffoldMessenger.of(context);
        await NotificationService.instance.showQualityUpdate(
          id: (widget.delivery['id'] as num).toInt(),
          title: 'Quality inspection completed',
          body: 'Inspection results are available and NCR follow-up has been created when required.',
        );
        if (!mounted) return;
        messenger.showSnackBar(
          const SnackBar(
            content: Text('Inspection completed. NCR list refreshed.'),
          ),
        );
        await widget.onSaved();
      }
    } catch (e) {
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      AppDropdown(
        label: 'Material',
        value: _materialId?.toString(),
        items: _deliveryItems
            .map((item) => item['materialId'].toString())
            .toSet()
            .toList(),
        itemLabels: _deliveryItems
            .map((item) => item['materialId'].toString())
            .toSet()
            .map(
              (id) =>
                  _deliveryItems
                      .firstWhere(
                        (item) => item['materialId'].toString() == id,
                      )['materialName']
                      ?.toString() ??
                  'Material #$id',
            )
            .toList(),
        onChanged: (value) => setState(() {
          _materialId = int.tryParse(value ?? '');
          _inspected.text = _receivedQuantity.toString();
          _rejected.text = '0';
          _reason.clear();
          _syncAccepted();
        }),
      ),
      const SizedBox(height: 12),
      Row(
        children: [
          Expanded(
            child: AppTextField(
              label: 'Inspected Quantity',
              controller: _inspected,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              onChanged: (_) => setState(_syncAccepted),
            ),
          ),
          const SizedBox(width: 10),
          Expanded(
            child: AppTextField(
              label: 'Rejected Quantity',
              controller: _rejected,
              keyboardType: const TextInputType.numberWithOptions(
                decimal: true,
              ),
              onChanged: (_) => setState(_syncAccepted),
            ),
          ),
        ],
      ),
      const SizedBox(height: 10),
      AppTextField(
        label: 'Accepted Quantity (Auto-calculated)',
        controller: _accepted,
        readOnly: true,
      ),
      const SizedBox(height: 14),
      Text(
        '5-Point Quality Checklist',
        style: Theme.of(context).textTheme.titleSmall,
      ),
      const SizedBox(height: 4),
      Text(
        'Pass / Fail for each criterion. Every point must be explicitly evaluated.',
        style: Theme.of(context).textTheme.bodySmall,
      ),
      const SizedBox(height: 6),
      for (final (label, key) in _checkPoints)
        Container(
          margin: const EdgeInsets.only(bottom: 6),
          padding: const EdgeInsets.symmetric(horizontal: 10, vertical: 4),
          decoration: BoxDecoration(
            color: _checks[key]! ? Colors.green.shade50 : Colors.red.shade50,
            borderRadius: BorderRadius.circular(8),
            border: Border.all(
              color: _checks[key]!
                  ? Colors.green.shade200
                  : Colors.red.shade200,
            ),
          ),
          child: Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                label,
                style: TextStyle(
                  fontWeight: FontWeight.w600,
                  color: _checks[key]!
                      ? Colors.green.shade900
                      : Colors.red.shade900,
                ),
              ),
              Row(
                mainAxisSize: MainAxisSize.min,
                children: [
                  Text(
                    _checks[key]! ? 'PASS' : 'FAIL',
                    style: TextStyle(
                      fontWeight: FontWeight.bold,
                      fontSize: 12,
                      color: _checks[key]!
                          ? Colors.green.shade700
                          : Colors.red.shade700,
                    ),
                  ),
                  const SizedBox(width: 6),
                  Switch(
                    value: _checks[key]!,
                    activeThumbColor: Colors.green.shade700,
                    inactiveThumbColor: Colors.red.shade700,
                    onChanged: (value) => setState(() => _checks[key] = value),
                  ),
                ],
              ),
            ],
          ),
        ),
      const SizedBox(height: 10),
      AppTextField(
        label: 'Rejection Reason (Required if rejected)',
        controller: _reason,
        maxLines: 2,
      ),
      const SizedBox(height: 10),
      AppTextField(
        label: 'Inspection Criteria',
        controller: _criteria,
        maxLines: 2,
      ),
      const SizedBox(height: 10),
      AppTextField(
        label: 'Observed Result',
        controller: _observed,
        maxLines: 2,
      ),
      const SizedBox(height: 10),
      AppTextField(
        label: 'Notes',
        hint: 'Inspector comments or re-inspection remarks',
        controller: _notes,
        maxLines: 2,
      ),
      const SizedBox(height: 14),
      EvidencePickerWidget(
        title: 'Inspection Photo Evidence',
        subtitle:
            'Capture damaged bags, packaging tears, or site test certificate.',
        onChanged: (photo) => setState(() => _evidencePhoto = photo),
      ),
      AppTextField(
        label: 'Evidence File Name (Optional)',
        controller: _evidenceName,
      ),
      if (_evidencePhoto == null) ...[
        const SizedBox(height: 8),
        AppTextField(
          label: 'Or Evidence URL (optional)',
          controller: _evidenceUrl,
          keyboardType: TextInputType.url,
        ),
      ],
      if (_error != null) ...[
        const SizedBox(height: 10),
        Text(
          _error!,
          style: const TextStyle(
            color: Colors.red,
            fontWeight: FontWeight.w600,
          ),
        ),
      ],
      const SizedBox(height: 16),
      AppButton(
        label: _submitting ? 'Submitting…' : 'Submit Inspection',
        expand: true,
        onPressed: _submitting ? null : _submit,
      ),
    ],
  );
}

/// Renders the five-point checklist for one inspection.
///
/// The three states are deliberately distinct, because they mean different
/// things and collapsing them would be misleading:
///   true  -> checked and passed  (✓ Pass)
///   false -> checked and failed  (✗ Fail)
///   null  -> never recorded      (legacy inspection predating the structured
///                                 checklist)
/// A legacy row must not render as a pass — showing "✓ Quantity" for an
/// inspection that never recorded it would fabricate a quality result.
class _ChecklistRow extends StatelessWidget {
  const _ChecklistRow({required this.inspection});

  final Map<String, dynamic> inspection;

  static const List<(String, String)> _points = [
    ('Quantity', 'quantityCheck'),
    ('Visual condition', 'visualConditionCheck'),
    ('Moisture', 'moistureCheck'),
    ('Packaging', 'packagingCheck'),
    ('Defects', 'defectsCheck'),
  ];

  @override
  Widget build(BuildContext context) {
    final recorded = _points.where((p) => inspection[p.$2] != null).length;
    if (recorded == 0) {
      return Text(
        'Quality checklist not recorded',
        style: Theme.of(context).textTheme.bodySmall
            ?.copyWith(fontStyle: FontStyle.italic),
      );
    }
    return Wrap(
      spacing: 10,
      runSpacing: 4,
      children: [
        for (final (label, key) in _points)
          _ChecklistChip(label: label, value: inspection[key] as bool?),
      ],
    );
  }
}

class _ChecklistChip extends StatelessWidget {
  const _ChecklistChip({required this.label, required this.value});

  final String label;
  final bool? value;

  @override
  Widget build(BuildContext context) {
    // An unrecorded point is shown as a dash, never as a tick.
    final text = value == null
        ? '$label · —'
        : value!
        ? '✓ $label'
        : '✗ $label';
    final color = value == null
        ? Theme.of(context).textTheme.bodySmall?.color
        : value!
        ? Colors.green.shade700
        : Colors.red.shade700;
    return Text(text, style: TextStyle(color: color, fontSize: 12));
  }
}
