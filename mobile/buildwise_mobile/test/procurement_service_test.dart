import 'dart:convert';

import 'package:flutter/services.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';
import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_service.dart';

void main() {
  TestWidgetsFlutterBinding.ensureInitialized();
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          const MethodChannel('plugins.it_nomads.com/flutter_secure_storage'),
          (call) async => call.method == 'read' ? 'test-session' : null,
        );
  });

  test(
    'loads all pages from the secured current purchase order endpoint',
    () async {
      final pages = <int>[];
      final client = MockClient((request) async {
        expect(request.url.path, '/api/purchase-orders');
        expect(request.headers['Authorization'], 'Bearer test-session');
        expect(request.url.queryParameters['pageSize'], '50');
        final page = int.parse(request.url.queryParameters['page']!);
        pages.add(page);
        return http.Response(
          jsonEncode({
            'items': [
              {
                'id': page,
                'materialRequestId': 42,
                'supplierName': 'Supplier',
                'status': 'Confirmed',
                'totalAmount': 500,
              },
            ],
            'total': 2,
            'page': page,
            'pageSize': 50,
          }),
          200,
        );
      });
      addTearDown(client.close);
      final orders = await ProcurementService(
        apiClient: ApiClient(client: client),
      ).getPurchaseOrders();
      expect(pages, [1, 2]);
      expect(orders.map((order) => order['id']), [1, 2]);
      expect(orders.first['materialRequestId'], 42);
    },
  );

  test('accepts an empty paginated result', () async {
    final client = MockClient(
      (_) async =>
          http.Response('{"items":[],"total":0,"page":1,"pageSize":50}', 200),
    );
    addTearDown(client.close);
    expect(
      await ProcurementService(apiClient: ApiClient(client: client))
          .getPurchaseOrders(),
      isEmpty,
    );
  });

  for (final status in [401, 403, 500]) {
    test('surfaces HTTP $status instead of returning an empty list', () async {
      final client = MockClient((_) async => http.Response('failure', status));
      addTearDown(client.close);
      await expectLater(
        ProcurementService(apiClient: ApiClient(client: client))
            .getPurchaseOrders(),
        throwsException,
      );
    });
  }

  test(
    'rejects an incomplete page instead of looping or silently truncating',
    () async {
      final client = MockClient(
        (_) async => http.Response('{"items":[],"total":2}', 200),
      );
      addTearDown(client.close);
      await expectLater(
        ProcurementService(apiClient: ApiClient(client: client))
            .getPurchaseOrders(),
        throwsFormatException,
      );
    },
  );
}
