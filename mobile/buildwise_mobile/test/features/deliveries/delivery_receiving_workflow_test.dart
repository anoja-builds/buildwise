import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/features/deliveries/screens/receive_delivery_screen.dart';

void main() {
  final sampleDelivery = {
    'id': 101,
    'deliveryReference': 'DEL-2026-001',
    'purchaseOrderId': 42,
    'status': 'InProgress',
    'items': [
      {
        'purchaseOrderItemId': 1,
        'materialName': 'Portland Composite Cement (50kg)',
        'materialUnit': 'Bags',
        'orderedQuantity': 250.0,
      }
    ]
  };

  Widget createWidgetUnderTest() {
    return MaterialApp(
      home: ReceiveDeliveryScreen(delivery: sampleDelivery),
    );
  }

  testWidgets('ReceiveDeliveryScreen displays delivery details and initial quantities',
      (WidgetTester tester) async {
    await tester.pumpWidget(createWidgetUnderTest());
    await tester.pumpAndSettle();

    expect(find.text('Receive: DEL-2026-001'), findsOneWidget);
    expect(find.text('PO #42'), findsOneWidget);
    expect(find.text('Portland Composite Cement (50kg)'), findsOneWidget);
    expect(find.text('Ordered: 250.0 Bags'), findsOneWidget);
    expect(find.text('Verify & Save'), findsOneWidget);
  });

  testWidgets('ReceiveDeliveryScreen rejects damaged quantity exceeding received quantity',
      (WidgetTester tester) async {
    await tester.pumpWidget(createWidgetUnderTest());
    await tester.pumpAndSettle();

    // Enter received qty = 100, damaged qty = 150
    final textFields = find.byType(TextFormField);
    expect(textFields, findsNWidgets(2));

    await tester.enterText(textFields.at(0), '100');
    await tester.enterText(textFields.at(1), '150');
    await tester.pumpAndSettle();

    await tester.tap(find.text('Verify & Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Damaged quantity cannot exceed received quantity'), findsOneWidget);
  });

  testWidgets('ReceiveDeliveryScreen rejects negative quantities',
      (WidgetTester tester) async {
    await tester.pumpWidget(createWidgetUnderTest());
    await tester.pumpAndSettle();

    final textFields = find.byType(TextFormField);
    await tester.enterText(textFields.at(0), '-5');
    await tester.pumpAndSettle();

    await tester.tap(find.text('Verify & Save'));
    await tester.pumpAndSettle();

    expect(find.textContaining('Quantities cannot be negative'), findsOneWidget);
  });
}
