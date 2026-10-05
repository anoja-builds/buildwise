import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../operations/services/operations_service.dart';
import 'material_request_procurement_view.dart';

/// Reachable home for Component 2's Flutter slice. Component 1's real
/// material-request list/detail screens don't exist yet, so this shows the
/// seeded demo request (see backend/BuildWise.Api/Data/DbSeeder.cs) — once
/// that list exists, [MaterialRequestProcurementView] drops straight into
/// its detail screen unchanged.
class ProcurementHomeScreen extends StatefulWidget {
  const ProcurementHomeScreen({super.key, this.siteScoped = false});

  final bool siteScoped;

  @override
  State<ProcurementHomeScreen> createState() => _ProcurementHomeScreenState();
}

class _ProcurementHomeScreenState extends State<ProcurementHomeScreen> {
  final _service = OperationsService();
  List<Map<String, dynamic>> _requests = const [];
  int? _selectedId;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
  }

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final requests = widget.siteScoped
          ? await _service.listMyRequests()
          : await _service.listRequests(status: 'Approved');
      if (!mounted) return;
      setState(() {
        _requests = requests;
        _selectedId = requests.isEmpty ? null : (requests.first['id'] as num).toInt();
      });
    } catch (error) {
      if (mounted) setState(() => _error = error.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Procurement Status'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
            ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
            : _requests.isEmpty
                ? const Center(child: Text('No approved material requests are available.'))
                : ListView(
                    padding: const EdgeInsets.all(16),
                    children: [
                      DropdownButtonFormField<int>(
                        initialValue: _selectedId,
                        decoration: const InputDecoration(labelText: 'Material request'),
                        items: _requests.map((request) => DropdownMenuItem<int>(
                          value: (request['id'] as num).toInt(),
                          child: Text('Request #${request['id']} · ${request['projectName']}'),
                        )).toList(),
                        onChanged: (value) => setState(() => _selectedId = value),
                      ),
                      const SizedBox(height: 18),
                      if (_selectedId != null)
                        MaterialRequestProcurementView(materialRequestId: _selectedId!),
                    ],
                  ),
  );
}
