import 'package:flutter/material.dart';
import '../services/material_request_service.dart';

class CreateMaterialRequestScreen extends StatefulWidget {
  const CreateMaterialRequestScreen({super.key});

  @override
  State<CreateMaterialRequestScreen> createState() => _CreateMaterialRequestScreenState();
}

class _CreateMaterialRequestScreenState extends State<CreateMaterialRequestScreen> {
  final _service = MaterialRequestService();
  final _reasonController = TextEditingController();
  final _notesController = TextEditingController();
  final _quantityController = TextEditingController(text: '200');

  String _priority = 'High';
  int _materialId = 1; // 1: OPC Cement, 2: Steel, 3: Sand
  String _unit = 'bags';
  bool _loading = false;

  Future<void> _submit() async {
    if (_reasonController.text.isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(content: Text('Please enter reason for request')),
      );
      return;
    }

    setState(() => _loading = true);
    try {
      final payload = {
        'projectId': 1,
        'requestedByUserId': 1,
        'priority': _priority,
        'requiredDate': DateTime.now().add(const Duration(days: 5)).toIso8601String(),
        'reason': _reasonController.text,
        'siteNotes': _notesController.text,
        'submitImmediately': true,
        'items': [
          {
            'materialId': _materialId,
            'quantity': double.tryParse(_quantityController.text) ?? 100,
            'unit': _unit,
            'requiredDate': DateTime.now().add(const Duration(days: 5)).toIso8601String(),
            'notes': _notesController.text
          }
        ]
      };

      await _service.createRequest(payload);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text('Material Request Submitted to Office!')),
        );
        Navigator.pop(context, true);
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text('Error: $e')),
        );
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('New Material Request'),
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Site Officer Material Demand Entry',
              style: TextStyle(fontSize: 18, fontWeight: FontWeight.bold),
            ),
            const SizedBox(height: 16),
            const Text('Material Select:', style: TextStyle(fontWeight: FontWeight.bold)),
            DropdownButton<int>(
              value: _materialId,
              isExpanded: true,
              items: const [
                DropdownMenuItem(value: 1, child: Text('OPC Cement (Bags)')),
                DropdownMenuItem(value: 2, child: Text('Reinforcement Steel (Tonnes)')),
                DropdownMenuItem(value: 3, child: Text('River Sand (Cubic Metres)')),
              ],
              onChanged: (val) {
                if (val != null) {
                  setState(() {
                    _materialId = val;
                    _unit = val == 1 ? 'bags' : val == 2 ? 'tonnes' : 'cubic metres';
                  });
                }
              },
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _quantityController,
              keyboardType: TextInputType.number,
              decoration: InputDecoration(
                labelText: 'Required Quantity ($_unit)',
                border: const OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 16),
            const Text('Priority Level:', style: TextStyle(fontWeight: FontWeight.bold)),
            DropdownButton<String>(
              value: _priority,
              isExpanded: true,
              items: const [
                DropdownMenuItem(value: 'Low', child: Text('Low Priority')),
                DropdownMenuItem(value: 'Medium', child: Text('Medium Priority')),
                DropdownMenuItem(value: 'High', child: Text('High Priority')),
                DropdownMenuItem(value: 'Urgent', child: Text('Urgent Priority')),
              ],
              onChanged: (val) {
                if (val != null) setState(() => _priority = val);
              },
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _reasonController,
              decoration: const InputDecoration(
                labelText: 'Purpose / Work Reason',
                hintText: 'e.g. Ground floor slab concreting',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 16),
            TextField(
              controller: _notesController,
              maxLines: 3,
              decoration: const InputDecoration(
                labelText: 'Site Logistics & Access Notes',
                hintText: 'e.g. Deliver to Gate B. Tower crane active.',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 24),
            SizedBox(
              width: double.infinity,
              height: 50,
              child: ElevatedButton(
                onPressed: _loading ? null : _submit,
                child: _loading
                    ? const CircularProgressIndicator()
                    : const Text('SUBMIT MATERIAL REQUEST', style: TextStyle(fontWeight: FontWeight.bold)),
              ),
            )
          ],
        ),
      ),
    );
  }
}
