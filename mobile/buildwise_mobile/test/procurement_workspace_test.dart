import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/core/theme/app_theme.dart';
import 'package:buildwise_mobile/features/procurement/models/procurement_models.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_service.dart';
import 'package:buildwise_mobile/features/procurement/screens/supplier_screens.dart';
import 'package:buildwise_mobile/features/procurement/screens/quotation_comparison_screen.dart';
import 'package:buildwise_mobile/features/procurement/screens/procurement_workflow_screen.dart';
import 'package:buildwise_mobile/features/procurement/screens/po_list_screen.dart';

// Fixtures live only in tests; their deliberately non-demo IDs verify navigation.
const supplier = {
  'id': 87,
  'name': 'Test supplier',
  'status': 'Active',
  'contactPerson': 'Test contact',
  'email': 'fixture@example.test',
  'phone': '012345',
  'address': 'Test address',
  'quotationHistory': <dynamic>[],
};
const quote = {
  'id': 94,
  'supplierId': 87,
  'supplierName': 'Test supplier',
  'supplierStatus': 'Active',
  'totalAmount': 9876.54,
  'status': 'Submitted',
  'validUntil': '2026-11-01',
};
const order = {
  'id': 203,
  'supplierId': 87,
  'supplierName': 'Test supplier',
  'materialRequestId': 731,
  'status': 'Confirmed',
  'totalAmount': 9876.54,
  'items': [
    {
      'materialName': 'Test cement',
      'orderedQuantity': 20,
      'unit': 'bags',
      'unitPrice': 400,
      'lineTotal': 8000,
    },
  ],
};
Map<String, dynamic> workflow(String decision, {int? po}) => {
  'id': 654,
  'materialRequestId': 731,
  'status': decision == 'Pending' ? 'AwaitingApproval' : 'Completed',
  'approvalStatus': decision,
  'purchaseOrderId': po,
  'updatedAt': '2026-09-30',
  'steps': <dynamic>[],
  'recommendation': {
    'recommendedSupplierName': 'Test supplier',
    'recommendedQuotationId': 94,
    'rationale': 'Public advisory summary',
    'warnings': <dynamic>[],
  },
};
http.Response json(Object? data) => http.Response(
  jsonEncode(data),
  200,
  headers: {'content-type': 'application/json'},
);

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          (call) async => call.method == 'read' ? 'test-jwt' : null,
        ),
  );
  tearDown(
    () => TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          null,
        ),
  );
  ProcurementService service(
    Future<http.Response> Function(http.Request) handler,
  ) {
    final client = MockClient((request) {
      expect(request.headers['Authorization'], 'Bearer test-jwt');
      return handler(request);
    });
    addTearDown(client.close);
    return ProcurementService(apiClient: ApiClient(client: client));
  }

  test(
    'supplier search, detail and quotation history use API fields',
    () async {
      final api = service((request) async {
        if (request.url.path.endsWith('/87')) {
          return json({
            ...supplier,
            'quotationHistory': [
              {
                'quotationId': 94,
                'materialRequestId': 731,
                'totalAmount': 9876.54,
              },
            ],
          });
        }
        expect(request.url.queryParameters['search'], 'Test & Co');
        expect(request.url.queryParameters['status'], 'Active');
        return json({
          'items': [supplier],
          'total': 1,
        });
      });
      expect(
        (await api.getSuppliers(
          search: 'Test & Co',
          status: 'Active',
        )).single.id,
        87,
      );
      final detail = await api.getSupplier(87);
      expect(detail.address, 'Test address');
      expect(detail.quotations.single['quotationId'], 94);
    },
  );
  test('comparison preserves server totals and coverage flags', () async {
    final api = service((request) async {
      expect(request.url.path, '/api/material-requests/731/quotations/compare');
      return json({
        'materialRequestId': 731,
        'projectName': 'Test site',
        'quotations': [quote],
        'rows': [
          {
            'offers': [
              {'quotationId': 94, 'coversFullQuantity': false},
            ],
          },
        ],
      });
    });
    final data = await api.compare(731);
    expect(data.quotations.single['totalAmount'], 9876.54);
    expect(data.fullyCovered(94), isFalse);
  });
  test(
    'workflow launch uses selected request and never supplies an actor ID',
    () async {
      final api = service((request) async {
        expect(request.method, 'POST');
        expect(
          request.url.path,
          '/api/material-requests/731/procurement-workflow',
        );
        expect(jsonDecode(request.body), isEmpty);
        return json({'workflowId': 654});
      });
      expect(await api.startWorkflow(731), 654);
    },
  );
  test('missing workflow is a real empty state', () async {
    final api = service((_) async => http.Response('', 204));
    expect(await api.latestWorkflow(731), isNull);
  });
  for (final entry in {
    'Pending': 'Awaiting Manager Review',
    'Approved': 'Approved',
    'Rejected': 'Rejected',
    'RevisionRequested': 'Revision Requested',
  }.entries) {
    test(
      'maps ${entry.key} decision',
      () => expect(
        ProcurementWorkflow.fromJson(workflow(entry.key)).displayStatus,
        entry.value,
      ),
    );
    for (final width in [360.0, 390.0, 412.0]) {
      testWidgets(
        '${entry.key} status fits $width and cannot make final decisions',
        (tester) async {
          tester.view.physicalSize = Size(width, 844);
          tester.view.devicePixelRatio = 1;
          addTearDown(tester.view.resetPhysicalSize);
          addTearDown(tester.view.resetDevicePixelRatio);
          final api = service(
            (request) async => request.url.path.endsWith('/quotations')
                ? json([quote])
                : json(
                    workflow(
                      entry.key,
                      po: entry.key == 'Approved' ? 203 : null,
                    ),
                  ),
          );
          await tester.pumpWidget(
            MaterialApp(
              theme: AppTheme.light,
              home: ProcurementWorkflowScreen(
                requestId: 731,
                service: api,
                manager: true,
                statusOnly: true,
              ),
            ),
          );
          await tester.pumpAndSettle();
          expect(find.text(entry.value), findsWidgets);
          expect(find.text('Approve'), findsNothing);
          expect(find.text('Reject'), findsNothing);
          expect(find.text('Request Revision'), findsNothing);
          await tester.drag(find.byType(ListView), const Offset(0, -800));
          await tester.pumpAndSettle();
          expect(
            find.text(
              entry.key == 'Approved'
                  ? 'Purchase Order Created: PO-0203'
                  : 'No Purchase Order Created',
            ),
            findsOneWidget,
          );
          expect(tester.takeException(), isNull);
        },
      );
    }
  }
  testWidgets('supplier list opens the selected API supplier', (tester) async {
    final paths = <String>[];
    final api = service((request) async {
      paths.add(request.url.path);
      if (request.url.path.endsWith('/suppliers/87')) return json(supplier);
      return json({
        'items': request.url.path.endsWith('/suppliers') ? [supplier] : [],
        'total': request.url.path.endsWith('/suppliers') ? 1 : 0,
      });
    });
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: SuppliersScreen(service: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('View Supplier →'));
    await tester.pumpAndSettle();
    expect(paths, contains('/api/suppliers/87'));
    expect(find.text('Test address'), findsOneWidget);
  });
  testWidgets('PO list opens details with server line totals', (tester) async {
    final api = service(
      (request) async => request.url.path.endsWith('/203')
          ? json(order)
          : json({
              'items': [order],
              'total': 1,
            }),
    );
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: PoListScreen(service: api),
      ),
    );
    await tester.pumpAndSettle();
    await tester.tap(find.text('View Order Details →'));
    await tester.pumpAndSettle();
    expect(find.text('Test cement'), findsOneWidget);
    expect(find.text('LKR 8,000.00'), findsOneWidget);
    expect(find.text('MR-0731'), findsOneWidget);
  });
  testWidgets('manager comparison is read only', (tester) async {
    final api = service(
      (_) async => json({
        'materialRequestId': 731,
        'projectName': 'Test site',
        'quotations': [quote],
        'rows': <dynamic>[],
      }),
    );
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: QuotationComparisonScreen(
          requestId: 731,
          service: api,
          manager: true,
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Run AI Evaluation'), findsNothing);
    expect(find.text('LKR 9,876.54'), findsOneWidget);
  });
  testWidgets(
    'refresh reads the React decision without a mobile decision POST',
    (tester) async {
      var decision = 'Pending';
      var reads = 0;
      final api = service((request) async {
        expect(request.method, 'GET');
        if (request.url.path.endsWith('/quotations')) return json([quote]);
        reads++;
        return json(workflow(decision));
      });
      await tester.pumpWidget(
        MaterialApp(
          theme: AppTheme.light,
          home: ProcurementWorkflowScreen(
            requestId: 731,
            service: api,
            manager: true,
            statusOnly: true,
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Awaiting Manager Review'), findsWidgets);
      decision = 'Rejected';
      await tester.scrollUntilVisible(find.text('Refresh status'), 200);
      await tester.tap(find.text('Refresh status'));
      await tester.pumpAndSettle();
      expect(reads, 2);
      await tester.drag(find.byType(ListView), const Offset(0, 800));
      await tester.pumpAndSettle();
      expect(find.text('Rejected'), findsWidgets);
    },
  );
}
