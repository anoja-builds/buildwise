import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_status_service.dart';
import 'package:buildwise_mobile/features/procurement/screens/material_request_procurement_view.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          (call) async => call.method == 'read' ? 'site-token' : null,
        );
  });
  test('uses the secured status route and parses the confirmed PO', () async {
    final client = MockClient((request) async {
      expect(request.url.path, '/api/material-requests/42/procurement-status');
      expect(request.headers['Authorization'], 'Bearer site-token');
      return http.Response(
        jsonEncode({
          'materialRequestId': 42,
          'status': 'PurchaseOrderCreated',
          'purchaseOrderId': 73,
          'purchaseOrderStatus': 'Confirmed',
        }),
        200,
      );
    });
    addTearDown(client.close);
    final info = await ProcurementStatusService(
      apiClient: ApiClient(client: client),
    ).getStatus(42);
    expect(info.status, ProcurementStatus.purchaseOrderCreated);
    expect(info.purchaseOrderStatus, 'Confirmed');
  });
  for (final code in [401, 403, 500]) {
    testWidgets('HTTP $code is visible and never becomes a fake status', (
      tester,
    ) async {
      final client = MockClient((_) async => http.Response('failure', code));
      addTearDown(client.close);
      await tester.pumpWidget(
        MaterialApp(
          home: Scaffold(
            body: MaterialRequestProcurementView(
              materialRequestId: 42,
              service: ProcurementStatusService(
                apiClient: ApiClient(client: client),
              ),
            ),
          ),
        ),
      );
      await tester.pumpAndSettle();
      expect(find.text('Could not load procurement status'), findsOneWidget);
      expect(find.textContaining('($code)'), findsOneWidget);
      expect(find.text('Procurement not started'), findsNothing);
    });
  }
  test('unknown statuses fail instead of showing not started', () {
    expect(
      () => ProcurementStatus.fromApi('Unexpected'),
      throwsFormatException,
    );
  });
  testWidgets('refresh replaces awaiting approval with the confirmed PO', (
    tester,
  ) async {
    var calls = 0;
    final client = MockClient(
      (_) async => http.Response(
        jsonEncode({
          'materialRequestId': 42,
          'status': ++calls == 1 ? 'AwaitingApproval' : 'PurchaseOrderCreated',
          'purchaseOrderId': calls == 1 ? null : 73,
          'purchaseOrderStatus': calls == 1 ? null : 'Confirmed',
        }),
        200,
      ),
    );
    addTearDown(client.close);
    await tester.pumpWidget(
      MaterialApp(
        home: Scaffold(
          body: MaterialRequestProcurementView(
            materialRequestId: 42,
            service: ProcurementStatusService(
              apiClient: ApiClient(client: client),
            ),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();
    expect(find.text('Awaiting manager approval'), findsOneWidget);
    await tester.tap(find.text('Refresh status'));
    await tester.pumpAndSettle();
    expect(find.text('Purchase Order Created (PO #73)'), findsOneWidget);
    expect(find.text('Order status: Confirmed'), findsOneWidget);
    expect(find.textContaining('Supplier'), findsNothing);
  });
}
