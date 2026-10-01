import 'package:flutter/material.dart';

import '../../../core/widgets/widgets.dart';
import '../services/material_request_service.dart';
import 'create_material_request_screen.dart';
import 'material_request_detail_screen.dart';
import '../../../common/screens/operational_home_screen.dart';

class MaterialRequestListScreen extends StatefulWidget {
  const MaterialRequestListScreen({
    super.key,
    this.service,
    this.canCreate = true,
    this.canViewProcurementStatus = true,
  });
  final MaterialRequestService? service;
  final bool canCreate, canViewProcurementStatus;
  @override
  State<MaterialRequestListScreen> createState() =>
      _MaterialRequestListScreenState();
}

class _MaterialRequestListScreenState extends State<MaterialRequestListScreen> {
  late final _service = widget.service ?? MaterialRequestService();
  String _filter = 'All', _search = '';
  int _revision = 0;
  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: WorkspaceAppBar(
      title: const Text('Material Requests'),
      subtitle: 'Track site material requirements',
      actions: [
        IconButton(
          tooltip: 'Site overview',
          onPressed: () => Navigator.push(
            context,
            MaterialPageRoute(
              builder: (_) => SiteEngineerHomeScreen(requests: _service),
            ),
          ),
          icon: const Icon(Icons.home_outlined),
        ),
        IconButton(
          tooltip: 'Refresh request status',
          onPressed: () => setState(() => _revision++),
          icon: const Icon(Icons.refresh),
        ),
      ],
    ),
    body: ApiView<List<dynamic>>(
      key: ValueKey(_revision),
      load: _service.getRequests,
      builder: (context, data, refresh) {
        final requests = data
            .where(
              (r) =>
                  (_filter == 'All' || r['status'] == _filter) &&
                  '${reference('MR', r['id'] as int)} ${r['projectName']} ${r['reason']}'
                      .toLowerCase()
                      .contains(_search.toLowerCase()),
            )
            .toList();
        return ListView(
          padding: const EdgeInsets.fromLTRB(16, 16, 16, 96),
          physics: const AlwaysScrollableScrollPhysics(),
          children: [
            TextFormField(
              initialValue: _search,
              decoration: const InputDecoration(
                hintText: 'Search requests...',
                prefixIcon: Icon(Icons.search),
              ),
              onChanged: (value) => setState(() => _search = value),
            ),
            const SizedBox(height: 12),
            FilterChips(
              values: const [
                'All',
                'Draft',
                'PendingApproval',
                'Approved',
                'Rejected',
                'Ordered',
              ],
              selected: _filter,
              onChanged: (value) => setState(() => _filter = value),
            ),
            const SizedBox(height: 16),
            Text(
              '${requests.length} requests',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 12),
            if (requests.isEmpty)
              const EmptyStateWidget(
                title: 'No material requests',
                message: 'Requests matching your search will appear here.',
              ),
            for (final r in requests)
              Padding(
                padding: const EdgeInsets.only(bottom: 12),
                child: RecordCard(
                  title: reference('MR', r['id'] as int),
                  status: r['status']?.toString(),
                  action: 'View Request',
                  onTap: () async {
                    await Navigator.push(
                      context,
                      MaterialPageRoute(
                        builder: (_) => MaterialRequestDetailScreen(
                          requestId: r['id'] as int,
                          service: _service,
                          canViewProcurementStatus:
                              widget.canViewProcurementStatus,
                        ),
                      ),
                    );
                    refresh();
                  },
                  children: [
                    Text('${r['projectName'] ?? 'Project not recorded'}'),
                    FieldRow('Required date', displayDate(r['requiredDate'])),
                    FieldRow(
                      'Materials',
                      '${r['itemsCount'] ?? (r['items'] as List? ?? []).length}',
                    ),
                    if (r['reason'] != null)
                      Text(
                        '${r['reason']}',
                        style: Theme.of(context).textTheme.bodySmall,
                      ),
                  ],
                ),
              ),
          ],
        );
      },
    ),
    floatingActionButton: widget.canCreate
        ? FloatingActionButton.extended(
            icon: const Icon(Icons.add),
            label: const Text('New Request'),
            onPressed: () async {
              final result = await Navigator.push(
                context,
                MaterialPageRoute(
                  builder: (_) =>
                      CreateMaterialRequestScreen(service: _service),
                ),
              );
              if (result == true && mounted) setState(() => _revision++);
            },
          )
        : null,
  );
}
