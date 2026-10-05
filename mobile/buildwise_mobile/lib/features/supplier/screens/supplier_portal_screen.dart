import 'package:flutter/material.dart';

import '../../../core/theme/app_colors.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../../../core/widgets/error_widget.dart' as buildwise;
import '../services/supplier_portal_service.dart';

/// Supplier-facing shell: RFQs addressed to this supplier, the quotations it
/// has submitted, and the orders awarded to it.
///
/// All data is already scoped to the signed-in supplier by the API, so these
/// screens never filter by supplier themselves.
class SupplierPortalScreen extends StatefulWidget {
  const SupplierPortalScreen({super.key, this.section = 'Rfqs'});

  /// One of `Rfqs`, `Quotations`, `PurchaseOrders`.
  final String section;

  @override
  State<SupplierPortalScreen> createState() => _SupplierPortalScreenState();
}

class _SupplierPortalScreenState extends State<SupplierPortalScreen> {
  final SupplierPortalService _service = SupplierPortalService();

  List<Map<String, dynamic>> _rows = const [];
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
      final rows = switch (widget.section) {
        'Quotations' => await _service.listQuotations(),
        'PurchaseOrders' => await _service.listPurchaseOrders(),
        _ => await _service.listRfqs(),
      };
      if (!mounted) return;
      setState(() => _rows = rows);
    } catch (error) {
      if (!mounted) return;
      setState(() => _error = error.toString().replaceFirst('Exception: ', ''));
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  String get _title => switch (widget.section) {
        'Quotations' => 'My Quotations',
        'PurchaseOrders' => 'My Purchase Orders',
        _ => 'My RFQs',
      };

  String get _emptyMessage => switch (widget.section) {
        'Quotations' => 'Quotations you submit against an open RFQ appear here.',
        'PurchaseOrders' => 'An order appears here once your quotation is selected.',
        _ => 'You will see an invitation once the procurement desk issues an RFQ to your organisation.',
      };

  @override
  Widget build(BuildContext context) {
    if (_loading) return const Center(child: CircularProgressIndicator());
    if (_error != null) {
      return Padding(
        padding: const EdgeInsets.all(16),
        child: buildwise.ErrorWidget(message: _error!, onRetry: _load),
      );
    }
    if (_rows.isEmpty) {
      return RefreshIndicator(
        onRefresh: _load,
        child: ListView(
          padding: const EdgeInsets.all(16),
          children: [
            Text(_title, style: Theme.of(context).textTheme.headlineSmall),
            const SizedBox(height: 18),
            AppCard(child: Text(_emptyMessage, style: const TextStyle(color: AppColors.textMuted))),
          ],
        ),
      );
    }

    return RefreshIndicator(
      onRefresh: _load,
      child: ListView(
        padding: const EdgeInsets.all(16),
        children: [
          Text(_title, style: Theme.of(context).textTheme.headlineSmall),
          const SizedBox(height: 14),
          ..._rows.map(_card),
        ],
      ),
    );
  }

  Widget _card(Map<String, dynamic> row) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: AppCard(
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: _rowsFor(row),
          ),
        ),
      );

  List<Widget> _rowsFor(Map<String, dynamic> row) => switch (widget.section) {
        'Quotations' => [
            _line('Quotation', 'Q-${row['id']}'),
            _line('RFQ', row['rfqId'] == null ? '—' : 'RFQ-${row['rfqId']}'),
            _line('Date', '${row['quotationDate'] ?? '—'}'),
            _line('Valid until', '${row['validUntil'] ?? '—'}'),
            _line('Total', _money(row['totalAmount'])),
            _line('Status', '${row['status'] ?? '—'}'),
          ],
        'PurchaseOrders' => [
            _line('Order', 'PO-${row['id']}'),
            _line('Order date', '${row['orderDate'] ?? '—'}'),
            _line('Expected', '${row['expectedDeliveryDate'] ?? '—'}'),
            _line('Total', _money(row['totalAmount'])),
            _line('Status', '${row['status'] ?? '—'}'),
          ],
        _ => [
            _line('RFQ', 'RFQ-${row['rfqId']}'),
            _line('Project', '${row['projectName'] ?? '—'}'),
            _line('Required by', '${row['requiredResponseDate'] ?? '—'}'),
            _line('RFQ status', '${row['rfqStatus'] ?? '—'}'),
            _line('Your response', '${row['invitationStatus'] ?? '—'}'),
            _line('Quotation submitted', row['hasSubmittedQuotation'] == true ? 'Yes' : 'No'),
          ],
      };

  Widget _line(String label, String value) => Padding(
        padding: const EdgeInsets.symmetric(vertical: 3),
        child: Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            SizedBox(
              width: 140,
              child: Text(label, style: const TextStyle(color: AppColors.textMuted)),
            ),
            Expanded(child: Text(value, style: const TextStyle(fontWeight: FontWeight.w600))),
          ],
        ),
      );

  /// Formats a monetary value without pulling in the `intl` package, which the
  /// app does not currently depend on. Null is rendered as an em dash so a
  /// redacted field reads as "not visible" rather than "zero".
  String _money(dynamic amount) {
    if (amount == null) return '—';
    final value = amount is num ? amount : double.tryParse('$amount');
    if (value == null) return '$amount';
    final digits = value.truncate().toString();
    final buffer = StringBuffer();
    for (var i = 0; i < digits.length; i++) {
      if (i > 0 && (digits.length - i) % 3 == 0) buffer.write(',');
      buffer.write(digits[i]);
    }
    final fraction = (value - value.truncate()).abs();
    if (fraction > 0) buffer.write(fraction.toStringAsFixed(2).substring(1));
    return buffer.toString();
  }
}