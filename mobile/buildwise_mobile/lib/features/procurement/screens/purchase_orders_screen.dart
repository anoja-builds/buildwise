import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/field_format.dart';
import '../../../core/widgets/field_messages.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/procurement_service.dart';

/// Purchase order register — mirrors the web app's Purchase Orders page.
///
/// A purchase order here is the *end* of the procurement chain, never a
/// shortcut: the quotation agent recommends a supplier, a manager accepts or
/// overrides at the approval gate, and only an approved workflow can become an
/// order. This screen reports and filters; it never creates one, which is what
/// keeps the human gate meaningful.
class PurchaseOrdersScreen extends StatefulWidget {
  const PurchaseOrdersScreen({
    super.key,
    this.service,
    this.canManage = false,
    this.initialOrderId,
  });

  final ProcurementService? service;
  final bool canManage;
  final int? initialOrderId;

  @override
  State<PurchaseOrdersScreen> createState() => _PurchaseOrdersScreenState();
}

class _PurchaseOrdersScreenState extends State<PurchaseOrdersScreen> {
  final _service = ProcurementService();

  /// Same status vocabulary the web filter offers. Completed orders are
  /// deliberately absent: a finished order is closed history, so the working
  /// filter offers the states an order can still be acted on. The status a
  /// purchase order *reaches* is unchanged — the update action can still move
  /// an in-progress order to Completed.
  static const List<String?> _statuses = [
    null,
    'Created',
    'Confirmed',
    'InProgress',
    'Cancelled',
  ];

  List<Map<String, dynamic>> _orders = const [];
  String? _status;
  String _search = '';
  int _page = 1;
  int _total = 0;
  static const _pageSize = 20;
  bool _loading = true;
  String? _error;

  @override
  void initState() {
    super.initState();
    _load();
    if (widget.initialOrderId != null) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (mounted) _openOrder(widget.initialOrderId!);
      });
    }
  }

  ProcurementService get _api => widget.service ?? _service;

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      final result = await _api.listPurchaseOrdersPage(
        status: _status,
        search: _search,
        page: _page,
        pageSize: _pageSize,
      );
      if (mounted) {
        setState(() {
          _orders = (result['items'] as List<dynamic>? ?? const [])
              .cast<Map<String, dynamic>>();
          _total = (result['total'] as num?)?.toInt() ?? _orders.length;
        });
      }
    } catch (e) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Purchase Orders'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
        : Column(
            children: [
              Padding(
                padding: const EdgeInsets.all(16),
                child: Row(
                  children: [
                    Expanded(
                      child: AppTextField(
                        label: 'Search',
                        hint: 'Order id or supplier',
                        onChanged: (value) => _search = value,
                      ),
                    ),
                    IconButton(
                      onPressed: () {
                        setState(() => _page = 1);
                        _load();
                      },
                      icon: const Icon(Icons.search),
                    ),
                  ],
                ),
              ),
              Padding(
                padding: const EdgeInsets.fromLTRB(16, 12, 16, 4),
                child: AppDropdown(
                  label: 'Status',
                  value: _status,
                  items: _statuses.map((s) => s ?? 'all').toList(),
                  itemLabels: _statuses
                      .map((s) => s ?? 'All statuses')
                      .toList(),
                  onChanged: (value) {
                    setState(() {
                      _status = value == 'all' ? null : value;
                      _page = 1;
                    });
                    _load();
                  },
                ),
              ),
              Expanded(
                child: _orders.isEmpty
                    ? const EmptyStateWidget(
                        title: 'No purchase orders yet',
                        message:
                            'A purchase order appears here once a manager '
                            'approves a procurement recommendation.',
                      )
                    : RefreshIndicator(
                        onRefresh: _load,
                        child: ListView.separated(
                          padding: const EdgeInsets.all(16),
                          itemCount: _orders.length,
                          separatorBuilder: (_, _) =>
                              const SizedBox(height: 10),
                          itemBuilder: (_, index) => _orderCard(_orders[index]),
                        ),
                      ),
              ),
              Row(
                mainAxisAlignment: MainAxisAlignment.center,
                children: [
                  TextButton(
                    onPressed: _page <= 1
                        ? null
                        : () {
                            setState(() => _page--);
                            _load();
                          },
                    child: const Text('Previous'),
                  ),
                  Text('Page $_page · $_total orders'),
                  TextButton(
                    onPressed: _page * _pageSize >= _total
                        ? null
                        : () {
                            setState(() => _page++);
                            _load();
                          },
                    child: const Text('Next'),
                  ),
                ],
              ),
            ],
          ),
  );

  Future<void> _openOrder(int id) async {
    try {
      final order = await _api.getPurchaseOrder(id);
      if (!mounted) return;
      await showModalBottomSheet<void>(
        context: context,
        isScrollControlled: true,
        builder: (sheetContext) => SafeArea(
          child: SingleChildScrollView(
            padding: const EdgeInsets.all(20),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              mainAxisSize: MainAxisSize.min,
              children: [
                Text(
                  'Purchase Order #$id',
                  style: Theme.of(sheetContext).textTheme.titleLarge,
                ),
                Text('Status: ${order['status']?.toString() ?? 'Unknown'}'),
                Text(order['supplierName']?.toString() ?? 'Supplier'),
                if (order['totalAmount'] != null)
                  Text('Total: ${order['totalAmount']}'),
                Text(
                  'Expected delivery: ${FieldFormat.date(order['expectedDeliveryDate'])}',
                ),
                for (final item
                    in (order['items'] as List<dynamic>? ?? const [])
                        .cast<Map<String, dynamic>>())
                  ListTile(
                    title: Text(item['materialName']?.toString() ?? 'Material'),
                    subtitle: Text(
                      'Ordered: ${item['orderedQuantity']?.toString() ?? '0'} ${item['materialUnit']?.toString() ?? item['unit']?.toString() ?? ''}',
                    ),
                    trailing: item['unitPrice'] == null
                        ? null
                        : Text('Unit price: ${item['unitPrice']}'),
                  ),
              ],
            ),
          ),
        ),
      );
    } catch (e) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(FieldMessages.friendly(e.toString()))),
        );
      }
    }
  }

  Widget _orderCard(Map<String, dynamic> order) {
    final id = (order['id'] as num?)?.toInt();
    final status = order['status']?.toString();
    return AppCard(
      onTap: id == null ? null : () => _openOrder(id),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Text(
                'PO${id == null ? '' : '#$id'}',
                style: const TextStyle(fontWeight: FontWeight.w700),
              ),
              StatusChip(
                label: FieldFormat.humanize(status),
                tone: FieldFormat.statusTone(status),
              ),
            ],
          ),
          const SizedBox(height: 8),
          Text(
            '${order['supplierName'] ?? 'Supplier'} · '
            '${order['projectName'] ?? ''}',
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          if (order['totalAmount'] != null) ...[
            const SizedBox(height: 4),
            Text(
              'Total ${order['totalAmount']}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
          ],
        ],
      ),
    );
  }
}
