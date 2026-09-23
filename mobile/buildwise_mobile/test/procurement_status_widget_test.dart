import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/features/procurement/services/procurement_status_service.dart';
import 'package:buildwise_mobile/features/procurement/widgets/procurement_status_widget.dart';

Widget _wrap(Widget child) => MaterialApp(home: Scaffold(body: child));

void main() {
  testWidgets('shows "Quotations in progress" and no supplier/price detail', (
    WidgetTester tester,
  ) async {
    const info = ProcurementStatusInfo(
      materialRequestId: 101,
      status: ProcurementStatus.quotationsInProgress,
    );

    await tester.pumpWidget(_wrap(const ProcurementStatusWidget(info: info)));

    expect(find.text('Quotations in progress'), findsOneWidget);
    // Spec §9.1: never show supplier names or prices on the Site Engineer's
    // Flutter status view.
    expect(find.textContaining('Supplier'), findsNothing);
    expect(find.textContaining('₹'), findsNothing);
  });

  testWidgets('shows "Awaiting manager approval" for AwaitingApproval', (
    WidgetTester tester,
  ) async {
    const info = ProcurementStatusInfo(
      materialRequestId: 101,
      status: ProcurementStatus.awaitingApproval,
    );

    await tester.pumpWidget(_wrap(const ProcurementStatusWidget(info: info)));

    expect(find.text('Awaiting manager approval'), findsOneWidget);
  });

  testWidgets('shows the PO number for PurchaseOrderCreated', (
    WidgetTester tester,
  ) async {
    const info = ProcurementStatusInfo(
      materialRequestId: 101,
      status: ProcurementStatus.purchaseOrderCreated,
      purchaseOrderId: 7001,
    );

    await tester.pumpWidget(_wrap(const ProcurementStatusWidget(info: info)));

    expect(find.text('Purchase Order Created (PO #7001)'), findsOneWidget);
  });
}
