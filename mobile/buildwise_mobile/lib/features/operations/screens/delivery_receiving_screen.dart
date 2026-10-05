import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
import '../widgets/agent_analysis_panels.dart';

class DeliveryReceivingScreen extends StatefulWidget {
  const DeliveryReceivingScreen({super.key, this.service, this.readOnly = false});
  final OperationsService? service;
  final bool readOnly;

  @override
  State<DeliveryReceivingScreen> createState() => _DeliveryReceivingScreenState();
}

class _DeliveryReceivingScreenState extends State<DeliveryReceivingScreen> {
  final _service = OperationsService();
  List<Map<String, dynamic>> _orders = const [];
  List<Map<String, dynamic>> _deliveries = const [];
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
      final orders = await (widget.service ?? _service).listConfirmedOrders();
      final deliveries = await (widget.service ?? _service).listDeliveries();
      if (mounted) {
        setState(() {
          _orders = orders;
          _deliveries = deliveries;
        });
      }
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  /// Runs the DeliveryDiscrepancyAgent (:8003) over one recorded delivery. The
  /// agent is read-only: it reports shortage and damage but never changes the
  /// delivery status or creates a discrepancy record.
  Future<void> _analyzeDelivery(Map<String, dynamic> delivery) async {
    final id = (delivery['id'] as num).toInt();
    await showAiAnalysisSheet(
      context,
      title: 'AI Delivery Discrepancy Analysis — DEL-$id',
      run: () => (widget.service ?? _service).analyzeDeliveryDiscrepancy(id),
      builder: buildDeliveryAnalysisPanel,
    );
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Delivery Receiving'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
            ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
            : _orders.isEmpty
                ? const EmptyStateWidget(
                    title: 'No confirmed purchase orders',
                    message: 'A delivery can only be received against a Confirmed PO.',
                  )
                : ListView(
                    padding: const EdgeInsets.all(16),
                    children: [
                      AppDropdown(
                        label: 'Confirmed Purchase Order',
                        value: _selected == null ? null : _selected!['id'].toString(),
                        items: _orders.map((order) => order['id'].toString()).toList(),
                        onChanged: (value) => setState(() {
                          _selected = _orders.firstWhere((order) => order['id'].toString() == value);
                        }),
                      ),
                      if (!widget.readOnly && _selected != null) ...[
                        const SizedBox(height: 16),
                        AppCard(child: _ReceiveForm(
                          order: _selected!,
                          service: widget.service ?? _service,
                          onSaved: _load,
                        )),
                      ],
                      const SizedBox(height: 28),
                      Text('Recorded Deliveries', style: Theme.of(context).textTheme.titleLarge),
                      const SizedBox(height: 6),
                      Text(
                        'Run the Delivery Discrepancy Agent over a received delivery to detect '
                        'shortage, over-receipt and damage.',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                      const SizedBox(height: 10),
                      if (_deliveries.isEmpty)
                        const AppCard(child: Text('No deliveries recorded yet.'))
                      else
                        ..._deliveries.map((delivery) => Padding(
                              padding: const EdgeInsets.only(bottom: 10),
                              child: _DeliveryCard(
                                delivery: delivery,
                                onAnalyze: () => _analyzeDelivery(delivery),
                              ),
                            )),
                    ],
                  ),
  );
}

/// One recorded delivery plus the action that runs the delivery agent over it.
class _DeliveryCard extends StatelessWidget {
  const _DeliveryCard({required this.delivery, required this.onAnalyze});

  final Map<String, dynamic> delivery;
  final VoidCallback onAnalyze;

  @override
  Widget build(BuildContext context) {
    final id = (delivery['id'] as num?)?.toInt() ?? 0;
    final status = delivery['status']?.toString() ?? 'Unknown';
    final items = delivery['items'] as List<dynamic>? ?? const [];
    return AppCard(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text('DEL-$id', style: Theme.of(context).textTheme.titleMedium),
              StatusChip(label: status, tone: _deliveryTone(status)),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            '${delivery['deliveryReference'] ?? 'no reference'} · ${items.length} item(s)',
            style: Theme.of(context).textTheme.bodySmall,
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

StatusTone _deliveryTone(String status) => switch (status) {
      'Received' => StatusTone.success,
      'Confirmed' => StatusTone.info,
      'PartiallyReceived' || 'ReceivingInProgress' => StatusTone.warning,
      'DiscrepancyReported' => StatusTone.danger,
      _ => StatusTone.neutral,
    };

class _ReceiveForm extends StatefulWidget {
  const _ReceiveForm({required this.order, required this.service, required this.onSaved});
  final Map<String, dynamic> order;
  final OperationsService service;
  final Future<void> Function() onSaved;

  @override
  State<_ReceiveForm> createState() => _ReceiveFormState();
}

class _ReceiveFormState extends State<_ReceiveForm> {
  final _reference = TextEditingController(text: 'INV-9081');
  final _received = TextEditingController(text: '240');
  final _damaged = TextEditingController(text: '5');
  bool _submitting = false;
  String? _error;

  @override
  void dispose() {
    _reference.dispose();
    _received.dispose();
    _damaged.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final received = double.tryParse(_received.text) ?? -1;
    final damaged = double.tryParse(_damaged.text) ?? -1;
    final items = widget.order['items'] as List<dynamic>? ?? const [];
    if (items.isEmpty || _reference.text.trim().isEmpty) {
      setState(() => _error = 'Select a PO with line items and enter a delivery reference.');
      return;
    }
    if (received < 0 || damaged < 0 || damaged > received) {
      setState(() => _error = 'Quantities cannot be negative and damaged cannot exceed received.');
      return;
    }
    final first = items.first as Map<String, dynamic>;
    setState(() { _submitting = true; _error = null; });
    try {
      await widget.service.recordDelivery(
        purchaseOrderId: (widget.order['id'] as num).toInt(),
        reference: _reference.text.trim(),
        materialId: (first['materialId'] as num?)?.toInt() ?? 1,
        received: received,
        damaged: damaged,
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(const SnackBar(content: Text('Delivery recorded. Discrepancy status saved by the API.')));
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
    crossAxisAlignment: CrossAxisAlignment.start,
    children: [
      Text('PO-${widget.order['id']}', style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18)),
      const SizedBox(height: 12),
      AppTextField(label: 'Delivery Reference / Invoice', controller: _reference),
      const SizedBox(height: 12),
      AppTextField(label: 'Received Quantity', controller: _received, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
      const SizedBox(height: 12),
      AppTextField(label: 'Damaged Quantity', controller: _damaged, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
      if (_error != null) ...[
        const SizedBox(height: 10),
        Text(_error!, style: const TextStyle(color: Colors.red)),
      ],
      const SizedBox(height: 16),
      AppButton(
        label: _submitting ? 'Recording…' : 'Submit Receiving',
        expand: true,
        onPressed: _submitting ? null : _submit,
      ),
    ],
  );
}
