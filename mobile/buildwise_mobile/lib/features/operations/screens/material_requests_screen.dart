import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/operations_service.dart';

class MaterialRequestsScreen extends StatefulWidget {
  const MaterialRequestsScreen({super.key, this.service, this.readOnly = false});
  final OperationsService? service;
  final bool readOnly;

  @override
  State<MaterialRequestsScreen> createState() => _MaterialRequestsScreenState();
}

class _MaterialRequestsScreenState extends State<MaterialRequestsScreen> {
  final _service = OperationsService();
  List<Map<String, dynamic>> _requests = const [];
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
      final requests = widget.readOnly
          ? await (widget.service ?? _service).listRequests()
          : await (widget.service ?? _service).listMyRequests();
      if (mounted) setState(() => _requests = requests);
    } catch (e) {
      if (mounted) setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _create() async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _CreateRequestSheet(service: widget.service ?? _service),
    );
    if (created == true) _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Material Requests'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
    ),
    floatingActionButton: widget.readOnly
        ? null
        : FloatingActionButton.extended(
          onPressed: _create,
          icon: const Icon(Icons.add),
          label: const Text('Create Request'),
        ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
            ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
            : _requests.isEmpty
                ? const EmptyStateWidget(
                    title: 'No material requests',
                    message: 'Create a request for an active project and material.',
                  )
                : RefreshIndicator(
                    onRefresh: _load,
                    child: ListView.separated(
                      padding: const EdgeInsets.all(16),
                      itemCount: _requests.length,
                      separatorBuilder: (_, _) => const SizedBox(height: 10),
                      itemBuilder: (_, index) {
                        final request = _requests[index];
                        return AppCard(
                          child: Column(
                            crossAxisAlignment: CrossAxisAlignment.start,
                            children: [
                              Text(
                                request['projectName']?.toString() ?? 'Project',
                                style: const TextStyle(fontWeight: FontWeight.bold),
                              ),
                              const SizedBox(height: 6),
                              Text('Request #${request['id']} · ${request['itemCount']} item(s)'),
                              const SizedBox(height: 6),
                              StatusChip(
                                label: request['status']?.toString() ?? 'Unknown',
                                tone: _statusTone(request['status']?.toString()),
                              ),
                            ],
                          ),
                        );
                      },
                    ),
                  ),
  );
}

StatusTone _statusTone(String? value) => switch (value) {
      'Approved' => StatusTone.success,
      'Rejected' => StatusTone.danger,
      'PendingApproval' => StatusTone.warning,
      _ => StatusTone.neutral,
    };

class _CreateRequestSheet extends StatefulWidget {
  const _CreateRequestSheet({required this.service});
  final OperationsService service;

  @override
  State<_CreateRequestSheet> createState() => _CreateRequestSheetState();
}

class _CreateRequestSheetState extends State<_CreateRequestSheet> {
  final _quantity = TextEditingController(text: '250');
  final _reason = TextEditingController(text: 'Urgent site requirement');
  final _siteNotes = TextEditingController();
  final _description = TextEditingController(text: 'OPC 42.5N, 50 kg bag');
  final _unit = TextEditingController(text: 'bags');
  final _requestDate = DateTime.now();
  DateTime _requiredDate = DateTime.now().add(const Duration(days: 5));
  String _priority = 'Normal';
  bool _submitting = false;
  String? _error;

  @override
  void dispose() {
    _quantity.dispose();
    _reason.dispose();
    _siteNotes.dispose();
    _description.dispose();
    _unit.dispose();
    super.dispose();
  }

  Future<void> _submit() async {
    final quantity = double.tryParse(_quantity.text) ?? 0;
    if (quantity <= 0 || _reason.text.trim().isEmpty) {
      setState(() => _error = 'Enter a positive quantity and reason.');
      return;
    }
    setState(() { _submitting = true; _error = null; });
    try {
      await widget.service.createRequest(
        projectId: 1,
        requiredDate: _requiredDate.toIso8601String().substring(0, 10),
        materialId: 1,
        quantity: quantity,
        reason: _reason.text.trim(),
        priority: _priority,
        siteNotes: _siteNotes.text.trim().isEmpty ? null : _siteNotes.text.trim(),
        description: _description.text.trim().isEmpty ? null : _description.text.trim(),
        unit: _unit.text.trim().isEmpty ? null : _unit.text.trim(),
        itemRequiredDate: _requiredDate.toIso8601String().substring(0, 10),
      );
      if (mounted) Navigator.pop(context, true);
    } catch (e) {
      setState(() => _error = e.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  Future<void> _pickDate() async {
    final selected = await showDatePicker(
      context: context,
      firstDate: DateTime.now().add(const Duration(days: 3)),
      lastDate: DateTime.now().add(const Duration(days: 365)),
      initialDate: _requiredDate,
    );
    if (selected != null) setState(() => _requiredDate = selected);
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.fromLTRB(20, 20, 20, MediaQuery.viewInsetsOf(context).bottom + 20),
    child: SingleChildScrollView(
      child: Column(
        mainAxisSize: MainAxisSize.min,
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Text('Create Material Request', style: Theme.of(context).textTheme.titleLarge),
          const SizedBox(height: 16),
          const AppTextField(label: 'Project', hint: 'Riverside Apartments — Block C (#1)', readOnly: true),
          const SizedBox(height: 12),
          const AppTextField(label: 'Material', hint: 'OPC Cement (#1)', readOnly: true),
          const SizedBox(height: 12),
          AppTextField(label: 'Specification / Description', controller: _description, maxLines: 2),
          const SizedBox(height: 12),
          AppTextField(label: 'Unit', controller: _unit),
          const SizedBox(height: 12),
          AppTextField(label: 'Quantity', controller: _quantity, keyboardType: const TextInputType.numberWithOptions(decimal: true)),
          const SizedBox(height: 12),
          DropdownButtonFormField<String>(
            initialValue: _priority,
            decoration: const InputDecoration(labelText: 'Priority', border: OutlineInputBorder()),
            items: const [
              DropdownMenuItem(value: 'Low', child: Text('Low')),
              DropdownMenuItem(value: 'Normal', child: Text('Normal')),
              DropdownMenuItem(value: 'High', child: Text('High')),
              DropdownMenuItem(value: 'Urgent', child: Text('Urgent')),
            ],
            onChanged: (value) => setState(() => _priority = value ?? 'Normal'),
          ),
          const SizedBox(height: 12),
          AppTextField(label: 'Site Notes', controller: _siteNotes, maxLines: 2),
          const SizedBox(height: 12),
          AppTextField(label: 'Reason / Purpose', controller: _reason, maxLines: 2),
          const SizedBox(height: 12),
          AppTextField(
            label: 'Request Date',
            hint: _requestDate.toIso8601String().substring(0, 10),
            readOnly: true,
          ),
          const SizedBox(height: 12),
          AppTextField(
            label: 'Required Date',
            hint: _requiredDate.toIso8601String().substring(0, 10),
            readOnly: true,
            onTap: _pickDate,
          ),
          if (_error != null) ...[
            const SizedBox(height: 10),
            Text(_error!, style: const TextStyle(color: Colors.red)),
          ],
          const SizedBox(height: 18),
          AppButton(
            label: _submitting ? 'Submitting…' : 'Submit Request',
            expand: true,
            onPressed: _submitting ? null : _submit,
          ),
        ],
      ),
    ),
  );
}
