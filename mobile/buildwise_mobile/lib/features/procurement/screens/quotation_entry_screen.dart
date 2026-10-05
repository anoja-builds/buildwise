import 'package:flutter/material.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/field_format.dart';
import '../../../core/widgets/field_messages.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/procurement_service.dart';

/// Records a supplier's returned quotation against an approved material request.
///
/// The mobile counterpart of the web app's `QuotationEntryForm`, pointed at the
/// identical endpoint (`POST /material-requests/{id}/quotations`) and the same
/// `CreateQuotationDto` shape, so a quotation keyed in here is indistinguishable
/// from one keyed in on the web.
///
/// The process is unchanged by moving the keyboard to a phone: BuildWise still
/// emails the RFQ, the supplier still replies by email, and the officer still
/// keys in what came back. Nothing is captured automatically.
class QuotationEntryScreen extends StatefulWidget {
  const QuotationEntryScreen({super.key, this.service, this.initialRequestId});

  final ProcurementService? service;
  final int? initialRequestId;

  @override
  State<QuotationEntryScreen> createState() => _QuotationEntryScreenState();
}

class _QuotationEntryScreenState extends State<QuotationEntryScreen> {
  final _service = ProcurementService();

  List<Map<String, dynamic>> _requests = const [];
  List<Map<String, dynamic>> _suppliers = const [];
  Map<String, dynamic>? _request;
  int? _supplierId;
  bool _loading = true;
  bool _saving = false;
  final _transport = TextEditingController(text: '0');
  final _paymentTerms = TextEditingController();
  String? _error;

  // One quantity/unit-price pair per request line, keyed by line id. Each entry
  // owns a controller so the field keeps what the officer typed; a bare `Text`
  // with an `initialValue` would be rebuilt away on every setState.
  final Map<int, TextEditingController> _quantities = {};
  final Map<int, TextEditingController> _prices = {};

  @override
  void dispose() {
    _clearLines();
    _transport.dispose();
    _paymentTerms.dispose();
    super.dispose();
  }

  TextEditingController _controllerFor(
    Map<int, TextEditingController> store,
    int id,
  ) => store.putIfAbsent(id, TextEditingController.new);

  /// Drops the typed lines when the request changes or a save succeeds.
  ///
  /// The controllers are disposed, not just dereferenced, so switching requests
  /// repeatedly does not leak a controller per line.
  void _clearLines() {
    for (final controller in [..._quantities.values, ..._prices.values]) {
      controller.dispose();
    }
    _quantities.clear();
    _prices.clear();
  }

  DateTime _quotationDate = DateTime.now();
  DateTime _validUntil = DateTime.now().add(const Duration(days: 30));
  DateTime? _promisedDelivery;

  @override
  void initState() {
    super.initState();
    _load();
  }

  ProcurementService get _api => widget.service ?? _service;

  Future<void> _load() async {
    setState(() {
      _loading = true;
      _error = null;
    });
    try {
      // Approved requests are the only ones a supplier can be quoted against,
      // and only an active supplier can be recorded here.
      final results = await Future.wait([
        _api.listApprovedMaterialRequests(),
        _api.listSuppliers(pageSize: 200),
      ]);
      if (!mounted) return;
      final payload = results[1];
      final suppliers = payload is Map
          ? ((payload['items'] as List<dynamic>?) ?? const [])
          : (payload as List<dynamic>? ?? const []);
      final requests = results[0] as List<Map<String, dynamic>>;
      final detail = requests.isEmpty
          ? null
          : await _api.getMaterialRequest(
              requests.any(
                    (request) => request['id'] == widget.initialRequestId,
                  )
                  ? widget.initialRequestId!
                  : (requests.first['id'] as num).toInt(),
            );
      if (!mounted) return;
      setState(() {
        _requests = requests;
        _suppliers = suppliers
            .cast<Map<String, dynamic>>()
            .where(
              (supplier) =>
                  supplier['status'] == null || supplier['status'] == 'Active',
            )
            .toList();
        _request = detail;
      });
    } catch (e) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  List<Map<String, dynamic>> get _items =>
      (_request?['items'] as List<dynamic>?)?.cast<Map<String, dynamic>>() ??
      const [];

  /// The same running total the web form shows, so the officer sees the same
  /// number in both clients.
  double get _total {
    var sum = 0.0;
    for (final item in _items) {
      final id = (item['id'] as num).toInt();
      final qty = double.tryParse(_quantities[id]?.text ?? '') ?? 0;
      final price = double.tryParse(_prices[id]?.text ?? '') ?? 0;
      sum += qty * price;
    }
    return sum + (double.tryParse(_transport.text) ?? 0);
  }

  /// Only lines the officer actually priced are sent, matching the web form's
  /// `filter((line) => line.quantity > 0 || line.unitPrice > 0)`.
  List<Map<String, dynamic>> get _quoteItems {
    final lines = <Map<String, dynamic>>[];
    for (final item in _items) {
      final id = (item['id'] as num).toInt();
      final quantity = double.tryParse(_quantities[id]?.text ?? '') ?? 0;
      final unitPrice = double.tryParse(_prices[id]?.text ?? '') ?? 0;
      if (quantity > 0 || unitPrice > 0) {
        lines.add({
          'materialRequestItemId': id,
          'quantity': quantity,
          'unitPrice': unitPrice,
        });
      }
    }
    return lines;
  }

  /// Date-only "today", so day comparisons ignore the time of day exactly as
  /// the web form's YYYY-MM-DD string rules do.
  DateTime get _today {
    final now = DateTime.now();
    return DateTime(now.year, now.month, now.day);
  }

  DateTime get _quotationDay =>
      DateTime(_quotationDate.year, _quotationDate.month, _quotationDate.day);

  // Date rules, identical to the web `QuotationEntryForm`:
  //   quotationDate        — past or today only
  //   validUntil           — must be after the quotation date (a past date is
  //                          still allowed, but warns that it has expired)
  //   promisedDeliveryDate — must be after the quotation date
  String? get _quotationDateError => _quotationDay.isAfter(_today)
      ? 'Quotation date cannot be in the future.'
      : null;

  String? get _validUntilError => !_validUntil.isAfter(_quotationDay)
      ? '"Valid until" must be after the quotation date.'
      : null;

  String? get _validUntilWarning => _validUntil.isBefore(_today)
      ? 'This quotation has already expired (valid-until is in the past).'
      : null;

  String? get _promisedError {
    final promised = _promisedDelivery;
    if (promised == null) return null;
    return promised.isAfter(_quotationDay)
        ? null
        : 'Promised delivery date must be after the quotation date.';
  }

  bool get _hasDateError =>
      _quotationDateError != null ||
      _validUntilError != null ||
      _promisedError != null;

  Future<void> _save() async {
    if (_supplierId == null) {
      setState(() => _error = 'Select a supplier.');
      return;
    }
    // The same gate the web form applies: an invalid date combination never
    // reaches the API.
    if (_hasDateError) {
      setState(() => _error = 'Fix date errors before saving.');
      return;
    }
    for (final item in _items) {
      final id = (item['id'] as num).toInt();
      final problem =
          _quantityError(item, _quantities[id]?.text ?? '') ??
          _priceError(_prices[id]?.text ?? '');
      if (problem != null) {
        setState(() => _error = problem);
        return;
      }
    }
    final transport = double.tryParse(_transport.text.trim());
    if (transport == null || !transport.isFinite || transport < 0) {
      setState(
        () => _error = 'Transport charge must be a number of 0 or more.',
      );
      return;
    }
    final lines = _quoteItems;
    if (lines.isEmpty) {
      setState(
        () => _error = 'Enter quantity and unit price for at least one item.',
      );
      return;
    }
    if (lines.any(
      (l) =>
          !(l['quantity'] as double).isFinite ||
          !(l['unitPrice'] as double).isFinite ||
          (l['quantity'] as double) <= 0 ||
          (l['unitPrice'] as double) < 0,
    )) {
      setState(
        () => _error =
            'Quantity must be positive and unit price cannot be negative.',
      );
      return;
    }
    final requestId = (_request!['id'] as num).toInt();
    setState(() {
      _saving = true;
      _error = null;
    });
    try {
      await _api.createQuotation(
        requestId,
        supplierId: _supplierId!,
        quotationDate: _iso(_quotationDate),
        validUntil: _iso(_validUntil),
        promisedDeliveryDate: _promisedDelivery == null
            ? null
            : _iso(_promisedDelivery!),
        items: lines,
        transportCharge: transport,
        paymentTerms: _paymentTerms.text.trim(),
      );
      if (!mounted) return;
      ScaffoldMessenger.of(context)
          .showSnackBar(const SnackBar(content: Text('Quotation recorded.')));
      setState(() {
        _supplierId = null;
        _clearLines();
      });
    } catch (e) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  static String _iso(DateTime value) =>
      '${value.year.toString().padLeft(4, '0')}-'
      '${value.month.toString().padLeft(2, '0')}-'
      '${value.day.toString().padLeft(2, '0')}';

  Future<void> _pickDate(
    DateTime initial,
    ValueChanged<DateTime> onPicked, {
    String? help,
  }) async {
    final picked = await showDatePicker(
      context: context,
      firstDate: DateTime.now().subtract(const Duration(days: 365)),
      lastDate: DateTime.now().add(const Duration(days: 365)),
      initialDate: initial,
      helpText: help,
    );
    if (picked != null) onPicked(picked);
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Record quotation'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
    ),
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null && _requests.isEmpty
        ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
        : ListView(
            padding: const EdgeInsets.all(16),
            children: [
              const SectionHeader(title: 'What the supplier quoted'),
              const SizedBox(height: 8),
              AppDropdown(
                label: 'Approved material request',
                value: _request == null
                    ? null
                    : (_request!['id'] as num).toString(),
                items: _requests
                    .map((r) => (r['id'] as num).toString())
                    .toList(),
                // Label each option with the material, not the bare id.
                itemLabels: _requests
                    .map(FieldFormat.materialRequestLabel)
                    .toList(),
                onChanged: (value) async {
                  if (value == null) return;
                  setState(() {
                    _loading = true;
                    _error = null;
                  });
                  try {
                    final detail = await _api.getMaterialRequest(
                      int.parse(value),
                    );
                    if (!mounted) return;
                    setState(() {
                      _clearLines();
                      _request = detail;
                    });
                  } catch (e) {
                    if (mounted) {
                      setState(
                        () => _error = FieldMessages.friendly(e.toString()),
                      );
                    }
                  } finally {
                    if (mounted) setState(() => _loading = false);
                  }
                },
              ),
              const SizedBox(height: 12),
              AppDropdown(
                label: 'Supplier',
                value: _supplierId?.toString(),
                items: _suppliers
                    .map((s) => (s['id'] as num).toString())
                    .toList(),
                itemLabels: _suppliers
                    .map((s) => s['name']?.toString() ?? 'Supplier')
                    .toList(),
                onChanged: (value) => setState(
                  () => _supplierId = value == null ? null : int.parse(value),
                ),
              ),
              const SizedBox(height: 18),
              const SectionHeader(title: 'Lines'),
              const SizedBox(height: 8),
              if (_items.isEmpty)
                const AppCard(child: Text('Select a request to see its lines.'))
              else
                ..._items.map(_lineCard),
              const SizedBox(height: 16),
              AppCard(
                child: Row(
                  mainAxisAlignment: MainAxisAlignment.spaceBetween,
                  children: [
                    const Text('Quotation total'),
                    Text(
                      _total.toStringAsFixed(2),
                      style: const TextStyle(fontWeight: FontWeight.w700),
                    ),
                  ],
                ),
              ),
              const SizedBox(height: 18),
              AppTextField(
                label: 'Transport charge',
                controller: _transport,
                keyboardType: const TextInputType.numberWithOptions(
                  decimal: true,
                ),
                onChanged: (_) => setState(() {}),
              ),
              const SizedBox(height: 12),
              AppTextField(
                label: 'Payment terms',
                controller: _paymentTerms,
                maxLines: 2,
              ),
              const SizedBox(height: 18),
              const SectionHeader(title: 'Dates'),
              const SizedBox(height: 8),
              _dateTile(
                'Quotation date',
                _quotationDate,
                () => _pickDate(
                  _quotationDate,
                  (v) => setState(() => _quotationDate = v),
                  help: 'Quotation date',
                ),
                errorText: _quotationDateError,
                hintText: 'Must be today or in the past.',
              ),
              _dateTile(
                'Valid until',
                _validUntil,
                () => _pickDate(
                  _validUntil,
                  (v) => setState(() => _validUntil = v),
                  help: 'Valid until',
                ),
                errorText: _validUntilError,
                warnText: _validUntilWarning,
                hintText: 'Must be after the quotation date.',
              ),
              _dateTile(
                'Promised delivery',
                _promisedDelivery,
                () => _pickDate(
                  _promisedDelivery ?? DateTime.now(),
                  (v) => setState(() => _promisedDelivery = v),
                  help: 'Promised delivery',
                ),
                errorText: _promisedError,
                hintText: 'Must be after the quotation date.',
              ),
              if (_error != null) ...[
                const SizedBox(height: 10),
                Text(_error!, style: const TextStyle(color: Colors.red)),
              ],
              const SizedBox(height: 16),
              AppButton(
                label: _saving ? 'Saving…' : 'Save quotation',
                expand: true,
                onPressed: _saving || _items.isEmpty ? null : _save,
              ),
            ],
          ),
  );

  /// Quantity rule, identical to the web form's per-line validation: a line the
  /// officer has started typing must hold a number greater than 0, and a whole
  /// number for discrete units such as bags. An untouched line is not an error
  /// — it is simply excluded from the quotation.
  String? _quantityError(Map<String, dynamic> item, String raw) {
    final text = raw.trim();
    if (text.isEmpty) return null;
    final value = double.tryParse(text);
    if (value == null || !value.isFinite || value <= 0) {
      return 'Quantity must be a positive number greater than 0.';
    }
    final unit = item['unit']?.toString() ?? '';
    const discrete = {
      'bag',
      'bags',
      'piece',
      'pieces',
      'nos',
      'sheet',
      'sheets',
      'unit',
      'units',
      'box',
      'boxes',
    };
    if (discrete.contains(unit.trim().toLowerCase()) &&
        value != value.roundToDouble()) {
      return "Decimal quantities are not allowed for '$unit'. "
          'Please specify a whole integer quantity (e.g. 10 instead of 10.5).';
    }
    return null;
  }

  /// Unit-price rule: a number that is never negative.
  String? _priceError(String raw) {
    final text = raw.trim();
    if (text.isEmpty) return null;
    final value = double.tryParse(text);
    if (value == null || !value.isFinite || value < 0) {
      return 'Unit price must be a number of 0 or more.';
    }
    return null;
  }

  Widget _lineCard(Map<String, dynamic> item) {
    final id = (item['id'] as num).toInt();
    final quantity = _controllerFor(_quantities, id);
    final price = _controllerFor(_prices, id);
    return Padding(
      padding: const EdgeInsets.only(bottom: 10),
      child: AppCard(
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: [
            Text(
              '${item['materialName'] ?? 'Material'}',
              style: const TextStyle(fontWeight: FontWeight.w600),
            ),
            const SizedBox(height: 2),
            Text(
              'Requested ${item['requestedQuantity'] ?? 0} ${item['unit'] ?? ''}',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 10),
            Row(
              children: [
                Expanded(
                  child: AppTextField(
                    label: 'Quantity',
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    controller: quantity,
                    // onChanged rebuilds the card so the rule below re-runs as
                    // the officer types, matching the web form's live errors.
                    onChanged: (_) => setState(() {}),
                    errorText: _quantityError(item, quantity.text),
                  ),
                ),
                const SizedBox(width: 10),
                Expanded(
                  child: AppTextField(
                    label: 'Unit price',
                    keyboardType: const TextInputType.numberWithOptions(
                      decimal: true,
                    ),
                    controller: price,
                    onChanged: (_) => setState(() {}),
                    errorText: _priceError(price.text),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
    );
  }

  /// A date row with the same inline messaging the web form uses: the rule
  /// error when it is broken, an expiry warning in amber, otherwise the hint.
  Widget _dateTile(
    String label,
    DateTime? value,
    VoidCallback onTap, {
    String? errorText,
    String? warnText,
    String? hintText,
  }) => Padding(
    padding: const EdgeInsets.only(bottom: 6),
    child: Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: [
        ListTile(
          contentPadding: EdgeInsets.zero,
          title: Text(label),
          trailing: Text(
            value == null ? 'Not set' : _iso(value),
            style: Theme.of(context).textTheme.bodyMedium,
          ),
          onTap: onTap,
        ),
        if (errorText != null)
          Padding(
            padding: const EdgeInsets.only(left: 4),
            child: Text(
              errorText,
              style: const TextStyle(color: Colors.red, fontSize: 12),
            ),
          )
        else if (warnText != null)
          Padding(
            padding: const EdgeInsets.only(left: 4),
            child: Text(
              warnText,
              style: TextStyle(color: Colors.orange.shade800, fontSize: 12),
            ),
          )
        else if (hintText != null)
          Padding(
            padding: const EdgeInsets.only(left: 4),
            child: Text(hintText, style: Theme.of(context).textTheme.bodySmall),
          ),
      ],
    ),
  );
}
