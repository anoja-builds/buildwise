import 'dart:async';
import 'dart:convert';

import 'package:buildwise_mobile/core/theme/app_theme.dart';
import 'package:buildwise_mobile/features/quality/screens/pending_inspections_screen.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  http.Response deliveries({
    Object status = 4,
    String? reference = 'DEL-007',
  }) => http.Response(
    jsonEncode([
      {
        'deliveryId': 7,
        'deliveryReference': reference,
        'status': status,
        'items': [
          {
            'deliveryItemId': 9,
            'purchaseOrderItemId': 2,
            'receivedQuantity': 10,
          },
          {
            'deliveryItemId': 10,
            'purchaseOrderItemId': 3,
            'receivedQuantity': 2.25,
          },
        ],
      },
    ]),
    200,
  );

  Future<void> open(
    WidgetTester tester,
    Future<http.Response> Function(http.Request) handler,
  ) async {
    final client = MockClient(handler);
    addTearDown(client.close);
    await tester.pumpWidget(
      MaterialApp(
        theme: AppTheme.light,
        home: PendingInspectionsScreen(
          service: QualityApiService(
            baseUrl: 'https://buildwise.example/api/',
            client: client,
          ),
        ),
      ),
    );
  }

  for (final entry in {
    'Received': 'Received',
    'DiscrepancyReported': 'Discrepancy Reported',
    'FutureStatus': 'Unknown status (FutureStatus)',
  }.entries) {
    testWidgets('renders merged API string status ${entry.key}', (
      tester,
    ) async {
      await open(tester, (_) async => deliveries(status: entry.key));
      await tester.pumpAndSettle();
      expect(find.text(entry.value), findsOneWidget);
      expect(find.text('DEL-007'), findsOneWidget);
    });
  }

  testWidgets('shows loading until the request completes', (tester) async {
    final response = Completer<http.Response>();
    await open(tester, (_) => response.future);
    expect(find.text('Loading pending inspections...'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    response.complete(http.Response('[]', 200));
    await tester.pumpAndSettle();
  });

  testWidgets('renders delivery and selection placeholder without POST', (
    tester,
  ) async {
    final methods = <String>[];
    await open(tester, (request) async {
      methods.add(request.method);
      expect(request.url.path, '/api/inspections/pending-deliveries');
      return deliveries();
    });
    await tester.pumpAndSettle();
    expect(find.text('DEL-007'), findsOneWidget);
    expect(find.text('Received'), findsOneWidget);
    expect(find.text('2 items'), findsOneWidget);
    expect(find.text('Item #9: received 10.00'), findsOneWidget);
    expect(find.text('Item #10: received 2.25'), findsOneWidget);
    await tester.tap(find.text('Start Inspection'));
    await tester.pumpAndSettle();
    expect(find.text('Delivery selected'), findsOneWidget);
    expect(
      find.textContaining('No inspection has been started.'),
      findsOneWidget,
    );
    expect(methods, ['GET']);
    await tester.tap(find.text('Back to deliveries'));
    await tester.pumpAndSettle();
    expect(find.text('DEL-007'), findsOneWidget);
  });

  testWidgets('empty response shows empty state', (tester) async {
    await open(tester, (_) async => http.Response('[]', 200));
    await tester.pumpAndSettle();
    expect(find.text('No deliveries ready for inspection'), findsOneWidget);
    expect(find.text('Start Inspection'), findsNothing);
  });

  testWidgets(
    'API error stays distinct from empty and retry loads deliveries',
    (tester) async {
      var calls = 0;
      await open(
        tester,
        (_) async => ++calls == 1
            ? http.Response('{"detail":"Quality service unavailable."}', 503)
            : deliveries(status: 6),
      );
      await tester.pumpAndSettle();
      expect(find.text('Quality service unavailable.'), findsOneWidget);
      expect(find.text('No deliveries ready for inspection'), findsNothing);
      await tester.tap(find.text('Try again'));
      await tester.pumpAndSettle();
      expect(calls, 2);
      expect(find.text('Discrepancy Reported'), findsOneWidget);
      expect(find.text('Quality service unavailable.'), findsNothing);
    },
  );

  testWidgets('unknown status and missing reference have safe labels', (
    tester,
  ) async {
    await open(tester, (_) async => deliveries(status: 99, reference: null));
    await tester.pumpAndSettle();
    expect(find.text('Unknown status (99)'), findsOneWidget);
    expect(find.text('Delivery #7'), findsOneWidget);
    expect(find.text('Start Inspection'), findsOneWidget);
  });

  testWidgets('leaving during a request does not update disposed state', (
    tester,
  ) async {
    final response = Completer<http.Response>();
    await open(tester, (_) => response.future);
    await tester.pumpWidget(const SizedBox());
    response.complete(deliveries());
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
  });

  testWidgets('delivery card fits a narrow mobile viewport', (tester) async {
    tester.view.physicalSize = const Size(320, 640);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.resetPhysicalSize);
    addTearDown(tester.view.resetDevicePixelRatio);
    await open(
      tester,
      (_) async => deliveries(
        status: 6,
        reference:
            'A long delivery reference that should wrap onto another line',
      ),
    );
    await tester.pumpAndSettle();
    expect(tester.takeException(), isNull);
    expect(find.text('Start Inspection'), findsOneWidget);
  });
}
