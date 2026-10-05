import 'dart:async';

import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';
import '../widgets/agent_analysis_panels.dart';

class DeliveryReceivingScreen extends StatefulWidget {
  const DeliveryReceivingScreen({
    super.key,
    this.service,
    this.readOnly = false,
    this.autoRefresh = true,
  });
  final OperationsService? service;
  final bool readOnly;
  final bool autoRefresh;

  @override
  State<DeliveryReceivingScreen> createState() =>
      _DeliveryReceivingScreenState();
}

class _DeliveryReceivingScreenState extends State<DeliveryReceivingScreen> {
  final _service = OperationsService();
  Timer? _refreshTimer;
  List<Map<String, dynamic>> _orders = const [];
  List<Map<String, dynamic>> _deliveries = const [];
  Map<String, dynamic>? _selected;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.autoRefresh) {
      _refreshTimer = Timer.periodic(
        const Duration(seconds: 15),
        (_) => _refreshOrders(),
      );
    }
  }

  @override
  void dispose() {
    _refreshTimer?.cancel();
    super.dispose();
  }

  Future<void> _refreshOrders() async {
    try {
      final orders = await (widget.service ?? _service).listConfirmedOrders();
      if (mounted) setState(() => _orders = orders);
    } catch (_) {
      /* Keep the order list during a temporary network failure. */
    }
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
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
      if (mounted) {
        setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
      }
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
        : ListView(
            padding: const EdgeInsets.all(16),
            children: [
              if (!widget.readOnly && _orders.isEmpty)
                const AppCard(
                  child: Text(
                    'No confirmed purchase orders. Recorded deliveries remain available below.',
                  ),
                ),
              if (!widget.readOnly && _orders.isNotEmpty)
                AppDropdown(
                  label: 'Confirmed Purchase Order',
                  value: _selected == null ? null : _selected!['id'].toString(),
                  items: _orders
                      .map((order) => order['id'].toString())
                      .toList(),
                  onChanged: (value) => setState(() {
                    _selected = _orders.firstWhere(
                      (order) => order['id'].toString() == value,
                    );
                  }),
                ),
              if (!widget.readOnly && _selected != null) ...[
                const SizedBox(height: 16),
                AppCard(
                  child: _ReceiveForm(
                    order: _selected!,
                    service: widget.service ?? _service,
                    onSaved: _load,
                  ),
                ),
              ],
              const SizedBox(height: 28),
              Text(
                'Recorded Deliveries',
                style: Theme.of(context).textTheme.titleLarge,
              ),
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
                ..._deliveries.map(
                  (delivery) => Padding(
                    padding: const EdgeInsets.only(bottom: 10),
                    child: _DeliveryCard(
                      delivery: delivery,
                      onAnalyze: () => _analyzeDelivery(delivery),
                    ),
                  ),
                ),
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
  const _ReceiveForm({
    required this.order,
    required this.service,
    required this.onSaved,
  });
  final Map<String, dynamic> order;
  final OperationsService service;
  final Future<void> Function() onSaved;

  @override
  State<_ReceiveForm> createState() => _ReceiveFormState();
}

class _ReceiveFormState extends State<_ReceiveForm> {
  final _reference = TextEditingController(text: 'INV-9081');
  final Map<int, TextEditingController> _receivedControllers = {};
  final Map<int, TextEditingController> _damagedControllers = {};

  /// The immutable facts of each PO line (ordered quantity, material, unit),
  /// keyed by line id. The text fields hold what the officer *entered*; this
  /// holds what was *ordered*, so the shortage can be derived rather than typed.
  final Map<int, _OrderLine> _lines = {};

  EvidencePhoto? _evidencePhoto;
  bool _submitting = false;
  String? _error;

  /// Ordered minus received, floored at zero. An over-delivery is not a
  /// negative shortage — it is rejected outright by [_validateLines].
  double _shortageFor(_OrderLine line) {
    final received =
        double.tryParse(_receivedControllers[line.id]?.text.trim() ?? '') ?? 0;
    final shortage = line.ordered - received;
    return shortage > 0 ? shortage : 0;
  }

  /// Trims trailing zeros so "10" never renders as "10.0".
  static String _fmt(double value) =>
      value % 1 == 0 ? value.toInt().toString() : value.toString();

  void _refreshLine(int id) {
    if (mounted) setState(() {});
  }

  @override
  void initState() {
    super.initState();
    _initControllers();
  }

  @override
  void didUpdateWidget(_ReceiveForm oldWidget) {
    super.didUpdateWidget(oldWidget);
    if (oldWidget.order['id'] != widget.order['id']) {
      _disposeControllers();
      _initControllers();
    }
  }

  void _initControllers() {
    final items = (widget.order['items'] as List<dynamic>? ?? const [])
        .cast<Map<String, dynamic>>();
    _lines.clear();
    for (final item in items) {
      final id =
          (item['id'] as num?)?.toInt() ??
          (item['materialId'] as num?)?.toInt() ??
          0;
      final ordered = (item['orderedQuantity'] as num?)?.toDouble() ?? 0.0;
      _lines[id] = _OrderLine(
        id: id,
        ordered: ordered,
        materialName: item['materialName']?.toString() ?? 'Item',
        unit:
            item['materialUnit']?.toString() ??
            item['unit']?.toString() ??
            'units',
      );
      _receivedControllers[id] = TextEditingController(
        text: ordered > 0
            ? (ordered % 1 == 0
                  ? ordered.toInt().toString()
                  : ordered.toString())
            : '0',
      );
      _damagedControllers[id] = TextEditingController(text: '0');
    }
  }

  void _disposeControllers() {
    for (final c in _receivedControllers.values) {
      c.dispose();
    }
    for (final c in _damagedControllers.values) {
      c.dispose();
    }
    _receivedControllers.clear();
    _damagedControllers.clear();
  }

  @override
  void dispose() {
    _reference.dispose();
    _disposeControllers();
    super.dispose();
  }

  Future<void> _submit() async {
    final ref = _reference.text.trim();
    if (ref.isEmpty) {
      setState(
        () => _error = 'Please enter a delivery reference or invoice number.',
      );
      return;
    }

    final rawItems = (widget.order['items'] as List<dynamic>? ?? const [])
        .cast<Map<String, dynamic>>();
    if (rawItems.isEmpty) {
      setState(() => _error = 'The selected Purchase Order has no line items.');
      return;
    }

    final payloadItems = <Map<String, dynamic>>[];
    for (final item in rawItems) {
      final id =
          (item['id'] as num?)?.toInt() ??
          (item['materialId'] as num?)?.toInt() ??
          0;
      final materialId = (item['materialId'] as num?)?.toInt() ?? id;
      final ordered = (item['orderedQuantity'] as num?)?.toDouble() ?? 0.0;
      final matName = item['materialName']?.toString() ?? 'Item';

      final rText = _receivedControllers[id]?.text.trim() ?? '';
      final dText = _damagedControllers[id]?.text.trim() ?? '';
      final received = double.tryParse(rText) ?? -1;
      final damaged = double.tryParse(dText) ?? -1;

      if (!received.isFinite ||
          !damaged.isFinite ||
          received < 0 ||
          damaged < 0) {
        setState(
          () => _error =
              'Invalid quantities for $matName. Quantities cannot be negative.',
        );
        return;
      }
      if (damaged > received) {
        setState(
          () => _error =
              'Damaged quantity cannot exceed received quantity for $matName.',
        );
        return;
      }
      if (received > ordered) {
        setState(
          () => _error =
              'Received quantity ($received) exceeds ordered quantity ($ordered) for $matName.',
        );
        return;
      }

      payloadItems.add({
        'materialId': materialId,
        'receivedQuantity': received,
        'damagedQuantity': damaged,
      });
    }

    setState(() {
      _submitting = true;
      _error = null;
    });

    try {
      final evidenceList = _evidencePhoto != null
          ? [_evidencePhoto!.toPayload()]
          : const <Map<String, dynamic>>[];
      await widget.service.recordDelivery(
        purchaseOrderId: (widget.order['id'] as num).toInt(),
        reference: ref,
        items: payloadItems,
        evidence: evidenceList,
      );
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text(
              'Delivery recorded successfully with item lines and discrepancy check.',
            ),
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
  Widget build(BuildContext context) {
    final rawItems = (widget.order['items'] as List<dynamic>? ?? const [])
        .cast<Map<String, dynamic>>();
    final supplier = widget.order['supplierName']?.toString() ?? 'Supplier';

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        Row(
          mainAxisAlignment: MainAxisAlignment.spaceBetween,
          children: [
            Text(
              'PO-${widget.order['id']}',
              style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 18),
            ),
            StatusChip(label: supplier, tone: StatusTone.info),
          ],
        ),
        const SizedBox(height: 12),
        AppTextField(
          label: 'Delivery Reference / Invoice',
          controller: _reference,
        ),
        const SizedBox(height: 16),
        Text('Order Line Items', style: Theme.of(context).textTheme.titleSmall),
        const SizedBox(height: 4),
        Text(
          'Enter received and damaged quantities for every line item.',
          style: Theme.of(context).textTheme.bodySmall,
        ),
        const SizedBox(height: 10),
        if (rawItems.isEmpty)
          const Text('No line items found in this purchase order.')
        else
          ...rawItems.map((item) {
            final id =
                (item['id'] as num?)?.toInt() ??
                (item['materialId'] as num?)?.toInt() ??
                0;
            final matName =
                item['materialName']?.toString() ??
                'Material #${item['materialId']}';
            final ordered = item['orderedQuantity']?.toString() ?? '0';
            // Prefer the unit captured with the line facts, so the "Ordered",
            // the input labels and the derived shortage all agree.
            final unit =
                _lines[id]?.unit ?? item['unit']?.toString() ?? 'units';
            final rCtrl = _receivedControllers[id];
            final dCtrl = _damagedControllers[id];

            return Container(
              margin: const EdgeInsets.only(bottom: 12),
              padding: const EdgeInsets.all(12),
              decoration: BoxDecoration(
                color: Colors.grey.shade50,
                border: Border.all(color: Colors.grey.shade300),
                borderRadius: BorderRadius.circular(8),
              ),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Row(
                    mainAxisAlignment: MainAxisAlignment.spaceBetween,
                    children: [
                      Expanded(
                        child: Text(
                          matName,
                          style: const TextStyle(
                            fontWeight: FontWeight.bold,
                            fontSize: 14,
                          ),
                        ),
                      ),
                      Text(
                        'Ordered: $ordered $unit',
                        style: TextStyle(
                          color: Colors.blueGrey.shade700,
                          fontWeight: FontWeight.w600,
                          fontSize: 12,
                        ),
                      ),
                    ],
                  ),
                  const SizedBox(height: 10),
                  Row(
                    children: [
                      Expanded(
                        child: rCtrl == null
                            ? const SizedBox.shrink()
                            : AppTextField(
                                label: 'Received ($unit)',
                                controller: rCtrl,
                                keyboardType:
                                    const TextInputType.numberWithOptions(
                                      decimal: true,
                                    ),
                                onChanged: (_) => _refreshLine(id),
                              ),
                      ),
                      const SizedBox(width: 10),
                      Expanded(
                        child: dCtrl == null
                            ? const SizedBox.shrink()
                            : AppTextField(
                                label: 'Damaged ($unit)',
                                controller: dCtrl,
                                keyboardType:
                                    const TextInputType.numberWithOptions(
                                      decimal: true,
                                    ),
                                onChanged: (_) => _refreshLine(id),
                              ),
                      ),
                    ],
                  ),
                  // Shortage is derived, never typed: Ordered - Received.
                  // Recomputed on every keystroke so the officer sees the
                  // consequence of what they entered immediately.
                  Builder(
                    builder: (context) {
                      final line = _lines[id];
                      if (line == null) return const SizedBox.shrink();
                      final shortage = _shortageFor(line);
                      return Padding(
                        padding: const EdgeInsets.only(top: 8),
                        child: Row(
                          children: [
                            Icon(
                              shortage > 0
                                  ? Icons.error_outline
                                  : Icons.check_circle_outline,
                              size: 16,
                              color: shortage > 0
                                  ? Colors.red.shade700
                                  : Colors.green.shade700,
                            ),
                            const SizedBox(width: 6),
                            Text(
                              'Shortage: ${_fmt(shortage)} $unit',
                              style: TextStyle(
                                fontSize: 12,
                                fontWeight: FontWeight.w700,
                                color: shortage > 0
                                    ? Colors.red.shade700
                                    : Colors.green.shade700,
                              ),
                            ),
                          ],
                        ),
                      );
                    },
                  ),
                ],
              ),
            );
          }),
        const SizedBox(height: 12),
        EvidencePickerWidget(
          title: 'Delivery Photo Evidence',
          subtitle: 'Attach delivery note, consignment photo, or damaged material picture.',
          onChanged: (photo) => setState(() => _evidencePhoto = photo),
        ),
        if (_error != null) ...[
          const SizedBox(height: 12),
          Text(
            _error!,
            style: const TextStyle(
              color: Colors.red,
              fontWeight: FontWeight.w600,
            ),
          ),
        ],
        const SizedBox(height: 18),
        AppButton(
          label: _submitting ? 'Recording…' : 'Submit Receiving',
          expand: true,
          onPressed: _submitting ? null : _submit,
        ),
      ],
    );
  }
}

/// The immutable facts of one purchase-order line, as returned by
/// `GET /api/deliveries/confirmed-orders`.
///
/// The officer's entered values live in text controllers; this holds what was
/// *ordered*, which is what the shortage is measured against.
class _OrderLine {
  const _OrderLine({
    required this.id,
    required this.ordered,
    required this.materialName,
    required this.unit,
  });

  final int id;
  final double ordered;
  final String materialName;
  final String unit;
}
