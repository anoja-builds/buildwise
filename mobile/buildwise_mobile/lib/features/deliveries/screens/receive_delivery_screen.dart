import 'package:flutter/material.dart';
import '../services/delivery_service.dart';

class ReceiveDeliveryScreen extends StatefulWidget {
  final dynamic delivery;
  const ReceiveDeliveryScreen({super.key, required this.delivery});

  @override
  State<ReceiveDeliveryScreen> createState() => _ReceiveDeliveryScreenState();
}

class _ReceiveDeliveryScreenState extends State<ReceiveDeliveryScreen> {
  final DeliveryService _service = DeliveryService();
  final TextEditingController _notesController = TextEditingController();
  late List<Map<String, dynamic>> _items;
  bool _isSubmitting = false;
  Map<String, dynamic>? _receiveResult;
  Map<String, dynamic>? _discrepancyResult;
  bool _isAnalyzing = false;

  @override
  void initState() {
    super.initState();
    _items = (widget.delivery['items'] as List).map((i) {
      return {
        'purchaseOrderItemId': i['purchaseOrderItemId'],
        'materialName': i['materialName'] ?? 'Unknown',
        'materialUnit': i['materialUnit'] ?? 'units',
        'orderedQuantity': i['orderedQuantity'],
        'receivedQuantity': i['orderedQuantity'], // Default to full
        'damagedQuantity': 0.0,
      };
    }).toList();
  }

  Future<void> _submit() async {
    // Validate before submitting
    for (var item in _items) {
      final received = (item['receivedQuantity'] as num).toDouble();
      final damaged = (item['damagedQuantity'] as num).toDouble();
      if (received < 0 || damaged < 0) {
        _showError('Quantities cannot be negative.');
        return;
      }
      if (damaged > received) {
        _showError('Damaged quantity cannot exceed received quantity for ${item['materialName']}.');
        return;
      }
    }

    setState(() => _isSubmitting = true);
    try {
      final payload = {
        'notes': _notesController.text,
        'items': _items.map((i) => {
          return {
            'purchaseOrderItemId': i['purchaseOrderItemId'],
            'receivedQuantity': i['receivedQuantity'],
            'damagedQuantity': i['damagedQuantity'],
          };
        }).toList(),
      };

      final result = await _service.receiveDelivery(widget.delivery['id'], payload);

      if (mounted) {
        setState(() {
          _receiveResult = result;
        });
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(result['message'] ?? 'Delivery reconciled successfully.'),
            backgroundColor: Colors.green,
          ),
        );
      }
    } catch (e) {
      _showError('$e');
    } finally {
      setState(() => _isSubmitting = false);
    }
  }

  Future<void> _runDiscrepancyAnalysis() async {
    setState(() => _isAnalyzing = true);
    try {
      final result = await _service.analyzeDiscrepancies(widget.delivery['id']);
      if (mounted) {
        setState(() => _discrepancyResult = result);
      }
    } catch (e) {
      _showError('Discrepancy analysis: $e');
    } finally {
      setState(() => _isAnalyzing = false);
    }
  }

  void _showError(String message) {
    if (mounted) {
      ScaffoldMessenger.of(context).showSnackBar(
        SnackBar(content: Text('Error: $message'), backgroundColor: Colors.red),
      );
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: Text('Receive: ${widget.delivery['deliveryReference']}')),
      body: _isSubmitting
        ? const Center(child: CircularProgressIndicator())
        : SingleChildScrollView(
            padding: const EdgeInsets.all(16),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Text('PO #${widget.delivery['purchaseOrderId']}',
                    style: const TextStyle(fontWeight: FontWeight.bold)),
                const SizedBox(height: 20),

                // Receiving status indicator (after submit)
                if (_receiveResult != null) ...[
                  _buildStatusCard(),
                  const SizedBox(height: 16),
                ],

                // Item entry cards
                if (_receiveResult == null) ...[
                  ..._items.asMap().entries.map((entry) {
                    int idx = entry.key;
                    var item = entry.value;
                    return Card(
                      margin: const EdgeInsets.only(bottom: 16),
                      child: Padding(
                        padding: const EdgeInsets.all(12),
                        child: Column(
                          crossAxisAlignment: CrossAxisAlignment.start,
                          children: [
                            Text(item['materialName'],
                                style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
                            Text('Ordered: ${item['orderedQuantity']} ${item['materialUnit'] ?? ''}'),
                            const SizedBox(height: 10),
                            Row(
                              children: [
                                Expanded(
                                  child: TextFormField(
                                    initialValue: item['receivedQuantity'].toString(),
                                    decoration: const InputDecoration(labelText: 'Received Qty'),
                                    keyboardType: TextInputType.number,
                                    onChanged: (v) => _items[idx]['receivedQuantity'] = double.tryParse(v) ?? 0.0,
                                  ),
                                ),
                                const SizedBox(width: 16),
                                Expanded(
                                  child: TextFormField(
                                    initialValue: item['damagedQuantity'].toString(),
                                    decoration: const InputDecoration(labelText: 'Damaged Qty'),
                                    keyboardType: TextInputType.number,
                                    onChanged: (v) => _items[idx]['damagedQuantity'] = double.tryParse(v) ?? 0.0,
                                  ),
                                ),
                              ],
                            ),
                          ],
                        ),
                      ),
                    );
                  }),
                  TextField(
                    controller: _notesController,
                    decoration: const InputDecoration(labelText: 'Overall Remarks'),
                    maxLines: 2,
                  ),
                  const SizedBox(height: 30),
                  SizedBox(
                    width: double.infinity,
                    child: ElevatedButton(
                      onPressed: _submit,
                      child: const Text('Verify & Save'),
                    ),
                  ),
                ],

                // Post-receive: discrepancy analysis section
                if (_receiveResult != null) ...[
                  const Divider(height: 32),
                  _buildDiscrepancySection(),
                ],
              ],
            ),
          ),
    );
  }

  Widget _buildStatusCard() {
    final status = _receiveResult?['status'] ?? 'Unknown';
    final isDiscrepancy = status == 'DiscrepancyReported';

    return Card(
      color: isDiscrepancy ? Colors.orange.shade50 : Colors.green.shade50,
      child: Padding(
        padding: const EdgeInsets.all(16),
        child: Row(
          children: [
            Icon(
              isDiscrepancy ? Icons.warning_amber : Icons.check_circle,
              color: isDiscrepancy ? Colors.orange : Colors.green,
              size: 32,
            ),
            const SizedBox(width: 12),
            Expanded(
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: [
                  Text(
                    _receiveResult?['message'] ?? 'Delivery recorded',
                    style: const TextStyle(fontWeight: FontWeight.bold, fontSize: 15),
                  ),
                  const SizedBox(height: 4),
                  Text(
                    'Status: $status',
                    style: TextStyle(
                      color: isDiscrepancy ? Colors.orange.shade800 : Colors.green.shade800,
                      fontWeight: FontWeight.w500,
                    ),
                  ),
                ],
              ),
            ),
          ],
        ),
      ),
    );
  }

  Widget _buildDiscrepancySection() {
    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        const Text(
          '🔍 Discrepancy Analysis',
          style: TextStyle(fontWeight: FontWeight.bold, fontSize: 16),
        ),
        const SizedBox(height: 8),
        if (_isAnalyzing)
          const Center(
            child: Padding(
              padding: EdgeInsets.all(20),
              child: Column(
                children: [
                  CircularProgressIndicator(),
                  SizedBox(height: 8),
                  Text('Analyzing discrepancies...'),
                ],
              ),
            ),
          )
        else if (_discrepancyResult == null)
          Column(
            children: [
              const Text(
                'Run the Discrepancy Agent to compare ordered vs received quantities.',
                style: TextStyle(color: Colors.grey),
              ),
              const SizedBox(height: 12),
              SizedBox(
                width: double.infinity,
                child: OutlinedButton.icon(
                  icon: const Icon(Icons.search),
                  label: const Text('Run Discrepancy Analysis'),
                  onPressed: _runDiscrepancyAnalysis,
                ),
              ),
            ],
          )
        else
          _buildDiscrepancyResults(),
      ],
    );
  }

  Widget _buildDiscrepancyResults() {
    final analysis = _discrepancyResult!;
    final hasDiscrepancies = analysis['hasDiscrepancies'] ?? false;
    final items = analysis['items'] as List? ?? [];
    final recommendations = analysis['recommendations'] as List? ?? [];

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        // Summary card
        Card(
          color: hasDiscrepancies ? Colors.orange.shade50 : Colors.green.shade50,
          child: Padding(
            padding: const EdgeInsets.all(12),
            child: Row(
              mainAxisAlignment: MainAxisAlignment.spaceEvenly,
              children: [
                _summaryChip('Shortage', '${analysis['totalShortage'] ?? 0}',
                    (analysis['totalShortage'] ?? 0) > 0 ? Colors.orange : Colors.green),
                _summaryChip('Damaged', '${analysis['totalDamaged'] ?? 0}',
                    (analysis['totalDamaged'] ?? 0) > 0 ? Colors.red : Colors.green),
                _summaryChip('Undamaged', '${analysis['totalUndamagedReceived'] ?? 0}',
                    Colors.blue),
              ],
            ),
          ),
        ),
        const SizedBox(height: 12),

        // Item-level analysis
        ...items.map((item) => Card(
          margin: const EdgeInsets.only(bottom: 8),
          child: Padding(
            padding: const EdgeInsets.all(10),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: [
                Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    Text(item['materialName'] ?? '', style: const TextStyle(fontWeight: FontWeight.bold)),
                    if (item['hasDiscrepancy'] == true)
                      ...((item['discrepancyFlags'] as List?)?.map((flag) =>
                        Chip(
                          label: Text(flag, style: const TextStyle(fontSize: 10)),
                          backgroundColor: Colors.orange.shade100,
                          materialTapTargetSize: MaterialTapTargetSize.shrinkWrap,
                          padding: EdgeInsets.zero,
                        )
                      ) ?? [])
                    else
                      const Icon(Icons.check, color: Colors.green, size: 18),
                  ],
                ),
                const SizedBox(height: 4),
                Text('Ordered: ${item['orderedQuantity']}  •  '
                    'Received: ${item['newlyReceivedQuantity']}  •  '
                    'Damaged: ${item['damagedQuantity']}  •  '
                    'Shortage: ${item['shortageQuantity']}',
                    style: const TextStyle(fontSize: 12, color: Colors.grey)),
              ],
            ),
          ),
        )),

        // Recommendations
        if (recommendations.isNotEmpty) ...[
          const SizedBox(height: 12),
          const Text('Recommendations', style: TextStyle(fontWeight: FontWeight.bold, fontSize: 14)),
          const SizedBox(height: 4),
          ...recommendations.map((rec) => Card(
            margin: const EdgeInsets.only(bottom: 6),
            child: ListTile(
              dense: true,
              leading: Icon(
                rec['isActionRequired'] == true ? Icons.priority_high : Icons.lightbulb_outline,
                color: rec['isActionRequired'] == true ? Colors.red : Colors.blue,
              ),
              title: Text('${rec['category']} — ${rec['materialName']}',
                  style: const TextStyle(fontSize: 13, fontWeight: FontWeight.w500)),
              subtitle: Text(rec['advisory'] ?? '', style: const TextStyle(fontSize: 11)),
            ),
          )),
        ],

        // Workflow info
        Padding(
          padding: const EdgeInsets.only(top: 8),
          child: Text(
            'Workflow #${analysis['workflowId']}  •  Mode: ${analysis['executionMode']}',
            style: const TextStyle(fontSize: 11, color: Colors.grey),
          ),
        ),
      ],
    );
  }

  Widget _summaryChip(String label, String value, Color color) {
    return Column(
      children: [
        Text(value, style: TextStyle(fontWeight: FontWeight.bold, fontSize: 18, color: color)),
        Text(label, style: const TextStyle(fontSize: 11, color: Colors.grey)),
      ],
    );
  }
}
