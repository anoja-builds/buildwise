import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
import '../widgets/agent_analysis_panels.dart';
import '../../procurement/services/notification_service.dart';

class QualityInspectionScreen extends StatefulWidget {
  const QualityInspectionScreen({super.key, this.service, this.readOnly = false});
  final OperationsService? service;
  final bool readOnly;

  @override
  State<QualityInspectionScreen> createState() => _QualityInspectionScreenState();
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
    setState(() { _loading = true; _error = null; });
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
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
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
      return s != 'Resolved' && s != 'Closed';
    }).length;
    final highNcrs = _ncrs.where((n) {
      final sev = n['severity']?.toString() ?? '';
      return sev == 'High' || sev == 'Critical';
    }).length;
    final resolvedNcrs = _ncrs.where((n) {
      final s = n['status']?.toString() ?? '';
      return s == 'Resolved' || s == 'Closed';
    }).length;
    final flaggedItems = _inspections.fold<int>(0, (sum, ins) {
      final items = ins['items'] as List<dynamic>? ?? [];
      return sum + items.where((i) =>
        ((i as Map<String, dynamic>)['rejectedQuantity'] as num? ?? 0) > 0).length;
    });

    return Scaffold(
      appBar: AppBar(
        title: const Text('Quality & NCRs'),
        actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
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
                          Text('Quality Inspections & NCRs',
                              style: Theme.of(context).textTheme.titleMedium),
                          const SizedBox(height: 4),
                          Text('Track site inspection results, defect rates and active NCRs.',
                              style: Theme.of(context).textTheme.bodySmall),
                          const SizedBox(height: 14),
                          Row(children: [
                            _StatTile(label: 'Open NCRs', value: '$openNcrs',
                                color: openNcrs > 0 ? Colors.orange.shade700 : Colors.green.shade700),
                            const SizedBox(width: 10),
                            _StatTile(label: 'High/Critical', value: '$highNcrs',
                                color: highNcrs > 0 ? Colors.red.shade700 : Colors.grey),
                            const SizedBox(width: 10),
                            _StatTile(label: 'Resolved', value: '$resolvedNcrs',
                                color: Colors.green.shade700),
                            const SizedBox(width: 10),
                            _StatTile(label: 'Flagged Items', value: '$flaggedItems',
                                color: flaggedItems > 0 ? Colors.orange.shade700 : Colors.grey),
                          ]),
                        ],
                      ),
                    ),
                    const SizedBox(height: 16),

                    // ── New inspection form ───────────────────────────────
                    if (!widget.readOnly) ...[
                      Text('New Inspection', style: Theme.of(context).textTheme.titleLarge),
                      const SizedBox(height: 4),
                      Text(
                        'DEL received → Inspector records result → AI quality risk analysis → NCR if rejected',
                        style: Theme.of(context).textTheme.bodySmall
                            ?.copyWith(fontStyle: FontStyle.italic),
                      ),
                      const SizedBox(height: 12),
                      AppDropdown(
                        label: 'Select Delivery',
                        value: _selected == null ? null : _selected!['id'].toString(),
                        items: _deliveries.map((d) => d['id'].toString()).toList(),
                        onChanged: (value) => setState(() {
                          _selected = _deliveries.firstWhere((d) => d['id'].toString() == value);
                        }),
                      ),
                      if (_selected != null) ...[
                        const SizedBox(height: 8),
                        AppCard(
                          child: Row(children: [
                            const Icon(Icons.local_shipping_outlined, size: 16),
                            const SizedBox(width: 6),
                            Text(
                              'DEL-${_selected!['id']}  ·  ${_selected!['deliveryReference'] ?? 'no ref'}',
                              style: Theme.of(context).textTheme.bodySmall,
                            ),
                          ]),
                        ),
                        const SizedBox(height: 8),
                        AppCard(child: _InspectionForm(
                          delivery: _selected!,
                          service: widget.service ?? _service,
                          onSaved: _load,
                        )),
                      ],
                      const SizedBox(height: 24),
                    ],

                    // ── Inspection history ────────────────────────────────
                    Text('Inspection History', style: Theme.of(context).textTheme.titleLarge),
                    const SizedBox(height: 10),
                    if (_inspections.isEmpty)
                      const AppCard(child: Text('No inspections recorded yet.'))
                    else
                      ..._inspections.map((inspection) => Padding(
                            padding: const EdgeInsets.only(bottom: 10),
                            child: _InspectionHistoryCard(
                              inspection: inspection,
                              onAnalyze: () => _analyzeInspection(inspection),
                            ),
                          )),
                    const SizedBox(height: 24),

                    // ── Active NCRs ───────────────────────────────────────
                    Row(children: [
                      Text('Active Non-Conformance Reports',
                          style: Theme.of(context).textTheme.titleLarge),
                      const SizedBox(width: 8),
                      if (openNcrs > 0)
                        StatusChip(label: '$openNcrs open', tone: StatusTone.danger),
                    ]),
                    const SizedBox(height: 10),
                    if (_ncrs.isEmpty)
                      const AppCard(
                          child: Text('No active NCRs. Rejected quantities generate one automatically.'))
                    else
                      ..._ncrs.map((ncr) => Padding(
                        padding: const EdgeInsets.only(bottom: 10),
                        child: AppCard(
                          child: _NcrCard(
                            ncr: ncr,
                            service: widget.service ?? _service,
                            onUpdated: _load,
                          ),
                        ),
                      )),
                  ],
                ),
    );
  }
}

/// Small coloured stat tile used in the summary header.
class _StatTile extends StatelessWidget {
  const _StatTile({required this.label, required this.value, required this.color});
  final String label;
  final String value;
  final Color color;

  @override
  Widget build(BuildContext context) => Expanded(
    child: Column(children: [
      Text(value, style: TextStyle(fontSize: 20, fontWeight: FontWeight.bold, color: color)),
      const SizedBox(height: 2),
      Text(label, style: Theme.of(context).textTheme.labelSmall, textAlign: TextAlign.center),
    ]),
  );
}

class _NcrCard extends StatefulWidget {
  const _NcrCard({required this.ncr, required this.service, required this.onUpdated});
  final Map<String, dynamic> ncr;
  final OperationsService service;
  final VoidCallback onUpdated;

  @override
  State<_NcrCard> createState() => _NcrCardState();
}

class _NcrCardState extends State<_NcrCard> {
  bool _transitioning = false;

  Future<void> _transition(String newStatus) async {
    setState(() => _transitioning = true);
    try {
      await widget.service.transitionNonConformance(
        (widget.ncr['id'] as num).toInt(),
        {'status': newStatus},
      );
      if (mounted) widget.onUpdated();
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Could not update NCR: ${e.toString().replaceFirst('Exception: ', '')}')),
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
    final isResolved = status == 'Resolved' || status == 'Closed';
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
                  : (sev == 'Critical' || sev == 'High' ? StatusTone.danger : StatusTone.warning),
            ),
          ],
        ),
        const SizedBox(height: 6),
        if (ncr['materialName'] != null)
          Text('Material: ${ncr['materialName']}',
              style: Theme.of(context).textTheme.bodySmall
                  ?.copyWith(fontWeight: FontWeight.w600)),
        const SizedBox(height: 4),
        Text(ncr['issueDescription']?.toString() ?? 'Quality issue',
            style: Theme.of(context).textTheme.bodySmall),
        if (ncr['correctiveAction'] != null) ...[
          const SizedBox(height: 6),
          Container(
            padding: const EdgeInsets.all(8),
            decoration: BoxDecoration(
              color: sevColor.withValues(alpha: 0.08),
              borderRadius: BorderRadius.circular(6),
              border: Border.all(color: sevColor.withValues(alpha: 0.25)),
            ),
            child: Row(crossAxisAlignment: CrossAxisAlignment.start, children: [
              Icon(Icons.build_outlined, size: 14, color: sevColor),
              const SizedBox(width: 6),
              Expanded(
                child: Text(
                  ncr['correctiveAction'].toString(),
                  style: Theme.of(context).textTheme.bodySmall,
                ),
              ),
            ]),
          ),
        ],
        const SizedBox(height: 8),
        Row(children: [
          Icon(Icons.calendar_today_outlined, size: 12, color: Colors.grey.shade600),
          const SizedBox(width: 4),
          Text(
            '$status  ·  Created ${_fmtDate(ncr['createdAt']?.toString())}',
            style: Theme.of(context).textTheme.labelSmall,
          ),
        ]),
        if (!isResolved) ...[
          const SizedBox(height: 10),
          Row(children: [
            Expanded(
              child: AppButton(
                label: _transitioning ? 'Updating…' : 'Mark Resolved',
                variant: AppButtonVariant.secondary,
                onPressed: _transitioning ? null : () => _transition('Resolved'),
              ),
            ),
            const SizedBox(width: 8),
            Expanded(
              child: AppButton(
                label: _transitioning ? 'Updating…' : 'Close NCR',
                onPressed: _transitioning ? null : () => _transition('Closed'),
              ),
            ),
          ]),
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
  const _InspectionHistoryCard({required this.inspection, required this.onAnalyze});

  final Map<String, dynamic> inspection;
  final VoidCallback onAnalyze;

  @override
  Widget build(BuildContext context) {
    final id = (inspection['id'] as num?)?.toInt() ?? 0;
    final decision = inspection['overallDecision']?.toString();
    final items = inspection['items'] as List<dynamic>? ?? const [];
    final rejected = items.fold<num>(
      0,
      (sum, item) => sum + ((item as Map<String, dynamic>)['rejectedQuantity'] as num? ?? 0),
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

class _InspectionForm extends StatefulWidget {
  const _InspectionForm({required this.delivery, required this.service, required this.onSaved});
  final Map<String, dynamic> delivery;
  final OperationsService service;
  final Future<void> Function() onSaved;

  @override
  State<_InspectionForm> createState() => _InspectionFormState();
}

class _InspectionFormState extends State<_InspectionForm> {
  final _inspected = TextEditingController(text: '240');
  final _accepted = TextEditingController(text: '235');
  final _rejected = TextEditingController(text: '5');
  final _reason = TextEditingController(text: 'Water damage');
  final _criteria = TextEditingController(text: 'Visual check for damage, moisture, and packaging integrity');
  final _observed = TextEditingController(text: 'Material is partially usable; damaged units require segregation');
  final _notes = TextEditingController(text: 'Photographic evidence to be attached before NCR review');
  final _evidenceUrl = TextEditingController();
  bool _submitting = false;
  String? _error;

  /// The five-point checklist, in display order: label then key.
  ///
  /// Every point is always sent with a definite value, because the backend
  /// rejects a completion that omits one. Defaults are Pass, so the inspector
  /// only has to act on what actually failed — but the section is always
  /// rendered, so a criterion can never be silently skipped.
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
  void dispose() {
    _inspected.dispose();
    _accepted.dispose();
    _rejected.dispose();
    _reason.dispose();
    _criteria.dispose();
    _observed.dispose();
    _notes.dispose();
    _evidenceUrl.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final inspected = double.tryParse(_inspected.text) ?? -1;
    final accepted = double.tryParse(_accepted.text) ?? -1;
    final rejected = double.tryParse(_rejected.text) ?? -1;
    if (inspected < 0 || accepted < 0 || rejected < 0 || accepted + rejected != inspected) {
      setState(() => _error = 'Accepted + Rejected must exactly equal Inspected; values cannot be negative.');
      return;
    }
    if (rejected > 0 && _reason.text.trim().isEmpty) {
      setState(() => _error = 'A rejection reason is required.');
      return;
    }
    final items = widget.delivery['items'] as List<dynamic>? ?? const [];
    if (items.isEmpty) {
      setState(() => _error = 'The selected delivery has no item lines.');
      return;
    }
    final first = items.first as Map<String, dynamic>;
    setState(() { _submitting = true; _error = null; });
    try {
      await widget.service.createInspection(
        deliveryId: (widget.delivery['id'] as num).toInt(),
        materialId: (first['materialId'] as num?)?.toInt() ?? 1,
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
        evidence: _evidenceUrl.text.trim().isEmpty ? const [] : [{
          'fileName': 'inspection-evidence.jpg',
          'fileUrl': _evidenceUrl.text.trim(),
          'contentType': 'image/jpeg',
          'fileSizeBytes': 0,
        }],
      );
      if (mounted) {
        final messenger = ScaffoldMessenger.of(context);
        await NotificationService.instance.showQualityUpdate(
          id: (widget.delivery['id'] as num).toInt(),
          title: 'Quality inspection completed',
          body: 'Inspection results are available and NCR follow-up has been created when required.',
        );
        if (!mounted) return;
        messenger.showSnackBar(const SnackBar(content: Text('Inspection completed. NCR list refreshed.')));
        await widget.onSaved();
      }
    } catch (e) {
      setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => Column(
    children: [
      AppTextField(label: 'Inspected Quantity', controller: _inspected, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
      const SizedBox(height: 10),
      AppTextField(label: 'Accepted Quantity', controller: _accepted, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
      const SizedBox(height: 10),
      AppTextField(label: 'Rejected Quantity', controller: _rejected, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
      const SizedBox(height: 14),
      // The five-point checklist. Always rendered, always sent: the backend
      // rejects a completion missing any point, and hiding this section would
      // let an inspector submit an inspection that states nothing about
      // moisture or packaging.
      Text('Quality checklist', style: Theme.of(context).textTheme.titleSmall),
      const SizedBox(height: 4),
      Text(
        'Pass / Fail for each criterion. Every point must be recorded.',
        style: Theme.of(context).textTheme.bodySmall,
      ),
      const SizedBox(height: 4),
      for (final (label, key) in _checkPoints)
        SwitchListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(label),
          subtitle: Text(_checks[key]! ? 'Pass' : 'Fail'),
          value: _checks[key]!,
          activeThumbColor: _checks[key]! ? Colors.green.shade700 : Colors.red.shade700,
          onChanged: (value) => setState(() => _checks[key] = value),
        ),
      const SizedBox(height: 10),
      AppTextField(label: 'Rejection Reason', controller: _reason, maxLines: 2),
      const SizedBox(height: 10),
      AppTextField(label: 'Inspection Criteria', controller: _criteria, maxLines: 2),
      const SizedBox(height: 10),
      AppTextField(label: 'Observed Result', controller: _observed, maxLines: 2),
      const SizedBox(height: 10),
      AppTextField(label: 'Notes', controller: _notes, maxLines: 2),
      const SizedBox(height: 10),
      AppTextField(label: 'Evidence URL (optional)', controller: _evidenceUrl, keyboardType: TextInputType.url),
      if (_error != null) ...[
        const SizedBox(height: 10),
        Text(_error!, style: const TextStyle(color: Colors.red)),
      ],
      const SizedBox(height: 14),
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
        style: Theme.of(context).textTheme.bodySmall?.copyWith(fontStyle: FontStyle.italic),
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
    final text = value == null ? '$label · —' : value! ? '✓ $label' : '✗ $label';
    final color = value == null
        ? Theme.of(context).textTheme.bodySmall?.color
        : value!
            ? Colors.green.shade700
            : Colors.red.shade700;
    return Text(text, style: TextStyle(color: color, fontSize: 12));
  }
}

