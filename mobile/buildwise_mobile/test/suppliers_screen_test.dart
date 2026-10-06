import 'package:buildwise_mobile/core/widgets/app_button.dart';
import 'package:buildwise_mobile/features/procurement/screens/suppliers_screen.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';

/// The mobile supplier directory must behave exactly like the web
/// `SupplierFormModal`: same endpoint, same field rules, same messages. These
/// tests pin the rules and the payload rather than the layout.
class FakeSupplierService extends ProcurementService {
  List<Map<String, dynamic>> suppliers = [
    {
      'id': 1,
      'name': 'Supplier A',
      'status': 'Active',
      'contactPerson': 'Priya Officer',
      'email': 'priya@gmail.com',
      'phone': '077 123 4567',
      'address': '12 Galle Rd, Colombo 03',
    },
    {'id': 2, 'name': 'Supplier B', 'status': 'Suspended'},
  ];

  Map<String, dynamic>? created;

  @override
  Future<Map<String, dynamic>> listSuppliers({int pageSize = 100}) async => {
    'items': suppliers,
    'total': suppliers.length,
  };

  @override
  Future<Map<String, dynamic>> createSupplier({
    required String name,
    String contactPerson = '',
    String email = '',
    String phone = '',
    String address = '',
  }) async {
    created = {
      'name': name,
      'contactPerson': contactPerson,
      'email': email,
      'phone': phone,
      'address': address,
    };
    return {'id': 3};
  }
}

Future<void> _pumpScreen(
  WidgetTester tester,
  FakeSupplierService service,
) async {
  await tester.pumpWidget(MaterialApp(home: SuppliersScreen(service: service)));
  await tester.pump();
  await tester.pump(const Duration(milliseconds: 300));
}

Future<void> _openAddSheet(WidgetTester tester) async {
  await tester.tap(find.widgetWithText(FloatingActionButton, 'Add supplier'));
  await tester.pumpAndSettle();
}

Future<void> _submitSheet(WidgetTester tester) async {
  await tester.tap(find.widgetWithText(AppButton, 'Add supplier'));
  await tester.pumpAndSettle();
}

Future<void> _fill(WidgetTester tester, String label, String value) async {
  await tester.enterText(find.widgetWithText(TextField, label), value);
  await tester.pump();
}

void main() {
  group('Suppliers directory', () {
    testWidgets('lists the suppliers returned by the API', (tester) async {
      await _pumpScreen(tester, FakeSupplierService());

      expect(find.text('Suppliers'), findsOneWidget);
      expect(find.text('Supplier A'), findsOneWidget);
      expect(find.text('Supplier B'), findsOneWidget);
      expect(find.text('Active'), findsNWidgets(2));
      expect(find.text('priya@gmail.com'), findsOneWidget);
    });

    testWidgets('search and status filters work together and can be cleared', (
      tester,
    ) async {
      await _pumpScreen(tester, FakeSupplierService());
      await _fill(tester, 'Search suppliers', 'priya');
      expect(find.text('Supplier A'), findsOneWidget);
      expect(find.text('Supplier B'), findsNothing);
      await tester.tap(find.widgetWithText(ChoiceChip, 'Suspended'));
      await tester.pumpAndSettle();
      expect(find.text('No matching suppliers'), findsOneWidget);
      await _fill(tester, 'Search suppliers', '');
      expect(find.text('Supplier B'), findsOneWidget);
      expect(find.text('Supplier A'), findsNothing);
      await tester.tap(find.widgetWithText(ChoiceChip, 'All'));
      await tester.pumpAndSettle();
      expect(find.text('Supplier A'), findsOneWidget);
      expect(find.text('Supplier B'), findsOneWidget);
    });

    testWidgets('shows the empty state when no suppliers exist', (
      tester,
    ) async {
      final service = FakeSupplierService()..suppliers = [];
      await _pumpScreen(tester, service);

      expect(find.text('No suppliers yet'), findsOneWidget);
    });
  });

  group('Add supplier validation mirrors the web form', () {
    testWidgets('requires a supplier name', (tester) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _submitSheet(tester);

      expect(find.text('Supplier name is required.'), findsOneWidget);
      expect(service.created, isNull);
    });

    testWidgets('rejects a name made only of numbers', (tester) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _fill(tester, 'Supplier name', '12345');
      await _submitSheet(tester);

      expect(
        find.textContaining('it cannot be only numbers or symbols'),
        findsOneWidget,
      );
      expect(service.created, isNull);
    });

    testWidgets('rejects a contact person containing digits', (tester) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _fill(tester, 'Supplier name', 'Supplier A');
      await _fill(tester, 'Contact person', 'Priya 007');
      await _submitSheet(tester);

      expect(
        find.textContaining('Contact person must contain letters only'),
        findsOneWidget,
      );
      expect(service.created, isNull);
    });

    testWidgets('rejects a malformed email and points at the gmail example', (
      tester,
    ) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _fill(tester, 'Supplier name', 'Supplier A');
      await _fill(tester, 'Email', 'sales@supplierade');
      await _submitSheet(tester);

      expect(find.textContaining('supplier@gmail.com'), findsOneWidget);
      expect(service.created, isNull);
    });

    testWidgets('rejects a phone that is not a Sri Lankan mobile', (
      tester,
    ) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _fill(tester, 'Supplier name', 'Supplier A');
      await _fill(tester, 'Phone', '5551234');
      await _submitSheet(tester);

      expect(
        find.textContaining('10-digit Sri Lankan mobile number'),
        findsOneWidget,
      );
      expect(service.created, isNull);
    });

    testWidgets('rejects an address made only of symbols', (tester) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _fill(tester, 'Supplier name', 'Supplier A');
      await _fill(tester, 'Address', '###');
      await _submitSheet(tester);

      expect(
        find.textContaining(
          'Address must contain letters, numbers and symbols',
        ),
        findsOneWidget,
      );
      expect(service.created, isNull);
    });

    testWidgets('saves a valid supplier and normalises the phone number', (
      tester,
    ) async {
      final service = FakeSupplierService();
      await _pumpScreen(tester, service);
      await _openAddSheet(tester);

      await _fill(tester, 'Supplier name', 'Supplier A (Pvt) Ltd');
      await _fill(tester, 'Contact person', 'Priya Officer');
      await _fill(tester, 'Email', 'salesdemo@gmail.com');
      await _fill(tester, 'Phone', '0771234567');
      await _fill(tester, 'Address', '12 Galle Rd, Colombo 03');
      await _submitSheet(tester);

      expect(service.created, isNotNull);
      expect(service.created!['name'], 'Supplier A (Pvt) Ltd');
      expect(service.created!['contactPerson'], 'Priya Officer');
      expect(service.created!['email'], 'salesdemo@gmail.com');
      // The TRCSL number is stored in its display form, as on the web.
      expect(service.created!['phone'], '077 123 4567');
    });
  });

  group('validateSupplierFields rules', () {
    test('passes a clean record with no field errors', () {
      expect(
        validateSupplierFields(
          name: 'Supplier B',
          contactPerson: 'Jane Doe',
          email: 'jane@gmail.com',
          phone: '0751234567',
          address: 'No. 4, Kandy Rd',
        ),
        isEmpty,
      );
    });

    test('allows empty optional fields', () {
      expect(validateSupplierFields(name: 'Supplier B'), isEmpty);
    });

    test('flags every optional field that is filled in wrongly', () {
      final errors = validateSupplierFields(
        name: 'Supplier B',
        contactPerson: 'J0hn',
        email: 'not-an-email@',
        phone: '123',
        address: '***',
      );
      expect(
        errors.keys,
        containsAll(['contactPerson', 'email', 'phone', 'address']),
      );
    });
  });

  group('normalizeSriLankanPhone', () {
    test('accepts 07X, +94 and 0094 forms and formats them alike', () {
      expect(normalizeSriLankanPhone('0771234567'), '077 123 4567');
      expect(normalizeSriLankanPhone('+94771234567'), '077 123 4567');
      expect(normalizeSriLankanPhone('0094 77 123 4567'), '077 123 4567');
    });

    test('rejects a number outside the TRCSL mobile plan', () {
      expect(normalizeSriLankanPhone('5551234'), isNull);
      expect(normalizeSriLankanPhone('0112345678'), isNull);
    });
  });
}
