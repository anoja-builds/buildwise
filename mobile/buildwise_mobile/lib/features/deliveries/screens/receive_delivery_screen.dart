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

  @override
  void initState() {
    super.initState();
    _items = (widget.delivery['items'] as List).map((i) {
      return {
        'purchaseOrderItemId': i['purchaseOrderItemId'],
        'materialName': i['materialName'],
        'orderedQuantity': i['orderedQuantity'],
        'receivedQuantity': i['orderedQuantity'], // Default to full
        'damagedQuantity': 0.0,
      };
    }).toList();
  }

  Future<void> _submit() async {
    setState(() => _isSubmitting = true);
    try {
      final payload = {
        'receivedByUserId': 2, // Ramya Fernando
        'notes': _notesController.text,
        'items': _items.map((i) => {
          'purchaseOrderItemId': i['purchaseOrderItemId'],
          'receivedQuantity': i['receivedQuantity'],
          'damagedQuantity': i['damagedQuantity'],
        }).toList(),
      };

      await _service.receiveDelivery(widget.delivery['id'], payload);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Delivery reconciled successfully.')),
        );
        Navigator.pop(context);
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Error: $e')),
        );
      }
    } finally {
      setState(() => _isSubmitting = false);
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
                Text('PO #${widget.delivery['purchaseOrderId']}', style: const TextStyle(fontWeight: FontWeight.bold)),
                const SizedBox(height: 20),
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
                          Text(item['materialName'], style: const TextStyle(fontSize: 16, fontWeight: FontWeight.bold)),
                          Text('Ordered: ${item['orderedQuantity']}'),
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
            ),
          ),
    );
  }
}
