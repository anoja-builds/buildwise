import 'package:flutter/material.dart';

import '../services/material_request_service.dart';

class CreateMaterialRequestScreen extends StatefulWidget {
  const CreateMaterialRequestScreen({super.key, this.service});

  final MaterialRequestService? service;

  @override
  State<CreateMaterialRequestScreen> createState() =>
      _CreateMaterialRequestScreenState();
}

class _CreateMaterialRequestScreenState
    extends State<CreateMaterialRequestScreen> {
  late final _service = widget.service ?? MaterialRequestService();
  final _reasonController = TextEditingController();
  final _notesController = TextEditingController();
  final _quantityController = TextEditingController();

  String _priority = 'High';
  int? _projectId;
  int? _materialId;
  String _unit = '';
  List<dynamic> _projects = [];
  List<dynamic> _materials = [];
  bool _loadingOptions = true;
  String? _optionsError;

  @override
  void initState() {
    super.initState();
    _loadOptions();
  }

  Future<void> _loadOptions() async {
    setState(() {
      _loadingOptions = true;
      _optionsError = null;
    });
    try {
      final options = await _service.getOptions();
      if (!mounted) return;
      setState(() {
        _projects = options['projects'] as List<dynamic>;
        _materials = options['materials'] as List<dynamic>;
        _projectId = null;
        _materialId = null;
        _unit = '';
      });
    } catch (e) {
      if (mounted) setState(() => _optionsError = e.toString());
    } finally {
      if (mounted) setState(() => _loadingOptions = false);
    }
  }

  @override
  void dispose() {
    _reasonController.dispose();
    _notesController.dispose();
    _quantityController.dispose();
    super.dispose();
  }

  bool _loading = false;

  Future<void> _submit() async {
    final quantity = double.tryParse(_quantityController.text);
    if (_projectId == null ||
        _materialId == null ||
        quantity == null ||
        !quantity.isFinite ||
        quantity <= 0 ||
        quantity > 1000000 ||
        _reasonController.text.trim().isEmpty) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text(
            'Select a project and material, enter a valid quantity and reason',
          ),
        ),
      );
      return;
    }

    setState(() => _loading = true);
    try {
      final payload = {
        'projectId': _projectId,
        'priority': _priority,
        'requiredDate': DateTime.now()
            .add(const Duration(days: 5))
            .toIso8601String(),
        'reason': _reasonController.text,
        'siteNotes': _notesController.text,
        'submitImmediately': true,
        'items': [
          {
            'materialId': _materialId,
            'quantity': quantity,
            'unit': _unit,
            'requiredDate': DateTime.now()
                .add(const Duration(days: 5))
                .toIso8601String(),
            'notes': _notesController.text,
          },
        ],
      };

      await _service.createRequest(payload);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(
            content: Text('Material Request Submitted to Office!'),
          ),
        );
        Navigator.pop(context, true);
      }
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('Error: $e')));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(title: const Text('New Material Request')),
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
            if (_loadingOptions) const LinearProgressIndicator(),
            if (_optionsError != null) ...[
              Text(_optionsError!),
              TextButton(
                onPressed: _loadOptions,
                child: const Text('Retry loading options'),
              ),
            ],
            if (!_loadingOptions &&
                _optionsError == null &&
                (_projects.isEmpty || _materials.isEmpty))
              const Text(
                'No active projects or materials are available. Contact the office.',
              ),
            const Text('Project / Construction Site'),
            DropdownButton<int>(
              key: const Key('project-select'),
              value: _projectId,
              hint: const Text('Select project'),
              isExpanded: true,
              items: _projects
                  .map(
                    (p) => DropdownMenuItem<int>(
                      value: p['id'] as int,
                      child: Text(p['name'] as String),
                    ),
                  )
                  .toList(),
              onChanged: _loading
                  ? null
                  : (value) => setState(() => _projectId = value),
            ),
            const Text(
              'Material Select:',
              style: TextStyle(fontWeight: FontWeight.bold),
            ),
            DropdownButton<int>(
              key: const Key('material-select'),
              value: _materialId,
              hint: const Text('Select material'),
              isExpanded: true,
              items: _materials
                  .map(
                    (m) => DropdownMenuItem<int>(
                      value: m['id'] as int,
                      child: Text('${m['name']} (${m['unit']})'),
                    ),
                  )
                  .toList(),
              onChanged: _loading
                  ? null
                  : (value) {
                      if (value == null) return;
                      setState(() {
                        _materialId = value;
                        _unit =
                            _materials.firstWhere(
                                  (m) => m['id'] == value,
                                )['unit']
                                as String;
                      });
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
            const Text(
              'Priority Level:',
              style: TextStyle(fontWeight: FontWeight.bold),
            ),
            DropdownButton<String>(
              value: _priority,
              isExpanded: true,
              items: const [
                DropdownMenuItem(value: 'Low', child: Text('Low Priority')),
                DropdownMenuItem(
                  value: 'Medium',
                  child: Text('Medium Priority'),
                ),
                DropdownMenuItem(value: 'High', child: Text('High Priority')),
                DropdownMenuItem(
                  value: 'Urgent',
                  child: Text('Urgent Priority'),
                ),
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
                onPressed:
                    _loading ||
                        _loadingOptions ||
                        _optionsError != null ||
                        _projects.isEmpty ||
                        _materials.isEmpty
                    ? null
                    : _submit,
                child: _loading
                    ? const CircularProgressIndicator()
                    : const Text(
                        'SUBMIT MATERIAL REQUEST',
                        style: TextStyle(fontWeight: FontWeight.bold),
                      ),
              ),
            ),
          ],
        ),
      ),
    );
  }
}
