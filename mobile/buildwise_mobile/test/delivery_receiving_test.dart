import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/features/operations/screens/delivery_receiving_screen.dart';
import 'package:buildwise_mobile/features/operations/services/operations_service.dart';

class MockDeliveryOperationsService extends OperationsService {
  List<Map<String, dynamic>> mockOrders = [
    {
      'id': 44,
      'supplierName': 'Supplier A',
      'items': [
        {
          'id': 101,
          'materialId': 1,
          'materialName': 'Cement (50kg bag)',
          'orderedQuantity': 450,
          'unit': 'bag',
        },
        {
          'id': 102,
          'materialId': 2,
          'materialName': 'River Sand',
          'orderedQuantity': 100,
          'unit': 'cube',
        },
      ],
    },
  ];

  List<Map<String, dynamic>> mockDeliveries = [
    {
      'id': 42,
      'purchaseOrderId': 44,
      'deliveryReference': 'INV-9081',
      'status': 'Received',
      'items': [
        {'materialId': 1, 'receivedQuantity': 450, 'damagedQuantity': 0},
      ],
    },
  ];

  bool recordCalled = false;
  Map<String, dynamic>? lastPayload;

  @override
  Future<List<Map<String, dynamic>>> listConfirmedOrders() async => mockOrders;

  @override
  Future<List<Map<String, dynamic>>> listDeliveries() async => mockDeliveries;

  @override
  Future<Map<String, dynamic>> recordDelivery({
    required int purchaseOrderId,
    required String reference,
    required List<Map<String, dynamic>> items,
    List<Map<String, dynamic>> evidence = const [],
  }) async {
    recordCalled = true;
    lastPayload = {
      'purchaseOrderId': purchaseOrderId,
      'reference': reference,
      'items': items,
      'evidence': evidence,
    };
    return {'id': 43, 'status': 'Received'};
  }
}

void main() {
  group('DeliveryReceivingScreen Multi-Item & Evidence Tests', () {
    testWidgets('Renders confirmed PO dropdown and recorded deliveries', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);

      final mockService = MockDeliveryOperationsService();

      await tester.pumpWidget(
        MaterialApp(
          home: DeliveryReceivingScreen(
            autoRefresh: false,
            service: mockService,
          ),
        ),
      );

      await tester.pumpAndSettle();

      expect(find.text('Delivery Receiving'), findsOneWidget);
      expect(find.text('Confirmed Purchase Order'), findsOneWidget);
      expect(find.text('Recorded Deliveries'), findsOneWidget);
      expect(find.text('DEL-42'), findsOneWidget);
      expect(find.text('Run AI Analysis'), findsOneWidget);
    });

    testWidgets(
      'Selecting PO renders all line items with ordered quantity and evidence picker',
      (tester) async {
        tester.view.physicalSize = const Size(1080, 2400);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(tester.view.resetPhysicalSize);

        final mockService = MockDeliveryOperationsService();

        await tester.pumpWidget(
          MaterialApp(
            home: DeliveryReceivingScreen(
              autoRefresh: false,
              service: mockService,
            ),
          ),
        );

        await tester.pumpAndSettle();

        await tester.tap(find.byType(DropdownButtonFormField<String>));
        await tester.pumpAndSettle();

        await tester.tap(find.text('44').last);
        await tester.pumpAndSettle();

        expect(find.text('PO-44'), findsOneWidget);
        expect(find.text('Supplier A'), findsOneWidget);
        expect(find.text('Cement (50kg bag)'), findsOneWidget);
        expect(find.text('Ordered: 450 bag'), findsOneWidget);
        expect(find.text('River Sand'), findsOneWidget);
        expect(find.text('Ordered: 100 cube'), findsOneWidget);

        expect(find.text('Delivery Photo Evidence'), findsOneWidget);
        expect(find.text('📷 Take Photo'), findsOneWidget);
        expect(find.text('📁 Gallery'), findsOneWidget);
        expect(find.text('Submit Receiving'), findsOneWidget);
      },
    );

    testWidgets(
      'Submitting multi-line receiving packages all item quantities',
      (tester) async {
        tester.view.physicalSize = const Size(1080, 2400);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(tester.view.resetPhysicalSize);

        final mockService = MockDeliveryOperationsService();

        await tester.pumpWidget(
          MaterialApp(
            home: DeliveryReceivingScreen(
              autoRefresh: false,
              service: mockService,
            ),
          ),
        );

        await tester.pumpAndSettle();

        await tester.tap(find.byType(DropdownButtonFormField<String>));
        await tester.pumpAndSettle();

        await tester.tap(find.text('44').last);
        await tester.pumpAndSettle();

        await tester.tap(find.text('Submit Receiving'));
        await tester.pumpAndSettle();

        expect(mockService.recordCalled, isTrue);
        expect(mockService.lastPayload?['purchaseOrderId'], equals(44));
        expect(mockService.lastPayload?['items'], isA<List>());
        final items = mockService.lastPayload?['items'] as List;
        expect(items.length, equals(2));
        expect(items[0]['materialId'], equals(1));
        expect(items[0]['receivedQuantity'], equals(450.0));
        expect(items[1]['materialId'], equals(2));
        expect(items[1]['receivedQuantity'], equals(100.0));
      },
    );
  });
}
