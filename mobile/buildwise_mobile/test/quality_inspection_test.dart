import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:buildwise_mobile/features/operations/screens/quality_inspection_screen.dart';
import 'package:buildwise_mobile/features/operations/services/operations_service.dart';

class MockQualityOperationsService extends OperationsService {
  List<Map<String, dynamic>> mockDeliveries = [
    {
      'id': 34,
      'deliveryReference': 'INV-9081',
      'items': [
        {
          'materialId': 1,
          'materialName': 'Cement (50kg bag)',
          'receivedQuantity': 240,
        },
      ],
    },
  ];

  List<Map<String, dynamic>> mockInspections = [
    {
      'id': 34,
      'deliveryId': 33,
      'overallDecision': 'PartiallyAccepted',
      'status': 'Completed',
      'quantityCheck': true,
      'visualConditionCheck': false,
      'moistureCheck': false,
      'packagingCheck': false,
      'defectsCheck': true,
      'items': [
        {
          'materialId': 1,
          'inspectedQuantity': 240,
          'acceptedQuantity': 235,
          'rejectedQuantity': 5,
        },
      ],
    },
  ];

  List<Map<String, dynamic>> mockNcrs = [
    {
      'id': 35,
      'ncrNumber': 'NCR-781611',
      'severity': 'Medium',
      'status': 'CorrectiveActionRequired',
      'materialName': 'Cement (50kg bag)',
      'issueDescription': 'Water damage during transport.',
      'correctiveAction': 'Issue credit note or replacement.',
      'createdAt': '2026-09-28T08:32:42Z',
    },
  ];

  bool inspectionCreated = false;
  Map<String, dynamic>? lastInspectionPayload;

  @override
  Future<List<Map<String, dynamic>>> listDeliveries() async => mockDeliveries;

  @override
  Future<List<Map<String, dynamic>>> listInspections({String? status}) async =>
      mockInspections;

  @override
  Future<List<Map<String, dynamic>>> listNonConformances() async => mockNcrs;

  @override
  Future<Map<String, dynamic>> createInspection({
    required int deliveryId,
    required int materialId,
    required double inspected,
    required double accepted,
    required double rejected,
    required String reason,
    required bool quantityCheck,
    required bool visualConditionCheck,
    required bool moistureCheck,
    required bool packagingCheck,
    required bool defectsCheck,
    String? criteria,
    String? observedResult,
    String? notes,
    List<Map<String, dynamic>> evidence = const [],
  }) async {
    inspectionCreated = true;
    lastInspectionPayload = {
      'deliveryId': deliveryId,
      'materialId': materialId,
      'inspected': inspected,
      'accepted': accepted,
      'rejected': rejected,
      'reason': reason,
      'quantityCheck': quantityCheck,
      'visualConditionCheck': visualConditionCheck,
      'moistureCheck': moistureCheck,
      'packagingCheck': packagingCheck,
      'defectsCheck': defectsCheck,
      'evidence': evidence,
    };
    return {
      'id': 36,
      'overallDecision': rejected > 0 ? 'PartiallyAccepted' : 'Accepted',
    };
  }
}

void main() {
  group('QualityInspectionScreen 5-Point Checklist & NCR Tests', () {
    testWidgets(
      'selects another delivery material and preserves fractional accepted quantity',
      (tester) async {
        tester.view.physicalSize = const Size(1080, 2400);
        tester.view.devicePixelRatio = 1;
        addTearDown(tester.view.reset);
        final service = MockQualityOperationsService();
        (service.mockDeliveries.first['items'] as List).add({
          'materialId': 2,
          'materialName': 'Steel 12mm',
          'receivedQuantity': 20,
        });
        await tester.pumpWidget(
          MaterialApp(home: QualityInspectionScreen(service: service)),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.byType(DropdownButtonFormField<String>));
        await tester.pumpAndSettle();
        await tester.tap(find.text('34').last);
        await tester.pumpAndSettle();
        await tester.tap(
          find.widgetWithText(DropdownButtonFormField<String>, 'Material'),
        );
        await tester.pumpAndSettle();
        await tester.tap(find.text('Steel 12mm').last);
        await tester.pumpAndSettle();
        await tester.enterText(
          find.widgetWithText(TextField, 'Inspected Quantity'),
          '10.125',
        );
        await tester.ensureVisible(find.text('Submit Inspection'));
        await tester.pumpAndSettle();
        await tester.tap(find.text('Submit Inspection'));
        await tester.pumpAndSettle();
        expect(service.lastInspectionPayload?['materialId'], 2);
        expect(service.lastInspectionPayload?['accepted'], 10.125);
      },
    );
    testWidgets('Renders quality stats, inspection history and active NCRs', (
      tester,
    ) async {
      tester.view.physicalSize = const Size(1080, 2400);
      tester.view.devicePixelRatio = 1.0;
      addTearDown(tester.view.resetPhysicalSize);

      final mockService = MockQualityOperationsService();

      await tester.pumpWidget(
        MaterialApp(home: QualityInspectionScreen(service: mockService)),
      );

      await tester.pumpAndSettle();

      expect(find.text('Quality Inspections'), findsOneWidget);
      expect(find.text('Open NCRs'), findsOneWidget);
      expect(find.text('High/Critical'), findsOneWidget);
      expect(find.text('INS-34'), findsOneWidget);
      expect(find.textContaining('Partially'), findsOneWidget);
      expect(find.text('NCR-781611'), findsOneWidget);
    });

    testWidgets(
      'Selecting delivery opens 5-point checklist with PASS/FAIL switches',
      (tester) async {
        tester.view.physicalSize = const Size(1080, 2400);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(tester.view.resetPhysicalSize);

        final mockService = MockQualityOperationsService();

        await tester.pumpWidget(
          MaterialApp(home: QualityInspectionScreen(service: mockService)),
        );

        await tester.pumpAndSettle();

        await tester.tap(find.byType(DropdownButtonFormField<String>));
        await tester.pumpAndSettle();

        await tester.tap(find.text('34').last);
        await tester.pumpAndSettle();

        expect(find.text('5-Point Quality Checklist'), findsOneWidget);
        expect(find.text('Quantity'), findsOneWidget);
        expect(find.text('Visual condition'), findsOneWidget);
        expect(find.text('Moisture'), findsOneWidget);
        expect(find.text('Packaging'), findsOneWidget);
        expect(find.text('Defects'), findsOneWidget);
        expect(find.text('PASS'), findsNWidgets(5));

        expect(find.text('Inspection Photo Evidence'), findsOneWidget);
        expect(find.text('📷 Take Photo'), findsOneWidget);
        expect(find.text('📁 Gallery'), findsOneWidget);
        expect(find.text('Submit Inspection'), findsOneWidget);
      },
    );

    testWidgets(
      'Submitting inspection sends checklist, quantities and evidence payload',
      (tester) async {
        tester.view.physicalSize = const Size(1080, 2400);
        tester.view.devicePixelRatio = 1.0;
        addTearDown(tester.view.resetPhysicalSize);

        final mockService = MockQualityOperationsService();

        await tester.pumpWidget(
          MaterialApp(home: QualityInspectionScreen(service: mockService)),
        );

        await tester.pumpAndSettle();

        await tester.tap(find.byType(DropdownButtonFormField<String>));
        await tester.pumpAndSettle();

        await tester.tap(find.text('34').last);
        await tester.pumpAndSettle();

        await tester.tap(find.text('Submit Inspection'));
        await tester.pumpAndSettle();

        expect(mockService.inspectionCreated, isTrue);
        expect(mockService.lastInspectionPayload?['deliveryId'], equals(34));
        expect(mockService.lastInspectionPayload?['inspected'], equals(240.0));
        expect(mockService.lastInspectionPayload?['accepted'], equals(240.0));
        expect(mockService.lastInspectionPayload?['rejected'], equals(0.0));
        expect(mockService.lastInspectionPayload?['quantityCheck'], isTrue);
      },
    );
  });
}
