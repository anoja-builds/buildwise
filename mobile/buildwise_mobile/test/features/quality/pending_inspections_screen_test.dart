import 'dart:async';
import 'dart:convert';

import 'package:buildwise_mobile/core/api/api_client.dart';
import 'package:flutter/services.dart';

import 'package:buildwise_mobile/core/theme/app_theme.dart';
import 'package:buildwise_mobile/features/quality/screens/pending_inspections_screen.dart';
import 'package:buildwise_mobile/features/quality/services/quality_api_service.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:http/http.dart' as http;
import 'package:http/testing.dart';

void main() {
  const storage = MethodChannel('plugins.it_nomads.com/flutter_secure_storage');
  setUp(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(
          storage,
          (call) async => call.method == 'read' ? 'test-jwt' : null,
        );
  });
  tearDown(() {
    TestDefaultBinaryMessengerBinding.instance.defaultBinaryMessenger
        .setMockMethodCallHandler(storage, null);
  });
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
          service: QualityApiService(apiClient: ApiClient(client: client)),
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

  for (final entry in {401: 'sign in again', 403: 'permission'}.entries) {
    testWidgets('displays HTTP ${entry.key} access error', (tester) async {
      await open(tester, (_) async => http.Response('', entry.key));
      await tester.pumpAndSettle();
      expect(find.textContaining(entry.value), findsOneWidget);
      expect(find.text('No deliveries ready for inspection'), findsNothing);
      expect(find.text('Try again'), findsOneWidget);
    });
  }

  testWidgets('network failure displays connection error', (tester) async {
    await open(tester, (_) async => throw http.ClientException('offline'));
    await tester.pumpAndSettle();
    expect(
      find.text('Unable to connect to the BuildWise API.'),
      findsOneWidget,
    );
  });

  testWidgets('invalid response displays parsing error', (tester) async {
    await open(tester, (_) async => http.Response('{}', 200));
    await tester.pumpAndSettle();
    expect(
      find.text('The BuildWise API returned an invalid response.'),
      findsOneWidget,
    );
  });

  testWidgets('shows loading until the request completes', (tester) async {
    final response = Completer<http.Response>();
    await open(tester, (_) => response.future);
    expect(find.text('Loading pending inspections...'), findsOneWidget);
    expect(find.byType(CircularProgressIndicator), findsOneWidget);
    response.complete(http.Response('[]', 200));
    await tester.pumpAndSettle();
  });

  testWidgets('opens creation form and cancels without POST', (tester) async {
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
    expect(find.text('Notes (optional)'), findsOneWidget);
    expect(find.text('Confirm Start Inspection'), findsOneWidget);
    expect(methods, ['GET']);
    await tester.ensureVisible(find.text('Cancel'));
    await tester.tap(find.text('Cancel'));
    await tester.pumpAndSettle();
    expect(find.text('DEL-007'), findsOneWidget);
    expect(methods, ['GET', 'GET']);
  });

  final created = jsonEncode({
    'id': 12,
    'deliveryId': 7,
    'inspectorUserId': 42,
    'status': 'UnderInspection',
    'items': [],
    'notes': null,
    'inspectionDate': '2026-09-23T00:00:00Z',
    'createdAt': '2026-09-23T00:00:00Z',
    'updatedAt': '2026-09-23T00:00:00Z',
    'overallDecision': null,
  });

  for (final notes in ['', '  Initial delivery check  ']) {
    testWidgets(
      'starts with optional notes and refreshes pending list: $notes',
      (tester) async {
        var posts = 0;
        var gets = 0;
        await open(tester, (request) async {
          if (request.method == 'GET') {
            gets++;
            return posts == 0 ? deliveries() : http.Response('[]', 200);
          }
          posts++;
          expect(request.url.path, '/api/inspections');
          expect(request.headers['Authorization'], 'Bearer test-jwt');
          expect(jsonDecode(request.body), {
            'deliveryId': 7,
            if (notes.isNotEmpty) 'notes': notes.trim(),
          });
          return http.Response(created, 201);
        });
        await tester.pumpAndSettle();
        await tester.tap(find.text('Start Inspection'));
        await tester.pumpAndSettle();
        expect(find.textContaining('Item #9 (order item #2)'), findsOneWidget);
        expect(
          find.byType(TextField),
          findsOneWidget,
        ); // No inspector identity input.
        await tester.enterText(find.byType(TextField), notes);
        await tester.ensureVisible(find.text('Confirm Start Inspection'));
        await tester.tap(find.text('Confirm Start Inspection'));
        await tester.pumpAndSettle();
        expect(
          find.text('Inspection #12 started successfully.'),
          findsOneWidget,
        );
        expect(find.text('Under Inspection'), findsOneWidget);
        expect(find.text('Confirm Start Inspection'), findsNothing);
        await tester.ensureVisible(find.text('Back to pending inspections'));
        await tester.tap(find.text('Back to pending inspections'));
        await tester.pumpAndSettle();
        expect(find.text('No deliveries ready for inspection'), findsOneWidget);
        expect(posts, 1);
        expect(gets, 2);
      },
    );
  }

  testWidgets(
    'submission disables duplicates and back navigation until response',
    (tester) async {
      final response = Completer<http.Response>();
      var posts = 0;
      await open(tester, (request) async {
        if (request.method == 'GET') return deliveries();
        posts++;
        return response.future;
      });
      await tester.pumpAndSettle();
      await tester.tap(find.text('Start Inspection'));
      await tester.pumpAndSettle();
      await tester.ensureVisible(find.text('Confirm Start Inspection'));
      await tester.tap(find.text('Confirm Start Inspection'));
      await tester.pump();
      expect(find.byType(LinearProgressIndicator), findsOneWidget);
      await tester.tap(find.text('Starting...'));
      await tester.pump();
      expect(posts, 1);
      expect(tester.widget<TextField>(find.byType(TextField)).readOnly, isTrue);
      await tester.binding.handlePopRoute();
      await tester.pump();
      expect(find.text('Starting...'), findsOneWidget);
      response.complete(http.Response(created, 201));
      await tester.pumpAndSettle();
      expect(find.text('Inspection #12 started successfully.'), findsOneWidget);
    },
  );

  for (final failure in [
    (
      400,
      '{"title":"Validation failed","errors":{"DeliveryId":["DeliveryId must be positive."]}}',
      'DeliveryId must be positive.',
    ),
    (
      409,
      '{"detail":"This delivery already has an active inspection."}',
      'This delivery already has an active inspection.',
    ),
    (401, '', 'sign in again'),
    (403, '', 'permission'),
    (201, '{}', 'invalid response'),
    (
      500,
      '{"detail":"Unable to start inspection."}',
      'Unable to start inspection.',
    ),
  ]) {
    testWidgets('start displays ${failure.$1} error and preserves notes', (
      tester,
    ) async {
      await open(
        tester,
        (request) async => request.method == 'GET'
            ? deliveries()
            : http.Response(failure.$2, failure.$1),
      );
      await tester.pumpAndSettle();
      await tester.tap(find.text('Start Inspection'));
      await tester.pumpAndSettle();
      await tester.enterText(find.byType(TextField), 'Retain these notes');
      await tester.ensureVisible(find.text('Confirm Start Inspection'));
      await tester.tap(find.text('Confirm Start Inspection'));
      await tester.pumpAndSettle();
      expect(find.textContaining(failure.$3), findsOneWidget);
      expect(find.text('Retain these notes'), findsOneWidget);
      expect(find.text('Confirm Start Inspection'), findsOneWidget);
      expect(find.textContaining('started successfully'), findsNothing);
    });
  }

  testWidgets('network failure allows explicit retry', (tester) async {
    var posts = 0;
    await open(tester, (request) async {
      if (request.method == 'GET') return deliveries();
      if (++posts == 1) throw http.ClientException('offline');
      return http.Response(created, 201);
    });
    await tester.pumpAndSettle();
    await tester.tap(find.text('Start Inspection'));
    await tester.pumpAndSettle();
    await tester.ensureVisible(find.text('Confirm Start Inspection'));
    await tester.tap(find.text('Confirm Start Inspection'));
    await tester.pumpAndSettle();
    expect(
      find.text('Unable to connect to the BuildWise API.'),
      findsOneWidget,
    );
    await tester.ensureVisible(find.text('Confirm Start Inspection'));
    await tester.tap(find.text('Confirm Start Inspection'));
    await tester.pumpAndSettle();
    expect(posts, 2);
    expect(find.text('Inspection #12 started successfully.'), findsOneWidget);
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
