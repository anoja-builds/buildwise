import 'package:flutter/material.dart';

import '../services/material_request_service.dart';
import 'create_material_request_screen.dart';
import '../../procurement/screens/material_request_procurement_view.dart';

class MaterialRequestListScreen extends StatefulWidget {
  const MaterialRequestListScreen({
    super.key,
    this.service,
    this.canCreate = true,
    this.canViewProcurementStatus = true,
  });

  final bool canCreate;
  final bool canViewProcurementStatus;

  final MaterialRequestService? service;

  @override
  State<MaterialRequestListScreen> createState() =>
      _MaterialRequestListScreenState();
}

class _MaterialRequestListScreenState extends State<MaterialRequestListScreen>
    with WidgetsBindingObserver {
  late final _service = widget.service ?? MaterialRequestService();
  late Future<List<dynamic>> _futureRequests;

  @override
  void initState() {
    super.initState();
    WidgetsBinding.instance.addObserver(this);
    _refresh();
  }

  @override
  void dispose() {
    WidgetsBinding.instance.removeObserver(this);
    super.dispose();
  }

  @override
  void didChangeAppLifecycleState(AppLifecycleState state) {
    if (state == AppLifecycleState.resumed && mounted) _refresh();
  }

  void _refresh() {
    setState(() {
      _futureRequests = _service.getRequests();
    });
  }

  @override
  Widget build(BuildContext context) {
    return Scaffold(
      appBar: AppBar(
        title: const Text('Material Requests'),
        actions: [
          IconButton(
            icon: const Icon(Icons.refresh),
            tooltip: 'Refresh request status',
            onPressed: _refresh,
          ),
        ],
      ),
      body: FutureBuilder<List<dynamic>>(
        future: _futureRequests,
        builder: (context, snapshot) {
          if (snapshot.connectionState == ConnectionState.waiting) {
            return const Center(child: CircularProgressIndicator());
          } else if (snapshot.hasError) {
            return Center(child: Text('Error: ${snapshot.error}'));
          } else if (!snapshot.hasData || snapshot.data!.isEmpty) {
            return const Center(child: Text('No material requests found.'));
          }

          final requests = snapshot.data!;
          return ListView.builder(
            itemCount: requests.length,
            padding: const EdgeInsets.all(12),
            itemBuilder: (context, index) {
              final item = requests[index];
              return Card(
                elevation: 2,
                margin: const EdgeInsets.only(bottom: 12),
                child: ListTile(
                  onTap: !widget.canViewProcurementStatus
                      ? null
                      : () => Navigator.push(
                          context,
                          MaterialPageRoute(
                            builder: (_) => Scaffold(
                              appBar: AppBar(
                                title: Text('Request #${item['id']}'),
                              ),
                              body: SingleChildScrollView(
                                padding: const EdgeInsets.all(16),
                                child: MaterialRequestProcurementView(
                                  materialRequestId: item['id'] as int,
                                ),
                              ),
                            ),
                          ),
                        ),
                  leading: CircleAvatar(
                    backgroundColor:
                        item['priority'] == 'High' ||
                            item['priority'] == 'Urgent'
                        ? Colors.orange.shade100
                        : Colors.blue.shade100,
                    child: Icon(
                      Icons.assignment,
                      color:
                          item['priority'] == 'High' ||
                              item['priority'] == 'Urgent'
                          ? Colors.orange.shade800
                          : Colors.blue.shade800,
                    ),
                  ),
                  title: Text(
                    'MR-${item['id'].toString().padLeft(4, '0')} - ${item['projectName'] ?? 'Site'}',
                    style: const TextStyle(fontWeight: FontWeight.bold),
                  ),
                  subtitle: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: [
                      const SizedBox(height: 4),
                      Text('Reason: ${item['reason'] ?? 'N/A'}'),
                      Text(
                        'Status: ${item['status']}',
                        style: TextStyle(
                          fontWeight: FontWeight.bold,
                          color:
                              item['status'] == 'Approved' ||
                                  item['status'] == 'Ordered'
                              ? Colors.green
                              : Colors.blue.shade800,
                        ),
                      ),
                    ],
                  ),
                  trailing: Container(
                    padding: const EdgeInsets.symmetric(
                      horizontal: 8,
                      vertical: 4,
                    ),
                    decoration: BoxDecoration(
                      color: Colors.grey.shade200,
                      borderRadius: BorderRadius.circular(4),
                    ),
                    child: Text(
                      item['priority'] ?? 'Medium',
                      style: const TextStyle(
                        fontSize: 12,
                        fontWeight: FontWeight.bold,
                      ),
                    ),
                  ),
                ),
              );
            },
          );
        },
      ),
      floatingActionButton: !widget.canCreate
          ? null
          : FloatingActionButton.extended(
              onPressed: () async {
                final res = await Navigator.push(
                  context,
                  MaterialPageRoute(
                    builder: (_) =>
                        CreateMaterialRequestScreen(service: _service),
                  ),
                );
                if (res == true && mounted) _refresh();
              },
              icon: const Icon(Icons.add),
              label: const Text('New Request'),
            ),
    );
  }
}
