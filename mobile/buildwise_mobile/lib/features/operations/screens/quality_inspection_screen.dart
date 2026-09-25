import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
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
      final values = await Future.wait([service.listDeliveries(), service.listNonConformances()]);
      if (mounted) {
        setState(() {
          _deliveries = values[0];
          _ncrs = values[1];
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
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
                  if (!widget.readOnly) ...[
                    Text('New Inspection', style: Theme.of(context).textTheme.titleLarge),
                    const SizedBox(height: 12),
                    AppDropdown(
                      label: 'Delivery',
                      value: _selected == null ? null : _selected!['id'].toString(),
                      items: _deliveries.map((delivery) => delivery['id'].toString()).toList(),
                      onChanged: (value) => setState(() {
                        _selected = _deliveries.firstWhere((delivery) => delivery['id'].toString() == value);
                      }),
                    ),
                    if (_selected != null) ...[
                      const SizedBox(height: 12),
                      AppCard(child: _InspectionForm(
                        delivery: _selected!,
                        service: widget.service ?? _service,
                        onSaved: _load,
                      )),
                    ],
                    const SizedBox(height: 24),
                  ],
                  Text('Active Non-Conformance Reports', style: Theme.of(context).textTheme.titleLarge),
                  const SizedBox(height: 10),
                  if (_ncrs.isEmpty)
                    const AppCard(child: Text('No active NCRs. Rejected quantities generate one automatically.'))
                  else
                    ..._ncrs.map((ncr) => Padding(
                      padding: const EdgeInsets.only(bottom: 10),
                      child: AppCard(child: _NcrCard(ncr: ncr)),
                    )),
                ],
              ),
  );
}

class _NcrCard extends StatelessWidget {
  const _NcrCard({required this.ncr});
  final Map<String, dynamic> ncr;

  @override
  Widget build(BuildContext context) => Column(
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text(ncr['ncrNumber']?.toString() ?? 'NCR', style: const TextStyle(fontWeight: FontWeight.bold)),
      const SizedBox(height: 6),
      Text(ncr['issueDescription']?.toString() ?? 'Quality issue'),
      const SizedBox(height: 6),
      StatusChip(
        label: '${ncr['severity']} · ${ncr['status']}',
        tone: ncr['severity'] == 'Critical' || ncr['severity'] == 'High'
          ? StatusTone.danger
          : StatusTone.warning,
      ),
    ],
  );
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
