import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';

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

  DateTime _requiredDate = DateTime.now().add(const Duration(days: 5));
  int? _projectId;
  int? _materialId;
  String _unit = '';
  List<dynamic> _projects = [];
  List<dynamic> _materials = [];
  final List<Map<String, dynamic>> _addedItems = [];
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

  Map<String, dynamic>? _currentItem() {
    final quantity = double.tryParse(_quantityController.text);
    if (_materialId == null ||
        quantity == null ||
        !quantity.isFinite ||
        quantity <= 0 ||
        quantity > 1000000) {
      return null;
    }
    return {
      'materialId': _materialId,
      'quantity': quantity,
      'unit': _unit,
      'notes': _notesController.text.trim(),
    };
  }

  void _addItem() {
    final item = _currentItem();
    if (item == null ||
        _addedItems.any((i) => i['materialId'] == item['materialId'])) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Choose a new material and enter a valid quantity.'),
        ),
      );
      return;
    }
    setState(() {
      _addedItems.add(item);
      _materialId = null;
      _unit = '';
      _quantityController.clear();
      _notesController.clear();
    });
  }

  Future<void> _submit({bool draft = false}) async {
    if (_loading) return;
    final current = _currentItem();
    final hasUnfinishedItem =
        _materialId != null || _quantityController.text.isNotEmpty;
    final items = [..._addedItems, ?current];
    final today = DateUtils.dateOnly(DateTime.now());
    if (DateUtils.dateOnly(_requiredDate).isBefore(today)) {
      ScaffoldMessenger.of(context).showSnackBar(
        const SnackBar(
          content: Text('Required date cannot be before today.'),
        ),
      );
      return;
    }
    if (_projectId == null ||
        items.isEmpty ||
        (hasUnfinishedItem && current == null) ||
        items.map((i) => i['materialId']).toSet().length != items.length ||
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
        'requiredDate': _requiredDate.toIso8601String(),
        'reason': _reasonController.text,
        'submitImmediately': !draft,
        'items': items,
      };

      await _service.createRequest(payload);
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(
            content: Text(
              draft
                  ? 'Material request draft saved.'
                  : 'Material Request Submitted to Office!',
            ),
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
      appBar: const WorkspaceAppBar(
        title: Text('New Material Request'),
        subtitle: 'Request materials for your project',
      ),
      body: SingleChildScrollView(
        padding: const EdgeInsets.all(16.0),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            const Text(
              'Request Details',
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
            for (final item in _addedItems)
              ListTile(
                contentPadding: EdgeInsets.zero,
                title: Text(
                  '${_materials.firstWhere((m) => m['id'] == item['materialId'])['name']}',
                ),
                subtitle: Text('${item['quantity']} ${item['unit']}'),
                trailing: IconButton(
                  tooltip: 'Remove material',
                  onPressed: _loading
                      ? null
                      : () => setState(() => _addedItems.remove(item)),
                  icon: const Icon(Icons.close),
                ),
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
                labelText: _unit.isEmpty
                    ? 'Required Quantity'
                    : 'Required Quantity ($_unit)',
                border: const OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 16),
            OutlinedButton.icon(
              icon: const Icon(Icons.calendar_today_outlined, size: 18),
              label: Text('Required date: ${displayDate(_requiredDate)}'),
              onPressed: _loading
                  ? null
                  : () async {
                      final today = DateUtils.dateOnly(DateTime.now());
                      final date = await showDatePicker(
                        context: context,
                        initialDate: _requiredDate.isBefore(today)
                            ? today
                            : _requiredDate,
                        firstDate: today,
                        lastDate: DateTime(today.year + 5),
                      );
                      if (date != null && mounted) {
                        setState(() => _requiredDate = date);
                      }
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
                labelText: 'Material / Delivery Notes',
                hintText: 'e.g. Deliver to Gate B. Tower crane active.',
                border: OutlineInputBorder(),
              ),
            ),
            const SizedBox(height: 24),
            OutlinedButton.icon(
              onPressed: _loading ? null : _addItem,
              icon: const Icon(Icons.add),
              label: const Text('Add Another Material Item'),
            ),
            const SizedBox(height: 12),
            OutlinedButton(
              onPressed: _loading || _loadingOptions || _optionsError != null
                  ? null
                  : () => _submit(draft: true),
              child: const Text('Save Draft'),
            ),
            const SizedBox(height: 12),
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
