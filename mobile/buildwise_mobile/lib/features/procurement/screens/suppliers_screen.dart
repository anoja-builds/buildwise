import 'package:flutter/material.dart';

import '../../../core/theme/app_colors.dart';

import '../../../core/widgets/error_widget.dart' as buildwise;
import '../../../core/widgets/field_messages.dart';
import '../../../core/widgets/widgets.dart' hide ErrorWidget;
import '../services/procurement_service.dart';

/// TRCSL mobile numbers are 07X XXX XXX (10 digits); +94 and 0094 prefixes are
/// accepted too. Returns the normalised display form, or null when the number
/// is not a Sri Lankan mobile. Mirrors the web `validateSriLankanMobile`.
String? normalizeSriLankanPhone(String raw) {
  final cleaned = raw.replaceAll(RegExp(r'[\s\-\(\)]'), '');
  final match = RegExp(r'^(?:0|\+94|0094)?(7[0-24-8]\d{7})$')
      .firstMatch(cleaned);
  if (match == null) return null;
  final seven = match.group(1)!;
  return '0${seven.substring(0, 2)} ${seven.substring(2, 5)} ${seven.substring(5)}';
}

final RegExp _emailPattern = RegExp(
  r'^[a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,}$',
);

/// Field rules for the supplier form, identical to the web
/// `SupplierFormModal.validateSupplierForm` — same messages, so an officer
/// sees the same wording whichever client they add the supplier from.
///
/// * name           — required text (letters must appear; digits-only rejected)
/// * contact person — letters only, when provided
/// * email          — a real mailbox (e.g. supplier@gmail.com), when provided
/// * phone          — TRCSL Sri Lankan mobile
/// * address        — letters, numbers and address symbols, when provided
Map<String, String> validateSupplierFields({
  required String name,
  String contactPerson = '',
  String email = '',
  String phone = '',
  String address = '',
}) {
  final errors = <String, String>{};

  final trimmedName = name.trim();
  if (trimmedName.isEmpty) {
    errors['name'] = 'Supplier name is required.';
  } else if (!RegExp(r'\p{L}', unicode: true).hasMatch(trimmedName)) {
    errors['name'] = 'Supplier name must include letters — it cannot be only numbers or symbols.';
  } else if (!RegExp(
    r"^[\p{L}\p{N}][\p{L}\p{N}\s&.'\-/()]*$",
    unicode: true,
  ).hasMatch(trimmedName)) {
    errors['name'] = 'Supplier name may only contain letters, numbers, spaces and basic symbols (&, ., -, /).';
  }

  final contact = contactPerson.trim();
  if (contact.isNotEmpty &&
      !RegExp(r"^\p{L}[\p{L}\s.'-]*$", unicode: true).hasMatch(contact)) {
    errors['contactPerson'] = 'Contact person must contain letters only (spaces, hyphens and apostrophes are allowed).';
  }

  final mail = email.trim();
  if (mail.isNotEmpty && !_emailPattern.hasMatch(mail)) {
    errors['email'] =
        'Please enter a valid email address (e.g. supplier@gmail.com).';
  }

  final mobile = phone.trim();
  if (mobile.isNotEmpty && normalizeSriLankanPhone(mobile) == null) {
    errors['phone'] = 'Please enter a valid 10-digit Sri Lankan mobile number (e.g. 0771234567 or 0751234567).';
  }

  final place = address.trim();
  if (place.isNotEmpty &&
      (!RegExp(r"^[\p{L}\p{N}\s,.\-/#()&']+$", unicode: true).hasMatch(place) ||
          !RegExp(r'[\p{L}\p{N}]', unicode: true).hasMatch(place))) {
    errors['address'] =
        'Address must contain letters, numbers and symbols only.';
  }

  return errors;
}

/// Supplier directory — the mobile counterpart of the web Suppliers page.
///
/// A supplier is an external party BuildWise emails; this screen only records
/// the contact details the procurement desk needs to raise an RFQ and receive
/// a quotation back. It never creates a login.
class SuppliersScreen extends StatefulWidget {
  const SuppliersScreen({super.key, this.service, this.canEdit = true});

  final ProcurementService? service;
  final bool canEdit;

  @override
  State<SuppliersScreen> createState() => _SuppliersScreenState();
}

class _SuppliersScreenState extends State<SuppliersScreen> {
  final _service = ProcurementService();
  List<Map<String, dynamic>> _suppliers = const [];
  bool _loading = true;
  String? _error;
  String _search = '';
  String _status = 'All';

  List<Map<String, dynamic>> get _visibleSuppliers =>
      _suppliers.where((supplier) {
        final query = _search.trim().toLowerCase();
        return (_status == 'All' || supplier['status'] == _status) &&
            (query.isEmpty ||
                ['name', 'contactPerson', 'email', 'phone'].any(
                  (key) => (supplier[key]?.toString() ?? '')
                      .toLowerCase()
                      .contains(query),
                ));
      }).toList();

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
      // listSuppliers answers with a paged object, so read its items list.
      final payload = await _api.listSuppliers(pageSize: 200);
      final items = payload['items'] as List<dynamic>? ?? const [];
      if (!mounted) return;
      setState(() => _suppliers = items.cast<Map<String, dynamic>>());
    } catch (e) {
      if (mounted) {
        setState(() => _error = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _loading = false);
    }
  }

  Future<void> _addSupplier() async {
    final created = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddSupplierSheet(service: _api),
    );
    if (created == true) {
      await _load();
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(const SnackBar(content: Text('Supplier added.')));
      }
    }
  }

  Future<void> _openSupplier(Map<String, dynamic> row) async {
    try {
      final detail = await _api.getSupplier((row['id'] as num).toInt());
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
                  detail['name']?.toString() ?? 'Supplier',
                  style: Theme.of(sheetContext).textTheme.titleLarge,
                ),
                for (final key in [
                  'contactPerson',
                  'email',
                  'phone',
                  'address',
                  'status',
                ])
                  if (detail[key] != null) Text(detail[key].toString()),
                const SizedBox(height: 12),
                const Text('Quotation history'),
                for (final quote
                    in (detail['quotationHistory'] as List<dynamic>? ??
                            const [])
                        .cast<Map<String, dynamic>>())
                  ListTile(
                    title: Text('Quotation #${quote['quotationId']}'),
                    subtitle: Text(
                      'Request #${quote['materialRequestId']} | ${quote['status']}',
                    ),
                    trailing: Text(quote['totalAmount'].toString()),
                  ),
                if (widget.canEdit) ...[
                  TextButton(
                    onPressed: () {
                      Navigator.pop(sheetContext);
                      _editSupplier(detail);
                    },
                    child: const Text('Edit supplier'),
                  ),
                  for (final status in ['Active', 'Inactive', 'Suspended'])
                    if (status != detail['status'])
                      TextButton(
                        onPressed: () async {
                          Navigator.pop(sheetContext);
                          try {
                            await _api.updateSupplierStatus(
                              (row['id'] as num).toInt(),
                              status,
                            );
                            await _load();
                          } catch (e) {
                            if (mounted) {
                              ScaffoldMessenger.of(context).showSnackBar(
                                SnackBar(
                                  content: Text(
                                    FieldMessages.friendly(e.toString()),
                                  ),
                                ),
                              );
                            }
                          }
                        },
                        child: Text('Mark $status'),
                      ),
                ],
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

  Future<void> _editSupplier(Map<String, dynamic> supplier) async {
    final saved = await showModalBottomSheet<bool>(
      context: context,
      isScrollControlled: true,
      builder: (_) => _AddSupplierSheet(service: _api, initial: supplier),
    );
    if (saved == true) await _load();
  }

  @override
  Widget build(BuildContext context) => Scaffold(
    appBar: AppBar(
      title: const Text('Suppliers'),
      actions: [IconButton(onPressed: _load, icon: const Icon(Icons.refresh))],
      bottom: PreferredSize(
        preferredSize: const Size.fromHeight(116),
        child: Padding(
          padding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
          child: Column(
            children: [
              AppTextField(
                label: 'Search suppliers',
                hint: 'Name, contact, email or phone',
                suffixIcon: const Icon(Icons.search),
                onChanged: (value) => setState(() => _search = value),
              ),
              const SizedBox(height: 8),
              SingleChildScrollView(
                scrollDirection: Axis.horizontal,
                child: Row(
                  children: [
                    for (final status in [
                      'All',
                      'Active',
                      'Inactive',
                      'Suspended',
                    ])
                      Padding(
                        padding: const EdgeInsets.only(right: 8),
                        child: ChoiceChip(
                          label: Text(status),
                          selected: _status == status,
                          showCheckmark: false,
                          selectedColor: AppColors.primary,
                          labelStyle: TextStyle(
                            color: _status == status
                                ? Colors.white
                                : AppColors.textMuted,
                          ),
                          onSelected: (_) => setState(() => _status = status),
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
    ),
    floatingActionButton: widget.canEdit
        ? FloatingActionButton.extended(
            onPressed: _addSupplier,
            icon: const Icon(Icons.add),
            label: const Text('Add supplier'),
          )
        : null,
    body: _loading
        ? const Center(child: CircularProgressIndicator())
        : _error != null
        ? buildwise.ErrorWidget(message: _error!, onRetry: _load)
        : _visibleSuppliers.isEmpty
        ? EmptyStateWidget(
            title: _suppliers.isEmpty
                ? 'No suppliers yet'
                : 'No matching suppliers',
            message: _suppliers.isEmpty
                ? 'Add your first supplier to start recording quotations.'
                : 'Change the search or status filter.',
          )
        : RefreshIndicator(
            onRefresh: _load,
            child: ListView.separated(
              padding: const EdgeInsets.all(16),
              itemCount: _visibleSuppliers.length,
              separatorBuilder: (_, _) => const SizedBox(height: 10),
              itemBuilder: (_, index) => _SupplierCard(
                supplier: _visibleSuppliers[index],
                onTap: () => _openSupplier(_visibleSuppliers[index]),
              ),
            ),
          ),
  );
}

class _SupplierCard extends StatelessWidget {
  const _SupplierCard({required this.supplier, this.onTap});

  final Map<String, dynamic> supplier;
  final VoidCallback? onTap;

  @override
  Widget build(BuildContext context) {
    final status = supplier['status']?.toString() ?? 'Unknown';
    String? field(String key) {
      final value = supplier[key]?.toString().trim() ?? '';
      return value.isEmpty ? null : value;
    }

    final details = [
      field('contactPerson'),
      field('email'),
      field('phone'),
      field('address'),
    ].whereType<String>().toList();

    return AppCard(
      onTap: onTap,
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: [
          Row(
            mainAxisAlignment: MainAxisAlignment.spaceBetween,
            children: [
              Expanded(
                child: Text(
                  supplier['name']?.toString() ?? 'Supplier',
                  style: Theme.of(context).textTheme.titleMedium,
                ),
              ),
              const SizedBox(width: 8),
              StatusChip(
                label: status,
                tone: status == 'Active'
                    ? StatusTone.success
                    : StatusTone.neutral,
              ),
            ],
          ),
          if (details.isNotEmpty) ...[
            const SizedBox(height: 6),
            for (final line in details)
              Text(line, style: Theme.of(context).textTheme.bodySmall),
          ],
        ],
      ),
    );
  }
}

/// Add-supplier sheet: the mobile counterpart of the web `SupplierFormModal`.
class _AddSupplierSheet extends StatefulWidget {
  const _AddSupplierSheet({required this.service, this.initial});

  final ProcurementService service;
  final Map<String, dynamic>? initial;

  @override
  State<_AddSupplierSheet> createState() => _AddSupplierSheetState();
}

class _AddSupplierSheetState extends State<_AddSupplierSheet> {
  final _name = TextEditingController();
  final _contact = TextEditingController();
  final _email = TextEditingController();
  final _phone = TextEditingController();
  final _address = TextEditingController();
  Map<String, String> _errors = {};
  bool _submitting = false;
  String? _formError;

  @override
  void initState() {
    super.initState();
    final data = widget.initial;
    if (data != null) {
      _name.text = data['name']?.toString() ?? '';
      _contact.text = data['contactPerson']?.toString() ?? '';
      _email.text = data['email']?.toString() ?? '';
      _phone.text = data['phone']?.toString() ?? '';
      _address.text = data['address']?.toString() ?? '';
    }
  }

  @override
  void dispose() {
    for (final controller in [_name, _contact, _email, _phone, _address]) {
      controller.dispose();
    }
    super.dispose();
  }

  /// Clear a field's message as soon as the officer retypes in it, so a stale
  /// error never sits under a corrected value.
  void _onChanged(String field) => setState(() {
    if ((_errors[field] ?? '').isNotEmpty) {
      _errors = {..._errors, field: ''};
    }
  });

  String? _errorFor(String field) {
    final message = _errors[field];
    return (message == null || message.isEmpty) ? null : message;
  }

  Future<void> _submit() async {
    final errors = validateSupplierFields(
      name: _name.text,
      contactPerson: _contact.text,
      email: _email.text,
      phone: _phone.text,
      address: _address.text,
    );
    setState(() => _errors = errors);
    if (errors.isNotEmpty) return;

    setState(() {
      _submitting = true;
      _formError = null;
    });
    try {
      final rawPhone = _phone.text.trim();
      if (widget.initial != null) {
        await widget.service.updateSupplier(
          (widget.initial!['id'] as num).toInt(),
          {
            'name': _name.text.trim(),
            'contactPerson': _contact.text.trim(),
            'email': _email.text.trim(),
            'phone': rawPhone.isEmpty ? '' : normalizeSriLankanPhone(rawPhone)!,
            'address': _address.text.trim(),
          },
        );
      } else {
        await widget.service.createSupplier(
          name: _name.text.trim(),
          contactPerson: _contact.text.trim(),
          email: _email.text.trim(),
          phone: rawPhone.isEmpty ? '' : normalizeSriLankanPhone(rawPhone)!,
          address: _address.text.trim(),
        );
      }
      if (mounted) Navigator.of(context).pop(true);
    } catch (e) {
      if (mounted) {
        setState(() => _formError = FieldMessages.friendly(e.toString()));
      }
    } finally {
      if (mounted) setState(() => _submitting = false);
    }
  }

  @override
  Widget build(BuildContext context) => Padding(
    padding: EdgeInsets.only(bottom: MediaQuery.of(context).viewInsets.bottom),
    child: SingleChildScrollView(
      child: Padding(
        padding: const EdgeInsets.all(20),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          mainAxisSize: MainAxisSize.min,
          children: [
            Text(
              widget.initial == null ? 'Add supplier' : 'Edit supplier',
              style: Theme.of(context).textTheme.titleLarge,
            ),
            const SizedBox(height: 4),
            Text(
              'Suppliers are contacted by email; this record is how the '
              'desk reaches them.',
              style: Theme.of(context).textTheme.bodySmall,
            ),
            const SizedBox(height: 16),
            AppTextField(
              label: 'Supplier name',
              controller: _name,
              onChanged: (_) => _onChanged('name'),
              errorText: _errorFor('name'),
              hint: _errorFor('name') == null
                  ? 'Letters, numbers and basic symbols (e.g. Supplier A (Pvt) Ltd)'
                  : null,
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Contact person',
              controller: _contact,
              onChanged: (_) => _onChanged('contactPerson'),
              errorText: _errorFor('contactPerson'),
              hint: _errorFor('contactPerson') == null
                  ? 'Letters only (e.g. Priya Officer)'
                  : null,
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Email',
              controller: _email,
              keyboardType: TextInputType.emailAddress,
              onChanged: (_) => _onChanged('email'),
              errorText: _errorFor('email'),
              hint: _errorFor('email') == null
                  ? 'e.g. supplier@gmail.com'
                  : null,
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Phone',
              controller: _phone,
              keyboardType: TextInputType.phone,
              onChanged: (_) => _onChanged('phone'),
              errorText: _errorFor('phone'),
              hint: _errorFor('phone') == null
                  ? 'Sri Lankan mobile, e.g. 0771234567'
                  : null,
            ),
            const SizedBox(height: 12),
            AppTextField(
              label: 'Address',
              controller: _address,
              maxLines: 2,
              onChanged: (_) => _onChanged('address'),
              errorText: _errorFor('address'),
              hint: _errorFor('address') == null
                  ? 'Letters, numbers and symbols (e.g. 12 Galle Rd, Colombo 03)'
                  : null,
            ),
            if (_formError != null) ...[
              const SizedBox(height: 10),
              Text(_formError!, style: const TextStyle(color: Colors.red)),
            ],
            const SizedBox(height: 16),
            AppButton(
              label: _submitting ? 'Saving…' : 'Add supplier',
              expand: true,
              onPressed: _submitting ? null : _submit,
            ),
            const SizedBox(height: 8),
            AppButton(
              label: 'Cancel',
              expand: true,
              variant: AppButtonVariant.secondary,
              onPressed: () => Navigator.of(context).pop(false),
            ),
          ],
        ),
      ),
    ),
  );
}
